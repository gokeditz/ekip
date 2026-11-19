// Ekip.Sunucu/Parti.cs'in karşılığı. Partiler geçici oyun gruplarıdır; Durable Object uykuya geçince bellek
// silindiği için SQLite'ta tutulur. Hiç bağlı kimse kalmayınca (ör. sunucu yeniden başladı) hepsi silinir.
import { Kurallar, PartiBilgisi, PartiDurumu, PartiMesaji, Profil } from "./kurallar";
import { Iliski, Veritabani } from "./veritabani";

export class PartiYoneticisi {
  constructor(
    private readonly sql: SqlStorage,
    private readonly db: Veritabani,
    private readonly cevrimici: (id: number) => boolean,
    private readonly baskasina: (p: Profil) => Profil,
  ) {
    sql.exec(`
      CREATE TABLE IF NOT EXISTS partiler (id TEXT PRIMARY KEY, lider INTEGER NOT NULL, oyun TEXT);
      CREATE TABLE IF NOT EXISTS parti_uyeleri (sira INTEGER PRIMARY KEY AUTOINCREMENT, parti_id TEXT NOT NULL, uye INTEGER NOT NULL UNIQUE);
      CREATE TABLE IF NOT EXISTS parti_davetleri (parti_id TEXT NOT NULL, uye INTEGER NOT NULL, PRIMARY KEY (parti_id, uye));
      CREATE TABLE IF NOT EXISTS parti_mesajlari (id INTEGER PRIMARY KEY AUTOINCREMENT, parti_id TEXT NOT NULL,
        gonderen_id INTEGER NOT NULL, ad TEXT NOT NULL, renk TEXT NOT NULL, metin TEXT NOT NULL, zaman INTEGER NOT NULL);
    `);
  }

  hepsiniSil() {
    this.sql.exec("DELETE FROM partiler; DELETE FROM parti_uyeleri; DELETE FROM parti_davetleri; DELETE FROM parti_mesajlari;");
  }

  private partisi(uye: number): string | null {
    return (this.sql.exec("SELECT parti_id FROM parti_uyeleri WHERE uye = ?", uye).toArray()[0]?.parti_id as string | undefined) ?? null;
  }

  private uyeler(partiId: string): number[] {
    return this.sql.exec("SELECT uye FROM parti_uyeleri WHERE parti_id = ? ORDER BY sira", partiId).toArray().map(r => r.uye as number);
  }

  durum(ben: number): PartiDurumu | null {
    const p = this.partisi(ben);
    return p ? this.durumYap(p) : null;
  }

  kur(ben: number): PartiDurumu {
    const mevcut = this.partisi(ben);
    if (mevcut) return this.durumYap(mevcut);
    const id = crypto.randomUUID().replace(/-/g, "").slice(0, 12);
    this.sql.exec("INSERT INTO partiler (id, lider) VALUES (?, ?)", id, ben);
    this.sql.exec("INSERT INTO parti_uyeleri (parti_id, uye) VALUES (?, ?)", id, ben);
    return this.durumYap(id);
  }

  // Hata varsa mesajını, yoksa parti kimliğini döner.
  davetEt(ben: number, kimi: number): { hata?: string; partiId?: string } {
    const p = this.partisi(ben);
    if (!p) return { hata: "Önce bir parti kur." };
    if (this.db.iliskiGetir(ben, kimi) !== Iliski.Arkadas) return { hata: "Sadece arkadaşlarını davet edebilirsin." };
    if (!this.cevrimici(kimi)) return { hata: "Bu arkadaşın şu an çevrimdışı." };
    const uyeler = this.uyeler(p);
    if (uyeler.includes(kimi)) return { hata: "Zaten partide." };
    if (uyeler.length >= Kurallar.PartiUyeMax) return { hata: "Parti dolu." };
    this.sql.exec("INSERT OR IGNORE INTO parti_davetleri (parti_id, uye) VALUES (?, ?)", p, kimi);
    return { partiId: p };
  }

