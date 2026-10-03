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
- Kategori bazlı sistem desteği: İsviçre, Berger (döner) ve takım turnuvaları; sistem kategorinin "Turnuva Tipi" bilgisinden okunur
- Takım maçlarında "1.2" gibi masa numarası ve takım adları (kâğıttaki Kulüp kutularına)
- Her kategorinin son turunu gösteren hızlı erişim düğmeleri
- JSON, CSV, TXT ve XLSX dosyalarını içe aktarma
- Basılacak masaları listeden işaretleyerek seçme (seçim her kategori ve tur için ayrı hatırlanır)
- Tur ve kategoriye göre toplu yazdırma
- Hazır notasyon kâğıtları için baskı şablonları oluşturma
- Alan konumlarını ve metin boyutlarını düzenleme
- Yazıcı hizalama (mm) ve hizalama test sayfası
- Masa hariç tutma, nüsha ve sayfa boyutu ayarları
- Kâğıdın soluk göründüğü baskı önizlemesi ve PDF çıktısı

## Kullanım

1. Chess-Results üzerinden bir turnuva seçin veya eşleştirme dosyasını açın. İl seçildiğinde o ilin en son eklenen turnuvası kendiliğinden açılır; arama ve link/no ile de getirilebilir.
2. Kategori ve turu seçin ya da hızlı erişim düğmesine basın (ör. "7 Yaş · T2"). Eşleştirmeler sağdaki listeye kendiliğinden gelir; varsayılan tur, eşleştirmesi yayımlanmış son turdur (Berger'de sonucu girilmemiş ilk tur).
3. Listede basılmayacak masaların işaretini kaldırın ve nüsha sayısını belirleyin (varsayılan 4). "Tüm Kategoriler" de her kategoride yaptığınız seçime uyar.
4. **Yazdır** (Ctrl+P) ile önizlemeyi açın. Önizlemede hazır kâğıt soluk görünür ama basılmaz. Yazıcıyı seçip basın; ardından sıradaki kategori kendiliğinden yüklenir.

Yazılar hazır kâğıttaki kutulara göre kayıksa **Ayarlar → Yazıcı hizalama** bölümünden milimetre cinsinden düzeltin ve **Hizalama testi** ile tek kâğıt basarak kontrol edin; şablonu değiştirmeniz gerekmez. Turnuva yeri, hakem ve zaman kontrolü bilgileri bilinçli olarak kâğıda basılmaz.

Şablon düzenleyicisinde turnuva, tarih, kategori, tur, masa, oyuncu ve takım alanlarının konumu ayrı ayrı ayarlanabilir. Varsayılan şablon "Ana Örnek 2"dir; önceki yerleşim "Ana Örnek" adıyla durur. **Ayarlar → Sisteme göre şablon** ile İsviçre, Berger ve takım kategorileri için farklı şablon seçilebilir.

<p align="center">
  <img src="docs/images/template-designer.png" alt="Hazır kâğıt şablonu tasarımcısı" width="720">
</p>

## İndirme

Windows x64 paketi [Releases](https://github.com/TunahanDilercan/chess-scoresheet-printer/releases/latest) sayfasından indirilebilir. ZIP arşivini çıkardıktan sonra `ChessScoresheetPrinter.exe` çalıştırılabilir. Paket .NET çalışma zamanını içerir ve Windows 10 ile Windows 11 için hazırlanmıştır.

Yayımlanan dosya kod imzalı değildir. Windows ilk çalıştırmada SmartScreen uyarısı gösterebilir.
