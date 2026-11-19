# Ekip — Sohbet, Sunucular, Gruplar, Keşfet, Dosyalar: Tasarım ve Sözleşmeler

Bu belge birden fazla geliştiricinin (ajanın) aynı anda çalışabilmesi için **tek doğru kaynaktır**.
Veri biçimleri `Ekip.Ortak/Modeller.cs`, istemci çağrıları `Ekip.Uygulama/Api.cs`, anlık olaylar
`Ekip.Uygulama/Canli.cs` içinde tanımlıdır; bu dosyalar **sözleşmedir, değiştirme** (eksik görürsen
raporuna yaz, organizatör ekler).

Genel kurallar:
- Her şey Türkçe: tanımlayıcılar, yorumlar, arayüz metinleri (mevcut kod gibi). Arayüz metinleri samimi ama düzgün.
- Hedef bilgisayarlar zayıf: animasyon/blur yok, gereksiz yeniden çizim yok, büyük resimler küçültülerek
  önbelleğe alınır, ağ işleri async.
- Arayüz kalitesi çok önemli: mevcut koyu tema (`Tema.cs`, `Cizim.cs`, `Kontroller.cs`) ile birebir
  uyumlu, hizalı, taşmayan, DPI ölçekli (`Tema.S(...)`) çizim. Ayrı pencere açma; sayfa ya da uygulama
  içi modal (`IcModal`) kullan. Açılır menüler için `KoyuMenu`.
- JSON camelCase (System.Text.Json Web varsayılanları). Enum'lar sayı olarak gider.

---

## 1. Sunucu (Ekip.Bulut — Cloudflare Workers + tek Durable Object + SQLite)

Mevcut yapı: `src/index.ts` (Worker giriş + `EkipSunucu` DO: HTTP yönlendirme + SignalR JSON hub
taklidi), `src/veritabani.ts`, `src/parti.ts`, `src/kurallar.ts`. Yeni dosyalar eklenebilir
(ör. `src/sunucular.ts`, `src/dosyalar.ts`, `src/gif.ts`). Mevcut uçlar aynen çalışmaya devam etmeli.

### 1.1 Veri (SQLite, DO içinde)
`ayar.surum` ile geçiş yap (şu an 4). Yeni sütun/tablolar `ALTER TABLE` / `CREATE TABLE IF NOT EXISTS`
ile; canlı veritabanında veri var, silme yok.

- `mesajlar`: mevcut tabloya sütunlar: `kanal_id`, `grup_id`, `yanit_id`, `ekler` (JSON metin),
  `duzenlenme`, `silindi`. DM'lerde `alici_id` karşı taraf; kanal/grup mesajında `alici_id = 0`.
  Uygun indeksler: `(kanal_id, id)`, `(grup_id, id)`.
- `tepkiler(mesaj_id, kullanici_id, emoji, zaman, PK(mesaj_id, kullanici_id, emoji))`
- `bahsetmeler(mesaj_id, kullanici_id, PK)` — okunmamış bahsetme sayısı için.
- `okumalar(kullanici_id, konusma TEXT, son_id, PK(kullanici_id, konusma))` — kanal/grup için
  ("k:12", "g:5"). DM'ler mevcut `okundu` sütununu kullanmaya devam eder.
- `sunucular(id, ad, aciklama, renk, ikon BLOB, ikon_surum, herkese, sahip_id, davet_kodu UNIQUE, olusturma)`
- `sunucu_uyeleri(sunucu_id, kullanici_id, rol, katilma, PK)`
- `kanallar(id, sunucu_id, ad, konu, sira, olusturma)`
- `gruplar(id, ad, aciklama, renk, ikon BLOB, ikon_surum, kurucu_id, olusturma)`
- `grup_uyeleri(grup_id, kullanici_id, katilma, PK)`
- `dosyalar(anahtar PK, sahip_id, ad, mime, boyut, zaman)` — R2'ye yüklenenlerin kaydı (kota ve
  "bu dosya gerçekten bu kişinin mi" kontrolü için).

### 1.2 Konuşmalar ve yetki
`/api/konusmalar/{tur}/{id}/...` — tur: `dm` (id = karşı kişi; arkadaş olmalı), `kanal` (sunucu
üyesi olmalı), `grup` (grup üyesi olmalı). Yetkisizse 403 `HataYaniti`.

