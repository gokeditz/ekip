// Ekip sunucusu, Cloudflare üzerinde. Ekip.Sunucu (C#) ile birebir aynı HTTP uçlarını sunar ve uygulamadaki
// SignalR istemcisinin konuştuğu "JSON hub protokolü"nü taklit eder; böylece uygulamada sunucu adresi dışında
// hiçbir şey değişmez.
//
// Bütün veri ve anlık bağlantılar tek bir Durable Object'te (EkipSunucu) durur: içinde SQLite var, tek iş
// parçacığında çalışır (kilit gerekmez). Bağlantılar "hibernation" ile tutulur: kimse bir şey yazmazken nesne
// uyur, ücret/kota harcanmaz; mesaj gelince uyanır.
import { DurableObject } from "cloudflare:workers";
import { DosyaDeposu, dosyaIndir, dosyaYukle } from "./dosyalar";
import { gifAra } from "./gif";
import { GrupRotalari } from "./gruplar";
import { KesfetRotalari } from "./kesfet";
import {
  Durum, Ek as DosyaEki, HubOlaylari, Kurallar, Profil, ProfilGuncelleme,
  gorunenAdKontrol, kullaniciAdiKontrol, profilKontrol, sifreKontrol,
} from "./kurallar";
import { MesajRotalari, MesajDeposu } from "./mesajlar";
import { Baglam, Govde, HttpHata, Kimlik, bos, hata, json, resimTuru, sayi, tamam } from "./ortak";
import { PartiYoneticisi } from "./parti";
import { SunucuRotalari } from "./sunucular";
import { Iliski, Veritabani } from "./veritabani";

export interface Env {
  EKIP: DurableObjectNamespace<EkipSunucu>;
  DOSYALAR: R2Bucket;          // Yüklenen dosyalar (R2); baytlar Durable Object'e girmez
  HESAP_LIMITI?: string;
  GIF_SAGLAYICI?: string;      // giphy | klipy | tenor (varsayılan giphy)
  GIF_ANAHTARI?: string;       // gizli değişken: npx wrangler secret put GIF_ANAHTARI
}

// Giriş noktası. Dosya yükleme/indirme ve GIF aracılığı burada (Worker'da) yapılır, Durable Object yorulmaz;
// geri kalan her şey tek sunucu nesnesine gider.
export default {
  async fetch(istek: Request, env: Env, ctx: ExecutionContext): Promise<Response> {
    const yol = new URL(istek.url).pathname;
    if (yol === "/api/dosya" && istek.method === "POST") return dosyaYukle(istek, env);
    if (yol.startsWith("/dosya/") && (istek.method === "GET" || istek.method === "HEAD")) return dosyaIndir(istek, env, yol);
    if (yol === "/api/gif" && istek.method === "GET") return gifAra(istek, env, ctx);
    return env.EKIP.get(env.EKIP.idFromName("ana")).fetch(istek);
  },
} satisfies ExportedHandler<Env>;

const RS = "\x1e"; // SignalR mesaj ayırıcı
const PING = '{"type":6}' + RS;

interface Ek { uid: number; el: boolean } // Bağlantıya iliştirilen bilgi (uykudan sonra da kalır). el: el sıkışma bitti mi

export class EkipSunucu extends DurableObject<Env> {
  private readonly db: Veritabani;
  private readonly parti: PartiYoneticisi;
  private readonly limitler = new Map<string, { bas: number; n: number }>();
  private readonly mesajlar: MesajRotalari;
  private readonly sunucular: SunucuRotalari;
  private readonly gruplar: GrupRotalari;
  private readonly kesfet: KesfetRotalari;
  private readonly dosyalar: DosyaDeposu;

