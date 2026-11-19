// Keşfet: kişileri ve herkese açık sunucuları bulma.
import { Durum } from "./kurallar";
import { Baglam, json } from "./ortak";
import { liste } from "./sql";
import { Iliski, PROFIL_SUTUNLARI, profilOku } from "./veritabani";

// LIKE'ta özel karakterleri kaçırır.
const likeKacir = (s: string) => s.replace(/[\\%_]/g, c => "\\" + c);

export class KesfetRotalari {
  constructor(private readonly b: Baglam) {}

  async yonlendir(p: string[], m: string, ben: number, q: string): Promise<Response | null> {
    if (p[0] !== "kesfet" || p.length !== 2 || m !== "GET") return null;
    if (p[1] === "kullanicilar") return json(this.kullanicilar(ben, q));
    if (p[1] === "sunucular") return json(this.sunucular(ben, q));
    return null;
  }

  private kullanicilar(ben: number, q: string) {
    const s = this.b.db.s;
    const ara = q.trim().replace(/^@+/, "");
    const kosullar: string[] = [], bag: (string | number)[] = [ben];
    if (/^#\d{1,15}$/.test(ara)) { kosullar.push("u.id = ?2"); bag.push(Number(ara.slice(1))); }
    else if (ara.length > 0) {
      let k = "(u.kullanici_adi LIKE ?2 ESCAPE '\\' OR u.gorunen_ad LIKE ?2 ESCAPE '\\'";
      bag.push("%" + likeKacir(ara) + "%");
      if (/^\d{1,15}$/.test(ara)) { k += " OR u.id = ?3"; bag.push(Number(ara)); }
      kosullar.push(k + ")");
    }
    const satirlar = s.hepsi(`SELECT ${PROFIL_SUTUNLARI}, u.olusturma AS kayit FROM kullanicilar u
      WHERE u.yasakli = 0 AND u.id != ?1 ${kosullar.map(x => "AND " + x).join(" ")}
      ORDER BY u.gorunen_ad COLLATE NOCASE LIMIT 2000`, ...bag);

    // İlişkilerim tek sorguda.
    const iliskiler = new Map<number, Iliski>();
    for (const r of s.hepsi("SELECT gonderen_id, alici_id, durum FROM arkadaslik WHERE gonderen_id = ?1 OR alici_id = ?1", ben)) {
      const o = (r.gonderen_id === ben ? r.alici_id : r.gonderen_id) as number;
      iliskiler.set(o, r.durum === 1 ? Iliski.Arkadas : r.gonderen_id === ben ? Iliski.BenGonderdim : Iliski.OGonderdi);
    }
    const sonuc = satirlar.map(r => {
      const pr = profilOku(r), iliski = iliskiler.get(pr.id) ?? Iliski.Yok;
      // Arkadaş olmayanın durumu gizli.
      const profil = iliski === Iliski.Arkadas ? this.b.baskasina(pr) : { ...pr, durum: Durum.Cevrimdisi };
      return { profil, iliski, kayit: r.kayit as number };
    });
    // Çevrimiçi arkadaşlar önce; kalanı zaten ada göre sıralı (sıralama kararlı).
    const onde = (x: { profil: { durum: number }; iliski: number }) => (x.iliski === Iliski.Arkadas && x.profil.durum !== Durum.Cevrimdisi ? 0 : 1);
    return sonuc.sort((a, c) => onde(a) - onde(c)).slice(0, 200);
  }

  private sunucular(ben: number, q: string) {
    const s = this.b.db.s;
    const ara = q.trim();
    const satirlar = s.hepsi(`SELECT id, ad, aciklama, renk, CASE WHEN ikon IS NULL THEN 0 ELSE ikon_surum END AS ikon_surum,
        (SELECT COUNT(*) FROM sunucu_uyeleri WHERE sunucu_id = sunucular.id) AS uye
      FROM sunucular WHERE herkese = 1 ${ara.length > 0 ? "AND (ad LIKE ?1 ESCAPE '\\' OR aciklama LIKE ?1 ESCAPE '\\')" : ""}
      ORDER BY uye DESC, ad COLLATE NOCASE LIMIT 100`, ...(ara.length > 0 ? ["%" + likeKacir(ara) + "%"] : []));
    const uyeler = new Map<number, number[]>();
    for (const r of s.hepsi(`SELECT sunucu_id, kullanici_id FROM sunucu_uyeleri WHERE sunucu_id IN (${liste(satirlar.map(x => x.id as number))})`)) {
      const l = uyeler.get(r.sunucu_id as number);
      if (l) l.push(r.kullanici_id as number); else uyeler.set(r.sunucu_id as number, [r.kullanici_id as number]);
    }
    return satirlar.map(r => {
      const u = uyeler.get(r.id as number) ?? [];
      return {
        id: r.id as number, ad: r.ad as string, aciklama: r.aciklama as string, renk: r.renk as string, ikonSurum: r.ikon_surum as number,
        uyeSayisi: r.uye as number, cevrimici: u.filter(x => this.b.cevrimici(x)).length, uyesin: u.includes(ben),
      };
    });
  }
}
