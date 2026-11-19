// Sunucular (Discord tarzı): roller, kanallar, davet kodları, üyelik, ikon. Veri katmanı + HTTP uçları.
import { HubOlaylari, Kurallar, Profil, SunucuRolu, kanalAdKontrol, kodDuzelt, renkGecerli } from "./kurallar";
import { MesajDeposu } from "./mesajlar";
import { Baglam, HttpHata, bos, hata, json, metinAl, resimGovdesi, resimTuru, sayi, tamSayi, tamam } from "./ortak";
import { Satir, simdiSn } from "./sql";
import { KOD_ALFABESI, PROFIL_SUTUNLARI, Veritabani, profilOku } from "./veritabani";

const IKON_SURUMU = "CASE WHEN ikon IS NULL THEN 0 ELSE ikon_surum END";

export interface KanalBilgisi { id: number; sunucuId: number; ad: string; konu: string; sira: number; okunmamis: number; bahsedilme: number }

export class SunucuDeposu {
  constructor(private readonly db: Veritabani, private readonly mesajlar: MesajDeposu) {}

  private get s() { return this.db.s; }

  var(id: number): boolean { return this.s.bir("SELECT 1 FROM sunucular WHERE id = ?", id) !== null; }

  rol(sunucuId: number, uid: number): number | null { return this.mesajlar.sunucuRolu(sunucuId, uid); }

  uyeIdleri(sunucuId: number): number[] { return this.mesajlar.sunucuUyeleri(sunucuId); }

  sunucuSayisi(uid: number): number { return this.s.sayi("SELECT COUNT(*) FROM sunucu_uyeleri WHERE kullanici_id = ?", uid); }

  kanalSatirlari(ben: number, where: string, ...p: (number | string)[]): KanalBilgisi[] {
    return this.s.hepsi(`SELECT k.id, k.sunucu_id, k.ad, k.konu, k.sira, ${MesajDeposu.okunmamisSql("kanal_id", "k", ben)}
      FROM kanallar k WHERE ${where} ORDER BY k.sunucu_id, k.sira, k.id`, ...p).map(r => ({
      id: r.id as number, sunucuId: r.sunucu_id as number, ad: r.ad as string, konu: r.konu as string, sira: r.sira as number,
      okunmamis: r.okunmamis as number, bahsedilme: r.bahsetme as number,
    }));
  }

  // Sol raydaki özetler: katılma sırasına göre.
  ozetler(ben: number) {
    const sayilar = new Map<number, { o: number; b: number }>();
    for (const k of this.kanalSatirlari(ben, "k.sunucu_id IN (SELECT sunucu_id FROM sunucu_uyeleri WHERE kullanici_id = ?)", ben)) {
      const t = sayilar.get(k.sunucuId) ?? { o: 0, b: 0 };
      t.o += k.okunmamis; t.b += k.bahsedilme;
      sayilar.set(k.sunucuId, t);
    }
    return this.s.hepsi(`SELECT s.id, s.ad, s.renk, ${IKON_SURUMU} AS ikon_surum FROM sunucular s
      JOIN sunucu_uyeleri su ON su.sunucu_id = s.id AND su.kullanici_id = ? ORDER BY su.katilma, s.id`, ben).map(r => ({
      id: r.id as number, ad: r.ad as string, renk: r.renk as string, ikonSurum: r.ikon_surum as number,
      okunmamis: sayilar.get(r.id as number)?.o ?? 0, bahsedilme: sayilar.get(r.id as number)?.b ?? 0,
    }));
  }

  getir(ben: number, id: number, baskasina: (p: Profil) => Profil) {
    const r = this.s.bir(`SELECT id, ad, aciklama, renk, ${IKON_SURUMU} AS ikon_surum, herkese, sahip_id, davet_kodu FROM sunucular WHERE id = ?`, id);
    if (!r) return null;
    const rolum = this.rol(id, ben) ?? SunucuRolu.Uye;
    const uyeler = this.s.hepsi(`SELECT ${PROFIL_SUTUNLARI}, su.rol AS rol, su.katilma AS katilma FROM sunucu_uyeleri su
      JOIN kullanicilar u ON u.id = su.kullanici_id WHERE su.sunucu_id = ? ORDER BY su.rol DESC, u.gorunen_ad COLLATE NOCASE`, id)
      .map(u => ({ profil: baskasina(profilOku(u)), rol: u.rol as number, katilma: u.katilma as number }));
    return {
      id, ad: r.ad as string, aciklama: r.aciklama as string, renk: r.renk as string, ikonSurum: r.ikon_surum as number,
      herkese: r.herkese === 1, sahipId: r.sahip_id as number, davetKodu: rolum >= SunucuRolu.Yonetici ? r.davet_kodu as string : null,
      rolum, kanallar: this.kanalSatirlari(ben, "k.sunucu_id = ?", id), uyeler,
    };
  }

