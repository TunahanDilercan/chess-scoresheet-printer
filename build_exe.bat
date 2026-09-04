@echo off
REM Kurulumsuz tek dosya .exe uretir (publish\ klasorune).
REM Eski Windows'ta .NET kurulu olmasa bile calisir.
echo Tek dosya self-contained yayin olusturuluyor...
dotnet publish src\App -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true ^
  -o publish
echo.
echo Bitti. Calistirilabilir dosya: publish\ChessScoresheetPrinter.exe
echo config.json dosyasini exe ile ayni klasorde tutun.
pause
