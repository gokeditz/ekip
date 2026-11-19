// HTTP yardımcıları ve modüllerin sunucu nesnesinden ihtiyaç duyduğu şeyler.
import { Profil } from "./kurallar";
import type { Veritabani } from "./veritabani";

export class HttpHata { constructor(public durum: number, public mesaj?: string, public alan?: string) {} }

export const json = (veri: unknown, durum = 200) =>
  new Response(JSON.stringify(veri), { status: durum, headers: { "Content-Type": "application/json; charset=utf-8" } });
export const hata = (durum: number, mesaj: string, alan?: string) => json({ mesaj, alan: alan ?? null }, durum);
export const tamam = () => new Response(null, { status: 200 });
export const bos = (durum: number) => new Response(null, { status: durum });

export type Govde = Record<string, unknown>;
export interface Kimlik { id: number; admin: boolean }

export function resimTuru(v: Uint8Array): string | null {
  if (v.length > 8 && v[0] === 0x89 && v[1] === 0x50 && v[2] === 0x4e && v[3] === 0x47) return "image/png";
  if (v.length > 3 && v[0] === 0xff && v[1] === 0xd8 && v[2] === 0xff) return "image/jpeg";
  return null;
}

// Sunucu nesnesinin (EkipSunucu) modüllere açtığı yetenekler.
export interface Baglam {
  db: Veritabani;
  gonder(kimlere: Iterable<number>, olay: string, ...args: unknown[]): void;
  cevrimici(id: number): boolean;
  baskasina(p: Profil): Profil;
  sinir(anahtar: string, izin: number, pencereMs: number): boolean;
  govde(istek: Request): Promise<Govde>;
}

// Yol parçasını sayıya çevirir; geçersizse 404.
export function sayi(s: string | undefined): number {
  if (!s || !/^\d{1,15}$/.test(s)) throw new HttpHata(404);
  return Number(s);
}

// Gövdeden tam sayı okur (yoksa null).
export const tamSayi = (x: unknown): number | null => (typeof x === "number" && Number.isInteger(x) ? x : null);

// Gövdeden metin okur: kırpılmış; yoksa "".
export const metinAl = (x: unknown): string => (typeof x === "string" ? x.trim() : "");

// Resim yükleme gövdesini okur (PNG/JPEG, sınırlı boyut). Geçersizse HttpHata.
export async function resimGovdesi(istek: Request, azami: number): Promise<ArrayBuffer> {
  if (Number(istek.headers.get("Content-Length") ?? "0") > azami) throw new HttpHata(400, "Resim çok büyük.");
  const veri = await istek.arrayBuffer();
  if (veri.byteLength > azami) throw new HttpHata(400, "Resim çok büyük.");
  if (!resimTuru(new Uint8Array(veri))) throw new HttpHata(400, "Sadece PNG veya JPEG yüklenebilir.");
  return veri;
}
