// Ekip.Sunucu/Veritabani.cs'in karşılığı. Durable Object'in içindeki SQLite kullanılır (senkron, tek iş parçacığı).
import { DavetKodu, KullaniciBilgisi, Profil, ProfilGuncelleme, kodDuzelt } from "./kurallar";
import { Sql, simdiSn, type Satir } from "./sql";


export enum Iliski { Yok, Arkadas, BenGonderdim, OGonderdi }

const KOD_ALFABESI = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // Karışan harfler yok (0/O, 1/I)
const SIFRE_TEKRAR = 100_000; // Workers'ın PBKDF2 için izin verdiği en yüksek değer.

const PROFIL_SUTUNLARI =
  "u.id AS p_id, u.kullanici_adi AS p_ad, u.gorunen_ad AS p_gorunen, u.hakkimda AS p_hakkimda, " +
  "u.durum_mesaji AS p_durum_mesaji, u.profil_rengi AS p_renk, u.avatar_surum AS p_avatar, u.secilen_durum AS p_durum";

function profilOku(r: Satir): Profil {
  return {
    id: r.p_id as number, kullaniciAdi: r.p_ad as string, gorunenAd: r.p_gorunen as string,
    hakkimda: r.p_hakkimda as string, durumMesaji: r.p_durum_mesaji as string, profilRengi: r.p_renk as string,
    avatarSurum: r.p_avatar as number, durum: r.p_durum as number,
  };
}

export { profilOku, PROFIL_SUTUNLARI, KOD_ALFABESI };

export class Veritabani {
  // Diğer modüller (mesajlar, sunucular, gruplar) aynı SQL yardımcısını kullanır.
  readonly s: Sql;

  constructor(private readonly sql: SqlStorage, private readonly islem: <T>(f: () => T) => T) {
    this.s = new Sql(sql, islem);
    this.tablolariOlustur();
    this.gecis();
  }

  private hepsi(q: string, ...p: SqlStorageValue[]): Satir[] { return this.s.hepsi(q, ...p); }
  private bir(q: string, ...p: SqlStorageValue[]): Satir | null { return this.s.bir(q, ...p); }
  private calistir(q: string, ...p: SqlStorageValue[]): number { return this.s.calistir(q, ...p); }

  // Şema sürümü geçişleri (yalnızca ekleme; canlı veri silinmez). Sürüm `ayar.surum` içinde.
  private gecis() {
    const surum = Number(this.bir("SELECT deger FROM ayar WHERE ad = 'surum'")?.deger ?? "4");
    if (surum < 5) this.islem(() => { this.surum5(); this.calistir("UPDATE ayar SET deger = '5' WHERE ad = 'surum'"); });
  }

