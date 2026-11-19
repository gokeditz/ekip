// Mesajlar: DM, sunucu kanalı ve grup için ortak. Veri katmanı (MesajDeposu) + HTTP uçları (MesajRotalari).
import { DosyaDeposu, dosyaEki, ekTuru } from "./dosyalar";
import { Ek, HubOlaylari, Kurallar, Mesaj, MesajOzeti, Profil, SunucuRolu, Tepki, gifAdresiIzinli } from "./kurallar";
import { Baglam, HttpHata, bos, hata, json, metinAl, sayi, tamSayi, tamam } from "./ortak";
import { Satir, liste } from "./sql";
import { Iliski, PROFIL_SUTUNLARI, Veritabani, profilOku } from "./veritabani";

export type Tur = "dm" | "kanal" | "grup";

// Erişimi doğrulanmış bir konuşma. DM'de id = karşı kişi; uyeler = iki taraf.
export interface Konusma {
  tur: Tur; id: number; kanalId: number | null; grupId: number | null; sunucuId: number | null; uyeler: number[];
}

const MESAJ_SUTUNLARI = "m.id, m.gonderen_id, m.alici_id, m.metin, m.zaman, m.kanal_id, m.grup_id, m.yanit_id, m.ekler, m.duzenlenme, m.silindi";

const kisalt = (s: string, n: number) => ([...s].length > n ? [...s].slice(0, n - 1).join("") + "…" : s);

function ekleriOku(json: unknown): Ek[] {
  if (typeof json !== "string" || json.length === 0) return [];
  try { const e = JSON.parse(json); return Array.isArray(e) ? e as Ek[] : []; } catch { return []; }
}

export class MesajDeposu {
  private readonly dosyalar: DosyaDeposu;

  constructor(private readonly db: Veritabani) {
    this.dosyalar = new DosyaDeposu(db.s);
  }

  private get s() { return this.db.s; }

  // ---- Konuşma erişimi ----

  sunucuUyeleri(sunucuId: number): number[] {
    return this.s.hepsi("SELECT kullanici_id FROM sunucu_uyeleri WHERE sunucu_id = ?", sunucuId).map(r => r.kullanici_id as number);
  }

  grupUyeleri(grupId: number): number[] {
    return this.s.hepsi("SELECT kullanici_id FROM grup_uyeleri WHERE grup_id = ?", grupId).map(r => r.kullanici_id as number);
  }

  // Erişim yoksa (arkadaş değil / üye değil / yok) null.
  konusmaAc(ben: number, tur: Tur, id: number): Konusma | null {
    if (tur === "dm") {
      if (id === ben || this.db.iliskiGetir(ben, id) !== Iliski.Arkadas) return null;
      return { tur, id, kanalId: null, grupId: null, sunucuId: null, uyeler: [ben, id] };
    }
    if (tur === "kanal") {
      const sunucuId = this.s.bir("SELECT sunucu_id FROM kanallar WHERE id = ?", id)?.sunucu_id as number | undefined;
      if (sunucuId === undefined) return null;
      const uyeler = this.sunucuUyeleri(sunucuId);
      return uyeler.includes(ben) ? { tur, id, kanalId: id, grupId: null, sunucuId, uyeler } : null;
    }
    const uyeler = this.grupUyeleri(id);
    return uyeler.includes(ben) ? { tur, id, kanalId: null, grupId: id, sunucuId: null, uyeler } : null;
  }

  // Bir mesajın konuşması (üyelik kontrolü yapılmadan).
  konusmaMesajdan(r: Satir): Konusma {
    if (r.kanal_id !== null) {
      const kanalId = r.kanal_id as number;
      const sunucuId = (this.s.bir("SELECT sunucu_id FROM kanallar WHERE id = ?", kanalId)?.sunucu_id as number | undefined) ?? 0;
      return { tur: "kanal", id: kanalId, kanalId, grupId: null, sunucuId, uyeler: this.sunucuUyeleri(sunucuId) };
    }
    if (r.grup_id !== null) {
      const grupId = r.grup_id as number;
      return { tur: "grup", id: grupId, kanalId: null, grupId, sunucuId: null, uyeler: this.grupUyeleri(grupId) };
    }
    const g = r.gonderen_id as number, a = r.alici_id as number;
    return { tur: "dm", id: a, kanalId: null, grupId: null, sunucuId: null, uyeler: [g, a] };
  }