  constructor(ctx: DurableObjectState, env: Env) {
    super(ctx, env);
    this.db = new Veritabani(ctx.storage.sql, f => ctx.storage.transactionSync(f));
    const baglam: Baglam = {
      db: this.db,
      gonder: (k, o, ...a) => this.gonder(k, o, ...a),
      cevrimici: id => this.cevrimici(id),
      baskasina: p => this.baskasina(p),
      sinir: (a, i, p) => this.sinir(a, i, p),
      govde: i => this.govde(i),
    };
    const mesajDeposu = new MesajDeposu(this.db);
    this.mesajlar = new MesajRotalari(baglam, mesajDeposu);
    this.sunucular = new SunucuRotalari(baglam, mesajDeposu);
    this.gruplar = new GrupRotalari(baglam, mesajDeposu);
    this.kesfet = new KesfetRotalari(baglam);
    this.dosyalar = new DosyaDeposu(this.db.s);
    this.parti = new PartiYoneticisi(ctx.storage.sql, this.db, id => this.cevrimici(id), p => this.baskasina(p));
    // Kimse bağlı değilse (ilk açılış ya da yeniden başlatma) eski partiler geçersizdir.
    if (ctx.getWebSockets().length === 0) this.parti.hepsiniSil();
    // Uygulamanın 15 saniyede bir gönderdiği ping'e nesneyi uyandırmadan cevap ver.
    ctx.setWebSocketAutoResponse(new WebSocketRequestResponsePair(PING, PING));
  }

  // ---- Varlık (kim çevrimiçi) ----

  private soketler(uid: number, haric?: WebSocket): WebSocket[] {
    return this.ctx.getWebSockets("u" + uid).filter(ws =>
      ws !== haric && ws.readyState === WebSocket.OPEN && (ws.deserializeAttachment() as Ek | null)?.el === true);
  }

  private cevrimici(id: number): boolean { return this.soketler(id).length > 0; }

  // Başkalarının göreceği durum: bağlı değilse ya da görünmezse çevrimdışı.
  private gorunen(id: number, secilen: number): number {
    return !this.cevrimici(id) || secilen === Durum.Gorunmez ? Durum.Cevrimdisi : secilen;
  }

  private baskasina(p: Profil): Profil { return { ...p, durum: this.gorunen(p.id, p.durum) }; }

  // ---- Bildirim (sunucudan uygulamalara olay) ----

  private gonder(kimlere: Iterable<number>, olay: string, ...args: unknown[]) {
    const cerceve = JSON.stringify({ type: 1, target: olay, arguments: args }) + RS;
    for (const uid of new Set(kimlere))
      for (const ws of this.soketler(uid)) { try { ws.send(cerceve); } catch { /* kapanıyor */ } }
  }

  private durumYay(id: number, durum: number) { this.gonder(this.db.gorenler(id), HubOlaylari.DurumDegisti, id, durum); }

  private profilDegisti(id: number) {
    const p = this.db.profilGetir(id);
    if (!p) return;
    this.gonder(this.db.gorenler(id), HubOlaylari.ProfilDegisti, this.baskasina(p));
    this.gonder([id], HubOlaylari.ProfilDegisti, p);
  }

  private partiGuncellemesiGonder(uyeler: number[]) {
    if (uyeler.length === 0) return;
    this.gonder(uyeler, HubOlaylari.PartiGuncellendi, this.parti.bilgisi(uyeler[0]));
  }

  // ---- Deneme sınırı (spam/şifre deneme) ----

  private sinir(anahtar: string, izin: number, pencereMs: number): boolean {
    const simdi = Date.now();
    const k = this.limitler.get(anahtar);
    if (!k || simdi - k.bas >= pencereMs) { this.limitler.set(anahtar, { bas: simdi, n: 1 }); return true; }
    if (k.n >= izin) return false;
    k.n++;
    if (this.limitler.size > 5000) this.limitler.clear(); // Bellek şişmesin.
    return true;
  }

  // ---- HTTP ----

  async fetch(istek: Request): Promise<Response> {
    try {
      return await this.yonlendir(istek, new URL(istek.url));
    } catch (e) {
      if (e instanceof HttpHata) return e.mesaj ? hata(e.durum, e.mesaj, e.alan) : bos(e.durum);
      console.error(e);
      return hata(500, "Sunucuda bir sorun oldu.");
    }
  }

