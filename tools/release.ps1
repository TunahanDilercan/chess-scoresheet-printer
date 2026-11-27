# Sürüm paketlerini hazırlar (yüklemez). Kullanım:
#   powershell -File tools\release.ps1 -Version 1.7.0 -Notes publish\release-v1.7.0\RELEASE_NOTES_v1.7.0.md [-Vpk C:\araclar\vpk.exe]
#
# Üretilenler (publish\release-vX.Y.Z\upload):
#   ChessScoresheetPrinter-win-Setup.exe       ilk kurulum (masaüstü + Başlat menüsü kısayolu)
#   ChessScoresheetPrinter-X.Y.Z-full.nupkg    tam paket
#   ChessScoresheetPrinter-X.Y.Z-delta.nupkg   önceki sürümden yalnız değişenler (kurulu sürümler bunu indirir)
#   releases.win.json, assets.win.json, RELEASES   Velopack paket listesi
#   ChessScoresheetPrinter-win-Portable.zip    kurulumsuz taşınabilir sürüm (kendini günceller)
#   ChessScoresheetPrinter-vX.Y.Z-win-x64.zip  tek exe (v1.6.x'ten güncellenenler için köprü)
#   ChessScoresheetPrinter-vX.Y.Z-win-x64.sha256   ilk satır ZIP (eski sürümler bunu okur), ikinci satır Setup
#
# Fark paketi için önceki sürüm GitHub'dan indirilir (vpk download github). vpk: dotnet tool install vpk.
param(
    [Parameter(Mandatory)] [string]$Version,
    [Parameter(Mandatory)] [string]$Notes,
    [string]$Vpk = "vpk",
    [string]$Repo = "https://github.com/TunahanDilercan/chess-scoresheet-printer"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$out = Join-Path $root "publish\release-v$Version"
$folder = Join-Path $out "app"          # Velopack: klasör hâlinde yayın
$single = Join-Path $out "single"       # köprü: tek exe
$velo = Join-Path $out "velopack"
$upload = Join-Path $out "upload"
foreach ($d in $folder, $single, $velo, $upload) {
    if ((Test-Path $d) -and (Get-ChildItem $d -Force | Select-Object -First 1)) { throw "$d boş değil; yeni bir sürüm numarası ya da boş klasör kullanın." }
    New-Item -ItemType Directory -Force $d | Out-Null
}
$csproj = Join-Path $root "src\App\App.csproj"
$notesFull = (Resolve-Path $Notes).Path

Write-Host "1/5 Klasör yayını"
dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -o $folder | Out-Host
if ($LASTEXITCODE) { throw "dotnet publish (klasör) başarısız" }

Write-Host "2/5 Önceki sürüm (fark paketi için)"
& $Vpk download github --skip-updates -x --repoUrl $Repo -o $velo | Out-Host
if ($LASTEXITCODE) { Write-Warning "Önceki Velopack sürümü bulunamadı: bu sürümde fark paketi olmayacak (ilk Velopack sürümü)." }

Write-Host "3/5 Velopack paketi"
& $Vpk pack --skip-updates -x -u ChessScoresheetPrinter -v $Version -p $folder -e ChessScoresheetPrinter.exe `
    --packTitle "Notasyon Yazdırıcı" --packAuthors "Tunahan Dilercan" -i (Join-Path $root "assets\app.ico") `
    --releaseNotes $notesFull -o $velo | Out-Host
if ($LASTEXITCODE) { throw "vpk pack başarısız" }

Write-Host "4/5 Tek exe (köprü ZIP)"
dotnet publish $csproj -c Release -r win-x64 --self-contained true -p:Version=$Version -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o $single | Out-Host
if ($LASTEXITCODE) { throw "dotnet publish (tek exe) başarısız" }
$zipName = "ChessScoresheetPrinter-v$Version-win-x64"
$stage = Join-Path $out $zipName
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $single "ChessScoresheetPrinter.exe") $stage
Copy-Item (Join-Path $single "LatoFont") $stage -Recurse
Compress-Archive -Path "$stage\*" -DestinationPath (Join-Path $upload "$zipName.zip")

Write-Host "5/5 Yüklenecek dosyalar ve özetler"
$mine = Get-ChildItem $velo -File | Where-Object {
    $_.Name -in @("releases.win.json", "assets.win.json", "RELEASES", "ChessScoresheetPrinter-win-Setup.exe", "ChessScoresheetPrinter-win-Portable.zip") -or
    $_.Name -like "ChessScoresheetPrinter-$Version-*.nupkg" }
$mine | Copy-Item -Destination $upload
$setup = "ChessScoresheetPrinter-win-Setup.exe"
$h = { param($f) (Get-FileHash (Join-Path $upload $f) -Algorithm SHA256).Hash.ToLower() }
[IO.File]::WriteAllText((Join-Path $upload "$zipName.sha256"), "$(& $h "$zipName.zip")  $zipName.zip`n$(& $h $setup)  $setup`n")
Get-ChildItem $upload | Sort-Object Name | ForEach-Object { "{0,-50} {1,10:N1} MB" -f $_.Name, ($_.Length / 1MB) } | Out-Host
Write-Host "Hazır: $upload"
