// GIF aracısı: uygulama anahtarı bilmez; Worker ayarlı sağlayıcıya (giphy | klipy | tenor) sorar ve sonucu
// tek biçime (GifSayfasi) çevirir. Sonuçlar ~5 dakika kenarda önbelleklenir.
import { bos, hata, json } from "./ortak";

export interface GifSonucu { id: string; baslik: string; url: string; onizlemeUrl: string; genislik: number; yukseklik: number }
export interface GifSayfasi { sonuclar: GifSonucu[]; sonraki: string | null }

export interface GifOrtami {
  GIF_SAGLAYICI?: string;
  GIF_ANAHTARI?: string;
  EKIP: DurableObjectNamespace<import("./index").EkipSunucu>;
}

const SAYFA = 24;

type Ham = Record<string, any>;
const sayiAl = (x: unknown): number => { const n = Number(x); return Number.isFinite(n) ? Math.round(n) : 0; };

// Sağlayıcıya özgü adres ve ayrıştırma. sonraki: bir önceki sayfanın verdiği değer.
interface Saglayici {
  adres(anahtar: string, q: string, sonraki: string, kimlik: string): string;
  coz(veri: Ham, sonraki: string): GifSayfasi;
}

// GIPHY v1: images.fixed_width_small (küçük, animasyonlu GIF), images.downsized / fixed_width (orta boy GIF).
const giphy: Saglayici = {
  adres(anahtar, q, sonraki) {
    const u = new URL("https://api.giphy.com/v1/gifs/" + (q ? "search" : "trending"));
    u.searchParams.set("api_key", anahtar);
    if (q) u.searchParams.set("q", q);
    u.searchParams.set("limit", String(SAYFA));
    u.searchParams.set("offset", String(Number(sonraki) || 0));
    u.searchParams.set("rating", "pg-13");
    if (q) u.searchParams.set("lang", "tr");
    return u.toString();
  },
  coz(veri, sonraki) {
    const sonuclar: GifSonucu[] = [];
    for (const g of (veri.data ?? []) as Ham[]) {
      const im = g.images ?? {};
      const orta = im.downsized ?? im.fixed_width ?? im.fixed_height, kucuk = im.fixed_width_small ?? im.fixed_width ?? orta;
      if (!orta?.url || !kucuk?.url) continue;
      sonuclar.push({ id: String(g.id), baslik: String(g.title ?? ""), url: orta.url, onizlemeUrl: kucuk.url, genislik: sayiAl(orta.width), yukseklik: sayiAl(orta.height) });
    }
    const p = veri.pagination ?? {}, sonrakiKonum = sayiAl(p.offset) + sayiAl(p.count);
    return { sonuclar, sonraki: sonuclar.length > 0 && sonrakiKonum < sayiAl(p.total_count) ? String(sonrakiKonum) : null };
  },
};

// KLIPY v1: /api/v1/{anahtar}/gifs/(search|trending); sonuç: data.data[].file.{hd,md,sm,xs}.gif.{url,width,height}; data.has_next.
const klipy: Saglayici = {
  adres(anahtar, q, sonraki, kimlik) {
    const u = new URL(`https://api.klipy.com/api/v1/${encodeURIComponent(anahtar)}/gifs/${q ? "search" : "trending"}`);
    if (q) u.searchParams.set("q", q);
    u.searchParams.set("page", String(Number(sonraki) || 1));
    u.searchParams.set("per_page", String(SAYFA));
    u.searchParams.set("customer_id", kimlik);
    u.searchParams.set("locale", "tr");
    return u.toString();
  },
  coz(veri, sonraki) {
    const d = veri.data ?? {};
    const sonuclar: GifSonucu[] = [];
    for (const g of (d.data ?? []) as Ham[]) {
      const f = g.file ?? {};
      const orta = f.md?.gif ?? f.hd?.gif ?? f.sm?.gif, kucuk = f.sm?.gif ?? f.xs?.gif ?? orta;
      if (!orta?.url || !kucuk?.url) continue;
      sonuclar.push({ id: String(g.id ?? g.slug), baslik: String(g.title ?? ""), url: orta.url, onizlemeUrl: kucuk.url, genislik: sayiAl(orta.width), yukseklik: sayiAl(orta.height) });
    }
    return { sonuclar, sonraki: d.has_next === true && sonuclar.length > 0 ? String((Number(sonraki) || 1) + 1) : null };
  },
};