  private tokenAl(istek: Request, url: URL): string | null {
    const b = istek.headers.get("Authorization") ?? "";
    if (/^bearer /i.test(b)) return b.slice(7).trim();
    if (url.pathname.startsWith("/hub")) return url.searchParams.get("access_token");
    return null;
  }

  private async kimlikIstekten(istek: Request, url: URL): Promise<Kimlik | null> {
    const t = this.tokenAl(istek, url);
    return t ? this.kimlik(t) : null;
  }

  // ---- RPC (Worker'dan çağrılır; dosya baytları buraya hiç gelmez) ----

  async kimlik(token: string): Promise<Kimlik | null> {
    const o = await this.db.oturumdanKullanici(token);
    return o ? { id: o.kullanici.id, admin: o.admin } : null;
  }

  async dosyaKotasi(uid: number, boyut: number): Promise<boolean> { return this.dosyalar.kotaVar(uid, boyut); }

  async dosyaKaydet(uid: number, anahtar: string, ad: string, mime: string, boyut: number): Promise<DosyaEki> {
    return this.dosyalar.kaydet(uid, anahtar, ad, mime, boyut);
  }

  private async govde(istek: Request): Promise<Govde> {
    let g: unknown;
    try { g = await istek.json(); } catch { throw new HttpHata(400); }
    if (g === null || typeof g !== "object" || Array.isArray(g)) throw new HttpHata(400);
    return g as Govde;
  }

