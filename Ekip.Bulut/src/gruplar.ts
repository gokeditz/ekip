// Gruplar (çok kişilik DM): oluşturma, özelleştirme, üyeler, ikon. Veri katmanı + HTTP uçları.
import { HubOlaylari, Kurallar, Profil, renkGecerli } from "./kurallar";
import { MesajDeposu } from "./mesajlar";
import { Baglam, HttpHata, bos, hata, json, metinAl, resimGovdesi, resimTuru, sayi, tamSayi, tamam } from "./ortak";
import { simdiSn } from "./sql";
import { Iliski, PROFIL_SUTUNLARI, Veritabani, profilOku } from "./veritabani";

const IKON_SURUMU = "CASE WHEN g.ikon IS NULL THEN 0 ELSE g.ikon_surum END";

export class GrupDeposu {
  constructor(private readonly db: Veritabani, private readonly mesajlar: MesajDeposu) {}

  private get s() { return this.db.s; }

  uyeIdleri(id: number): number[] {
    return this.s.hepsi("SELECT kullanici_id FROM grup_uyeleri WHERE grup_id = ? ORDER BY katilma, kullanici_id", id).map(r => r.kullanici_id as number);
  }

  var(id: number): boolean { return this.s.bir("SELECT 1 FROM gruplar WHERE id = ?", id) !== null; }
  uyeMi(id: number, uid: number): boolean { return this.s.bir("SELECT 1 FROM grup_uyeleri WHERE grup_id = ? AND kullanici_id = ?", id, uid) !== null; }
  kurucu(id: number): number { return (this.s.bir("SELECT kurucu_id FROM gruplar WHERE id = ?", id)?.kurucu_id as number | undefined) ?? 0; }

  // Ben'in grupları (son mesaja göre yeni üstte). tek: sadece o grup.
  liste(ben: number, baskasina: (p: Profil) => Profil, tek?: number) {
    const satirlar = this.s.hepsi(`
      SELECT g.id, g.ad, g.aciklama, g.renk, ${IKON_SURUMU} AS ikon_surum, g.kurucu_id,
             (SELECT MAX(id) FROM mesajlar WHERE grup_id = g.id) AS son_id,
             ${MesajDeposu.okunmamisSql("grup_id", "g", ben)}
      FROM gruplar g WHERE g.id IN (SELECT grup_id FROM grup_uyeleri WHERE kullanici_id = ?1) ${tek !== undefined ? "AND g.id = ?2" : ""}
      ORDER BY COALESCE(son_id, 0) DESC, g.olusturma DESC, g.id DESC`, ...(tek !== undefined ? [ben, tek] : [ben]));
    const sonlar = new Map(this.mesajlar.idlerden(satirlar.map(r => r.son_id as number | null).filter((x): x is number => x !== null)).map(m => [m.id, m]));
    return satirlar.map(r => ({
      id: r.id as number, ad: r.ad as string, aciklama: r.aciklama as string, renk: r.renk as string, ikonSurum: r.ikon_surum as number,
      kurucuId: r.kurucu_id as number, uyeler: this.uyeProfilleri(r.id as number, baskasina),
      sonMesaj: sonlar.get(r.son_id as number) ?? null, okunmamis: r.okunmamis as number, bahsedilme: r.bahsetme as number,
    }));
  }

  uyeProfilleri(id: number, baskasina: (p: Profil) => Profil): Profil[] {
    return this.s.hepsi(`SELECT ${PROFIL_SUTUNLARI} FROM grup_uyeleri gu JOIN kullanicilar u ON u.id = gu.kullanici_id
      WHERE gu.grup_id = ? ORDER BY gu.katilma, u.id`, id).map(r => baskasina(profilOku(r)));
  }

  // Ad boşsa üyelerin görünen adlarından.
  varsayilanAd(uyeler: number[]): string {
    const adlar = this.s.hepsi(`SELECT gorunen_ad FROM kullanicilar WHERE id IN (${uyeler.join(",") || "NULL"}) ORDER BY gorunen_ad COLLATE NOCASE`).map(r => r.gorunen_ad as string);
    const ad = adlar.join(", ");
    return [...ad].length > Kurallar.GrupAdMax ? [...ad].slice(0, Kurallar.GrupAdMax - 1).join("") + "…" : ad;
  }