- `GET .../mesajlar?once=` → `List<Mesaj>` (en yeni 50, eskiden yeniye). Her mesaj tam: `Yanit`
  özeti, `Ekler`, `Tepkiler`, `Bahsedilenler` dolu. Silinmiş mesaj: `Silindi=true`, `Metin=""`,
  `Ekler=[]` (yer tutucu olarak listede kalır).
- `POST .../mesajlar` (`MesajGonderme`) → `Mesaj`. Kurallar:
  - Metin trim; boşsa en az 1 ek şart. `Kurallar.MesajMax`, `Kurallar.MesajEkMax`.
  - `YanitId` aynı konuşmadaki bir mesaj olmalı (değilse yok say).
  - Ek doğrulama: `Url` `/dosya/<anahtar>/...` ise `dosyalar` tablosunda olmalı ve sahibi gönderen
    olmalı; ad/mime/boyut sunucudaki kayıttan alınır (istemciye güvenme). GIF ise `Url` ve
    `OnizlemeUrl` https olmalı ve alan adı izinli listede: `*.giphy.com`, `*.klipy.com`,
    `media.tenor.com`, `*.tenor.com`. `Tur` sunucuda mime'dan belirlenir (image/gif dahil "resim";
    sadece GIF sağlayıcıdan gelen "gif").
  - Bahsetmeler: metindeki `@kullaniciadi` (büyük-küçük harf duyarsız) konuşmanın üyesiyse
    `Bahsedilenler`e eklenir; `@herkes` kanal/grupta tüm üyeleri ekler (gönderen hariç). DM'de karşı taraf.
  - Olay: konuşmanın tüm üyelerine (DM'de iki taraf, gönderenin diğer cihazları dahil) `MesajGeldi(Mesaj)`.
  - Sınır: kullanıcı başına 10 sn'de 20 mesaj (mevcut gibi).
- `POST .../okundu` → DM: mevcut davranış. Kanal/grup: `okumalar.son_id` = konuşmadaki en büyük id;
  kendi diğer cihazlarına `KonusmaOkundu(konusma)`.
- `PUT /api/mesaj/{id}` (`MesajDuzenleme`) → sadece gönderen; silinmişse 404. Metin boş olamaz (ek
  yoksa). Bahsetmeler yeniden hesaplanır. Olay: `MesajGuncellendi(Mesaj)` konuşmanın üyelerine.
- `DELETE /api/mesaj/{id}` → gönderen ya da (kanalda) sunucunun yönetici/sahibi. Olay: `MesajGuncellendi`.
- `POST /api/mesaj/{id}/tepki` (`TepkiIstegi`) → aç/kapa. Emoji: 1–16 karakter, boşluk yok.
  Mesaj başına en fazla `Kurallar.TepkiMax` farklı emoji. Dönen: güncel `Mesaj`; olay `MesajGuncellendi`.
- Eski DM uçları (`/api/mesajlar/{karsiId}`, `/okundu`, `/api/sohbetler`) aynen çalışır; yeni
  alanlar da dolu döner. `SohbetOzeti.SonMesaj` silinmişse yine döner (Silindi=true).
- Hub: `KonusmaYaziyor(string konusma)` → üye kontrolü, diğer üyelere `KonusmaYaziyor(konusma, ben)`.

### 1.3 Keşfet ve arkadaş kimliği
- `GET /api/kesfet/kullanicilar?q=` → yasaklı olmayan herkes (ben hariç), `q` kullanıcı adı/görünen
  ad/`#id` içinde arar; çevrimiçi arkadaşlar önce, sonra ada göre; en fazla 200. Arkadaş olmayanın
  `Durum`u `Cevrimdisi`.
- `GET /api/kesfet/sunucular?q=` → `Herkese=true` sunucular, üye sayısına göre.
- `POST /api/arkadaslar/istek`: `Id` doluysa onu kullan; `KullaniciAdi` `#12` / `12` biçimindeyse
  numara olarak da ara. Geri kalan davranış aynı.

### 1.4 Sunucular
Rol: `Sahip(2) > Yonetici(1) > Uye(0)`. Bir kişi en fazla `Kurallar.SunucuMax` sunucuda.
- `GET /api/sunucular` → `List<SunucuOzeti>` (okunmamış/bahsetme toplamları ile).
- `POST /api/sunucular` (`SunucuAyari`) → `Sunucu`. Oluşturan sahip olur; otomatik `genel` kanalı
  ve 8 karakterlik davet kodu (`XXXX-XXXX`, mevcut kod alfabesi). Ad 1–`SunucuAdMax`, renk
  `Kurallar.ProfilRenkleri`'nden.
- `GET /api/sunucular/{id}` → `Sunucu` (üye değilse 403; `Herkese` ise yine 403 — önce katılmalı).
  `Kanallar` sıraya göre, okunmamış/bahsetme dolu. `Uyeler` rol sonra ada göre; profillerin
  `Durum`u başkasına göründüğü gibi.
- `PUT /api/sunucular/{id}` yönetici+. `DELETE` sadece sahip (kanallar/mesajlar/üyeler silinir).
- `PUT|DELETE /api/sunucular/{id}/ikon` yönetici+ (PNG/JPEG, en fazla `AvatarMaxBayt`, ikon_surum
  artar). `GET /api/ikon/sunucu/{id}?v=` ve `GET /api/ikon/grup/{id}?v=` (oturum ister; üye olmasa da
  verilebilir — Keşfet'te görünür).
- Kanallar: `POST /api/sunucular/{id}/kanallar`, `PUT|DELETE /api/kanallar/{kanalId}` yönetici+.
  Ad `Kurallar.KanalAdKontrol`. Son kanal silinemez. En fazla `SunucuKanalMax`.
- Katılma: `POST /api/sunucular/{id}/katil` (sadece `Herkese`), `POST /api/sunucular/katil-kod`
  (`SunucuKoduIstegi`, `KodDuzelt` ile normalleştir). Zaten üyeyse aynen `Sunucu` döner.
- `POST /api/sunucular/{id}/ayril` (sahip ayrılamaz: önce sunucuyu sil ya da sahipliği devret —
  devretme: `PUT .../uyeler/{uid}/rol` ile `Sahip` verilirse eski sahip `Yonetici` olur).
- `POST /api/sunucular/{id}/davet-kodu` yönetici+ → yeni kod.
- `PUT /api/sunucular/{id}/uyeler/{uid}/rol` (`RolAyari`) sadece sahip.
- `DELETE /api/sunucular/{id}/uyeler/{uid}` (at) — yönetici+ ve hedefin rolü kendisinden düşük olmalı.
- Olaylar: üyelik değişince ilgili kişilere `SunucularDegisti`; ayar/kanal/üye/rol değişince tüm
  üyelere `SunucuGuncellendi(id)`; atılan/çıkan kişiye `SunucularDegisti`.
  Üyelerden biri çevrimiçi/çevrimdışı olunca durum zaten `DurumDegisti` ile gidiyor; sunucu üyesi
  ama arkadaş olmayanlar için de `DurumDegisti` gönder (aynı sunucudaki herkese).

### 1.5 Gruplar
- `GET /api/gruplar` → `List<Grup>` (son mesaja göre yeni üstte; `Okunmamis`, `Bahsedilme`).
- `POST /api/gruplar` (`GrupOlusturma`) → `Grup`. Üyeler benim arkadaşlarım olmalı, toplam
  2..`GrupUyeMax` (ben dahil en az 3 kişi değil, en az 2: ben + 1).
- `PUT /api/gruplar/{id}` (`GrupAyari`) — her üye. `PUT|DELETE .../ikon` her üye.
- `POST /api/gruplar/{id}/uyeler` (`UyeEkleme`) — ekleyenin arkadaşı olmalı, sınır `GrupUyeMax`.
- `DELETE /api/gruplar/{id}/uyeler/{uid}` — kendisi (ayrılma) ya da kurucu. Kurucu ayrılırsa en eski
  üye kurucu olur; son üye ayrılınca grup silinir.
- Olay: değişince tüm (eski+yeni) üyelere `GruplarDegisti`.

### 1.6 Dosyalar (R2) — sunucuyu yormadan
Dosya baytları **Durable Object'e hiç girmez**. Worker, `DOSYALAR` R2 bağlamasıyla doğrudan akıtır.
- `POST /api/dosya?ad=&mime=` (Bearer, gövde ham bayt, `Content-Length` şart): Worker oturumu DO'ya
  RPC ile sorar (`kimlik(token)`), kotayı sorar (`dosyaKotasi(uid, boyut)` — günlük
  `GunlukYuklemeBayt`), `DosyaMaxBayt` üstü 413. Anahtar: 128 bit rastgele (base64url). R2'ye
  `httpMetadata: { contentType, contentDisposition }` ile yaz, sonra DO'ya `dosyaKaydet(...)`.
  Dönen `Ek`: Tur mime'dan ("image/*"→resim, "video/*"→video, "audio/*"→ses, diğer→dosya),
  `Url = /dosya/<anahtar>/<ad-urlencoded>`, resimse `OnizlemeUrl = Url`. Ad temizlenir (yol/kontrol
  karakteri yok, en fazla 120 karakter).
- `GET /dosya/<anahtar>/<ad>`: oturum gerekmez (anahtar tahmin edilemez). R2'den akıt; `Content-Type`,
  `Content-Length`, `Cache-Control: public, max-age=31536000, immutable`, `ETag`. `Range` desteği
  (video için). Bulunamazsa 404. İsteğe bağlı: `caches.default` ile kenar önbelleği.
- R2 kovası: `ekip-dosyalar` (bağlama adı `DOSYALAR`). Yaşam döngüsü kuralı (90 gün) organizatör
  tarafından ayarlanır.

**Neden eşler arası (P2P) değil:** P2P'de iki kişinin aynı anda çevrimiçi olması gerekir, çevrimdışı
arkadaşa fotoğraf gitmez, NAT arkasında çoğu zaman bağlantı kurulamaz (bedava TURN yok). R2 + Worker
akışında sunucu (DO) hiç yorulmaz, indirme ücreti yok, 10 GB bedava.

### 1.7 GIF
- `GET /api/gif?q=&sonraki=` (oturum ister). `q` boşsa popüler GIF'ler. Sağlayıcı `GIF_SAGLAYICI`
  ortam değişkeni (`giphy` | `klipy` | `tenor`, varsayılan `giphy`), anahtar `GIF_ANAHTARI` gizli
  değişkeni (`npx wrangler secret put GIF_ANAHTARI`). Anahtar yoksa 503
  `{"mesaj":"GIF servisi henüz ayarlanmadı."}`. Sonuçlar `GifSayfasi`'na normalleştirilir (sayfa
  başı 24; `OnizlemeUrl` küçük animasyonlu GIF, `Url` orta boy GIF; mp4/webp değil, GIF formatı).
  `rating`/içerik filtresi: orta (pg-13).
- Sonuçlar Worker'da `caches.default` ile ~5 dk önbelleklenebilir.

---

## 2. İstemci (Ekip.Uygulama — WinForms, .NET 10)

### 2.1 Konuşma deposu (Dalga 2 — "sohbet" ajanı)
`SohbetDeposu` (yeni): her `KonusmaKimligi` için `SohbetVerisi` tutar; yükleme/sayfalama, iyimser
gönderme (`IstemciId`, geçici negatif Id), tekrar deneme, düzenleme, silme, tepki, `MesajGeldi`/
`MesajGuncellendi` işleme, okunmamış sayıları ve "yazıyor" durumunu yönetir. `AnaForm`'daki DM
mantığı buraya taşınır; DM, kanal ve grup aynı yoldan çalışır. Mini sohbet ve overlay DM'leri
(`MiniSohbet.cs`, `Overlay.cs`, `AnaForm.Parti.cs`) çalışmaya devam etmeli (sadece DM; eki olan mesajı
"📎 Fotoğraf" gibi kısa metinle göster).

### 2.2 Zengin mesaj listesi ve yazma kutusu (Dalga 2)
`MesajListesi` / `SohbetPaneli` yeniden:
- Metin: `**kalın**`, `*italik*`, `` `kod` ``, `~~çizili~~`, tıklanabilir bağlantılar, `@bahsetme`
  (renkli hap), renkli emoji (bkz. 2.3). Bana bahsedildiyse satır arka planı sarımsı + sol çizgi.
- Yanıt: mesajın üstünde küçük alıntı satırı (kim + kısaltılmış metin); tıklayınca o mesaja kaydır ve
  kısa vurgula.
- Ekler: resim/GIF satır içinde (en fazla ~400×300, oran korunur, yuvarlak köşe, yüklenene kadar yer
  tutucu; GIF animasyonlu ama sadece görünürken). Tıklayınca `ResimGoruntuleyici`. Video/dosya/ses:
  kart (simge, ad, boyut, "İndir" / "Aç"; ilerleme çubuğu).
- Tepkiler: mesaj altında hap (emoji + sayı), benimki vurgulu; tıklayınca aç/kapa; "+" ile seçici.
- Üzerine gelince sağ üstte küçük araç çubuğu: Tepki, Yanıtla, (kendi mesajımsa) Düzenle, Sil,
  "…" menü (Metni kopyala, Bağlantıyı kopyala, Mesaj kimliğini kopyala). Sağ tık aynı menü.
- "(düzenlendi)" etiketi; silinmiş: "Bu mesaj silindi." soluk italik.
- Yazma kutusu: sol "+" (dosya ekle), sağda GIF ve emoji düğmeleri; sürükle-bırak dosya; Ctrl+V ile
  panodaki resmi ekle; ekler gönderilmeden önce kutunun üstünde küçük önizleme kartlarıyla (çarpı ile
  kaldır); yükleme ilerlemesi.
- Yanıt modu: kutunun üstünde "X kişisine yanıt veriyorsun ✕" çubuğu; Esc iptal.
- Kısayollar: Enter gönder, Shift+Enter satır, ↑ (kutu boşken) son mesajımı düzenle, Esc yanıt/
  düzenlemeyi iptal, Ctrl+E emoji seçici, Ctrl+G GIF seçici, Ctrl+B/Ctrl+I seçili metni kalın/italik
  yapar, `:` + en az 2 harf → emoji önerisi, `@` → üye önerisi (yukarı/aşağı + Enter/Tab).
- Parti sohbeti (`BasitMod`): yanıt/düzenleme/tepki/ek yok; emoji seçici ve emoji çizimi var.

### 2.3 Bileşenler (Dalga 1 — "bileşen" ajanı; hepsi yeni dosyalar)
- **Renkli emoji:** GDI renkli emoji çizemez. Twemoji (CC-BY 4.0) 72×72 PNG'lerinden yaygın
  ~1500'lük bir alt küme gömülü kaynak (zip) olarak; `EmojiVeri` (ad tr/en anahtar kelimeler,
  kategori, unicode dizisi) + `EmojiCizici` (dizeyi bulup küçültülmüş önbellekli resim çizer, yoksa
  font glifine düşer). Atıf: Ayarlar sayfasında küçük satır.