  private yeniKod(): string {
    for (;;) {
      const b = crypto.getRandomValues(new Uint8Array(8));
      const h = Array.from(b, x => KOD_ALFABESI[x % KOD_ALFABESI.length]).join("");
      const kod = h.slice(0, 4) + "-" + h.slice(4);
      if (!this.s.bir("SELECT 1 FROM sunucular WHERE davet_kodu = ?", kod)) return kod;
    }
  }

  yenileKod(id: number): string {
    const kod = this.yeniKod();
    this.s.calistir("UPDATE sunucular SET davet_kodu = ? WHERE id = ?", kod, id);
    return kod;
  }

  olustur(ben: number, ad: string, aciklama: string, renk: string, herkese: boolean): number {
    return this.s.islem(() => {
      const simdi = simdiSn();
      const id = this.s.bir("INSERT INTO sunucular (ad, aciklama, renk, herkese, sahip_id, davet_kodu, olusturma) VALUES (?, ?, ?, ?, ?, ?, ?) RETURNING id",
        ad, aciklama, renk, herkese ? 1 : 0, ben, this.yeniKod(), simdi)!.id as number;
      this.s.calistir("INSERT INTO sunucu_uyeleri (sunucu_id, kullanici_id, rol, katilma) VALUES (?, ?, ?, ?)", id, ben, SunucuRolu.Sahip, simdi);
      this.s.calistir("INSERT INTO kanallar (sunucu_id, ad, konu, sira, olusturma) VALUES (?, 'genel', '', 0, ?)", id, simdi);
      return id;
    });
  }

  guncelle(id: number, ad: string, aciklama: string, renk: string, herkese: boolean) {
    this.s.calistir("UPDATE sunucular SET ad = ?, aciklama = ?, renk = ?, herkese = ? WHERE id = ?", ad, aciklama, renk, herkese ? 1 : 0, id);
  }

  // Üye ekler; eski mesajlar okunmamış görünmesin diye kanallarda okuma işareti konur.
  uyeEkle(id: number, uid: number) {
    this.s.islem(() => {
      this.s.calistir("INSERT OR IGNORE INTO sunucu_uyeleri (sunucu_id, kullanici_id, rol, katilma) VALUES (?, ?, 0, ?)", id, uid, simdiSn());
      for (const r of this.s.hepsi("SELECT id FROM kanallar WHERE sunucu_id = ?", id)) this.mesajlar.okumaBaslat(uid, "kanal", r.id as number);
    });
  }

  uyeCikar(id: number, uid: number) {
    this.s.islem(() => {
      this.s.calistir("DELETE FROM sunucu_uyeleri WHERE sunucu_id = ? AND kullanici_id = ?", id, uid);
      for (const r of this.s.hepsi("SELECT id FROM kanallar WHERE sunucu_id = ?", id))
        this.s.calistir("DELETE FROM okumalar WHERE kullanici_id = ? AND konusma = ?", uid, "k:" + r.id);
    });
  }

  sil(id: number) {
    this.s.islem(() => {
      for (const r of this.s.hepsi("SELECT id FROM kanallar WHERE sunucu_id = ?", id)) this.mesajlar.konusmaTemizle("kanal", r.id as number);
      this.s.calistir("DELETE FROM kanallar WHERE sunucu_id = ?", id);
      this.s.calistir("DELETE FROM sunucu_uyeleri WHERE sunucu_id = ?", id);
      this.s.calistir("DELETE FROM sunucular WHERE id = ?", id);
    });
  }

  ikonAyarla(id: number, veri: ArrayBuffer | null) {
    this.s.calistir("UPDATE sunucular SET ikon = ?, ikon_surum = ikon_surum + 1 WHERE id = ?", veri, id);
  }