  // v5: kanal/grup mesajları, ekler, tepkiler, sunucular, gruplar, dosyalar.
  // mesajlar tablosu yeniden kurulur: alici_id artık kullanıcıya bağlı olmak zorunda değil (kanal/grup mesajında 0).
  private surum5() {
    const e = (q: string) => this.sql.exec(q);
    e(`CREATE TABLE mesajlar_yeni (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        gonderen_id INTEGER NOT NULL,
        alici_id    INTEGER NOT NULL,
        metin       TEXT    NOT NULL,
        zaman       INTEGER NOT NULL,
        okundu      INTEGER NOT NULL DEFAULT 0,
        kanal_id    INTEGER,
        grup_id     INTEGER,
        yanit_id    INTEGER,
        ekler       TEXT,
        duzenlenme  INTEGER,
        silindi     INTEGER NOT NULL DEFAULT 0
      )`);
    e("INSERT INTO mesajlar_yeni (id, gonderen_id, alici_id, metin, zaman, okundu) SELECT id, gonderen_id, alici_id, metin, zaman, okundu FROM mesajlar");
    e("DROP TABLE mesajlar");
    e("ALTER TABLE mesajlar_yeni RENAME TO mesajlar");
    e("CREATE INDEX mesajlar_cift ON mesajlar(gonderen_id, alici_id, id)");
    e("CREATE INDEX mesajlar_okunmamis ON mesajlar(alici_id, okundu)");
    e("CREATE INDEX mesajlar_kanal ON mesajlar(kanal_id, id)");
    e("CREATE INDEX mesajlar_grup ON mesajlar(grup_id, id)");
    e("CREATE TABLE IF NOT EXISTS tepkiler (mesaj_id INTEGER NOT NULL, kullanici_id INTEGER NOT NULL, emoji TEXT NOT NULL, zaman INTEGER NOT NULL, PRIMARY KEY (mesaj_id, kullanici_id, emoji))");
    e("CREATE TABLE IF NOT EXISTS bahsetmeler (mesaj_id INTEGER NOT NULL, kullanici_id INTEGER NOT NULL, PRIMARY KEY (mesaj_id, kullanici_id))");
    e("CREATE INDEX IF NOT EXISTS bahsetmeler_kullanici ON bahsetmeler(kullanici_id, mesaj_id)");
    e("CREATE TABLE IF NOT EXISTS okumalar (kullanici_id INTEGER NOT NULL, konusma TEXT NOT NULL, son_id INTEGER NOT NULL, PRIMARY KEY (kullanici_id, konusma))");
    e(`CREATE TABLE IF NOT EXISTS sunucular (
        id INTEGER PRIMARY KEY AUTOINCREMENT, ad TEXT NOT NULL, aciklama TEXT NOT NULL DEFAULT '', renk TEXT NOT NULL,
        ikon BLOB, ikon_surum INTEGER NOT NULL DEFAULT 0, herkese INTEGER NOT NULL DEFAULT 0, sahip_id INTEGER NOT NULL,
        davet_kodu TEXT NOT NULL UNIQUE, olusturma INTEGER NOT NULL)`);
    e(`CREATE TABLE IF NOT EXISTS sunucu_uyeleri (
        sunucu_id INTEGER NOT NULL, kullanici_id INTEGER NOT NULL, rol INTEGER NOT NULL DEFAULT 0, katilma INTEGER NOT NULL,
        PRIMARY KEY (sunucu_id, kullanici_id))`);
    e("CREATE INDEX IF NOT EXISTS sunucu_uyeleri_kullanici ON sunucu_uyeleri(kullanici_id)");
    e(`CREATE TABLE IF NOT EXISTS kanallar (
        id INTEGER PRIMARY KEY AUTOINCREMENT, sunucu_id INTEGER NOT NULL, ad TEXT NOT NULL, konu TEXT NOT NULL DEFAULT '',
        sira INTEGER NOT NULL DEFAULT 0, olusturma INTEGER NOT NULL)`);
    e("CREATE INDEX IF NOT EXISTS kanallar_sunucu ON kanallar(sunucu_id, sira)");
    e(`CREATE TABLE IF NOT EXISTS gruplar (
        id INTEGER PRIMARY KEY AUTOINCREMENT, ad TEXT NOT NULL, aciklama TEXT NOT NULL DEFAULT '', renk TEXT NOT NULL,
        ikon BLOB, ikon_surum INTEGER NOT NULL DEFAULT 0, kurucu_id INTEGER NOT NULL, olusturma INTEGER NOT NULL)`);
    e(`CREATE TABLE IF NOT EXISTS grup_uyeleri (
        grup_id INTEGER NOT NULL, kullanici_id INTEGER NOT NULL, katilma INTEGER NOT NULL, PRIMARY KEY (grup_id, kullanici_id))`);
    e("CREATE INDEX IF NOT EXISTS grup_uyeleri_kullanici ON grup_uyeleri(kullanici_id)");
    e(`CREATE TABLE IF NOT EXISTS dosyalar (
        anahtar TEXT PRIMARY KEY, sahip_id INTEGER NOT NULL, ad TEXT NOT NULL, mime TEXT NOT NULL, boyut INTEGER NOT NULL, zaman INTEGER NOT NULL)`);
    e("CREATE INDEX IF NOT EXISTS dosyalar_sahip ON dosyalar(sahip_id, zaman)");
  }