  private async yonlendir(istek: Request, url: URL): Promise<Response> {
    const y = url.pathname.replace(/\/+$/, "") || "/";
    const m = istek.method;
    const ip = istek.headers.get("CF-Connecting-IP") ?? "?";

    if (y === "/" && m === "GET") return new Response("Uygulama sunucusu çalışıyor.", { headers: { "Content-Type": "text/plain; charset=utf-8" } });

    // ---- Hesap (oturumsuz) ----
    if (m === "POST" && (y === "/api/kayit" || y === "/api/giris")) {
      if (!this.sinir("h:" + ip, Number(this.env.HESAP_LIMITI ?? "10") || 10, 60_000)) return bos(429);
      const i = await this.govde(istek);
      return y === "/api/kayit" ? this.kayit(i) : this.giris(i);
    }

    // Buradan sonrası oturum ister.
    const ben = await this.kimlikIstekten(istek, url);

    if (y === "/hub/negotiate" && m === "POST") {
      if (!ben) return bos(401);
      const cid = crypto.randomUUID();
      return json({
        negotiateVersion: 1, connectionId: cid, connectionToken: crypto.randomUUID(),
        availableTransports: [{ transport: "WebSockets", transferFormats: ["Text", "Binary"] }],
      });
    }
    if (y === "/hub") {
      if (istek.headers.get("Upgrade")?.toLowerCase() !== "websocket") return bos(400);
      if (!ben) return bos(401);
      const cift = new WebSocketPair();
      this.ctx.acceptWebSocket(cift[1], ["u" + ben.id]);
      cift[1].serializeAttachment({ uid: ben.id, el: false } satisfies Ek);
      return new Response(null, { status: 101, webSocket: cift[0] });
    }

    if (!y.startsWith("/api/")) return bos(404);
    if (!ben) return bos(401);
    const p = y.split("/").slice(2); // "/api/a/b" -> ["a","b"]

    if (p[0] === "cikis" && p.length === 1 && m === "POST") {
      await this.db.oturumKapat(this.tokenAl(istek, url)!);
      return tamam();
    }
    if (p[0] === "admin") {
      if (!ben.admin) return bos(403);
      return this.yonetim(istek, m, p.slice(1), ben, sayi);
    }

    // ---- Profil ----
    if (p[0] === "ben" && p.length === 1 && m === "GET") {
      const pr = this.db.profilGetir(ben.id);
      return pr ? json(pr) : bos(401);
    }
    if (p[0] === "profil" && p.length === 1 && m === "PUT") {
      const g = await this.govde(istek) as unknown as ProfilGuncelleme;
      if (typeof g.profilRengi !== "string" || (g.hakkimda != null && typeof g.hakkimda !== "string") ||
        (g.durumMesaji != null && typeof g.durumMesaji !== "string")) return bos(400);
      const h = profilKontrol(g);
      if (h) return hata(400, h);
      this.db.profilGuncelle(ben.id, g);
      this.profilDegisti(ben.id);
      return json(this.db.profilGetir(ben.id));
    }
    if (p[0] === "profil" && p[1] === "avatar" && p.length === 2) {
      if (m === "PUT") {
        if (Number(istek.headers.get("Content-Length") ?? "0") > Kurallar.AvatarMaxBayt) return hata(400, "Resim çok büyük.");
        const veri = await istek.arrayBuffer();
        if (veri.byteLength > Kurallar.AvatarMaxBayt) return hata(400, "Resim çok büyük.");
        if (!resimTuru(new Uint8Array(veri))) return hata(400, "Sadece PNG veya JPEG yüklenebilir.");
        this.db.avatarAyarla(ben.id, veri);
      } else if (m === "DELETE") {
        this.db.avatarAyarla(ben.id, null);
      } else return bos(405);
      this.profilDegisti(ben.id);
      return json(this.db.profilGetir(ben.id));
    }
    if (p[0] === "avatar" && p.length === 2 && m === "GET") {
      const veri = this.db.avatarGetir(sayi(p[1]));
      const tur = veri ? resimTuru(new Uint8Array(veri)) : null;
      if (!veri || !tur) return bos(404);
      return new Response(veri, { headers: { "Content-Type": tur, "Cache-Control": "private, max-age=86400" } });
    }

    // ---- Arkadaşlar ----
    if (p[0] === "arkadaslar") {
      if (p.length === 1 && m === "GET") {
        const { arkadaslar, gelen, giden } = this.db.arkadaslariGetir(ben.id);
        const gizli = (x: Profil) => ({ ...x, durum: Durum.Cevrimdisi }); // Arkadaş olmayanın durumu gizli.
        return json({ arkadaslar: arkadaslar.map(x => this.baskasina(x)), gelen: gelen.map(gizli), giden: giden.map(gizli) });
      }
      if (p.length === 2 && p[1] === "istek" && m === "POST") {
        if (!this.sinir("i:" + ben.id, 20, 60_000)) return bos(429);
        return this.istekGonder(ben.id, await this.govde(istek));
      }
      if (p.length === 3 && p[2] === "kabul" && m === "POST") {
        const id = sayi(p[1]);
        if (!this.db.istekKabulEt(ben.id, id)) return hata(404, "Bu istek artık geçerli değil.");
        this.gonder([ben.id, id], HubOlaylari.ArkadaslarDegisti);
        return tamam();
      }
      if (p.length === 2 && m === "DELETE") {
        const id = sayi(p[1]);
        if (this.db.iliskiSil(ben.id, id)) this.gonder([ben.id, id], HubOlaylari.ArkadaslarDegisti);
        return tamam();
      }
    }

    // ---- Mesajlar, sunucular, gruplar, keşfet ----
    return await this.mesajlar.yonlendir(istek, url, p, m, ben.id)
      ?? await this.sunucular.yonlendir(istek, p, m, ben.id)
      ?? await this.gruplar.yonlendir(istek, p, m, ben.id)
      ?? await this.kesfet.yonlendir(p, m, ben.id, url.searchParams.get("q") ?? "")
      ?? bos(404);
  }

  private async kayit(i: Govde): Promise<Response> {
    const h = kullaniciAdiKontrol(i.kullaniciAdi) ?? gorunenAdKontrol(i.gorunenAd) ?? sifreKontrol(i.sifre);
    if (h) return hata(400, h);
    const s = await this.db.kayitOl(i.kullaniciAdi as string, (i.gorunenAd as string).trim(), i.sifre as string, i.kod);
    if (!s.kullanici) return hata(s.alan === "kullaniciAdi" ? 409 : 400, s.hata!, s.alan);
    return json({ token: await this.db.oturumAc(s.kullanici.id), kullanici: s.kullanici });
  }

