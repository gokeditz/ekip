# Ekip — Kurulum Rehberi

Bu rehber; kodu GitHub'a koymayı, sunucuyu bedava ve sürekli açık bir yerde çalıştırmayı ve
arkadaşlarına uygulamayı dağıtmayı adım adım anlatır.

```
Arkadaşların (Windows uygulaması)
        │  https://ekip.duckdns.org
        ▼
Oracle Cloud sanal makinesi (bedava, 7/24 açık)
   ├─ Caddy  → HTTPS sertifikasını otomatik alır, 443 → 5080
   └─ Ekip.Sunucu (servis) → SQLite: /var/lib/ekip/ekip.db (+ her gece yedek)

GitHub (özel depo)
   ├─ Kod burada durur
   └─ "v1.0.0" etiketi atınca: uygulama + sunucu otomatik derlenir, Release'e eklenir
```

---

## 1. GitHub

1. Depo özel (private) olsun; kod sadece sende kalır.
2. İlk yükleme (proje klasöründe):
   ```
   gh repo create ekip --private --source . --push
   ```
3. Yeni sürüm yayınlamak için:
   ```
   git tag v1.0.0
   git push --tags
   ```
   GitHub birkaç dakika içinde `Ekip-Windows.zip` ve `Ekip-Sunucu-linux-arm64.zip` dosyalarını
   **Releases** sayfasına koyar (`.github/workflows/surum.yml`).

> Depo özel olunca Releases da özeldir. Arkadaşların indirebilsin ve otomatik güncelleme
> çalışsın diye, uygulamayı dağıtacağın zaman ya depoyu açık yaparsın ya da Release dosyalarını
> başka bir yere koyarız (otomatik güncelleme adımında karar veririz).

---

## 2. Sunucu: Oracle Cloud "Always Free"

**Neden Oracle:** Gerçekten bedava ve uyumuyor (Render gibi servisler 15 dk sonra uyuyor, anlık
sohbet kopar). Kayıt olurken kart istiyor ama sadece kimlik doğrulama için, ücret çekmiyor.

1. https://www.oracle.com/cloud/free/ → hesap aç. Bölge (home region) olarak yakın bir yer seç
   (ör. Frankfurt); sonradan değiştirilemez.
2. **Compute → Instances → Create instance**
   - Image: **Ubuntu 24.04**
   - Shape: **Ampere (VM.Standard.A1.Flex)** — 1 OCPU, 6 GB RAM yeter (bedava sınır 4 OCPU / 24 GB)
   - SSH keys: "Generate a key pair" → **özel anahtarı indir**, `C:\Users\<sen>\.ssh\oracle.key` olarak kaydet
   - "Out of capacity" hatası verirse birkaç saat sonra tekrar dene; Oracle'da bu sık olur.
3. Makinenin **Public IP** adresini not al.
4. **Ağ izni (Oracle tarafı):** Instance sayfası → Subnet → Security List → *Add Ingress Rules*:
   kaynak `0.0.0.0/0`, TCP, hedef port `80` ve `443`.

## 3. Alan adı (bedava): DuckDNS

1. https://www.duckdns.org → GitHub hesabınla gir.
2. Bir alt alan al (ör. `ekip` → `ekip.duckdns.org`), IP olarak Oracle'ın Public IP'sini yaz.

HTTPS sertifikasını Caddy kendisi alır ve yeniler; senin bir şey yapmana gerek yok.

## 4. Sunucuyu kur ve çalıştır

Windows'ta proje klasöründe (dosyaları derler ve sunucuya gönderir):
```
powershell -ExecutionPolicy Bypass -File dagitim\yayinla.ps1 -Sunucu <PUBLIC_IP>
```
İlk seferde bir de sunucuya bağlanıp kurulum betiğini çalıştır (sadece bir kez):
```
ssh -i %USERPROFILE%\.ssh\oracle.key ubuntu@<PUBLIC_IP>
sudo bash /tmp/ekip-kurulum/sunucu-kur.sh ekip.duckdns.org
sudo systemctl start ekip-sunucu
```
Tarayıcıda `https://ekip.duckdns.org` → **"Ekip sunucusu çalışıyor."** yazıyorsa tamam.

Sonraki güncellemelerde sadece `yayinla.ps1` yeter.

## 5. İlk admin

Sunucu boşken **ilk kayıt olan kişi kod gerekmeden kaydolur ve admin olur** — bu sen ol.
Sonra herkes davet koduyla kaydolur: Ekip → **Yönetim → Davet kodları → Kod oluştur**
(kod panoya kopyalanır, arkadaşına gönder).

Admin hesabını kaybedersen sunucuda:
```
sudo systemctl stop ekip-sunucu
sudo -u ekip VeritabaniYolu=/var/lib/ekip/ekip.db /opt/ekip/Ekip.Sunucu --AdminYap kullaniciadin
(Ctrl+C) ; sudo systemctl start ekip-sunucu
```

## 6. Arkadaşlara dağıtım

1. `Ekip.Uygulama\Ayarlar.cs` içindeki `VarsayilanSunucu` değerini `https://ekip.duckdns.org` yap.
2. Yeni sürüm etiketi at (`git tag v1.0.0 && git push --tags`).
3. Release'deki `Ekip-Windows.zip`'i arkadaşlarına gönder; açıp `Ekip.Uygulama.exe`'yi çalıştırsınlar.
   .NET kurmalarına gerek yok (her şey exe'nin içinde).

## 7. Bakım

| İş | Nasıl |
|---|---|
| Sunucu kayıtları | `journalctl -u ekip-sunucu -f` |
| Yeniden başlat | `sudo systemctl restart ekip-sunucu` |
| Yedekler | `/var/lib/ekip/yedek-1.db … yedek-7.db` (haftanın günleri, her gece 04:00) |
| Yedeği geri yükle | servisi durdur → `cp yedek-3.db ekip.db` → başlat |
| Disk/RAM | `htop`, `df -h` |