  // Beni görebilecek kişiler: arkadaşlarım + ortak sunucudakiler + ortak gruptakiler (durum/profil değişimi buraya gider).
  gorenler(ben: number): number[] {
    return this.hepsi(`
      SELECT CASE WHEN gonderen_id = ?1 THEN alici_id ELSE gonderen_id END AS id
        FROM arkadaslik WHERE durum = 1 AND (gonderen_id = ?1 OR alici_id = ?1)
      UNION SELECT b.kullanici_id FROM sunucu_uyeleri a JOIN sunucu_uyeleri b ON b.sunucu_id = a.sunucu_id
        WHERE a.kullanici_id = ?1 AND b.kullanici_id != ?1
      UNION SELECT b.kullanici_id FROM grup_uyeleri a JOIN grup_uyeleri b ON b.grup_id = a.grup_id
        WHERE a.kullanici_id = ?1 AND b.kullanici_id != ?1`, ben).map(r => r.id as number);
  }

  // Keşfet'te ve kimlikle istek atarken: numarayla kullanıcı (yasaklı değilse).
  kullaniciVarMi(id: number): boolean { return this.bir("SELECT 1 FROM kullanicilar WHERE id = ? AND yasakli = 0", id) !== null; }

  private tablolariOlustur() {
    this.sql.exec(`
      CREATE TABLE IF NOT EXISTS kullanicilar (
        id            INTEGER PRIMARY KEY AUTOINCREMENT,
        kullanici_adi TEXT NOT NULL UNIQUE COLLATE NOCASE,
        gorunen_ad    TEXT NOT NULL,
        sifre_hash    TEXT NOT NULL,
        olusturma     INTEGER NOT NULL,
        hakkimda      TEXT NOT NULL DEFAULT '',
        durum_mesaji  TEXT NOT NULL DEFAULT '',
        profil_rengi  TEXT NOT NULL DEFAULT '#7C5CFF',
        avatar        BLOB,
        avatar_surum  INTEGER NOT NULL DEFAULT 0,
        secilen_durum INTEGER NOT NULL DEFAULT 1,
        admin         INTEGER NOT NULL DEFAULT 0,
        yasakli       INTEGER NOT NULL DEFAULT 0,
        davet_kodu    TEXT
      );
      CREATE TABLE IF NOT EXISTS oturumlar (
        token_hash   TEXT PRIMARY KEY,
        kullanici_id INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
        olusturma    INTEGER NOT NULL
      );
      -- durum: 0 = istek bekliyor, 1 = arkadaşlar
      CREATE TABLE IF NOT EXISTS arkadaslik (
        gonderen_id INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
        alici_id    INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
        durum       INTEGER NOT NULL,
        zaman       INTEGER NOT NULL,
        PRIMARY KEY (gonderen_id, alici_id)
      );
      CREATE INDEX IF NOT EXISTS arkadaslik_alici ON arkadaslik(alici_id);
      CREATE TABLE IF NOT EXISTS mesajlar (
        id          INTEGER PRIMARY KEY AUTOINCREMENT,
        gonderen_id INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
        alici_id    INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
        metin       TEXT    NOT NULL,
        zaman       INTEGER NOT NULL,
        okundu      INTEGER NOT NULL DEFAULT 0
      );
      CREATE INDEX IF NOT EXISTS mesajlar_cift ON mesajlar(gonderen_id, alici_id, id);
      CREATE INDEX IF NOT EXISTS mesajlar_okunmamis ON mesajlar(alici_id, okundu);
      -- kalan: -1 sınırsız. son_kullanma: unix saniye, NULL süresiz.
      CREATE TABLE IF NOT EXISTS davet_kodlari (
        kod          TEXT PRIMARY KEY,
        olusturan_id INTEGER NOT NULL REFERENCES kullanicilar(id) ON DELETE CASCADE,
        kalan        INTEGER NOT NULL,
        olusturma    INTEGER NOT NULL,
        son_kullanma INTEGER,
        iptal        INTEGER NOT NULL DEFAULT 0
      );
      -- İleride tablo değişirse sürüm buradan takip edilir.
      CREATE TABLE IF NOT EXISTS ayar (ad TEXT PRIMARY KEY, deger TEXT NOT NULL);
      INSERT OR IGNORE INTO ayar (ad, deger) VALUES ('surum', '4');
    `);
  }

