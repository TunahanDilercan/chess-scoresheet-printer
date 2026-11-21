@echo off
REM Kurulumsuz tek dosya .exe uretir (publish\ klasorune). Surum paketleri icin: tools\release.ps1
REM Eski Windows'ta .NET kurulu olmasa bile calisir.
echo Tek dosya self-contained yayin olusturuluyor...
dotnet publish src\App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -p:EnableCompressionInSingleFile=true ^
  -o publish
echo.
echo Bitti. Calistirilabilir dosya: publish\ChessScoresheetPrinter.exe
echo Ayarlar %%APPDATA%%\ChessScoresheetPrinter klasorunde tutulur (exe yaninda "portable" dosyasi varsa exe klasorunde).
pause
