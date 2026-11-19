# Ekip.Bulut

Ekip sunucusu: Cloudflare Workers + tek Durable Object (SQLite). Tasarım ve sözleşmeler için kök dizindeki `TASARIM.md`.

- `npm run dev` yerelde çalıştırır (`wrangler dev`), `npm run denetle` tür denetimi yapar.
- Dosyalar (`/api/dosya`, `/dosya/...`) R2 kovası `ekip-dosyalar` (bağlama `DOSYALAR`) üzerinden, GIF aracılığı (`/api/gif`) Worker içinde çalışır; ikisi de Durable Object'i yormaz.

## GIF servisini ayarlama

Uygulamadaki GIF seçici sunucu üzerinden çalışır; sağlayıcının anahtarı uygulamada durmaz.
Anahtar ayarlanmadıkça `/api/gif` 503 ("GIF servisi henüz ayarlanmadı.") döner ve uygulama bunu boş durum olarak gösterir.

1. Sağlayıcıdan bir API anahtarı al: GIPHY, KLIPY ya da Tenor.
2. Anahtarı gizli değişken olarak kaydet (sonra yazdığın komut anahtarı sorar):

   ```
   npx wrangler secret put GIF_ANAHTARI
   ```

3. Sağlayıcıyı `wrangler.jsonc` içindeki `vars.GIF_SAGLAYICI` ile seç: `"giphy"` (varsayılan), `"klipy"` ya da `"tenor"`.
   Değiştirdikten sonra `npx wrangler deploy` ile yayınla. Sağlayıcıyı değiştirirsen `GIF_ANAHTARI`'nı da yeni sağlayıcının anahtarıyla güncelle.

Yerel denemede anahtarı `Ekip.Bulut/.dev.vars` dosyasına `GIF_ANAHTARI=...` diye yazabilirsin (dosya git'e girmemeli).

## R2 kovası

Bir kez oluştur: `npx wrangler r2 bucket create ekip-dosyalar`. Eski dosyaları silmek için kovada 90 günlük yaşam döngüsü kuralı tanımla.