  // ---- Kayıt (davet kodlu) ----

  // Tek işlemde: kodu doğrula, bir kullanım düş, kullanıcıyı ekle. Biri başarısız olursa hiçbiri olmaz.
  async kayitOl(kullaniciAdi: string, gorunenAd: string, sifre: string, kodGirdi: unknown):
    Promise<{ kullanici?: KullaniciBilgisi; hata?: string; alan?: string }> {
    const hash = await Sifre.hashle(sifre); // Yavaş işlem: işlemden önce.
    const simdi = simdiSn();
    class Iptal { constructor(public hata: string, public alan: string) {} }
    try {
      return this.islem(() => {
        const ilk = (this.bir("SELECT COUNT(*) AS n FROM kullanicilar")!.n as number) === 0;
        let kullanilanKod: string | null = null;
        if (!ilk) {
          const kod = kodDuzelt(kodGirdi);
          if (kod.length === 0) throw new Iptal("Kayıt olmak için davet kodu gerekli.", "kod");
          const r = this.bir("SELECT kalan, son_kullanma, iptal FROM davet_kodlari WHERE kod = ?", kod);
          if (!r) throw new Iptal("Bu davet kodu geçersiz.", "kod");
          if (r.iptal === 1) throw new Iptal("Bu davet kodu iptal edilmiş.", "kod");
          if (r.son_kullanma !== null && (r.son_kullanma as number) < simdi) throw new Iptal("Bu davet kodunun süresi dolmuş.", "kod");
          if (r.kalan === 0) throw new Iptal("Bu davet kodu kullanılmış.", "kod");
          this.calistir("UPDATE davet_kodlari SET kalan = kalan - 1 WHERE kod = ? AND kalan > 0", kod);
          kullanilanKod = kod;
        }
        const r = this.bir(`
          INSERT INTO kullanicilar (kullanici_adi, gorunen_ad, sifre_hash, olusturma, admin, davet_kodu)
          VALUES (?, ?, ?, ?, ?, ?) ON CONFLICT(kullanici_adi) DO NOTHING RETURNING id`,
          kullaniciAdi, gorunenAd, hash, simdi, ilk ? 1 : 0, kullanilanKod);
        // İşlem geri alınır: kod harcanmaz.
        if (!r) throw new Iptal("Bu kullanıcı adı alınmış.", "kullaniciAdi");
        return { kullanici: { id: r.id as number, kullaniciAdi, gorunenAd } };
      });
    } catch (e) {
      if (e instanceof Iptal) return { hata: e.hata, alan: e.alan };
      throw e;
    }
  }

  // ---- Yönetim ----

  kodOlustur(olusturan: number, kullanim: number, gun: number | null): DavetKodu {
    const b = crypto.getRandomValues(new Uint8Array(8));
    const h = Array.from(b, x => KOD_ALFABESI[x % KOD_ALFABESI.length]).join(""); // 256, 32'ye tam bölünür: eşit dağılım.
    const kod = h.slice(0, 4) + "-" + h.slice(4);
    const simdi = simdiSn();
    const son = gun !== null && gun > 0 ? simdi + gun * 86400 : null;
    this.calistir("INSERT INTO davet_kodlari (kod, olusturan_id, kalan, olusturma, son_kullanma) VALUES (?, ?, ?, ?, ?)",
      kod, olusturan, kullanim <= 0 ? -1 : kullanim, simdi, son);
    return this.kodlar().find(x => x.kod === kod)!;
  }