  sunucuRolu(sunucuId: number, uid: number): number | null {
    const r = this.s.bir("SELECT rol FROM sunucu_uyeleri WHERE sunucu_id = ? AND kullanici_id = ?", sunucuId, uid);
    return r ? r.rol as number : null;
  }

  // Konuşmadaki mesajları süzen koşul (sayılar doğrulanmış, doğrudan yazılır).
  private kosul(ben: number, k: Konusma): string {
    if (k.tur === "kanal") return `kanal_id = ${k.id}`;
    if (k.tur === "grup") return `grup_id = ${k.id}`;
    return `kanal_id IS NULL AND grup_id IS NULL AND ((gonderen_id = ${ben} AND alici_id = ${k.id}) OR (gonderen_id = ${k.id} AND alici_id = ${ben}))`;
  }

  satir(id: number): Satir | null {
    return this.s.bir(`SELECT ${MESAJ_SUTUNLARI} FROM mesajlar m WHERE m.id = ?`, id);
  }

  // ---- Mesaj nesnesi kurma (yanıt, ekler, tepkiler, bahsetmeler dolu) ----

  private hazirla(satirlar: Satir[]): Mesaj[] {
    if (satirlar.length === 0) return [];
    const idler = satirlar.map(r => r.id as number);
    const yanitlar = new Map<number, MesajOzeti>();
    const yanitIdleri = [...new Set(satirlar.map(r => r.yanit_id).filter((x): x is number => x !== null))];
    if (yanitIdleri.length > 0)
      for (const r of this.s.hepsi(`SELECT id, gonderen_id, metin, ekler, silindi FROM mesajlar WHERE id IN (${liste(yanitIdleri)})`)) {
        const silindi = r.silindi === 1;
        yanitlar.set(r.id as number, {
          id: r.id as number, gonderenId: r.gonderen_id as number, metin: silindi ? "" : kisalt(r.metin as string, 120),
          ekVar: !silindi && ekleriOku(r.ekler).length > 0, silindi,
        });
      }
    const tepkiler = new Map<number, Map<string, number[]>>();
    for (const r of this.s.hepsi(`SELECT mesaj_id, emoji, kullanici_id FROM tepkiler WHERE mesaj_id IN (${liste(idler)}) ORDER BY zaman, kullanici_id`)) {
      let m = tepkiler.get(r.mesaj_id as number);
      if (!m) tepkiler.set(r.mesaj_id as number, m = new Map());
      const l = m.get(r.emoji as string);
      if (l) l.push(r.kullanici_id as number); else m.set(r.emoji as string, [r.kullanici_id as number]);
    }
    const bahisler = new Map<number, number[]>();
    for (const r of this.s.hepsi(`SELECT mesaj_id, kullanici_id FROM bahsetmeler WHERE mesaj_id IN (${liste(idler)})`)) {
      const l = bahisler.get(r.mesaj_id as number);
      if (l) l.push(r.kullanici_id as number); else bahisler.set(r.mesaj_id as number, [r.kullanici_id as number]);
    }
    return satirlar.map(r => {
      const silindi = r.silindi === 1, id = r.id as number;
      const t: Tepki[] = silindi ? [] : [...(tepkiler.get(id) ?? [])].map(([emoji, kullanicilar]) => ({ emoji, kullanicilar }));
      return {
        id, gonderenId: r.gonderen_id as number, aliciId: r.alici_id as number, metin: silindi ? "" : r.metin as string,
        zaman: r.zaman as number, istemciId: null, kanalId: r.kanal_id as number | null, grupId: r.grup_id as number | null,
        yanitId: r.yanit_id as number | null, yanit: silindi ? null : yanitlar.get(r.yanit_id as number) ?? null,
        ekler: silindi ? [] : ekleriOku(r.ekler), tepkiler: t, duzenlenme: r.duzenlenme as number | null, silindi,
        bahsedilenler: silindi ? [] : bahisler.get(id) ?? [],
      } satisfies Mesaj;
    });
  }

