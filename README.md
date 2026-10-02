# Chess Scoresheet Printer

Türkçe | [English](README.en.md)

[![CI](https://github.com/TunahanDilercan/chess-scoresheet-printer/actions/workflows/ci.yml/badge.svg)](https://github.com/TunahanDilercan/chess-scoresheet-printer/actions/workflows/ci.yml)
[![Sürüm](https://img.shields.io/github/v/release/TunahanDilercan/chess-scoresheet-printer?label=s%C3%BCr%C3%BCm)](https://github.com/TunahanDilercan/chess-scoresheet-printer/releases/latest)

Chess Scoresheet Printer, turnuva eşleştirme bilgilerini hazır basılı satranç notasyon kâğıtlarına yerleştirmek ve yazdırmak için hazırlanmış bir Windows masaüstü aracıdır. Chess-Results ve Swiss-Manager çıktılarıyla çalışır.

Uygulama, iki TSF il temsilciliğinde yürütülen turnuvalarda kullanılmaktadır. Bağımsız bir projedir ve Türkiye Satranç Federasyonunun resmî uygulaması değildir.

<p align="center">
  <img src="docs/images/main-window.png" alt="Chess Scoresheet Printer ana penceresi" width="760">
</p>

## Temel işlevler

- Chess-Results üzerinden turnuva arama ve eşleştirme alma (BAY ve eşlenmeyen oyuncular ayrıştırılır)
- JSON, CSV, TXT ve XLSX dosyalarını içe aktarma
- Basılacak masaları listeden işaretleyerek seçme
- Tur ve kategoriye göre toplu yazdırma
- Hazır notasyon kâğıtları için baskı şablonları oluşturma
- Alan konumlarını ve metin boyutlarını düzenleme
- Yazıcı hizalama (mm) ve hizalama test sayfası
- Masa hariç tutma, nüsha ve sayfa boyutu ayarları
- Kâğıdın soluk göründüğü baskı önizlemesi ve PDF çıktısı

## Kullanım

1. Chess-Results üzerinden bir turnuva seçin (il, arama ya da link/no ile) veya eşleştirme dosyasını açın.
2. Kategori ve turu seçin. Eşleştirmeler sağdaki listeye kendiliğinden gelir; varsayılan tur, eşleştirmesi yayımlanmış son turdur.
3. Listede basılmayacak masaların işaretini kaldırın ve nüsha sayısını belirleyin.
4. **Yazdır** (Ctrl+P) ile önizlemeyi açın. Önizlemede hazır kâğıt soluk görünür ama basılmaz. Yazıcıyı seçip basın; ardından sıradaki kategori kendiliğinden yüklenir.

Yazılar hazır kâğıttaki kutulara göre kayıksa **Ayarlar → Yazıcı hizalama** bölümünden milimetre cinsinden düzeltin ve **Hizalama testi** ile tek kâğıt basarak kontrol edin; şablonu değiştirmeniz gerekmez. Turnuva yeri, hakem ve zaman kontrolü bilgileri bilinçli olarak kâğıda basılmaz.

Şablon düzenleyicisinde turnuva, tarih, kategori, tur, masa ve oyuncu alanlarının konumu ayrı ayrı ayarlanabilir.

<p align="center">
  <img src="docs/images/template-designer.png" alt="Hazır kâğıt şablonu tasarımcısı" width="720">
</p>

## İndirme

Windows x64 paketi [Releases](https://github.com/TunahanDilercan/chess-scoresheet-printer/releases/latest) sayfasından indirilebilir. ZIP arşivini çıkardıktan sonra `ChessScoresheetPrinter.exe` çalıştırılabilir. Paket .NET çalışma zamanını içerir ve Windows 10 ile Windows 11 için hazırlanmıştır.

Yayımlanan dosya kod imzalı değildir. Windows ilk çalıştırmada SmartScreen uyarısı gösterebilir.