  kodlar(): DavetKodu[] {
    return this.hepsi(`
      SELECT d.kod, d.kalan, (SELECT COUNT(*) FROM kullanicilar x WHERE x.davet_kodu = d.kod) AS kullanan,
             d.olusturma, d.son_kullanma, u.gorunen_ad, d.iptal
      FROM davet_kodlari d JOIN kullanicilar u ON u.id = d.olusturan_id
      ORDER BY d.olusturma DESC`).map(r => ({
      kod: r.kod as string, kalanKullanim: r.kalan as number, kullanan: r.kullanan as number, olusturma: r.olusturma as number,
      sonKullanma: r.son_kullanma as number | null, olusturan: r.gorunen_ad as string, iptal: r.iptal === 1,
    }));
  }

  kodIptal(kod: string): boolean {
    return this.calistir("UPDATE davet_kodlari SET iptal = 1 WHERE kod = ?", kodDuzelt(kod)) > 0;
  }

  kullanicilar() {
    return this.hepsi("SELECT id, kullanici_adi, gorunen_ad, profil_rengi, avatar_surum, olusturma, admin, yasakli, davet_kodu FROM kullanicilar ORDER BY id")
      .map(r => ({
        id: r.id as number, ad: r.kullanici_adi as string, gorunen: r.gorunen_ad as string, renk: r.profil_rengi as string,
        avatar: r.avatar_surum as number, kayit: r.olusturma as number, admin: r.admin === 1, yasakli: r.yasakli === 1,
        kod: r.davet_kodu as string | null,
      }));
  }

  adminMi(id: number): boolean { return this.bir("SELECT admin FROM kullanicilar WHERE id = ?", id)?.admin === 1; }

  adminAyarla(id: number, admin: boolean) { this.calistir("UPDATE kullanicilar SET admin = ? WHERE id = ?", admin ? 1 : 0, id); }

  // Yasaklayınca tüm oturumları silinir: hiçbir cihazdan devam edemez.
  yasakAyarla(id: number, yasakli: boolean) {
    this.calistir("UPDATE kullanicilar SET yasakli = ? WHERE id = ?", yasakli ? 1 : 0, id);
    if (yasakli) this.calistir("DELETE FROM oturumlar WHERE kullanici_id = ?", id);
  }

  // ---- Profil ----

  // Durum burada kullanıcının SEÇTİĞİ durumdur; çevrimiçi olup olmadığı varlık bilgisiyle birleştirilir.
  profilGetir(id: number): Profil | null {
    const r = this.bir(`SELECT ${PROFIL_SUTUNLARI} FROM kullanicilar u WHERE u.id = ?`, id);
    return r ? profilOku(r) : null;
  }

  kullaniciIdBul(kullaniciAdi: string): number | null {
    return (this.bir("SELECT id FROM kullanicilar WHERE kullanici_adi = ?", kullaniciAdi)?.id as number | undefined) ?? null;
  }

  profilGuncelle(id: number, p: ProfilGuncelleme) {
    this.calistir("UPDATE kullanicilar SET gorunen_ad = ?, hakkimda = ?, durum_mesaji = ?, profil_rengi = ? WHERE id = ?",
      p.gorunenAd.trim(), (p.hakkimda ?? "").trim(), (p.durumMesaji ?? "").trim(), p.profilRengi, id);
  }

  durumAyarla(id: number, durum: number) { this.calistir("UPDATE kullanicilar SET secilen_durum = ? WHERE id = ?", durum, id); }

  // null verilirse resim kaldırılır. Sürüm artar, böylece uygulamalar önbelleği yeniler.
  avatarAyarla(id: number, veri: ArrayBuffer | null) {
    if (veri === null) this.calistir("UPDATE kullanicilar SET avatar = NULL, avatar_surum = 0 WHERE id = ?", id);
    else this.calistir("UPDATE kullanicilar SET avatar = ?, avatar_surum = (SELECT MAX(avatar_surum) FROM kullanicilar) + 1 WHERE id = ?", veri, id);
  }

