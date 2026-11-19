# GIF anahtarini Cloudflare'a gizli degisken (GIF_ANAHTARI) olarak kaydeder.
# Anahtar ekrana, dosyaya ya da gecmise yazilmaz; dogrudan wrangler'a aktarilir.
# Kullanim: dagitim\gif-anahtari.ps1 dosyasina sag tik > "PowerShell ile calistir"
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..\Ekip.Bulut")
$env:NODE_OPTIONS = "--dns-result-order=ipv4first"

Write-Host ""
Write-Host " GIPHY anahtarini yapistir (sag tik ya da Ctrl+V), sonra Enter'a bas:" -ForegroundColor Cyan
$g = Read-Host -AsSecureString " Anahtar"
$b = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($g)
try { $k = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($b).Trim() }
finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($b) }

if (-not $k) {
    Write-Host " Anahtar bos geldi, hicbir sey yapilmadi." -ForegroundColor Red
}
else {
    # Bu bilgisayarda Cloudflare'a baglanti ara ara zaman asimina dusuyor: birkac kez dene.
    $tamam = $false
    for ($i = 1; $i -le 5 -and -not $tamam; $i++) {
        Write-Host " Cloudflare'a kaydediliyor... (deneme $i/5)" -ForegroundColor Cyan
        $k | npx wrangler secret put GIF_ANAHTARI
        if ($LASTEXITCODE -eq 0) { $tamam = $true }
        elseif ($i -lt 5) { Write-Host " Baglanti koptu, 5 saniye sonra tekrar deneniyor..." -ForegroundColor Yellow; Start-Sleep 5 }
    }
    if ($tamam) { Write-Host " Tamam, anahtar kaydedildi." -ForegroundColor Green }
    else { Write-Host " 5 denemede de baglanamadi. Modemi yeniden baslatip tekrar dene ya da bu ekrani gonder." -ForegroundColor Red }
}
$k = $null
Read-Host " Kapatmak icin Enter"