  ikonGetir(id: number): ArrayBuffer | null {
    return (this.s.bir("SELECT ikon FROM sunucular WHERE id = ?", id)?.ikon as ArrayBuffer | null | undefined) ?? null;
  }

  kanalSil(kid: number) {
    this.s.islem(() => { this.mesajlar.konusmaTemizle("kanal", kid); this.s.calistir("DELETE FROM kanallar WHERE id = ?", kid); });
  }

  kanalSatiri(id: number): Satir | null { return this.s.bir("SELECT id, sunucu_id FROM kanallar WHERE id = ?", id); }
  kanalGetir(ben: number, id: number): KanalBilgisi { return this.kanalSatirlari(ben, "k.id = ?", id)[0]; }
}

export class SunucuRotalari {
  private readonly depo: SunucuDeposu;

  constructor(private readonly b: Baglam, mesajlar: MesajDeposu) {
    this.depo = new SunucuDeposu(b.db, mesajlar);
  }

  // Yönetim gerektiren işlerde rolü döner; üye değilse 403, sunucu yoksa 404.
  private rolAl(ben: number, id: number, enAz: SunucuRolu = SunucuRolu.Uye): number {
    const r = this.depo.rol(id, ben);
    if (r === null) throw this.depo.var(id) ? new HttpHata(403, "Bu sunucunun üyesi değilsin.") : new HttpHata(404, "Sunucu bulunamadı.");
    if (r < enAz) throw new HttpHata(403, "Bunun için yetkin yok.");
    return r;
  }

  private sunucu(ben: number, id: number) {
    return json(this.depo.getir(ben, id, p => this.b.baskasina(p)));
  }

  private guncellendi(id: number) { this.b.gonder(this.depo.uyeIdleri(id), HubOlaylari.SunucuGuncellendi, id); }

  // Ad, açıklama, renk, herkese: doğrular. Hata varsa Response döner.
  private async ayar(istek: Request): Promise<{ ad: string; aciklama: string; renk: string; herkese: boolean } | Response> {
    const i = await this.b.govde(istek);
    const ad = metinAl(i.ad), aciklama = metinAl(i.aciklama);
    if (ad.length === 0) return hata(400, "Sunucu adı boş olamaz.", "ad");
    if ([...ad].length > Kurallar.SunucuAdMax) return hata(400, `Sunucu adı en fazla ${Kurallar.SunucuAdMax} karakter olabilir.`, "ad");
    if ([...aciklama].length > Kurallar.SunucuAciklamaMax) return hata(400, `Açıklama en fazla ${Kurallar.SunucuAciklamaMax} karakter olabilir.`, "aciklama");
    if (!renkGecerli(i.renk)) return hata(400, "Geçersiz renk.", "renk");
    return { ad, aciklama, renk: i.renk as string, herkese: i.herkese === true };
  }

  private katilmaKontrol(ben: number): Response | null {
    return this.depo.sunucuSayisi(ben) >= Kurallar.SunucuMax ? hata(400, `En fazla ${Kurallar.SunucuMax} sunucuya üye olabilirsin.`) : null;
  }

  private katil(ben: number, id: number): Response {
    if (this.depo.rol(id, ben) === null) {
      const h = this.katilmaKontrol(ben);
      if (h) return h;
      this.depo.uyeEkle(id, ben);
      this.b.gonder([ben], HubOlaylari.SunucularDegisti);
      this.guncellendi(id);
    }
    return this.sunucu(ben, id);
  }

