// Dosyalar (R2). Baytlar Durable Object'e HİÇ girmez: Worker `request.body`'yi doğrudan R2'ye akıtır, DO'ya sadece
// küçük RPC çağrıları gider (oturum, kota, kayıt). İndirme de Worker'dan, oturumsuz (anahtar tahmin edilemez).
import { Ek, Kurallar } from "./kurallar";
import { bos, hata, json } from "./ortak";
import type { Satir } from "./sql";
import { Sql } from "./sql";

export interface DosyaOrtami {
  DOSYALAR: R2Bucket;
  EKIP: DurableObjectNamespace<import("./index").EkipSunucu>;
}

// ---- Ortak yardımcılar (DO ve Worker) ----

export function ekTuru(mime: string): string {
  const m = mime.toLowerCase();
  if (m.startsWith("image/")) return "resim";
  if (m.startsWith("video/")) return "video";
  if (m.startsWith("audio/")) return "ses";
  return "dosya";
}

// Yol/kontrol karakteri yok, en fazla 120 karakter.
export function adTemizle(ad: string | null | undefined): string {
  let s = (ad ?? "").replace(/[\u0000-\u001f\u007f<>:"|?*\\/]/g, "_").trim().replace(/^\.+/, "");
  const parcalar = [...s];
  if (parcalar.length > 120) {
    // Uzantıyı koruyarak kısalt.
    const nokta = s.lastIndexOf(".");
    const uzanti = nokta > 0 && s.length - nokta <= 12 ? s.slice(nokta) : "";
    s = parcalar.slice(0, 120 - [...uzanti].length).join("") + uzanti;
  }
  return s.length === 0 ? "dosya" : s;
}

export function mimeTemizle(mime: string | null | undefined): string {
  const m = (mime ?? "").trim().toLowerCase();
  return /^[a-z0-9][\w.+-]*\/[a-z0-9][\w.+-]*$/.test(m) && m.length <= 100 ? m : "application/octet-stream";
}

export function dosyaAdresi(anahtar: string, ad: string): string {
  return `/dosya/${anahtar}/${encodeURIComponent(ad)}`;
}

export function dosyaEki(anahtar: string, ad: string, mime: string, boyut: number): Ek {
  const tur = ekTuru(mime), url = dosyaAdresi(anahtar, ad);
  return { tur, ad, mime, boyut, url, onizlemeUrl: tur === "resim" ? url : null, genislik: 0, yukseklik: 0 };
}

// ---- DO tarafı: kayıt tablosu (sadece küçük satırlar) ----

export class DosyaDeposu {
  constructor(private readonly s: Sql) {}

  // Günlük yükleme kotası: bu boyutta yeni dosya sığar mı?
  kotaVar(uid: number, boyut: number): boolean {
    const bugun = this.s.sayi("SELECT COALESCE(SUM(boyut), 0) FROM dosyalar WHERE sahip_id = ? AND zaman > ?", uid, Date.now() - 86_400_000);
    return bugun + boyut <= Kurallar.GunlukYuklemeBayt;
  }

  kaydet(uid: number, anahtar: string, ad: string, mime: string, boyut: number): Ek {
    this.s.calistir("INSERT INTO dosyalar (anahtar, sahip_id, ad, mime, boyut, zaman) VALUES (?, ?, ?, ?, ?, ?)",
      anahtar, uid, ad, mime, boyut, Date.now());
    return dosyaEki(anahtar, ad, mime, boyut);
  }

  getir(anahtar: string): { sahip: number; ad: string; mime: string; boyut: number } | null {
    const r: Satir | null = this.s.bir("SELECT sahip_id, ad, mime, boyut FROM dosyalar WHERE anahtar = ?", anahtar);
    return r ? { sahip: r.sahip_id as number, ad: r.ad as string, mime: r.mime as string, boyut: r.boyut as number } : null;
  }

  // Saklama süresi geçen kayıtları temizler (R2'deki nesneleri yaşam döngüsü kuralı siler).
  eskileriTemizle() {
    this.s.calistir("DELETE FROM dosyalar WHERE zaman < ?", Date.now() - Kurallar.DosyaSaklamaGun * 86_400_000 - 86_400_000);
  }
}

// ---- Worker tarafı: yükleme ve indirme ----

const anahtarUret = (): string => {
  const b = crypto.getRandomValues(new Uint8Array(16));
  let s = "";
  for (const x of b) s += String.fromCharCode(x);
  return btoa(s).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
};

const tokenAl = (istek: Request): string | null => {
  const b = istek.headers.get("Authorization") ?? "";
  return /^bearer /i.test(b) ? b.slice(7).trim() : null;
};

// Satır içi gösterilebilecek güvenli türler; geri kalan (html, svg, ...) indirme olarak sunulur.
const SATIR_ICI = /^(image\/(png|jpe?g|gif|webp|bmp|avif)|video\/(mp4|webm|quicktime)|audio\/(mpeg|mp3|ogg|wav|webm|aac|mp4|x-m4a|flac))$/;

function icerikIsmi(mime: string, ad: string): string {
  const ascii = ad.replace(/[^\x20-\x7e]/g, "_").replace(/["\\;]/g, "_");
  return `${SATIR_ICI.test(mime) ? "inline" : "attachment"}; filename="${ascii}"; filename*=UTF-8''${encodeURIComponent(ad)}`;
}

// POST /api/dosya?ad=&mime=  (Bearer, gövde = ham bayt, Content-Length şart)
export async function dosyaYukle(istek: Request, env: DosyaOrtami): Promise<Response> {
  const token = tokenAl(istek);
  if (!token) return bos(401);
  const stub = env.EKIP.get(env.EKIP.idFromName("ana"));
  const ben = await stub.kimlik(token);
  if (!ben) return bos(401);

  const uzunlukBaslik = istek.headers.get("Content-Length");
  const uzunluk = uzunlukBaslik !== null && /^\d+$/.test(uzunlukBaslik) ? Number(uzunlukBaslik) : -1;
  if (uzunluk < 0 || !istek.body) return hata(411, "Dosya boyutu bilinmiyor (Content-Length gerekli).");
  if (uzunluk === 0) return hata(400, "Dosya boş.");
  if (uzunluk > Kurallar.DosyaMaxBayt) return hata(413, `Dosya çok büyük (en fazla ${Math.floor(Kurallar.DosyaMaxBayt / 1024 / 1024)} MB).`);
  if (!(await stub.dosyaKotasi(ben.id, uzunluk))) return hata(413, "Günlük yükleme sınırına ulaştın. Yarın tekrar dene.");

  const url = new URL(istek.url);
  const ad = adTemizle(url.searchParams.get("ad"));
  const mime = mimeTemizle(url.searchParams.get("mime"));
  const anahtar = anahtarUret();

  const nesne = await env.DOSYALAR.put(anahtar, istek.body, {
    httpMetadata: { contentType: mime, contentDisposition: icerikIsmi(mime, ad) },
    customMetadata: { sahip: String(ben.id) },
  });
  if (nesne.size > Kurallar.DosyaMaxBayt) {
    await env.DOSYALAR.delete(anahtar);
    return hata(413, "Dosya çok büyük.");
  }
  return json(await stub.dosyaKaydet(ben.id, anahtar, ad, mime, nesne.size));
}

// GET /dosya/<anahtar>/<ad>  (oturumsuz; Range destekli)
export async function dosyaIndir(istek: Request, env: DosyaOrtami, yol: string): Promise<Response> {
  const anahtar = yol.split("/")[2] ?? "";
  if (!/^[A-Za-z0-9_-]{16,64}$/.test(anahtar)) return bos(404);

  const baslik = istek.headers.get("Range");
  let nesne: R2ObjectBody | R2Object | null;
  try {
    nesne = istek.method === "HEAD"
      ? await env.DOSYALAR.head(anahtar)
      : await env.DOSYALAR.get(anahtar, { range: istek.headers, onlyIf: istek.headers });
  } catch {
    return new Response(null, { status: 416 }); // Geçersiz aralık
  }
  if (!nesne) return bos(404);

  const basliklar = new Headers();
  nesne.writeHttpMetadata(basliklar);
  basliklar.set("ETag", nesne.httpEtag);
  basliklar.set("Accept-Ranges", "bytes");
  basliklar.set("Cache-Control", "public, max-age=31536000, immutable");
  basliklar.set("X-Content-Type-Options", "nosniff");

  if (istek.method === "HEAD") {
    basliklar.set("Content-Length", String(nesne.size));
    return new Response(null, { headers: basliklar });
  }
  if (!("body" in nesne) || !nesne.body) return new Response(null, { status: 304, headers: basliklar }); // onlyIf tuttu

  let durum = 200;
  if (baslik && nesne.range) {
    const r = nesne.range as { offset?: number; length?: number; suffix?: number };
    const basla = r.suffix !== undefined ? Math.max(0, nesne.size - r.suffix) : (r.offset ?? 0);
    const son = r.suffix !== undefined ? nesne.size - 1 : r.length !== undefined ? basla + r.length - 1 : nesne.size - 1;
    basliklar.set("Content-Range", `bytes ${basla}-${son}/${nesne.size}`);
    basliklar.set("Content-Length", String(son - basla + 1));
    durum = 206;
  } else {
    basliklar.set("Content-Length", String(nesne.size));
  }
  return new Response((nesne as R2ObjectBody).body, { status: durum, headers: basliklar });
}
