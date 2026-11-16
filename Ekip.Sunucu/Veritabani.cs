using System.Security.Cryptography;
using System.Text;
using Ekip.Ortak;
using Microsoft.Data.Sqlite;

namespace Ekip.Sunucu;

// Tek dosyalık SQLite veritabanı. Ayrı bir servis yok, her şey burada.
public class Veritabani
{
    private readonly string _baglanti;

    public Veritabani(string dosyaYolu)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dosyaYolu))!);
        _baglanti = new SqliteConnectionStringBuilder { DataSource = dosyaYolu, Pooling = true }.ToString();
        TablolariOlustur();
    }

    private SqliteConnection Ac()
    {
        var b = new SqliteConnection(_baglanti);
        b.Open();
        return b;
    }

    private void TablolariOlustur()
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS kullanicilar (
                id            INTEGER PRIMARY KEY AUTOINCREMENT,
                kullanici_adi TEXT NOT NULL UNIQUE COLLATE NOCASE,
                gorunen_ad    TEXT NOT NULL,
                sifre_hash    TEXT NOT NULL,
                olusturma     INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS oturumlar (
                token_hash    TEXT PRIMARY KEY,
                kullanici_id  INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
                olusturma     INTEGER NOT NULL
            );
            """;
        k.ExecuteNonQuery();

        // Sürüm bazlı geçişler: eski veritabanları kaybolmadan yeni sütunları alır.
        k.CommandText = "PRAGMA user_version";
        var surum = Convert.ToInt32(k.ExecuteScalar());
        if (surum < 2)
        {
            using var t = b.BeginTransaction();
            k.Transaction = t;
            k.CommandText = """
                ALTER TABLE kullanicilar ADD COLUMN hakkimda      TEXT    NOT NULL DEFAULT '';
                ALTER TABLE kullanicilar ADD COLUMN durum_mesaji  TEXT    NOT NULL DEFAULT '';
                ALTER TABLE kullanicilar ADD COLUMN profil_rengi  TEXT    NOT NULL DEFAULT '#7C5CFF';
                ALTER TABLE kullanicilar ADD COLUMN avatar        BLOB;
                ALTER TABLE kullanicilar ADD COLUMN avatar_surum  INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE kullanicilar ADD COLUMN secilen_durum INTEGER NOT NULL DEFAULT 1;

                -- durum: 0 = istek bekliyor, 1 = arkadaşlar
                CREATE TABLE arkadaslik (
                    gonderen_id INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
                    alici_id    INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
                    durum       INTEGER NOT NULL,
                    zaman       INTEGER NOT NULL,
                    PRIMARY KEY (gonderen_id, alici_id)
                );
                CREATE INDEX arkadaslik_alici ON arkadaslik(alici_id);

                PRAGMA user_version = 2;
                """;
            k.ExecuteNonQuery();
            t.Commit();
        }
        if (surum < 3)
        {
            using var t = b.BeginTransaction();
            k.Transaction = t;
            k.CommandText = """
                CREATE TABLE mesajlar (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    gonderen_id INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
                    alici_id    INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
                    metin       TEXT    NOT NULL,
                    zaman       INTEGER NOT NULL,
                    okundu      INTEGER NOT NULL DEFAULT 0
                );
                CREATE INDEX mesajlar_cift ON mesajlar(gonderen_id, alici_id, id);
                CREATE INDEX mesajlar_okunmamis ON mesajlar(alici_id, okundu);
                PRAGMA user_version = 3;
                """;
            k.ExecuteNonQuery();
            t.Commit();
        }
        if (surum < 4)
        {
            using var t = b.BeginTransaction();
            k.Transaction = t;
            k.CommandText = """
                ALTER TABLE kullanicilar ADD COLUMN admin       INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE kullanicilar ADD COLUMN yasakli     INTEGER NOT NULL DEFAULT 0;
                ALTER TABLE kullanicilar ADD COLUMN davet_kodu  TEXT;

                -- kalan: -1 sınırsız. son_kullanma: unix saniye, NULL süresiz.
                CREATE TABLE davet_kodlari (
                    kod           TEXT PRIMARY KEY,
                    olusturan_id  INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
                    kalan         INTEGER NOT NULL,
                    olusturma     INTEGER NOT NULL,
                    son_kullanma  INTEGER,
                    iptal         INTEGER NOT NULL DEFAULT 0
                );

                -- Eski veritabanında admin yoksa ilk kullanıcı admin olsun.
                UPDATE kullanicilar SET admin = 1
                WHERE id = (SELECT MIN(id) FROM kullanicilar) AND NOT EXISTS (SELECT 1 FROM kullanicilar WHERE admin = 1);

                PRAGMA user_version = 4;
                """;
            k.ExecuteNonQuery();
            t.Commit();
        }
    }

    // ---- Kayıt (davet kodlu) ----

    public record KayitSonucu(KullaniciBilgisi? Kullanici, string? Hata, string? Alan);

    // Tek işlemde: kodu doğrula, bir kullanım düş, kullanıcıyı ekle. Biri başarısız olursa hiçbiri olmaz.
    public KayitSonucu KayitOl(string kullaniciAdi, string gorunenAd, string sifre, string? kod)
    {
        var hash = Sifre.Hashle(sifre); // Yavaş işlem: kilidi tutmadan önce yap.
        var simdi = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        using var b = Ac();
        using var t = b.BeginTransaction();
        using var k = b.CreateCommand();
        k.Transaction = t;

        k.CommandText = "SELECT COUNT(*) FROM kullanicilar";
        bool ilkKullanici = Convert.ToInt64(k.ExecuteScalar()) == 0;

        string? kullanilanKod = null;
        if (!ilkKullanici)
        {
            kod = KodDuzelt(kod);
            if (kod.Length == 0) return new(null, "Kayıt olmak için davet kodu gerekli.", "kod");
            k.CommandText = "SELECT kalan, son_kullanma, iptal FROM davet_kodlari WHERE kod = $kod";
            k.Parameters.AddWithValue("$kod", kod);
            using (var r = k.ExecuteReader())
            {
                if (!r.Read()) return new(null, "Bu davet kodu geçersiz.", "kod");
                if (r.GetInt32(2) == 1) return new(null, "Bu davet kodu iptal edilmiş.", "kod");
                if (!r.IsDBNull(1) && r.GetInt64(1) < simdi) return new(null, "Bu davet kodunun süresi dolmuş.", "kod");
                if (r.GetInt32(0) == 0) return new(null, "Bu davet kodu kullanılmış.", "kod");
            }
            k.CommandText = "UPDATE davet_kodlari SET kalan = kalan - 1 WHERE kod = $kod AND kalan > 0";
            k.ExecuteNonQuery();
            k.Parameters.Clear();
            kullanilanKod = kod;
        }

        k.CommandText = """
            INSERT INTO kullanicilar (kullanici_adi, gorunen_ad, sifre_hash, olusturma, admin, davet_kodu)
            VALUES ($ad, $gorunen, $hash, $zaman, $admin, $kod)
            ON CONFLICT(kullanici_adi) DO NOTHING
            RETURNING id;
            """;
        k.Parameters.AddWithValue("$ad", kullaniciAdi);
        k.Parameters.AddWithValue("$gorunen", gorunenAd);
        k.Parameters.AddWithValue("$hash", hash);
        k.Parameters.AddWithValue("$zaman", simdi);
        k.Parameters.AddWithValue("$admin", ilkKullanici ? 1 : 0);
        k.Parameters.AddWithValue("$kod", (object?)kullanilanKod ?? DBNull.Value);
        var id = k.ExecuteScalar();
        if (id is null) return new(null, "Bu kullanıcı adı alınmış.", "kullaniciAdi"); // İşlem geri alınır: kod harcanmaz.
        t.Commit();
        return new(new KullaniciBilgisi((long)id, kullaniciAdi, gorunenAd), null, null);
    }

    // Kullanıcı "k7m3 q9xr" yazsa da "K7M3-Q9XR" olarak eşleşsin.
    public static string KodDuzelt(string? kod)
    {
        var s = new string((kod ?? "").ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        return s.Length == 8 ? s[..4] + "-" + s[4..] : s;
    }

    // ---- Yönetim ----

    private const string KodAlfabesi = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Karışan harfler yok (0/O, 1/I)

    public DavetKodu KodOlustur(long olusturan, int kullanim, int? gun)
    {
        var rastgele = RandomNumberGenerator.GetItems<char>(KodAlfabesi, 8);
        var kod = new string(rastgele, 0, 4) + "-" + new string(rastgele, 4, 4);
        var simdi = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long? son = gun is > 0 ? simdi + gun.Value * 86400L : null;
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "INSERT INTO davet_kodlari (kod, olusturan_id, kalan, olusturma, son_kullanma) VALUES ($kod, $o, $kalan, $z, $son)";
        k.Parameters.AddWithValue("$kod", kod);
        k.Parameters.AddWithValue("$o", olusturan);
        k.Parameters.AddWithValue("$kalan", kullanim <= 0 ? -1 : kullanim);
        k.Parameters.AddWithValue("$z", simdi);
        k.Parameters.AddWithValue("$son", (object?)son ?? DBNull.Value);
        k.ExecuteNonQuery();
        return Kodlar().First(x => x.Kod == kod);
    }

    public List<DavetKodu> Kodlar()
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = """
            SELECT d.kod, d.kalan, (SELECT COUNT(*) FROM kullanicilar x WHERE x.davet_kodu = d.kod), d.olusturma, d.son_kullanma, u.gorunen_ad, d.iptal
            FROM davet_kodlari d JOIN kullanicilar u ON u.id = d.olusturan_id
            ORDER BY d.olusturma DESC
            """;
        using var r = k.ExecuteReader();
        var liste = new List<DavetKodu>();
        while (r.Read())
            liste.Add(new DavetKodu(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetInt64(3), r.IsDBNull(4) ? null : r.GetInt64(4), r.GetString(5), r.GetInt32(6) == 1));
        return liste;
    }

    public bool KodIptal(string kod)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "UPDATE davet_kodlari SET iptal = 1 WHERE kod = $kod";
        k.Parameters.AddWithValue("$kod", KodDuzelt(kod));
        return k.ExecuteNonQuery() > 0;
    }

    public List<(long id, string ad, string gorunen, string renk, int avatar, long kayit, bool admin, bool yasakli, string? kod)> Kullanicilar()
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "SELECT id, kullanici_adi, gorunen_ad, profil_rengi, avatar_surum, olusturma, admin, yasakli, davet_kodu FROM kullanicilar ORDER BY id";
        using var r = k.ExecuteReader();
        var liste = new List<(long, string, string, string, int, long, bool, bool, string?)>();
        while (r.Read())
            liste.Add((r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetInt32(4), r.GetInt64(5),
                r.GetInt32(6) == 1, r.GetInt32(7) == 1, r.IsDBNull(8) ? null : r.GetString(8)));
        return liste;
    }

    public bool AdminMi(long id)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "SELECT admin FROM kullanicilar WHERE id = $id";
        k.Parameters.AddWithValue("$id", id);
        return Convert.ToInt64(k.ExecuteScalar() ?? 0L) == 1;
    }

    public void AdminAyarla(long id, bool admin) => Calistir("UPDATE kullanicilar SET admin = $v WHERE id = $id", id, admin);

    // Yasaklayınca tüm oturumları silinir: hiçbir cihazdan devam edemez.
    public void YasakAyarla(long id, bool yasakli)
    {
        Calistir("UPDATE kullanicilar SET yasakli = $v WHERE id = $id", id, yasakli);
        if (yasakli) Calistir("DELETE FROM oturumlar WHERE kullanici_id = $id AND $v = 1", id, true);
    }

    public bool AdminYapKullaniciAdiyla(string kullaniciAdi)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "UPDATE kullanicilar SET admin = 1 WHERE kullanici_adi = $ad";
        k.Parameters.AddWithValue("$ad", kullaniciAdi);
        return k.ExecuteNonQuery() > 0;
    }

    private void Calistir(string sql, long id, bool deger)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = sql;
        k.Parameters.AddWithValue("$id", id);
        k.Parameters.AddWithValue("$v", deger ? 1 : 0);
        k.ExecuteNonQuery();
    }

    // ---- Mesajlar ----

    private static Mesaj MesajOku(SqliteDataReader r, int i = 0) =>
        new(r.GetInt64(i), r.GetInt64(i + 1), r.GetInt64(i + 2), r.GetString(i + 3), r.GetInt64(i + 4));

    public Mesaj MesajEkle(long gonderen, long alici, string metin)
    {
        var zaman = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "INSERT INTO mesajlar (gonderen_id, alici_id, metin, zaman) VALUES ($g, $a, $m, $z) RETURNING id";
        k.Parameters.AddWithValue("$g", gonderen);
        k.Parameters.AddWithValue("$a", alici);
        k.Parameters.AddWithValue("$m", metin);
        k.Parameters.AddWithValue("$z", zaman);
        return new Mesaj((long)k.ExecuteScalar()!, gonderen, alici, metin, zaman);
    }

    // En yeni "adet" mesaj (once verilirse ondan eskiler), eskiden yeniye sıralı.
    public List<Mesaj> Mesajlar(long ben, long karsi, long? once, int adet)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = """
            SELECT id, gonderen_id, alici_id, metin, zaman FROM mesajlar
            WHERE ((gonderen_id = $ben AND alici_id = $o) OR (gonderen_id = $o AND alici_id = $ben))
              AND id < $once
            ORDER BY id DESC LIMIT $adet
            """;
        k.Parameters.AddWithValue("$ben", ben);
        k.Parameters.AddWithValue("$o", karsi);
        k.Parameters.AddWithValue("$once", once ?? long.MaxValue);
        k.Parameters.AddWithValue("$adet", adet);
        using var r = k.ExecuteReader();
        var liste = new List<Mesaj>();
        while (r.Read()) liste.Add(MesajOku(r));
        liste.Reverse();
        return liste;
    }

    public int OkunduIsaretle(long ben, long karsi)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "UPDATE mesajlar SET okundu = 1 WHERE gonderen_id = $o AND alici_id = $ben AND okundu = 0";
        k.Parameters.AddWithValue("$ben", ben);
        k.Parameters.AddWithValue("$o", karsi);
        return k.ExecuteNonQuery();
    }

    // Arkadaşlarımla olan sohbetler: son mesaj ve okunmamış sayısı, en yeni sohbet üstte.
    public List<(Profil karsi, Mesaj son, int okunmamis)> SohbetOzetleri(long ben)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = $"""
            WITH konusmalar AS (
                SELECT CASE WHEN gonderen_id = $ben THEN alici_id ELSE gonderen_id END AS karsi, MAX(id) AS son_id
                FROM mesajlar WHERE gonderen_id = $ben OR alici_id = $ben
                GROUP BY karsi
            )
            SELECT {ProfilSutunlari}, m.id, m.gonderen_id, m.alici_id, m.metin, m.zaman,
                   (SELECT COUNT(*) FROM mesajlar x WHERE x.gonderen_id = k.karsi AND x.alici_id = $ben AND x.okundu = 0)
            FROM konusmalar k
            JOIN mesajlar m ON m.id = k.son_id
            JOIN kullanicilar u ON u.id = k.karsi
            JOIN arkadaslik a ON a.durum = 1 AND ((a.gonderen_id = $ben AND a.alici_id = k.karsi) OR (a.gonderen_id = k.karsi AND a.alici_id = $ben))
            ORDER BY m.id DESC
            """;
        k.Parameters.AddWithValue("$ben", ben);
        using var r = k.ExecuteReader();
        var liste = new List<(Profil, Mesaj, int)>();
        while (r.Read()) liste.Add((ProfilOku(r), MesajOku(r, 8), r.GetInt32(13)));
        return liste;
    }

    private static long Simdi => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private const string ProfilSutunlari =
        "u.id, u.kullanici_adi, u.gorunen_ad, u.hakkimda, u.durum_mesaji, u.profil_rengi, u.avatar_surum, u.secilen_durum";

    // Durum burada kullanıcının SEÇTİĞİ durumdur; çevrimiçi olup olmadığı Varlik sınıfında birleştirilir.
    private static Profil ProfilOku(SqliteDataReader r) => new(
        r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4),
        r.GetString(5), r.GetInt32(6), (Durum)r.GetInt32(7));

    public Profil? ProfilGetir(long id)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = $"SELECT {ProfilSutunlari} FROM kullanicilar u WHERE u.id = $id";
        k.Parameters.AddWithValue("$id", id);
        using var r = k.ExecuteReader();
        return r.Read() ? ProfilOku(r) : null;
    }

    public long? KullaniciIdBul(string kullaniciAdi)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "SELECT id FROM kullanicilar WHERE kullanici_adi = $ad";
        k.Parameters.AddWithValue("$ad", kullaniciAdi);
        return k.ExecuteScalar() is long id ? id : null;
    }

    public void ProfilGuncelle(long id, ProfilGuncelleme p)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = """
            UPDATE kullanicilar SET gorunen_ad = $g, hakkimda = $h, durum_mesaji = $d, profil_rengi = $r WHERE id = $id
            """;
        k.Parameters.AddWithValue("$g", p.GorunenAd.Trim());
        k.Parameters.AddWithValue("$h", (p.Hakkimda ?? "").Trim());
        k.Parameters.AddWithValue("$d", (p.DurumMesaji ?? "").Trim());
        k.Parameters.AddWithValue("$r", p.ProfilRengi);
        k.Parameters.AddWithValue("$id", id);
        k.ExecuteNonQuery();
    }

    public void DurumAyarla(long id, Durum durum)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "UPDATE kullanicilar SET secilen_durum = $d WHERE id = $id";
        k.Parameters.AddWithValue("$d", (int)durum);
        k.Parameters.AddWithValue("$id", id);
        k.ExecuteNonQuery();
    }

    // null verilirse resim kaldırılır. Sürüm artar, böylece uygulamalar önbelleği yeniler.
    public void AvatarAyarla(long id, byte[]? veri)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = veri is null
            ? "UPDATE kullanicilar SET avatar = NULL, avatar_surum = 0 WHERE id = $id"
            : "UPDATE kullanicilar SET avatar = $v, avatar_surum = (SELECT MAX(avatar_surum) FROM kullanicilar) + 1 WHERE id = $id";
        if (veri != null) k.Parameters.AddWithValue("$v", veri);
        k.Parameters.AddWithValue("$id", id);
        k.ExecuteNonQuery();
    }

    public byte[]? AvatarGetir(long id)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "SELECT avatar FROM kullanicilar WHERE id = $id";
        k.Parameters.AddWithValue("$id", id);
        return k.ExecuteScalar() as byte[];
    }

    // ---- Arkadaşlık ----

    public enum Iliski { Yok, Arkadas, BenGonderdim, OGonderdi }

    public Iliski IliskiGetir(long ben, long o)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = """
            SELECT gonderen_id, durum FROM arkadaslik
            WHERE (gonderen_id = $ben AND alici_id = $o) OR (gonderen_id = $o AND alici_id = $ben)
            """;
        k.Parameters.AddWithValue("$ben", ben);
        k.Parameters.AddWithValue("$o", o);
        using var r = k.ExecuteReader();
        if (!r.Read()) return Iliski.Yok;
        if (r.GetInt32(1) == 1) return Iliski.Arkadas;
        return r.GetInt64(0) == ben ? Iliski.BenGonderdim : Iliski.OGonderdi;
    }

    public void IstekEkle(long gonderen, long alici)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "INSERT OR IGNORE INTO arkadaslik (gonderen_id, alici_id, durum, zaman) VALUES ($g, $a, 0, $z)";
        k.Parameters.AddWithValue("$g", gonderen);
        k.Parameters.AddWithValue("$a", alici);
        k.Parameters.AddWithValue("$z", Simdi);
        k.ExecuteNonQuery();
    }

    // Sadece bana gelmiş bekleyen bir istek kabul edilebilir.
    public bool IstekKabulEt(long ben, long gonderen)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "UPDATE arkadaslik SET durum = 1, zaman = $z WHERE gonderen_id = $g AND alici_id = $ben AND durum = 0";
        k.Parameters.AddWithValue("$g", gonderen);
        k.Parameters.AddWithValue("$ben", ben);
        k.Parameters.AddWithValue("$z", Simdi);
        return k.ExecuteNonQuery() > 0;
    }

    // Arkadaşlıktan çıkarma, isteği reddetme ve gönderdiğim isteği geri çekme: hepsi aynı satırı siler.
    public bool IliskiSil(long ben, long o)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = """
            DELETE FROM arkadaslik
            WHERE (gonderen_id = $ben AND alici_id = $o) OR (gonderen_id = $o AND alici_id = $ben)
            """;
        k.Parameters.AddWithValue("$ben", ben);
        k.Parameters.AddWithValue("$o", o);
        return k.ExecuteNonQuery() > 0;
    }

    public List<long> ArkadasIdleri(long ben)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = """
            SELECT CASE WHEN gonderen_id = $ben THEN alici_id ELSE gonderen_id END
            FROM arkadaslik WHERE durum = 1 AND (gonderen_id = $ben OR alici_id = $ben)
            """;
        k.Parameters.AddWithValue("$ben", ben);
        using var r = k.ExecuteReader();
        var liste = new List<long>();
        while (r.Read()) liste.Add(r.GetInt64(0));
        return liste;
    }

    // Arkadaşlar, bana gelen ve benim gönderdiğim istekler tek sorguda.
    public (List<Profil> arkadaslar, List<Profil> gelen, List<Profil> giden) ArkadaslariGetir(long ben)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = $"""
            SELECT {ProfilSutunlari}, a.gonderen_id, a.durum
            FROM arkadaslik a
            JOIN kullanicilar u ON u.id = CASE WHEN a.gonderen_id = $ben THEN a.alici_id ELSE a.gonderen_id END
            WHERE a.gonderen_id = $ben OR a.alici_id = $ben
            ORDER BY u.gorunen_ad COLLATE NOCASE
            """;
        k.Parameters.AddWithValue("$ben", ben);
        using var r = k.ExecuteReader();
        List<Profil> arkadaslar = [], gelen = [], giden = [];
        while (r.Read())
        {
            var p = ProfilOku(r);
            if (r.GetInt32(9) == 1) arkadaslar.Add(p);
            else if (r.GetInt64(8) == ben) giden.Add(p);
            else gelen.Add(p);
        }
        return (arkadaslar, gelen, giden);
    }


    public KullaniciBilgisi? GirisDogrula(string kullaniciAdi, string sifre, out bool yasakli)
    {
        yasakli = false;
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "SELECT id, kullanici_adi, gorunen_ad, sifre_hash, yasakli FROM kullanicilar WHERE kullanici_adi = $ad";
        k.Parameters.AddWithValue("$ad", kullaniciAdi);
        using var r = k.ExecuteReader();
        if (!r.Read())
        {
            Sifre.SahteDogrula(sifre); // Kullanıcı var mı yok mu, süreden anlaşılmasın.
            return null;
        }
        if (!Sifre.Dogrula(sifre, r.GetString(3))) return null;
        yasakli = r.GetInt32(4) == 1; // Şifre doğruysa söyleriz; yanlışsa yasaklı olduğu bile anlaşılmasın.
        return yasakli ? null : new KullaniciBilgisi(r.GetInt64(0), r.GetString(1), r.GetString(2));
    }

    public string OturumAc(long kullaniciId)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "INSERT INTO oturumlar (token_hash, kullanici_id, olusturma) VALUES ($h, $id, $zaman)";
        k.Parameters.AddWithValue("$h", TokenHash(token));
        k.Parameters.AddWithValue("$id", kullaniciId);
        k.Parameters.AddWithValue("$zaman", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        k.ExecuteNonQuery();
        return token;
    }

    // Yasaklı kullanıcının oturumu geçersiz sayılır.
    public (KullaniciBilgisi kullanici, bool admin)? OturumdanKullanici(string token)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = """
            SELECT u.id, u.kullanici_adi, u.gorunen_ad, u.admin
            FROM oturumlar o JOIN kullanicilar u ON u.id = o.kullanici_id
            WHERE o.token_hash = $h AND u.yasakli = 0
            """;
        k.Parameters.AddWithValue("$h", TokenHash(token));
        using var r = k.ExecuteReader();
        return r.Read() ? (new KullaniciBilgisi(r.GetInt64(0), r.GetString(1), r.GetString(2)), r.GetInt32(3) == 1) : null;
    }

    public void OturumKapat(string token)
    {
        using var b = Ac();
        using var k = b.CreateCommand();
        k.CommandText = "DELETE FROM oturumlar WHERE token_hash = $h";
        k.Parameters.AddWithValue("$h", TokenHash(token));
        k.ExecuteNonQuery();
    }

    // Veritabanı sızsa bile token'lar kullanılamasın diye sadece hash'i saklanır.
    private static string TokenHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public static class Sifre
{
    private const int Tekrar = 100_000;
    private static readonly string SahteHash = Hashle("sahte-sifre");

    public static string Hashle(string sifre)
    {
        var tuz = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(sifre, tuz, Tekrar, HashAlgorithmName.SHA256, 32);
        return $"v1${Tekrar}${Convert.ToBase64String(tuz)}${Convert.ToBase64String(hash)}";
    }

    public static bool Dogrula(string sifre, string kayitli)
    {
        var p = kayitli.Split('$');
        if (p.Length != 4 || p[0] != "v1") return false;
        var tuz = Convert.FromBase64String(p[2]);
        var beklenen = Convert.FromBase64String(p[3]);
        var hash = Rfc2898DeriveBytes.Pbkdf2(sifre, tuz, int.Parse(p[1]), HashAlgorithmName.SHA256, beklenen.Length);
        return CryptographicOperations.FixedTimeEquals(hash, beklenen);
    }

    public static void SahteDogrula(string sifre) => Dogrula(sifre, SahteHash);
}
