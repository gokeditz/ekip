# Uygulama — Android

.NET MAUI ile yazıldı; **Android Studio gerekmez**. Sunucu bağlantı kodu masaüstüyle ortak
(`Ekip.Uygulama/Api.cs`, `Ag.cs`, `Canli.cs` bu projeye bağlantı olarak eklenir), veri modelleri `Ekip.Ortak`.

## Gerekenler (bir kez)
```
dotnet workload install maui-android
dotnet build Ekip.Mobil -t:InstallAndroidDependencies -f net10.0-android -p:AcceptAndroidSDKLicenses=true "-p:JavaSdkDirectory=%LOCALAPPDATA%\Uygulama-gelistirme\jdk" "-p:AndroidSdkDirectory=%LOCALAPPDATA%\Android\Sdk"
```
Proje bu iki klasörü varsa kendiliğinden kullanır.

## APK üretmek
```
dotnet publish Ekip.Mobil -c Release -o yayin\mobil
```
`yayin\mobil\com.uygulama.mobil-Signed.apk` telefona kurulacak dosyadır.

## Telefona kurmak
- **Kablosuz:** APK'yı telefona gönder (WhatsApp/Drive vb.), dosyaya dokun, "Bilinmeyen kaynaklara izin ver" de, kur.
- **USB ile:** Telefonda Geliştirici seçenekleri → USB hata ayıklama açık, kabloyu tak, sonra
  `%LOCALAPPDATA%\Android\Sdk\platform-tools\adb install -r yayin\mobil\com.uygulama.mobil-Signed.apk`

## Şimdilik olanlar
Giriş / davet kodlu kayıt (oturum telefonda şifreli saklanır), sohbetler (DM + gruplar), sunucular ve kanallar,
sohbet (biçimli yazı, @bahsetme, yanıt, tepki, düzenle/sil, fotoğraf gönder/gör, dosya aç), Keşfet (arkadaş ekle),
profil (kimlik kopyala, durum, çıkış).

## Sırada
Bildirimler (uygulama kapalıyken), sunucu/grup oluşturma ve ayarları, GIF seçici, emoji seçici, kendi imzalama anahtarı
(şu an hata ayıklama anahtarıyla imzalı; arkadaşlara dağıtmadan önce sabit bir anahtar gerekir).
