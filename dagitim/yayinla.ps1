# Sunucunun yeni sürümünü derleyip Oracle sunucusuna gönderir ve yeniden başlatır. Windows'ta çalıştır.
# Kullanım:  powershell -File dagitim\yayinla.ps1 -Sunucu 123.45.67.89
# İlk seferde sunucuda bir kez:  sudo bash /tmp/ekip-kurulum/sunucu-kur.sh <alan-adi>  (bu betik dosyaları oraya da koyar)
param(
    [Parameter(Mandatory)][string]$Sunucu,
    [string]$Kullanici = "ubuntu",
    [string]$Anahtar = "$HOME\.ssh\oracle.key"   # Oracle'ın VM oluştururken verdiği özel anahtar
)
$ErrorActionPreference = "Stop"
$kok = Split-Path $PSScriptRoot -Parent
$cikti = Join-Path $kok "yayin\sunucu"

Write-Host "==> Derleniyor (Linux ARM64, .NET kurulumu gerektirmeyen tek dosya)"
if (Test-Path $cikti) { Remove-Item $cikti -Recurse -Force }
dotnet publish (Join-Path $kok "Ekip.Sunucu") -c Release -r linux-arm64 --self-contained -p:PublishSingleFile=true -o $cikti
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız." }

$hedef = "$Kullanici@$Sunucu"
Write-Host "==> Gönderiliyor: $hedef"
ssh -i $Anahtar $hedef "mkdir -p /tmp/ekip /tmp/ekip-kurulum"
scp -i $Anahtar -r "$cikti\*" "${hedef}:/tmp/ekip/"
scp -i $Anahtar "$PSScriptRoot\sunucu-kur.sh" "$PSScriptRoot\ekip-sunucu.service" "${hedef}:/tmp/ekip-kurulum/"

Write-Host "==> Yeniden başlatılıyor"
ssh -i $Anahtar $hedef "sudo systemctl stop ekip-sunucu 2>/dev/null; sudo cp -r /tmp/ekip/* /opt/ekip/ && sudo chown -R ekip:ekip /opt/ekip && sudo chmod +x /opt/ekip/Ekip.Sunucu && sudo systemctl start ekip-sunucu && sleep 2 && systemctl is-active ekip-sunucu"
Write-Host "Bitti."