// Tenor v2: /search ve /featured; results[].media_formats.{tinygif,mediumgif,gif}.{url,dims:[g,y]}; next ("" = son sayfa).
const tenor: Saglayici = {
  adres(anahtar, q, sonraki) {
    const u = new URL("https://tenor.googleapis.com/v2/" + (q ? "search" : "featured"));
    u.searchParams.set("key", anahtar);
    u.searchParams.set("client_key", "ekip");
    if (q) u.searchParams.set("q", q);
    u.searchParams.set("limit", String(SAYFA));
    if (sonraki) u.searchParams.set("pos", sonraki);
    u.searchParams.set("contentfilter", "medium");
    u.searchParams.set("media_filter", "tinygif,mediumgif,gif");
    u.searchParams.set("locale", "tr_TR");
    return u.toString();
  },
  coz(veri) {
    const sonuclar: GifSonucu[] = [];
    for (const g of (veri.results ?? []) as Ham[]) {
      const mf = g.media_formats ?? {};
      const orta = mf.mediumgif ?? mf.gif, kucuk = mf.tinygif ?? orta;
      if (!orta?.url || !kucuk?.url) continue;
      sonuclar.push({
        id: String(g.id), baslik: String(g.content_description ?? g.title ?? ""), url: orta.url, onizlemeUrl: kucuk.url,
        genislik: sayiAl(orta.dims?.[0]), yukseklik: sayiAl(orta.dims?.[1]),
      });
    }
    return { sonuclar, sonraki: typeof veri.next === "string" && veri.next.length > 0 && sonuclar.length > 0 ? veri.next : null };
  },
};

const saglayicilar: Record<string, Saglayici> = { giphy, klipy, tenor };

function tokenAl(istek: Request): string | null {
  const b = istek.headers.get("Authorization") ?? "";
  return /^bearer /i.test(b) ? b.slice(7).trim() : null;
}

// GET /api/gif?q=&sonraki=
export async function gifAra(istek: Request, env: GifOrtami, ctx: ExecutionContext): Promise<Response> {
  const token = tokenAl(istek);
  const ben = token ? await env.EKIP.get(env.EKIP.idFromName("ana")).kimlik(token) : null;
  if (!ben) return bos(401);

  const anahtar = env.GIF_ANAHTARI?.trim();
  if (!anahtar) return hata(503, "GIF servisi henüz ayarlanmadı.");
  const ad = (env.GIF_SAGLAYICI ?? "giphy").trim().toLowerCase();
  const sag = saglayicilar[ad];
  if (!sag) return hata(503, "GIF servisi henüz ayarlanmadı.");

  const url = new URL(istek.url);
  const q = (url.searchParams.get("q") ?? "").trim().slice(0, 100);
  const sonraki = url.searchParams.get("sonraki") ?? "";
  if (sonraki.length > 200) return bos(400);

  // Önbellek anahtarı anahtarı içermez; sağlayıcı + arama + sayfa.
  const onbellek = caches.default;
  const onbellekAnahtari = new Request(`https://gif-onbellek.ekip.invalid/${ad}?q=${encodeURIComponent(q)}&s=${encodeURIComponent(sonraki)}`);
  const bulunan = await onbellek.match(onbellekAnahtari);
  if (bulunan) return bulunan;

  let sayfa: GifSayfasi;
  try {
    const yanit = await fetch(sag.adres(anahtar, q, sonraki, String(ben.id)), { headers: { Accept: "application/json" } });
    if (!yanit.ok) return hata(502, "GIF servisi şu an cevap vermiyor.");
    sayfa = sag.coz(await yanit.json() as Ham, sonraki);
  } catch {
    return hata(502, "GIF servisi şu an cevap vermiyor.");
  }
  const sonuc = json(sayfa);
  sonuc.headers.set("Cache-Control", "private, max-age=300");
  const kopya = sonuc.clone();
  kopya.headers.set("Cache-Control", "public, max-age=300");
  ctx.waitUntil(onbellek.put(onbellekAnahtari, kopya));
  return sonuc;
}