  private async giris(i: Govde): Promise<Response> {
    const ad = i.kullaniciAdi, sifre = i.sifre;
    if (typeof ad !== "string" || ad.trim().length === 0 || typeof sifre !== "string" || sifre.length === 0 || sifre.length > Kurallar.SifreMax)
      return hata(400, "Kullanıcı adı ve şifre gerekli.");
    const s = await this.db.girisDogrula(ad, sifre);
    if (s.yasakli) return hata(403, "Bu hesap yasaklandı.");
    if (!s.kullanici) return hata(401, "Kullanıcı adı veya şifre yanlış.");
    return json({ token: await this.db.oturumAc(s.kullanici.id), kullanici: s.kullanici });
  }

  private istekGonder(ben: number, i: Govde): Response {
    // Kimlik (Keşfet'ten) doluysa o; yoksa kullanıcı adı. "#12" ya da "12" numara olarak da aranır.
    const ad = (typeof i.kullaniciAdi === "string" ? i.kullaniciAdi : "").trim().replace(/^@+/, "");
    const kimlikle = typeof i.id === "number" && Number.isInteger(i.id) ? i.id : null;
    if (ad.length === 0 && kimlikle === null) return hata(400, "Bir kullanıcı adı ya da #numara yaz.");
    let o: number | null;
    if (kimlikle !== null) o = this.db.kullaniciVarMi(kimlikle) ? kimlikle : null;
    else {
      o = /^#\d{1,15}$/.test(ad) ? null : this.db.kullaniciIdBul(ad);
      const numara = /^#?(\d{1,15})$/.exec(ad);
      if (o === null && numara && this.db.kullaniciVarMi(Number(numara[1]))) o = Number(numara[1]);
    }
    if (o === null) return hata(404, `"${ad || kimlikle}" adında biri yok. Yazımı kontrol et.`);
    if (o === ben) return hata(400, "Kendine istek gönderemezsin 🙂");
    switch (this.db.iliskiGetir(ben, o)) {
      case Iliski.Arkadas: return hata(409, "Zaten arkadaşsınız.");
      case Iliski.BenGonderdim: return hata(409, "Bu kişiye zaten istek gönderdin.");
      case Iliski.OGonderdi:
        // O da bana istek atmış: direkt arkadaş olalım.
        this.db.istekKabulEt(ben, o);
        this.gonder([ben, o], HubOlaylari.ArkadaslarDegisti);
        return json({ mesaj: "O da sana istek göndermişti, artık arkadaşsınız!", arkadasOldunuz: true });
      default: {
        this.db.istekEkle(ben, o);
        this.gonder([ben, o], HubOlaylari.ArkadaslarDegisti);
        const adi = this.db.profilGetir(o)?.gorunenAd ?? ad;
        return json({ mesaj: `İstek gönderildi. ${adi} kabul edince arkadaş olacaksınız.`, arkadasOldunuz: false });
      }
    }
  }

  // ---- Yönetim (admin) ----