  olustur(ben: number, ad: string, uyeler: number[]): number {
    return this.s.islem(() => {
      const simdi = simdiSn();
      const tum = [ben, ...uyeler];
      const renk = Kurallar.ProfilRenkleri[Math.floor(Math.random() * Kurallar.ProfilRenkleri.length)];
      const id = this.s.bir("INSERT INTO gruplar (ad, renk, kurucu_id, olusturma) VALUES (?, ?, ?, ?) RETURNING id",
        ad || this.varsayilanAd(tum), renk, ben, simdi)!.id as number;
      for (const u of tum) this.s.calistir("INSERT INTO grup_uyeleri (grup_id, kullanici_id, katilma) VALUES (?, ?, ?)", id, u, simdi);
      return id;
    });
  }

  uyeEkle(id: number, uid: number) {
    this.s.islem(() => {
      this.s.calistir("INSERT OR IGNORE INTO grup_uyeleri (grup_id, kullanici_id, katilma) VALUES (?, ?, ?)", id, uid, simdiSn());
      this.mesajlar.okumaBaslat(uid, "grup", id);
    });
  }

  // Çıkan kişiyi siler; kurucuysa en eski üye kurucu olur; kimse kalmazsa grup silinir. Grup silindiyse true.
  uyeCikar(id: number, uid: number): boolean {
    return this.s.islem(() => {
      this.s.calistir("DELETE FROM grup_uyeleri WHERE grup_id = ? AND kullanici_id = ?", id, uid);
      this.s.calistir("DELETE FROM okumalar WHERE kullanici_id = ? AND konusma = ?", uid, "g:" + id);
      const kalan = this.uyeIdleri(id);
      if (kalan.length === 0) {
        this.mesajlar.konusmaTemizle("grup", id);
        this.s.calistir("DELETE FROM gruplar WHERE id = ?", id);
        return true;
      }
      this.s.calistir("UPDATE gruplar SET kurucu_id = ? WHERE id = ? AND kurucu_id = ?", kalan[0], id, uid);
      return false;
    });
  }

  ikonAyarla(id: number, veri: ArrayBuffer | null) {
    this.s.calistir("UPDATE gruplar SET ikon = ?, ikon_surum = ikon_surum + 1 WHERE id = ?", veri, id);
  }

  ikonGetir(id: number): ArrayBuffer | null {
    return (this.s.bir("SELECT ikon FROM gruplar WHERE id = ?", id)?.ikon as ArrayBuffer | null | undefined) ?? null;
  }
}

export class GrupRotalari {
  private readonly depo: GrupDeposu;

  constructor(private readonly b: Baglam, mesajlar: MesajDeposu) {
    this.depo = new GrupDeposu(b.db, mesajlar);
  }

  private uyeKontrol(ben: number, id: number) {
    if (!this.depo.uyeMi(id, ben)) throw this.depo.var(id) ? new HttpHata(403, "Bu grubun üyesi değilsin.") : new HttpHata(404, "Grup bulunamadı.");
  }

  private grup(ben: number, id: number) {
    return json(this.depo.liste(ben, p => this.b.baskasina(p), id)[0]);
  }

  private degisti(uyeler: number[]) { this.b.gonder(uyeler, HubOlaylari.GruplarDegisti); }