- **ZenginMetin:** metni parçalara ayırıp (kelime, boşluk, satır sonu, emoji, bahsetme, bağlantı,
  biçim) satırlara yerleştiren ve çizen motor: `Olc(genislik)` → yükseklik, `Ciz(g, x, y)`,
  `Bul(nokta)` → tıklanan bağlantı/bahsetme. Mesaj başına sonuç önbelleklenir.
- **EmojiSecici:** açılır panel (kategori sekmeleri, arama — Türkçe ve İngilizce anahtar kelime, son
  kullanılanlar `Ayarlar`'da), `Secildi(string emoji)` olayı. Klavye: oklar + Enter, Esc kapatır.
- **GifSecici:** açılır panel; popüler + arama (yazarken 400 ms bekle), 2-3 sütun ızgara, kaydırınca
  sonraki sayfa, küçük önizlemeler; `Secildi(GifSonucu)`. 503 gelirse "GIF servisi henüz ayarlanmadı"
  boş durumu.
- **ResimGoruntuleyici:** uygulama içi tam alan modal (koyu perde): resim sığdırılmış, tekerlekle
  yakınlaştır, sürükle; altta ad + "İndir" + "Tarayıcıda/klasörde aç"; Esc/dışarı tıkla kapatır.
- **DosyaOnbellegi:** `%LocalAppData%\Ekip\onbellek` (profil adı varsa ayrı), en fazla ~300 MB, en eski
  silinir; `ResimAl(url, maksBoyut)` → küçültülmüş `Image` (bellek önbelleği + disk), `Indir(ek, hedef)`
  ilerlemeli; "İndir" → kullanıcının İndirilenler klasörü (aynı ad varsa "ad (1).ext").
  Video/ses/dosya "Aç": indirip varsayılan programla aç.
- **AnimasyonluResim:** GIF karelerini sadece görünürken ilerletir (tek ortak zamanlayıcı).

### 2.4 Gezinme, sunucular, gruplar, keşfet (Dalga 1: sayfalar; Dalga 3: AnaForm'a bağlama — "gezinme" ajanı)
- **Sunucu rayı** (Discord gibi en solda ~72px dikey şerit): üstte "Ana sayfa" (logo; arkadaşlar +
  DM'ler), ayırıcı, sunucu simgeleri (ikon ya da baş harfler + renk; seçili olan yuvarlak kare + sol
  gösterge; okunmamış nokta, bahsetme sayısı rozeti), "+" (sunucu oluştur / kodla katıl), pusula
  (Keşfet). Ana sayfadayken mevcut `KenarCubugu` görünür (DM listesinde gruplar da: grup ikonu, ad,
  üye sayısı).
- **Sunucu görünümü:** kenar çubuğu yerine kanal listesi (üstte sunucu adı + açılır menü: Sunucu
  ayarları, Davet kodu, Kanal oluştur, Sunucudan ayrıl), kanal satırları `# ad`, okunmamış kalın,
  bahsetme rozeti; ortada kanal sohbeti (başlıkta `# ad` + konu), sağda açılıp kapanan üye listesi
  (rol başlıkları altında, çevrimiçi önce).
- **Sunucu ayarları sayfası** (ayarlar sayfası gibi tam sayfa): Genel (ikon yükle/kaldır, ad,
  açıklama, renk paleti, "Keşfet'te görünsün"), Kanallar (ekle/yeniden adlandır/konu/sil), Üyeler
  (rol değiştir, at, sahipliği devret), Davet (kodu göster/kopyala/yenile), Tehlikeli bölge (sil).
  Önizleme kartı profil kartı tarzında.
- **Sunucu oluştur / katıl modalı:** iki sekme: "Oluştur" (ad, renk, ikon) ve "Kodla katıl".
- **Grup oluştur modalı:** arkadaş listesinden çoklu seçim + isteğe bağlı ad. **Grup ayarları**
  (sohbet başlığındaki ⚙): ikon, ad, açıklama, renk, üyeler (ekle/çıkar), gruptan ayrıl.
- **Keşfet sayfası:** iki sekme: "Kişiler" (arama kutusu, kart ızgarası: avatar, ad, @ad ve #id,
  durum; düğmeler: Arkadaş ekle / İstek gönderildi / Kabul et / Mesaj) ve "Sunucular" (kart: ikon,
  ad, açıklama, üye/çevrimiçi sayısı, Katıl / Aç). Karta tıklayınca profil kartı modalı.
- **Arkadaş kimliği:** profil kartında ve kendi kartımda `@kullaniciadi · #id` ve "Kopyala";
  "Arkadaş ekle" sayfası `ad`, `@ad` ya da `#12` kabul eder (ipucu yazısı güncellenir).

### 2.5 Dosya sahipliği (çakışmayı önlemek için)
| Dalga | Ajan | Dokunabileceği dosyalar |
|---|---|---|
| 1 | sunucu | `Ekip.Bulut/**` (deploy YOK) |
| 1 | bileşen | yeni: `Emoji*.cs`, `ZenginMetin.cs`, `GifSecici.cs`, `ResimGoruntuleyici.cs`, `DosyaOnbellegi.cs`, `AnimasyonluResim.cs`, `Varliklar/emoji/**`, csproj'a gömülü kaynak satırı |
| 1 | gezinme (sayfalar) | yeni: `SunucuRayi.cs`, `KanalListesi.cs`, `UyeListesi.cs`, `SunucuAyarlari.cs`, `SunucuModallari.cs`, `GrupModallari.cs`, `KesfetSayfasi.cs`, `IkonOnbellek.cs` |
| 2 | sohbet | `Sohbet.cs`, yeni `SohbetDeposu.cs`, `AnaForm.cs`/`AnaForm.Parti.cs`/`MiniSohbet.cs`/`Overlay.cs` (sadece sohbet bağlantıları), `Ayarlar.cs` (son emojiler) |
| 3 | gezinme (bağlama) | `AnaForm*.cs`, `Kenar.cs`, `Listeler.cs`, `ProfilFormu.cs`, `AyarlarSayfasi.cs` |

Sözleşme dosyaları (`Ekip.Ortak/Modeller.cs`, `Api.cs`, `Canli.cs`) sadece organizatörde.