  avatarGetir(id: number): ArrayBuffer | null {
    return (this.bir("SELECT avatar FROM kullanicilar WHERE id = ?", id)?.avatar as ArrayBuffer | null | undefined) ?? null;
  }

  // ---- Arkadaşlık ----

  iliskiGetir(ben: number, o: number): Iliski {
    const r = this.bir("SELECT gonderen_id, durum FROM arkadaslik WHERE (gonderen_id = ?1 AND alici_id = ?2) OR (gonderen_id = ?2 AND alici_id = ?1)", ben, o);
    if (!r) return Iliski.Yok;
    if (r.durum === 1) return Iliski.Arkadas;
    return r.gonderen_id === ben ? Iliski.BenGonderdim : Iliski.OGonderdi;
  }

  istekEkle(gonderen: number, alici: number) {
    this.calistir("INSERT OR IGNORE INTO arkadaslik (gonderen_id, alici_id, durum, zaman) VALUES (?, ?, 0, ?)", gonderen, alici, simdiSn());
  }

  // Sadece bana gelmiş bekleyen bir istek kabul edilebilir.
  istekKabulEt(ben: number, gonderen: number): boolean {
    return this.calistir("UPDATE arkadaslik SET durum = 1, zaman = ? WHERE gonderen_id = ? AND alici_id = ? AND durum = 0", simdiSn(), gonderen, ben) > 0;
  }

  // Arkadaşlıktan çıkarma, isteği reddetme ve gönderdiğim isteği geri çekme: hepsi aynı satırı siler.
  iliskiSil(ben: number, o: number): boolean {
    return this.calistir("DELETE FROM arkadaslik WHERE (gonderen_id = ?1 AND alici_id = ?2) OR (gonderen_id = ?2 AND alici_id = ?1)", ben, o) > 0;
  }

  arkadasIdleri(ben: number): number[] {
    return this.hepsi(`SELECT CASE WHEN gonderen_id = ?1 THEN alici_id ELSE gonderen_id END AS id
      FROM arkadaslik WHERE durum = 1 AND (gonderen_id = ?1 OR alici_id = ?1)`, ben).map(r => r.id as number);
  }

  // Arkadaşlar, bana gelen ve benim gönderdiğim istekler tek sorguda.
  arkadaslariGetir(ben: number) {
    const arkadaslar: Profil[] = [], gelen: Profil[] = [], giden: Profil[] = [];
    for (const r of this.hepsi(`
      SELECT ${PROFIL_SUTUNLARI}, a.gonderen_id AS a_gonderen, a.durum AS a_durum
      FROM arkadaslik a
      JOIN kullanicilar u ON u.id = CASE WHEN a.gonderen_id = ?1 THEN a.alici_id ELSE a.gonderen_id END
      WHERE a.gonderen_id = ?1 OR a.alici_id = ?1
      ORDER BY u.gorunen_ad COLLATE NOCASE`, ben)) {
      const p = profilOku(r);
      if (r.a_durum === 1) arkadaslar.push(p);
      else if (r.a_gonderen === ben) giden.push(p);
      else gelen.push(p);
    }
    return { arkadaslar, gelen, giden };
  }

  // ---- Giriş / oturum ----

  async girisDogrula(kullaniciAdi: string, sifre: string): Promise<{ kullanici?: KullaniciBilgisi; yasakli: boolean }> {
    const r = this.bir("SELECT id, kullanici_adi, gorunen_ad, sifre_hash, yasakli FROM kullanicilar WHERE kullanici_adi = ?", kullaniciAdi);
    if (!r) {
      await Sifre.sahteDogrula(sifre); // Kullanıcı var mı yok mu, süreden anlaşılmasın.
      return { yasakli: false };
    }
    if (!(await Sifre.dogrula(sifre, r.sifre_hash as string))) return { yasakli: false };
    // Şifre doğruysa söyleriz; yanlışsa yasaklı olduğu bile anlaşılmasın.
    if (r.yasakli === 1) return { yasakli: true };
    return { kullanici: { id: r.id as number, kullaniciAdi: r.kullanici_adi as string, gorunenAd: r.gorunen_ad as string }, yasakli: false };
  }

