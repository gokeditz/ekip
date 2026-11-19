// Ekip.Ortak/Modeller.cs'in karşılığı: uygulamayla aynı kurallar ve aynı JSON biçimleri (camelCase).

export enum Durum { Cevrimdisi = 0, Cevrimici = 1, Bosta = 2, RahatsizEtmeyin = 3, Gorunmez = 4 }

export interface KullaniciBilgisi { id: number; kullaniciAdi: string; gorunenAd: string }

export interface Profil {
  id: number; kullaniciAdi: string; gorunenAd: string; hakkimda: string; durumMesaji: string;
  profilRengi: string; avatarSurum: number; durum: number;
}

export interface Mesaj { id: number; gonderenId: number; aliciId: number; metin: string; zaman: number; istemciId: string | null }

export interface DavetKodu {
  kod: string; kalanKullanim: number; kullanan: number; olusturma: number; sonKullanma: number | null; olusturan: string; iptal: boolean;
}

export interface PartiBilgisi { id: string; liderId: number; uyeler: Profil[]; oyun: string | null }
export interface PartiMesaji { gonderenId: number; gonderenAd: string; gonderenRenk: string; metin: string; zaman: number }
export interface PartiDurumu { bilgi: PartiBilgisi; mesajlar: PartiMesaji[] }

export const HubOlaylari = {
  ArkadaslarDegisti: "ArkadaslarDegisti",
  ProfilDegisti: "ProfilDegisti",
  DurumDegisti: "DurumDegisti",
  MesajGeldi: "MesajGeldi",
  Yaziyor: "Yaziyor",
  SohbetOkundu: "SohbetOkundu",
  PartiDaveti: "PartiDaveti",
  PartiGuncellendi: "PartiGuncellendi",
  PartiMesaji: "PartiMesaji",
  OturumSonlandi: "OturumSonlandi",
} as const;

export const Kurallar = {
  KullaniciAdiMin: 3,
  KullaniciAdiMax: 20,
  GorunenAdMax: 32,
  SifreMin: 6,
  SifreMax: 128,
  HakkimdaMax: 190,
  DurumMesajiMax: 60,
  AvatarMaxBayt: 512 * 1024,
  MesajMax: 2000,
  MesajSayfasi: 50,
  PartiMesajMax: 300,
  PartiUyeMax: 10,
  ProfilRenkleri: ["#7C5CFF", "#3DA9FC", "#2CB67D", "#F59E0B", "#EF4565", "#E862C9", "#14B8A6", "#F97316", "#64748B"],
};

const bosMu = (s: unknown): boolean => typeof s !== "string" || s.trim().length === 0;

export function kullaniciAdiKontrol(ad: unknown): string | null {
  if (bosMu(ad)) return "Kullanıcı adı boş olamaz.";
  const s = ad as string;
  if (s.length < Kurallar.KullaniciAdiMin || s.length > Kurallar.KullaniciAdiMax)
    return `Kullanıcı adı ${Kurallar.KullaniciAdiMin}-${Kurallar.KullaniciAdiMax} karakter olmalı.`;
  if (!/^[A-Za-z0-9_.]+$/.test(s)) return "Kullanıcı adında sadece harf, rakam, _ ve . olabilir.";
  return null;
}

export function gorunenAdKontrol(ad: unknown): string | null {
  if (bosMu(ad)) return "Görünen ad boş olamaz.";
  if ((ad as string).trim().length > Kurallar.GorunenAdMax) return `Görünen ad en fazla ${Kurallar.GorunenAdMax} karakter olabilir.`;
  return null;
}

export function sifreKontrol(sifre: unknown): string | null {
  if (typeof sifre !== "string" || sifre.length < Kurallar.SifreMin) return `Şifre en az ${Kurallar.SifreMin} karakter olmalı.`;
  if (sifre.length > Kurallar.SifreMax) return "Şifre çok uzun.";
  return null;
}

export interface ProfilGuncelleme { gorunenAd: string; hakkimda?: string | null; durumMesaji?: string | null; profilRengi: string }

export function profilKontrol(p: ProfilGuncelleme): string | null {
  return gorunenAdKontrol(p.gorunenAd)
    ?? ((p.hakkimda?.length ?? 0) > Kurallar.HakkimdaMax ? `Hakkımda en fazla ${Kurallar.HakkimdaMax} karakter olabilir.` : null)
    ?? ((p.durumMesaji?.length ?? 0) > Kurallar.DurumMesajiMax ? `Durum mesajı en fazla ${Kurallar.DurumMesajiMax} karakter olabilir.` : null)
    ?? (!Kurallar.ProfilRenkleri.includes(p.profilRengi) ? "Geçersiz profil rengi." : null);
}

// Kullanıcı "k7m3 q9xr" yazsa da "K7M3-Q9XR" olarak eşleşsin.
export function kodDuzelt(kod: unknown): string {
  const s = (typeof kod === "string" ? kod : "").toUpperCase().replace(/[^\p{L}\p{N}]/gu, "");
  return s.length === 8 ? s.slice(0, 4) + "-" + s.slice(4) : s;
}