  // Katılınca eski partisinden çıkar. (yeni durum, eski partinin kalan üyeleri)
  katil(ben: number, partiId: string): { durum: PartiDurumu | null; eskiUyeler: number[] } {
    const davet = this.sql.exec("DELETE FROM parti_davetleri WHERE parti_id = ? AND uye = ?", partiId, ben).rowsWritten > 0;
    const var_ = this.sql.exec("SELECT 1 FROM partiler WHERE id = ?", partiId).toArray().length > 0;
    if (!davet || !var_) return { durum: null, eskiUyeler: [] };
    if (this.uyeler(partiId).length >= Kurallar.PartiUyeMax) return { durum: null, eskiUyeler: [] };
    const eski = this.ayril(ben);
    this.sql.exec("INSERT INTO parti_uyeleri (parti_id, uye) VALUES (?, ?)", partiId, ben);
    return { durum: this.durumYap(partiId), eskiUyeler: eski };
  }

  reddet(ben: number, partiId: string) {
    this.sql.exec("DELETE FROM parti_davetleri WHERE parti_id = ? AND uye = ?", partiId, ben);
  }

  // Ayrılınca kalan üyeler döner (onlara güncelleme gidecek).
  ayril(ben: number): number[] {
    const p = this.partisi(ben);
    if (!p) return [];
    this.sql.exec("DELETE FROM parti_uyeleri WHERE uye = ?", ben);
    const kalan = this.uyeler(p);
    if (kalan.length === 0) {
      for (const t of ["partiler WHERE id", "parti_davetleri WHERE parti_id", "parti_mesajlari WHERE parti_id"])
        this.sql.exec(`DELETE FROM ${t} = ?`, p);
      return [];
    }
    this.sql.exec("UPDATE partiler SET lider = ? WHERE id = ? AND lider = ?", kalan[0], p, ben);
    return kalan;
  }

  oyunSec(ben: number, oyun: string | null): PartiBilgisi | null {
    const p = this.partisi(ben);
    if (!p) return null;
    const t = (oyun ?? "").trim();
    this.sql.exec("UPDATE partiler SET oyun = ? WHERE id = ?", t.length === 0 ? null : t.slice(0, 200), p);
    return this.bilgi(p);
  }

  mesajEkle(ben: number, metinGirdi: unknown): { mesaj: PartiMesaji | null; alicilar: number[] } {
    const metin = (typeof metinGirdi === "string" ? metinGirdi : "").trim();
    if (metin.length === 0 || metin.length > Kurallar.PartiMesajMax) return { mesaj: null, alicilar: [] };
    const profil = this.db.profilGetir(ben);
    const p = this.partisi(ben);
    if (!profil || !p) return { mesaj: null, alicilar: [] };
    const m: PartiMesaji = { gonderenId: ben, gonderenAd: profil.gorunenAd, gonderenRenk: profil.profilRengi, metin, zaman: Date.now() };
    this.sql.exec("INSERT INTO parti_mesajlari (parti_id, gonderen_id, ad, renk, metin, zaman) VALUES (?, ?, ?, ?, ?, ?)",
      p, ben, m.gonderenAd, m.gonderenRenk, metin, m.zaman);
    // Son 50 mesaj kalsın.
    this.sql.exec(`DELETE FROM parti_mesajlari WHERE parti_id = ?1 AND id NOT IN
      (SELECT id FROM parti_mesajlari WHERE parti_id = ?1 ORDER BY id DESC LIMIT 50)`, p);
    return { mesaj: m, alicilar: this.uyeler(p) };
  }

  bilgisi(uye: number): PartiBilgisi | null {
    const p = this.partisi(uye);
    return p ? this.bilgi(p) : null;
  }

  private durumYap(p: string): PartiDurumu {
    const mesajlar = this.sql.exec("SELECT gonderen_id, ad, renk, metin, zaman FROM parti_mesajlari WHERE parti_id = ? ORDER BY id", p)
      .toArray().map(r => ({
        gonderenId: r.gonderen_id as number, gonderenAd: r.ad as string, gonderenRenk: r.renk as string,
        metin: r.metin as string, zaman: r.zaman as number,
      }));
    return { bilgi: this.bilgi(p), mesajlar };
  }

  private bilgi(p: string): PartiBilgisi {
    const r = this.sql.exec("SELECT lider, oyun FROM partiler WHERE id = ?", p).toArray()[0];
    const uyeler = this.uyeler(p).map(id => this.db.profilGetir(id)).filter((x): x is Profil => x !== null).map(this.baskasina);
    return { id: p, liderId: r.lider as number, uyeler, oyun: (r.oyun as string | null) ?? null };
  }
}