  // Verilen sırayı koruyarak mesajları kurar.
  idlerden(idler: number[]): Mesaj[] {
    if (idler.length === 0) return [];
    const harita = new Map(this.hazirla(this.s.hepsi(`SELECT ${MESAJ_SUTUNLARI} FROM mesajlar m WHERE m.id IN (${liste(idler)})`)).map(m => [m.id, m]));
    return idler.map(i => harita.get(i)).filter((m): m is Mesaj => m !== undefined);
  }

  getir(id: number): Mesaj | null { return this.idlerden([id])[0] ?? null; }

  // En yeni "adet" mesaj (once verilirse ondan eskiler), eskiden yeniye sıralı.
  liste(ben: number, k: Konusma, once: number | null, adet: number): Mesaj[] {
    const idler = this.s.hepsi(`SELECT id FROM mesajlar WHERE ${this.kosul(ben, k)} AND id < ?1 ORDER BY id DESC LIMIT ?2`,
      once ?? Number.MAX_SAFE_INTEGER, adet).map(r => r.id as number);
    return this.idlerden(idler.reverse());
  }

  // ---- Gönderme ----

  // Yanıtlanacak mesaj bu konuşmadaysa kimliği, değilse null.
  yanitGecerli(ben: number, k: Konusma, yanitId: number | null): number | null {
    if (yanitId === null) return null;
    return this.s.bir(`SELECT 1 FROM mesajlar WHERE id = ?1 AND ${this.kosul(ben, k)}`, yanitId) ? yanitId : null;
  }

  // Ek listesini doğrular; sunucudaki kayıttan yeniden kurar. Hata varsa mesaj metni döner.
  ekleriDogrula(ben: number, girdi: unknown): Ek[] | string {
    if (girdi === undefined || girdi === null) return [];
    if (!Array.isArray(girdi)) return "Ekler geçersiz.";
    if (girdi.length > Kurallar.MesajEkMax) return `Bir mesajda en fazla ${Kurallar.MesajEkMax} ek olabilir.`;
    const sonuc: Ek[] = [];
    for (const x of girdi) {
      if (x === null || typeof x !== "object") return "Ekler geçersiz.";
      const e = x as Record<string, unknown>;
      const url = typeof e.url === "string" ? e.url : "";
      const boyut = (v: unknown) => { const n = tamSayi(v); return n !== null && n >= 0 && n <= 16384 ? n : 0; };
      const dosya = /^\/dosya\/([A-Za-z0-9_-]{16,64})\//.exec(url);
      if (dosya) {
        const kayit = this.dosyalar.getir(dosya[1]);
        if (!kayit || kayit.sahip !== ben) return "Eklenen dosya bulunamadı ya da sana ait değil.";
        const ek = dosyaEki(dosya[1], kayit.ad, kayit.mime, kayit.boyut);
        ek.genislik = boyut(e.genislik); ek.yukseklik = boyut(e.yukseklik);
        sonuc.push(ek);
      } else if (e.tur === "gif") {
        const onizleme = typeof e.onizlemeUrl === "string" && e.onizlemeUrl.length > 0 ? e.onizlemeUrl : url;
        if (url.length > 1000 || onizleme.length > 1000 || !gifAdresiIzinli(url) || !gifAdresiIzinli(onizleme)) return "Bu GIF adresine izin verilmiyor.";
        const ad = typeof e.ad === "string" && e.ad.trim().length > 0 ? kisalt(e.ad.trim(), 120) : "GIF";
        sonuc.push({ tur: "gif", ad, mime: "image/gif", boyut: 0, url, onizlemeUrl: onizleme, genislik: boyut(e.genislik), yukseklik: boyut(e.yukseklik) });
      } else return "Ek adresi geçersiz.";
    }
    return sonuc;
  }

  // Metindeki @kullaniciadi ve @herkes'i konuşma üyelerine çözer (gönderen hariç).
  bahsedilenleriBul(metin: string, ben: number, uyeler: number[]): number[] {
    const adaGore = new Map<string, number>();
    for (const r of this.s.hepsi(`SELECT id, kullanici_adi FROM kullanicilar WHERE id IN (${liste(uyeler)})`))
      adaGore.set((r.kullanici_adi as string).toLowerCase(), r.id as number);
    const bulunan = new Set<number>();
    for (const e of metin.matchAll(/(?<![A-Za-z0-9_.])@([A-Za-z0-9_.]+)/g)) {
      let ad = e[1].toLowerCase();
      if (ad === "herkes") { for (const u of uyeler) bulunan.add(u); continue; }
      while (ad.length > 0) { // "@ali." gibi cümle sonu noktalarını at
        const id = adaGore.get(ad);
        if (id !== undefined) { bulunan.add(id); break; }
        if (!ad.endsWith(".")) break;
        ad = ad.slice(0, -1);
      }
    }
    bulunan.delete(ben);
    return [...bulunan];
  }