  private async yonetim(istek: Request, m: string, p: string[], ben: Kimlik, sayi: (s?: string) => number): Promise<Response> {
    if (p[0] === "ozet" && p.length === 1 && m === "GET") {
      const k = this.db.kullanicilar();
      const simdi = Math.floor(Date.now() / 1000);
      const aktifKod = this.db.kodlar().filter(x => !x.iptal && x.kalanKullanim !== 0 && (x.sonKullanma === null || x.sonKullanma > simdi)).length;
      return json({ kullanici: k.length, cevrimici: k.filter(x => this.cevrimici(x.id)).length, aktifKod });
    }
    if (p[0] === "kodlar") {
      if (p.length === 1 && m === "GET") return json(this.db.kodlar());
      if (p.length === 1 && m === "POST") {
        const i = await this.govde(istek);
        const kullanim = i.kullanim, gun = i.gun ?? null;
        if (!Number.isInteger(kullanim) || (gun !== null && !Number.isInteger(gun))) return bos(400);
        if ((kullanim as number) < 0 || (kullanim as number) > 1000 || (gun !== null && ((gun as number) < 0 || (gun as number) > 3650)))
          return hata(400, "Geçersiz kod ayarı.");
        return json(this.db.kodOlustur(ben.id, kullanim as number, gun as number | null));
      }
      if (p.length === 2 && m === "DELETE")
        return this.db.kodIptal(decodeURIComponent(p[1])) ? tamam() : hata(404, "Böyle bir kod yok.");
    }
    if (p[0] === "kullanicilar") {
      if (p.length === 1 && m === "GET")
        return json(this.db.kullanicilar().map(x => ({
          id: x.id, kullaniciAdi: x.ad, gorunenAd: x.gorunen, profilRengi: x.renk, avatarSurum: x.avatar, kayit: x.kayit,
          admin: x.admin, yasakli: x.yasakli, cevrimici: this.cevrimici(x.id), davetKodu: x.kod,
        })));
      if (p.length === 3 && m === "POST" && (p[2] === "admin" || p[2] === "yasak")) {
        const id = sayi(p[1]);
        const deger = (await this.govde(istek)).deger;
        if (typeof deger !== "boolean") return bos(400);
        if (p[2] === "admin") {
          if (id === ben.id && !deger) return hata(400, "Kendi adminliğini kaldıramazsın.");
          this.db.adminAyarla(id, deger);
          return tamam();
        }
        if (id === ben.id) return hata(400, "Kendini yasaklayamazsın.");
        if (deger && this.db.adminMi(id)) return hata(400, "Önce adminliğini kaldır, sonra yasakla.");
        this.db.yasakAyarla(id, deger);
        // Açık uygulaması varsa hemen çıkış yaptır (oturumları zaten silindi).
        if (deger) this.gonder([id], HubOlaylari.OturumSonlandi, "Hesabın yasaklandı.");
        return tamam();
      }
    }
    return bos(404);
  }

  // ---- SignalR (JSON hub protokolü, WebSocket üzerinden) ----

  async webSocketMessage(ws: WebSocket, ileti: string | ArrayBuffer) {
    const metin = typeof ileti === "string" ? ileti : new TextDecoder().decode(ileti);
    for (const parca of metin.split(RS)) {
      if (parca.length === 0) continue;
      let o: Govde;
      try { o = JSON.parse(parca); } catch { ws.close(1003, "Geçersiz mesaj"); return; }
      const ek = ws.deserializeAttachment() as Ek;

      if (!ek.el) {
        // İlk mesaj el sıkışma: {"protocol":"json","version":1}
        if (o.protocol !== "json") { ws.send(JSON.stringify({ error: "Sadece json protokolü destekleniyor." }) + RS); ws.close(1000, ""); return; }
        ws.send("{}" + RS);
        const ilk = this.soketler(ek.uid).length === 0;
        ws.serializeAttachment({ uid: ek.uid, el: true } satisfies Ek);
        if (ilk) {
          const pr = this.db.profilGetir(ek.uid);
          if (pr && pr.durum !== Durum.Gorunmez) this.durumYay(ek.uid, pr.durum);
        }
        continue;
      }

      switch (o.type) {
        case 1: await this.cagir(ws, ek.uid, o); break;   // Çağrı
        case 6: ws.send(PING); break;                      // Ping (başka mesajla birlikte geldiyse)
        case 7: ws.close(1000, ""); return;                // Kapat
      }
    }
  }

  private async cagir(ws: WebSocket, ben: number, o: Govde) {
    const id = typeof o.invocationId === "string" ? o.invocationId : null;
    const a = Array.isArray(o.arguments) ? o.arguments as unknown[] : [];
    const tamamla = (sonuc: Govde) => { if (id) { try { ws.send(JSON.stringify({ type: 3, invocationId: id, ...sonuc }) + RS); } catch { } } };
    try {
      const r = this.hubMetodu(ben, String(o.target), a);
      tamamla(r === undefined ? {} : { result: r });
    } catch (e) {
      tamamla({ error: e instanceof HttpHata ? e.mesaj ?? "Geçersiz çağrı." : "Sunucuda bir sorun oldu." });
      if (!(e instanceof HttpHata)) console.error(e);
    }
  }

