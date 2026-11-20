# Uygulama — Kurulum Rehberi

Bu rehber; sunucunun nerede çalıştığını, yeni sürüm yayınlamayı ve arkadaşlarına uygulamayı
dağıtmayı anlatır.

```
Arkadaşların (Windows uygulaması: Ekip.Uygulama)
        │  https://ekip.ekip-bulut.workers.dev
        ▼
Cloudflare Workers (bedava, 7/24 açık, bakım gerektirmez)
   └─ Ekip.Bulut → tek Durable Object: içinde SQLite (kullanıcılar, mesajlar, davet kodları)
                    + anlık bağlantılar (SignalR protokolü)

GitHub (açık depo: gokeditz/ekip)
   ├─ Kod burada durur
   └─ "v1.0.1" etiketi atınca uygulama derlenir, Release'e eklenir;
      uygulamalar güncellemeyi buradan indirir
```

---

## 1. Sunucu: Cloudflare (Ekip.Bulut)

Sunucu `Ekip.Bulut/` klasöründe, TypeScript ile yazılı. Uygulama sunucuyla SignalR (JSON hub protokolü) ve REST üzerinden konuşur.

- **Bedava sınırlar:** günde 100.000 istek, 1 GB veri. 10-20 kişilik bir grup bunun çok altında kalır.
- **Uyku:** Kimse bir şey yapmazken sunucu uyur, mesaj gelince anında uyanır; bağlantılar kopmaz.
- **Yedek:** Cloudflare, Durable Object verisini son 30 güne kadar herhangi bir ana geri alabilir
  (point-in-time recovery). Ayrıca bir şey kurmana gerek yok.

### Sunucuda değişiklik yayınlamak

Gerekenler: Node.js. İlk seferde bir kez `npx wrangler login` (tarayıcıda Allow).
```
cd Ekip.Bulut
npm install
npx wrangler deploy
```
Yayınlarken açık bağlantılar bir an kopar; uygulamalar birkaç saniye içinde kendiliğinden
yeniden bağlanır. Açık partiler dağılır (partiler geçicidir).

> Bu bilgisayarda Cloudflare'a bağlanırken zaman aşımı olursa (IPv6 sorunu):
> `set NODE_OPTIONS=--dns-result-order=ipv4first` yazıp tekrar dene.

### Yerelde denemek
```
cd Ekip.Bulut
npx wrangler dev --port 8787
```
Uygulamada giriş ekranındaki **Sunucu ayarı**'ndan `http://127.0.0.1:8787` gir.
İki hesabı aynı bilgisayarda denemek için uygulamayı `--profil isim` ile aç.

---

## 2. İlk admin

Sunucu boşken **ilk kayıt olan kişi kod gerekmeden kaydolur ve admin olur** — bu sen ol.
Sonra herkes davet koduyla kaydolur: Ekip → **Yönetim → Davet kodları → Kod oluştur**
(kod panoya kopyalanır, arkadaşına gönder). Yönetim → Kullanıcılar'dan başkasını da admin
yapabilir ya da yasaklayabilirsin.

---

## 3. Yeni sürüm yayınlamak (uygulama)

Depo: `gokeditz/ekip` (açık). Releases herkese açık olduğu için arkadaşların ilk kurulumu da
güncellemeleri de buradan indirir.

> Depoya asla şifre, token, `.db` dosyası koyma.

1. Sürüm numarasını bir yerde değiştirmene gerek yok; etiketten (`v1.0.1` → `1.0.1`) alınır.
2. Değişiklikleri commit'le (commit başlığı güncelleme notu olarak gösterilir), sonra:
   ```
   git tag v1.0.2
   git push origin main v1.0.2
   ```
3. GitHub birkaç dakika içinde Release'i oluşturur (`.github/workflows/surum.yml`):
   - `Uygulama-Windows.zip` → ilk kurulum için
   - `Uygulama-1.0.2.zip` + `son.json` → otomatik güncelleme için

### Otomatik güncelleme nasıl çalışır

Uygulama açılışta ve açık kaldıkça 30 dakikada bir
`https://github.com/gokeditz/ekip/releases/latest/download/son.json` adresine bakar. Yeni sürüm
varsa uygulama içinde "Yeni sürüm hazır" diye sorar; "Şimdi güncelle" deyince indirir, dosyanın
bozuk olmadığını (sha256) kontrol eder, exe'yi değiştirir ve kendini yeniden açar. Pencere
tepsideyse köşede bildirim çıkar, Ekip açılınca sorulur.

Elle paket hazırlamak istersen:
```
powershell -ExecutionPolicy Bypass -File dagitim\guncelleme-paketle.ps1 -Surum 1.0.2 -Notlar "Neler değişti"
```

---

## 4. Arkadaşlara dağıtım

Arkadaşlarına şu linki gönder (her zaman en son sürümü indirir):
`https://github.com/gokeditz/ekip/releases/latest/download/Uygulama-Windows.zip`

Zip'i bir klasöre açıp `Uygulama.exe`'yi çalıştırsınlar. .NET kurmalarına gerek yok,
sunucu adresi uygulamanın içinde. Bundan sonraki sürümler kendiliğinden gelir.
(Masaüstü/Program Files yerine kendi klasörlerine, ör. `Belgeler\Ekip`, açsınlar: güncelleme
exe'nin yanına yazabilmeli.) Windows "bilinmeyen yayımcı" uyarısı verirse **Ek bilgi → Yine de çalıştır**.

---