  private bahsetmeleriYaz(mesajId: number, kimler: number[]) {
    this.s.calistir("DELETE FROM bahsetmeler WHERE mesaj_id = ?", mesajId);
    for (const u of kimler) this.s.calistir("INSERT OR IGNORE INTO bahsetmeler (mesaj_id, kullanici_id) VALUES (?, ?)", mesajId, u);
  }

  ekle(ben: number, k: Konusma, metin: string, yanitId: number | null, ekler: Ek[], bahsedilenler: number[]): Mesaj {
    const id = this.s.islem(() => {
      const r = this.s.bir(`INSERT INTO mesajlar (gonderen_id, alici_id, metin, zaman, kanal_id, grup_id, yanit_id, ekler)
        VALUES (?, ?, ?, ?, ?, ?, ?, ?) RETURNING id`,
        ben, k.tur === "dm" ? k.id : 0, metin, Date.now(), k.kanalId, k.grupId, yanitId, ekler.length > 0 ? JSON.stringify(ekler) : null)!;
      const id = r.id as number;
      this.bahsetmeleriYaz(id, bahsedilenler);
      return id;
    });
    return this.getir(id)!;
  }

  duzenle(id: number, metin: string, bahsedilenler: number[]): Mesaj {
    this.s.islem(() => {
      this.s.calistir("UPDATE mesajlar SET metin = ?, duzenlenme = ? WHERE id = ?", metin, Date.now(), id);
      this.bahsetmeleriYaz(id, bahsedilenler);
    });
    return this.getir(id)!;
  }

  sil(id: number): Mesaj {
    this.s.islem(() => {
      this.s.calistir("UPDATE mesajlar SET silindi = 1, metin = '', ekler = NULL WHERE id = ?", id);
      this.s.calistir("DELETE FROM tepkiler WHERE mesaj_id = ?", id);
      this.s.calistir("DELETE FROM bahsetmeler WHERE mesaj_id = ?", id);
    });
    return this.getir(id)!;
  }

  // Aç/kapa. Mesaj başına en fazla TepkiMax farklı emoji; aşılırsa false döner.
  tepkiDegistir(id: number, uid: number, emoji: string): boolean {
    if (this.s.calistir("DELETE FROM tepkiler WHERE mesaj_id = ? AND kullanici_id = ? AND emoji = ?", id, uid, emoji) > 0) return true;
    const mevcut = this.s.sayi("SELECT COUNT(DISTINCT emoji) FROM tepkiler WHERE mesaj_id = ?", id);
    const bu = this.s.bir("SELECT 1 FROM tepkiler WHERE mesaj_id = ? AND emoji = ?", id, emoji);
    if (!bu && mevcut >= Kurallar.TepkiMax) return false;
    this.s.calistir("INSERT INTO tepkiler (mesaj_id, kullanici_id, emoji, zaman) VALUES (?, ?, ?, ?)", id, uid, emoji, Date.now());
    return true;
  }

  // ---- Okundu / okunmamış ----

  // DM: karşıdan gelen okunmamışlar okundu olur. Değişen satır sayısı döner.
  dmOkundu(ben: number, karsi: number): number {
    return this.s.calistir("UPDATE mesajlar SET okundu = 1 WHERE gonderen_id = ? AND alici_id = ? AND okundu = 0 AND kanal_id IS NULL AND grup_id IS NULL", karsi, ben);
  }