  async yonlendir(istek: Request, p: string[], m: string, ben: number): Promise<Response | null> {
    // GET /api/ikon/grup/{id}
    if (p[0] === "ikon" && p[1] === "grup" && p.length === 3 && m === "GET") {
      const veri = this.depo.ikonGetir(sayi(p[2]));
      const tur = veri ? resimTuru(new Uint8Array(veri)) : null;
      if (!veri || !tur) return bos(404);
      return new Response(veri, { headers: { "Content-Type": tur, "Cache-Control": "private, max-age=86400" } });
    }
    if (p[0] !== "gruplar") return null;

    if (p.length === 1 && m === "GET") return json(this.depo.liste(ben, x => this.b.baskasina(x)));
    if (p.length === 1 && m === "POST") return this.olustur(istek, ben);

    const id = sayi(p[1]);
    if (p.length === 2 && m === "PUT") {
      this.uyeKontrol(ben, id);
      const i = await this.b.govde(istek);
      const aciklama = metinAl(i.aciklama);
      let ad = metinAl(i.ad);
      if ([...ad].length > Kurallar.GrupAdMax) return hata(400, `Grup adı en fazla ${Kurallar.GrupAdMax} karakter olabilir.`, "ad");
      if ([...aciklama].length > Kurallar.SunucuAciklamaMax) return hata(400, `Açıklama en fazla ${Kurallar.SunucuAciklamaMax} karakter olabilir.`, "aciklama");
      if (!renkGecerli(i.renk)) return hata(400, "Geçersiz renk.", "renk");
      if (ad.length === 0) ad = this.depo.varsayilanAd(this.depo.uyeIdleri(id));
      this.b.db.s.calistir("UPDATE gruplar SET ad = ?, aciklama = ?, renk = ? WHERE id = ?", ad, aciklama, i.renk as string, id);
      this.degisti(this.depo.uyeIdleri(id));
      return this.grup(ben, id);
    }
    if (p.length === 3 && p[2] === "ikon" && (m === "PUT" || m === "DELETE")) {
      this.uyeKontrol(ben, id);
      this.depo.ikonAyarla(id, m === "PUT" ? await resimGovdesi(istek, Kurallar.AvatarMaxBayt) : null);
      this.degisti(this.depo.uyeIdleri(id));
      return this.grup(ben, id);
    }
    if (p.length === 3 && p[2] === "uyeler" && m === "POST") return this.uyeEkle(istek, ben, id);
    if (p.length === 4 && p[2] === "uyeler" && m === "DELETE") return this.uyeCikar(ben, id, sayi(p[3]));
    return null;
  }

  private async olustur(istek: Request, ben: number): Promise<Response> {
    if (!this.b.sinir("g:" + ben, 10, 60_000)) return bos(429);
    const i = await this.b.govde(istek);
    const ad = metinAl(i.ad);
    if ([...ad].length > Kurallar.GrupAdMax) return hata(400, `Grup adı en fazla ${Kurallar.GrupAdMax} karakter olabilir.`, "ad");
    if (!Array.isArray(i.uyeler)) return bos(400);
    const uyeler = [...new Set(i.uyeler.map(tamSayi))].filter(x => x !== ben);
    if (uyeler.some(x => x === null)) return bos(400);
    const idler = uyeler as number[];
    if (idler.length < 1) return hata(400, "Gruba en az bir arkadaşını eklemelisin.");
    if (idler.length + 1 > Kurallar.GrupUyeMax) return hata(400, `Bir grupta en fazla ${Kurallar.GrupUyeMax} kişi olabilir.`);
    if (idler.some(x => this.b.db.iliskiGetir(ben, x) !== Iliski.Arkadas)) return hata(400, "Gruba sadece arkadaşlarını ekleyebilirsin.");
    const id = this.depo.olustur(ben, ad, idler);
    this.degisti([ben, ...idler]);
    return this.grup(ben, id);
  }

  private async uyeEkle(istek: Request, ben: number, id: number): Promise<Response> {
    this.uyeKontrol(ben, id);
    const yeni = tamSayi((await this.b.govde(istek)).id);
    if (yeni === null) return bos(400);
    if (this.depo.uyeMi(id, yeni)) return hata(409, "Bu kişi zaten grupta.");
    if (this.b.db.iliskiGetir(ben, yeni) !== Iliski.Arkadas) return hata(400, "Gruba sadece arkadaşlarını ekleyebilirsin.");
    if (this.depo.uyeIdleri(id).length >= Kurallar.GrupUyeMax) return hata(400, `Bir grupta en fazla ${Kurallar.GrupUyeMax} kişi olabilir.`);
    this.depo.uyeEkle(id, yeni);
    this.degisti(this.depo.uyeIdleri(id));
    return this.grup(ben, id);
  }

  private uyeCikar(ben: number, id: number, uid: number): Response {
    this.uyeKontrol(ben, id);
    if (uid !== ben && this.depo.kurucu(id) !== ben) return hata(403, "Başkasını sadece grubun kurucusu çıkarabilir.");
    if (!this.depo.uyeMi(id, uid)) return hata(404, "Bu kişi grupta değil.");
    const eskiler = this.depo.uyeIdleri(id);
    this.depo.uyeCikar(id, uid);
    this.degisti(eskiler); // Çıkan kişi de dahil.
    return tamam();
  }
}
