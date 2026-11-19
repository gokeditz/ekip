// Ekip.Sunucu/Veritabani.cs'in karşılığı. Durable Object'in içindeki SQLite kullanılır (senkron, tek iş parçacığı).
import { DavetKodu, KullaniciBilgisi, Mesaj, Profil, ProfilGuncelleme, kodDuzelt } from "./kurallar";

type Satir = Record<string, SqlStorageValue>;

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

function mesajOku(r: Satir, on = ""): Mesaj {
  return {
    id: r[on + "id"] as number, gonderenId: r[on + "gonderen_id"] as number, aliciId: r[on + "alici_id"] as number,
    metin: r[on + "metin"] as string, zaman: r[on + "zaman"] as number, istemciId: null,
  };
}

const simdiSn = () => Math.floor(Date.now() / 1000);

export class Veritabani {
  constructor(private readonly sql: SqlStorage, private readonly islem: <T>(f: () => T) => T) {
    this.tablolariOlustur();
  }

  private hepsi(q: string, ...p: SqlStorageValue[]): Satir[] { return this.sql.exec(q, ...p).toArray() as Satir[]; }
  private bir(q: string, ...p: SqlStorageValue[]): Satir | null { return this.hepsi(q, ...p)[0] ?? null; }
  private calistir(q: string, ...p: SqlStorageValue[]): number { return this.sql.exec(q, ...p).rowsWritten; }

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

  // ---- Mesajlar ----

  mesajEkle(gonderen: number, alici: number, metin: string): Mesaj {
    const zaman = Date.now();
    const r = this.bir("INSERT INTO mesajlar (gonderen_id, alici_id, metin, zaman) VALUES (?, ?, ?, ?) RETURNING id", gonderen, alici, metin, zaman)!;
    return { id: r.id as number, gonderenId: gonderen, aliciId: alici, metin, zaman, istemciId: null };
  }

  // En yeni "adet" mesaj (once verilirse ondan eskiler), eskiden yeniye sıralı.
  mesajlar(ben: number, karsi: number, once: number | null, adet: number): Mesaj[] {
    return this.hepsi(`
      SELECT id, gonderen_id, alici_id, metin, zaman FROM mesajlar
      WHERE ((gonderen_id = ?1 AND alici_id = ?2) OR (gonderen_id = ?2 AND alici_id = ?1)) AND id < ?3
      ORDER BY id DESC LIMIT ?4`, ben, karsi, once ?? Number.MAX_SAFE_INTEGER, adet).map(r => mesajOku(r)).reverse();
  }

  okunduIsaretle(ben: number, karsi: number): number {
    return this.calistir("UPDATE mesajlar SET okundu = 1 WHERE gonderen_id = ? AND alici_id = ? AND okundu = 0", karsi, ben);
  }

  // Arkadaşlarımla olan sohbetler: son mesaj ve okunmamış sayısı, en yeni sohbet üstte.
  sohbetOzetleri(ben: number): { karsi: Profil; son: Mesaj; okunmamis: number }[] {
    return this.hepsi(`
      WITH konusmalar AS (
        SELECT CASE WHEN gonderen_id = ?1 THEN alici_id ELSE gonderen_id END AS karsi, MAX(id) AS son_id
        FROM mesajlar WHERE gonderen_id = ?1 OR alici_id = ?1
        GROUP BY karsi
      )
      SELECT ${PROFIL_SUTUNLARI}, m.id AS m_id, m.gonderen_id AS m_gonderen_id, m.alici_id AS m_alici_id, m.metin AS m_metin, m.zaman AS m_zaman,
             (SELECT COUNT(*) FROM mesajlar x WHERE x.gonderen_id = k.karsi AND x.alici_id = ?1 AND x.okundu = 0) AS okunmamis
      FROM konusmalar k
      JOIN mesajlar m ON m.id = k.son_id
      JOIN kullanicilar u ON u.id = k.karsi
      JOIN arkadaslik a ON a.durum = 1 AND ((a.gonderen_id = ?1 AND a.alici_id = k.karsi) OR (a.gonderen_id = k.karsi AND a.alici_id = ?1))
      ORDER BY m.id DESC`, ben).map(r => ({ karsi: profilOku(r), son: mesajOku(r, "m_"), okunmamis: r.okunmamis as number }));
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