  // Kanal/grup: okuma işaretini konuşmadaki en büyük id'ye getirir.
  konusmaOkundu(ben: number, tur: "kanal" | "grup", id: number) {
    const sutun = tur === "kanal" ? "kanal_id" : "grup_id", anahtar = (tur === "kanal" ? "k:" : "g:") + id;
    this.s.calistir(`INSERT INTO okumalar (kullanici_id, konusma, son_id)
      SELECT ?1, ?2, COALESCE(MAX(id), 0) FROM mesajlar WHERE ${sutun} = ?3
      ON CONFLICT (kullanici_id, konusma) DO UPDATE SET son_id = MAX(son_id, excluded.son_id)`, ben, anahtar, id);
  }

  // Yeni üyenin eski mesajları "okunmamış" görünmesin.
  okumaBaslat(uid: number, tur: "kanal" | "grup", id: number) { this.konusmaOkundu(uid, tur, id); }

  // DM sohbet özetleri: son mesaj ve okunmamış sayısı, en yeni üstte.
  sohbetOzetleri(ben: number): { karsi: Profil; son: Mesaj; okunmamis: number }[] {
    const satirlar = this.s.hepsi(`
      WITH konusmalar AS (
        SELECT CASE WHEN gonderen_id = ?1 THEN alici_id ELSE gonderen_id END AS karsi, MAX(id) AS son_id
        FROM mesajlar WHERE kanal_id IS NULL AND grup_id IS NULL AND (gonderen_id = ?1 OR alici_id = ?1)
        GROUP BY karsi
      )
      SELECT ${PROFIL_SUTUNLARI}, k.son_id AS son_id,
             (SELECT COUNT(*) FROM mesajlar x WHERE x.gonderen_id = k.karsi AND x.alici_id = ?1 AND x.okundu = 0 AND x.silindi = 0
                AND x.kanal_id IS NULL AND x.grup_id IS NULL) AS okunmamis
      FROM konusmalar k
      JOIN kullanicilar u ON u.id = k.karsi
      JOIN arkadaslik a ON a.durum = 1 AND ((a.gonderen_id = ?1 AND a.alici_id = k.karsi) OR (a.gonderen_id = k.karsi AND a.alici_id = ?1))
      ORDER BY k.son_id DESC`, ben);
    const mesajlar = new Map(this.idlerden(satirlar.map(r => r.son_id as number)).map(m => [m.id, m]));
    return satirlar.map(r => ({ karsi: profilOku(r), son: mesajlar.get(r.son_id as number)!, okunmamis: r.okunmamis as number }));
  }

  // Kanal ve grup için okunmamış/bahsetme sayıları: SQL parçası ("k.id" ya da "g.id" ile kullanılır).
  static okunmamisSql(sutun: "kanal_id" | "grup_id", on: "k" | "g", benNo: number): string {
    const anahtar = `'${on}:' || ${on}.id`;
    const son = `COALESCE((SELECT son_id FROM okumalar o WHERE o.kullanici_id = ${benNo} AND o.konusma = ${anahtar}), 0)`;
    return `(SELECT COUNT(*) FROM mesajlar m WHERE m.${sutun} = ${on}.id AND m.id > ${son} AND m.gonderen_id != ${benNo} AND m.silindi = 0) AS okunmamis,
      (SELECT COUNT(*) FROM bahsetmeler b JOIN mesajlar m ON m.id = b.mesaj_id WHERE b.kullanici_id = ${benNo} AND m.${sutun} = ${on}.id AND m.id > ${son} AND m.silindi = 0) AS bahsetme`;
  }

  // Kanal/grup silinirken mesajlarını ve bağlı kayıtlarını temizler.
  konusmaTemizle(tur: "kanal" | "grup", id: number) {
    const sutun = tur === "kanal" ? "kanal_id" : "grup_id";
    const altSorgu = `SELECT id FROM mesajlar WHERE ${sutun} = ${Math.trunc(id)}`;
    this.s.calistir(`DELETE FROM tepkiler WHERE mesaj_id IN (${altSorgu})`);
    this.s.calistir(`DELETE FROM bahsetmeler WHERE mesaj_id IN (${altSorgu})`);
    this.s.calistir(`DELETE FROM mesajlar WHERE ${sutun} = ?`, id);
    this.s.calistir("DELETE FROM okumalar WHERE konusma = ?", (tur === "kanal" ? "k:" : "g:") + id);
  }
}

// ---- HTTP ----

export class MesajRotalari {
  constructor(private readonly b: Baglam, readonly depo: MesajDeposu) {}

  private get db() { return this.b.db; }