  async yonlendir(istek: Request, p: string[], m: string, ben: number): Promise<Response | null> {
    // GET /api/ikon/sunucu/{id}
    if (p[0] === "ikon" && p[1] === "sunucu" && p.length === 3 && m === "GET") {
      const veri = this.depo.ikonGetir(sayi(p[2]));
      const tur = veri ? resimTuru(new Uint8Array(veri)) : null;
      if (!veri || !tur) return bos(404);
      return new Response(veri, { headers: { "Content-Type": tur, "Cache-Control": "private, max-age=86400" } });
    }

    if (p[0] === "kanallar" && p.length === 2) return this.kanalIstegi(istek, p, m, ben);
    if (p[0] !== "sunucular") return null;

    if (p.length === 1 && m === "GET") return json(this.depo.ozetler(ben));
    if (p.length === 1 && m === "POST") {
      if (!this.b.sinir("s:" + ben, 10, 60_000)) return bos(429);
      const a = await this.ayar(istek);
      if (a instanceof Response) return a;
      const h = this.katilmaKontrol(ben);
      if (h) return h;
      const id = this.depo.olustur(ben, a.ad, a.aciklama, a.renk, a.herkese);
      this.b.gonder([ben], HubOlaylari.SunucularDegisti);
      return this.sunucu(ben, id);
    }
    if (p[1] === "katil-kod" && p.length === 2 && m === "POST") {
      if (!this.b.sinir("k:" + ben, 10, 60_000)) return bos(429);
      const i = await this.b.govde(istek);
      const kod = kodDuzelt(i.kod);
      const r = this.b.db.s.bir("SELECT id FROM sunucular WHERE davet_kodu = ?", kod);
      if (!r) return hata(404, "Bu davet kodu geçersiz.", "kod");
      return this.katil(ben, r.id as number);
    }

    const id = sayi(p[1]);
    if (p.length === 2) {
      if (m === "GET") { this.rolAl(ben, id); return this.sunucu(ben, id); }
      if (m === "PUT") {
        this.rolAl(ben, id, SunucuRolu.Yonetici);
        const a = await this.ayar(istek);
        if (a instanceof Response) return a;
        this.depo.guncelle(id, a.ad, a.aciklama, a.renk, a.herkese);
        this.guncellendi(id);
        return this.sunucu(ben, id);
      }
      if (m === "DELETE") {
        this.rolAl(ben, id, SunucuRolu.Sahip);
        const uyeler = this.depo.uyeIdleri(id);
        this.depo.sil(id);
        this.b.gonder(uyeler, HubOlaylari.SunucularDegisti);
        return tamam();
      }
      return null;
    }

    const alt = p[2];
    if (alt === "ikon" && p.length === 3 && (m === "PUT" || m === "DELETE")) {
      this.rolAl(ben, id, SunucuRolu.Yonetici);
      this.depo.ikonAyarla(id, m === "PUT" ? await resimGovdesi(istek, Kurallar.AvatarMaxBayt) : null);
      this.guncellendi(id);
      return this.sunucu(ben, id);
    }
    if (alt === "kanallar" && p.length === 3 && m === "POST") {
      this.rolAl(ben, id, SunucuRolu.Yonetici);
      const i = await this.b.govde(istek);
      const hk = kanalAdKontrol(i.ad);
      if (hk) return hata(400, hk, "ad");
      const konu = metinAl(i.konu);
      if ([...konu].length > Kurallar.KanalKonuMax) return hata(400, `Konu en fazla ${Kurallar.KanalKonuMax} karakter olabilir.`, "konu");
      const s = this.b.db.s;
      if (s.sayi("SELECT COUNT(*) FROM kanallar WHERE sunucu_id = ?", id) >= Kurallar.SunucuKanalMax)
        return hata(400, `Bir sunucuda en fazla ${Kurallar.SunucuKanalMax} kanal olabilir.`);
      const kid = s.bir("INSERT INTO kanallar (sunucu_id, ad, konu, sira, olusturma) VALUES (?1, ?2, ?3, (SELECT COALESCE(MAX(sira), -1) + 1 FROM kanallar WHERE sunucu_id = ?1), ?4) RETURNING id",
        id, metinAl(i.ad), konu, simdiSn())!.id as number;
      this.guncellendi(id);
      return json(this.depo.kanalGetir(ben, kid));
    }
    if (alt === "katil" && p.length === 3 && m === "POST") {
      if (!this.b.db.s.bir("SELECT 1 FROM sunucular WHERE id = ? AND herkese = 1", id)) {
        if (this.depo.rol(id, ben) !== null) return this.sunucu(ben, id);
        return this.depo.var(id) ? hata(403, "Bu sunucuya sadece davet koduyla katılabilirsin.") : hata(404, "Sunucu bulunamadı.");
      }
      return this.katil(ben, id);
    }
    if (alt === "ayril" && p.length === 3 && m === "POST") {
      const rol = this.rolAl(ben, id);
      if (rol === SunucuRolu.Sahip) return hata(400, "Sahibi olduğun sunucudan ayrılamazsın. Önce sunucuyu sil ya da sahipliği devret.");
      this.depo.uyeCikar(id, ben);
      this.b.gonder([ben], HubOlaylari.SunucularDegisti);
      this.guncellendi(id);
      return tamam();
    }
    if (alt === "davet-kodu" && p.length === 3 && m === "POST") {
      this.rolAl(ben, id, SunucuRolu.Yonetici);
      const kod = this.depo.yenileKod(id);
      this.guncellendi(id);
      return json({ kod });
    }
    if (alt === "uyeler" && p.length >= 4) return this.uyeIstegi(istek, p, m, ben, id);
    return null;
  }