  async oturumAc(kullaniciId: number): Promise<string> {
    const token = base64Url(crypto.getRandomValues(new Uint8Array(32)));
    this.calistir("INSERT INTO oturumlar (token_hash, kullanici_id, olusturma) VALUES (?, ?, ?)", await tokenHash(token), kullaniciId, simdiSn());
    return token;
  }

  // Yasaklı kullanıcının oturumu geçersiz sayılır.
  async oturumdanKullanici(token: string): Promise<{ kullanici: KullaniciBilgisi; admin: boolean } | null> {
    const h = await tokenHash(token);
    const r = this.bir(`SELECT u.id, u.kullanici_adi, u.gorunen_ad, u.admin
      FROM oturumlar o JOIN kullanicilar u ON u.id = o.kullanici_id WHERE o.token_hash = ? AND u.yasakli = 0`, h);
    return r ? { kullanici: { id: r.id as number, kullaniciAdi: r.kullanici_adi as string, gorunenAd: r.gorunen_ad as string }, admin: r.admin === 1 } : null;
  }

  async oturumKapat(token: string) { this.calistir("DELETE FROM oturumlar WHERE token_hash = ?", await tokenHash(token)); }
}

// ---- Yardımcılar ----

const yaziKodla = new TextEncoder();

function base64(b: Uint8Array): string { let s = ""; for (const x of b) s += String.fromCharCode(x); return btoa(s); }
function base64Coz(s: string): Uint8Array { const t = atob(s); return Uint8Array.from(t, c => c.charCodeAt(0)); }
function base64Url(b: Uint8Array): string { return base64(b).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, ""); }

// Veritabanı sızsa bile token'lar kullanılamasın diye sadece hash'i saklanır.
async function tokenHash(token: string): Promise<string> {
  const h = new Uint8Array(await crypto.subtle.digest("SHA-256", yaziKodla.encode(token)));
  return Array.from(h, x => x.toString(16).padStart(2, "0")).join("").toUpperCase();
}

export const Sifre = {
  sahteHash: null as string | null,

  async turet(sifre: string, tuz: Uint8Array, tekrar: number, uzunluk: number): Promise<Uint8Array> {
    const anahtar = await crypto.subtle.importKey("raw", yaziKodla.encode(sifre), "PBKDF2", false, ["deriveBits"]);
    return new Uint8Array(await crypto.subtle.deriveBits({ name: "PBKDF2", hash: "SHA-256", salt: tuz, iterations: tekrar }, anahtar, uzunluk * 8));
  },

  async hashle(sifre: string): Promise<string> {
    const tuz = crypto.getRandomValues(new Uint8Array(16));
    const hash = await this.turet(sifre, tuz, SIFRE_TEKRAR, 32);
    return `v1$${SIFRE_TEKRAR}$${base64(tuz)}$${base64(hash)}`;
  },

  async dogrula(sifre: string, kayitli: string): Promise<boolean> {
    const p = kayitli.split("$");
    if (p.length !== 4 || p[0] !== "v1") return false;
    const beklenen = base64Coz(p[3]);
    const hash = await this.turet(sifre, base64Coz(p[2]), Math.min(parseInt(p[1], 10), SIFRE_TEKRAR), beklenen.length);
    return hash.length === beklenen.length && crypto.subtle.timingSafeEqual(hash, beklenen);
  },

  async sahteDogrula(sifre: string) {
    this.sahteHash ??= await this.hashle("sahte-sifre");
    await this.dogrula(sifre, this.sahteHash);
  },
};
