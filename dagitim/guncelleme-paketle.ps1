# Otomatik güncelleme paketi hazırlar:
#   yayin/guncelleme/Ekip-<sürüm>.zip  -> uygulamanın kendisi (tek exe)
#   yayin/guncelleme/son.json          -> { surum, indir, sha256, notlar }
# Bu iki dosya aynı klasöre (Cloudflare R2 vb.) yüklenince uygulamalar yeni sürümü kendileri bulur.
#
# Kullanım:  powershell -File dagitim/guncelleme-paketle.ps1 -Surum 1.0.1 -Notlar "Mini sohbet düzeltmeleri"
# GitHub Actions da (surum.yml) aynı betiği kullanır.
param(
    [Parameter(Mandatory = $true)][string]$Surum,
    [string]$Notlar = "",
    [string]$Cikti = "yayin/guncelleme"
)
$ErrorActionPreference = "Stop"
if ($Surum -notmatch '^\d+\.\d+\.\d+$') { throw "Sürüm 1.2.3 biçiminde olmalı (verilen: $Surum)" }

$kok = Split-Path -Parent $PSScriptRoot
$cikti = [IO.Path]::GetFullPath($(if ([IO.Path]::IsPathRooted($Cikti)) { $Cikti } else { Join-Path $kok $Cikti }))
$dosyalar = Join-Path $cikti "dosyalar"
if (Test-Path $cikti) { Remove-Item -LiteralPath $cikti -Recurse -Force }
New-Item -ItemType Directory -Force $dosyalar | Out-Null

# Tek exe, .NET kurulu olmasa da çalışır. pdb (hata ayıklama dosyası) pakete girmez.
dotnet publish (Join-Path $kok "Ekip.Uygulama") -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:Version=$Surum -o $dosyalar -nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Derleme başarısız" }

Add-Type -AssemblyName System.IO.Compression.FileSystem
$zipAdi = "Ekip-$Surum.zip"
$zip = Join-Path $cikti $zipAdi
[IO.Compression.ZipFile]::CreateFromDirectory($dosyalar, $zip, [IO.Compression.CompressionLevel]::Optimal, $false)
$sha = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()

$bilgi = [ordered]@{ surum = $Surum; indir = $zipAdi; sha256 = $sha; notlar = $Notlar }
[IO.File]::WriteAllText((Join-Path $cikti "son.json"), ($bilgi | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
Remove-Item -LiteralPath $dosyalar -Recurse -Force

$mb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Hazır: $zip ($mb MB)"
Write-Host "       $(Join-Path $cikti 'son.json')"