  private yetkisiz(tur: Tur): Response {
    return hata(403, tur === "dm" ? "Sadece arkadaşlarınla mesajlaşabilirsin." : tur === "kanal" ? "Bu kanala erişimin yok." : "Bu grubun üyesi değilsin.");
  }

  // Eski DM uçları ve yeni konuşma uçları. Eşleşmezse null.
  async yonlendir(istek: Request, url: URL, p: string[], m: string, ben: number): Promise<Response | null> {
    if (p[0] === "sohbetler" && p.length === 1 && m === "GET")
      return json(this.depo.sohbetOzetleri(ben).map(s => ({ karsi: this.b.baskasina(s.karsi), sonMesaj: s.son, okunmamis: s.okunmamis })));

    // Eski: /api/mesajlar/{karsiId}[/okundu]
    if (p[0] === "mesajlar" && p.length >= 2 && p.length <= 3) return this.konusmaIstegi(istek, url, ben, m, "dm", sayi(p[1]), p[2]);

    // Yeni: /api/konusmalar/{tur}/{id}/(mesajlar|okundu)
    if (p[0] === "konusmalar" && p.length === 4 && (p[1] === "dm" || p[1] === "kanal" || p[1] === "grup")) {
      if (p[3] === "mesajlar") return this.konusmaIstegi(istek, url, ben, m, p[1], sayi(p[2]), undefined);
      if (p[3] === "okundu") return this.konusmaIstegi(istek, url, ben, m, p[1], sayi(p[2]), "okundu");
    }

    if (p[0] === "mesaj" && p.length >= 2 && p.length <= 3) {
      const id = sayi(p[1]);
      if (p.length === 2 && m === "PUT") return this.duzenle(istek, ben, id);
      if (p.length === 2 && m === "DELETE") return this.sil(ben, id);
      if (p.length === 3 && p[2] === "tepki" && m === "POST") return this.tepki(istek, ben, id);
    }
    return null;
  }

  private async konusmaIstegi(istek: Request, url: URL, ben: number, m: string, tur: Tur, id: number, alt: string | undefined): Promise<Response | null> {
    if (alt !== undefined && alt !== "okundu") return null;
    if (alt === undefined && m === "GET") {
      const o = url.searchParams.get("once");
      if (o !== null && !/^-?\d{1,18}$/.test(o)) return bos(400);
      const k = this.depo.konusmaAc(ben, tur, id);
      if (!k) return this.yetkisiz(tur);
      return json(this.depo.liste(ben, k, o === null ? null : Number(o), Kurallar.MesajSayfasi));
    }
    if (alt === undefined && m === "POST") return this.gonder(istek, ben, tur, id);
    if (alt === "okundu" && m === "POST") {
      const k = this.depo.konusmaAc(ben, tur, id);
      if (!k) return this.yetkisiz(tur);
      if (tur === "dm") {
        if (this.depo.dmOkundu(ben, id) > 0) this.b.gonder([ben], HubOlaylari.SohbetOkundu, id);
      } else {
        this.depo.konusmaOkundu(ben, tur, id);
        this.b.gonder([ben], HubOlaylari.KonusmaOkundu, (tur === "kanal" ? "k:" : "g:") + id);
      }
      return tamam();
    }
    return null;
  }

  private metinKontrol(metin: string, ekVar: boolean): Response | null {
    if (metin.length === 0 && !ekVar) return hata(400, "Boş mesaj gönderilemez.");
    if (metin.length > Kurallar.MesajMax) return hata(400, `Mesaj en fazla ${Kurallar.MesajMax} karakter olabilir.`);
    return null;
  }

  private async gonder(istek: Request, ben: number, tur: Tur, id: number): Promise<Response> {
    if (!this.b.sinir("m:" + ben, 20, 10_000)) return bos(429);
    const i = await this.b.govde(istek);
    const metin = metinAl(i.metin);
    const ekler = this.depo.ekleriDogrula(ben, i.ekler);
    if (typeof ekler === "string") return hata(400, ekler);
    const h = this.metinKontrol(metin, ekler.length > 0);
    if (h) return h;
    const k = this.depo.konusmaAc(ben, tur, id);
    if (!k) return this.yetkisiz(tur);
    const yanitId = this.depo.yanitGecerli(ben, k, tamSayi(i.yanitId));
    const mesaj = this.depo.ekle(ben, k, metin, yanitId, ekler, this.depo.bahsedilenleriBul(metin, ben, k.uyeler));
    mesaj.istemciId = typeof i.istemciId === "string" && i.istemciId.length <= 64 ? i.istemciId : null;
    this.b.gonder(k.uyeler, HubOlaylari.MesajGeldi, mesaj); // Alıcılara ve diğer cihazlarıma.
    return json(mesaj);
  }