  private async uyeIstegi(istek: Request, p: string[], m: string, ben: number, id: number): Promise<Response | null> {
    const uid = sayi(p[3]);
    if (p.length === 5 && p[4] === "rol" && m === "PUT") {
      this.rolAl(ben, id, SunucuRolu.Sahip);
      const rol = tamSayi((await this.b.govde(istek)).rol);
      if (rol === null || rol < 0 || rol > 2) return hata(400, "Geçersiz rol.");
      if (uid === ben) return hata(400, "Kendi rolünü değiştiremezsin.");
      if (this.depo.rol(id, uid) === null) return hata(404, "Bu kişi sunucuda değil.");
      const s = this.b.db.s;
      s.islem(() => {
        if (rol === SunucuRolu.Sahip) { // Sahipliği devret: eski sahip yönetici olur.
          s.calistir("UPDATE sunucu_uyeleri SET rol = ? WHERE sunucu_id = ? AND kullanici_id = ?", SunucuRolu.Yonetici, id, ben);
          s.calistir("UPDATE sunucular SET sahip_id = ? WHERE id = ?", uid, id);
        }
        s.calistir("UPDATE sunucu_uyeleri SET rol = ? WHERE sunucu_id = ? AND kullanici_id = ?", rol, id, uid);
      });
      this.guncellendi(id);
      return tamam();
    }
    if (p.length === 4 && m === "DELETE") {
      const benRol = this.rolAl(ben, id, SunucuRolu.Yonetici);
      if (uid === ben) return hata(400, "Kendini atamazsın; ayrılmak için \"Sunucudan ayrıl\"ı kullan.");
      const hedef = this.depo.rol(id, uid);
      if (hedef === null) return hata(404, "Bu kişi sunucuda değil.");
      if (hedef >= benRol) return hata(403, "Bu kişiyi atmak için yetkin yok.");
      this.depo.uyeCikar(id, uid);
      this.b.gonder([uid], HubOlaylari.SunucularDegisti);
      this.guncellendi(id);
      return tamam();
    }
    return null;
  }

  private async kanalIstegi(istek: Request, p: string[], m: string, ben: number): Promise<Response | null> {
    const kid = sayi(p[1]);
    const k = this.depo.kanalSatiri(kid);
    if (!k) return hata(404, "Kanal bulunamadı.");
    const sunucuId = k.sunucu_id as number;
    this.rolAl(ben, sunucuId, SunucuRolu.Yonetici);
    const s = this.b.db.s;
    if (m === "PUT") {
      const i = await this.b.govde(istek);
      const hk = kanalAdKontrol(i.ad);
      if (hk) return hata(400, hk, "ad");
      const konu = metinAl(i.konu);
      if ([...konu].length > Kurallar.KanalKonuMax) return hata(400, `Konu en fazla ${Kurallar.KanalKonuMax} karakter olabilir.`, "konu");
      s.calistir("UPDATE kanallar SET ad = ?, konu = ? WHERE id = ?", metinAl(i.ad), konu, kid);
      this.guncellendi(sunucuId);
      return json(this.depo.kanalGetir(ben, kid));
    }
    if (m === "DELETE") {
      if (s.sayi("SELECT COUNT(*) FROM kanallar WHERE sunucu_id = ?", sunucuId) <= 1) return hata(400, "Son kanal silinemez.");
      this.depo.kanalSil(kid);
      this.guncellendi(sunucuId);
      return tamam();
    }
    return null;
  }
}