  // Ekip.Sunucu/Varlik.cs EkipHub metotları. undefined dönen = sonucu olmayan (void) metot.
  private hubMetodu(ben: number, ad: string, a: unknown[]): unknown {
    const sayiArg = (x: unknown) => { if (typeof x !== "number" || !Number.isInteger(x)) throw new HttpHata(400, "Geçersiz çağrı."); return x; };
    const yaziArg = (x: unknown) => { if (typeof x !== "string") throw new HttpHata(400, "Geçersiz çağrı."); return x; };

    switch (ad) {
      case "PartiDurumum": return this.parti.durum(ben);
      case "PartiKur": return this.parti.kur(ben);
      case "PartiyeDavetEt": {
        const kimi = sayiArg(a[0]);
        const r = this.parti.davetEt(ben, kimi);
        if (r.hata) return r.hata;
        const p = this.db.profilGetir(ben);
        if (p) this.gonder([kimi], HubOlaylari.PartiDaveti, r.partiId, this.baskasina(p));
        return null;
      }
      case "DaveteKatil": {
        const { durum, eskiUyeler } = this.parti.katil(ben, yaziArg(a[0]));
        if (!durum) return null;
        this.partiGuncellemesiGonder(eskiUyeler);
        // Diğer cihazlarım da dahil herkese yeni üye listesi.
        this.gonder(durum.bilgi.uyeler.map(x => x.id), HubOlaylari.PartiGuncellendi, durum.bilgi);
        return durum;
      }
      case "DavetiReddet": this.parti.reddet(ben, yaziArg(a[0])); return undefined;
      case "PartidenAyril":
        this.partiGuncellemesiGonder(this.parti.ayril(ben));
        this.gonder([ben], HubOlaylari.PartiGuncellendi, null);
        return undefined;
      case "OyunSec": {
        const b = this.parti.oyunSec(ben, a[0] == null ? null : yaziArg(a[0]));
        if (b) this.gonder(b.uyeler.map(x => x.id), HubOlaylari.PartiGuncellendi, b);
        return undefined;
      }
      case "PartiMesajiGonder": {
        const { mesaj, alicilar } = this.parti.mesajEkle(ben, a[0]);
        if (mesaj) this.gonder(alicilar, HubOlaylari.PartiMesaji, mesaj);
        return undefined;
      }
      case "DurumAyarla": {
        const d = sayiArg(a[0]);
        if (d < 1 || d > 4) return undefined;
        this.db.durumAyarla(ben, d);
        this.durumYay(ben, this.gorunen(ben, d));
        this.gonder([ben], HubOlaylari.DurumDegisti, ben, d); // Diğer cihazlarım da bilsin.
        return undefined;
      }
      case "KonusmaYaziyor": this.mesajlar.konusmaYaziyor(ben, yaziArg(a[0])); return undefined;
      case "Yaziyor": {
        const karsi = sayiArg(a[0]);
        if (this.db.iliskiGetir(ben, karsi) === Iliski.Arkadas) this.gonder([karsi], HubOlaylari.Yaziyor, ben);
        return undefined;
      }
      default: throw new HttpHata(400, `Bilinmeyen metot: ${ad}`);
    }
  }

  async webSocketClose(ws: WebSocket) { this.ayrildi(ws); }
  async webSocketError(ws: WebSocket) { this.ayrildi(ws); }

  // Kapanış hem "close" hem "error" olarak gelebilir; aynı bağlantıyı bir kez işle.
  private readonly islenenler = new WeakSet<WebSocket>();

  private ayrildi(ws: WebSocket) {
    if (this.islenenler.has(ws)) return;
    this.islenenler.add(ws);
    let ek: Ek | null = null;
    try { ek = ws.deserializeAttachment() as Ek | null; } catch { }
    try { ws.close(1000, ""); } catch { }
    if (!ek?.el) return; // El sıkışmamış.
    if (this.soketler(ek.uid, ws).length > 0) return; // Başka cihazından hâlâ bağlı.
    this.durumYay(ek.uid, Durum.Cevrimdisi);
    this.partiGuncellemesiGonder(this.parti.ayril(ek.uid)); // Tamamen çıkınca partiden de düşer.
  }
}