  // Düzenleme/silme/tepki için mesaj + konuşma; yoksa HttpHata.
  private mesajBul(id: number): { r: Satir; k: Konusma } {
    const r = this.depo.satir(id);
    if (!r) throw new HttpHata(404, "Mesaj bulunamadı.");
    return { r, k: this.depo.konusmaMesajdan(r) };
  }

  private async duzenle(istek: Request, ben: number, id: number): Promise<Response> {
    const i = await this.b.govde(istek);
    const { r, k } = this.mesajBul(id);
    if (r.silindi === 1) return hata(404, "Bu mesaj silinmiş.");
    if (r.gonderen_id !== ben || !k.uyeler.includes(ben)) return hata(403, "Sadece kendi mesajını düzenleyebilirsin.");
    const metin = metinAl(i.metin);
    const ekVar = r.ekler !== null && (r.ekler as string).length > 2;
    const h = this.metinKontrol(metin, ekVar);
    if (h) return h;
    const mesaj = this.depo.duzenle(id, metin, this.depo.bahsedilenleriBul(metin, ben, k.uyeler));
    this.b.gonder(k.uyeler, HubOlaylari.MesajGuncellendi, mesaj);
    return json(mesaj);
  }

  private sil(ben: number, id: number): Response {
    const { r, k } = this.mesajBul(id);
    if (r.silindi === 1) return tamam();
    const kendi = r.gonderen_id === ben;
    const yonetici = k.sunucuId !== null && (this.depo.sunucuRolu(k.sunucuId, ben) ?? 0) >= SunucuRolu.Yonetici;
    if (!k.uyeler.includes(ben) || !(kendi || yonetici)) return hata(403, "Bu mesajı silemezsin.");
    this.b.gonder(k.uyeler, HubOlaylari.MesajGuncellendi, this.depo.sil(id));
    return tamam();
  }

  private async tepki(istek: Request, ben: number, id: number): Promise<Response> {
    const i = await this.b.govde(istek);
    const emoji = typeof i.emoji === "string" ? i.emoji : "";
    if ([...emoji].length < 1 || [...emoji].length > 16 || /\s/u.test(emoji)) return hata(400, "Geçersiz emoji.");
    const { r, k } = this.mesajBul(id);
    if (r.silindi === 1) return hata(404, "Bu mesaj silinmiş.");
    const erisim = k.uyeler.includes(ben) && (k.tur !== "dm" || this.depo.konusmaAc(ben, "dm", k.uyeler.find(x => x !== ben) ?? 0) !== null);
    if (!erisim) return this.yetkisiz(k.tur);
    if (!this.depo.tepkiDegistir(id, ben, emoji)) return hata(400, `Bir mesaja en fazla ${Kurallar.TepkiMax} farklı emoji eklenebilir.`);
    const mesaj = this.depo.getir(id)!;
    this.b.gonder(k.uyeler, HubOlaylari.MesajGuncellendi, mesaj);
    return json(mesaj);
  }

  // Hub: "dm:5" / "k:3" / "g:2" konuşmasında yazıyorum. Diğer üyelere kendi açılarından anahtarla gider.
  konusmaYaziyor(ben: number, anahtar: string) {
    const e = /^(dm|k|g):(\d{1,15})$/.exec(anahtar);
    if (!e) return;
    const tur: Tur = e[1] === "dm" ? "dm" : e[1] === "k" ? "kanal" : "grup";
    const k = this.depo.konusmaAc(ben, tur, Number(e[2]));
    if (!k) return;
    const digerleri = k.uyeler.filter(x => x !== ben);
    this.b.gonder(digerleri, HubOlaylari.KonusmaYaziyor, tur === "dm" ? "dm:" + ben : anahtar, ben);
  }
}
