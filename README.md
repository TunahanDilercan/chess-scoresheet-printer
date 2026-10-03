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
- Her kategorinin son turunu gösteren, tek tıkla o turu basan hızlı erişim düğmeleri (F5 hepsini yeniler)
- BAY masası varsayılan olarak basılmaz; yalnızca listede elle işaretlenirse basılır
- Turnuva yönergesi, raporu ve teknik toplantı tutanağı: gömülü şablonlar ya da kendi .docx şablonunuz chess-results verisi ve ilin TSF sitesindeki yönergeyle doldurulur; tüm alanlar tek tabloda düzenlenir; PDF, yazıcı veya Word çıktısı
- Tempoya göre resmi düşünme süresi önerisi (klasik, hızlı, yıldırım)
- TSF prosedürüne göre program ve kategori kontrolü: tempoya göre iki tur arası en az süre (ör. 35+30 → 150 dk), günlük tur sınırı (UKD 4, ELO 3; 2400+ ELO'da 90+30 ve 2 tur), 12 saat sınırı, kayıt kontrol süresi (30–60 dk), sporcu sayısına göre sistem/tur sayısı, kategorilerde tempo birliği, "Yaş ve Altı" kategorilerinin çift yaşta bitmesi; "Programı öner" ile tur saatleri otomatik hesaplanır
- Kategori masa kartları (A4 yatay): üstte turnuva afişi, altta büyük kategori adı; her kategorinin tema rengi ve iki stil (yazı renkli / beyaz zemin ya da zemin renkli / beyaz yazı); afiş kırpma aracı (kart yuvası, 16:9, 4:3, 1:1 ya da serbest) ve kenardan kenara tam genişlik seçeneği; gömülü yazı tipleri (Montserrat, Oswald, Inter); kategori adı tüm kartlarda aynı, hepsine sığan en büyük puntoda; adet kategorideki masa sayısı kadar önerilir
- Hakem yaka kartları (85×54 veya 90×60 mm, A4'e 2×5 / 2×4 dizili, kesim işaretli): logo, fotoğraf, ad soyad, görev ve unvana göre renkli şerit
- Yazıcı ayarları: hedef yazıcı, tepsi, kopya sayısı, yazıcının kendi tercihleri (sessiz mod, kalite) ve önizlemesiz "doğrudan yazdır"
- JSON, CSV, TXT ve XLSX dosyalarını içe aktarma
- Basılacak masaları listeden işaretleyerek seçme (seçim her kategori ve tur için ayrı hatırlanır)
- Tur ve kategoriye göre toplu yazdırma
- Hazır notasyon kâğıtları için baskı şablonları oluşturma
- Alan konumlarını ve metin boyutlarını düzenleme
- Yazıcı hizalama (mm) ve hizalama test sayfası
- Masa hariç tutma, nüsha ve sayfa boyutu ayarları
- Kâğıdın soluk göründüğü baskı önizlemesi ve PDF çıktısı

## Kullanım

1. Chess-Results üzerinden bir turnuva seçin veya eşleştirme dosyasını açın. İl seçildiğinde o ilin en son eklenen turnuvası kendiliğinden açılır; arama ve link/no ile de getirilebilir. **Ayarlar → Varsayılan il** seçiliyse program açılırken o il seçili gelir. İl listesinde harfe basmak o harfle başlayan ile, tekrar basmak sıradakine götürür.
2. Kategori ve turu seçin. Eşleştirmeler sağdaki listeye kendiliğinden gelir; varsayılan tur, eşleştirmesi yayımlanmış son turdur (Berger'de sonucu girilmemiş ilk tur). Hızlı erişim düğmesi (ör. "🖨 7 Yaş · T2") o kategorinin son turunu doğrudan baskıya gönderir. Yeni tur yayımlanınca F5 ile tüm düğmeler güncellenir.
3. Listede basılmayacak masaların işaretini kaldırın ve nüsha sayısını belirleyin (varsayılan 4). Seçimler hatırlanır; "Tüm Kategoriler" ve hızlı erişim düğmeleri de bu seçime uyar.
4. **Yazdır** (Ctrl+P) ile önizlemeyi açın. Önizlemede hazır kâğıt soluk görünür ama basılmaz. Yazıcıyı seçip basın; baskıdan sonra seçimler ve filtreler olduğu gibi kalır.

Başlıktaki **📄 Yönerge / Tutanak** düğmesi turnuva bilgilerini (tarih, yer, sistem, düşünme süresi, kategoriler, direktör, başhakem, hakemler, tur programı) chess-results'tan alır. Varsayılan ilin TSF sitesindeki (ör. isparta.tsf.org.tr) yönerge de bulunur ve içindeki son başvuru, iletişim, organizasyon ve program saatleri okunur. Hazır şablonlar "Turnuva Yönergesi" ve "Teknik Toplantı Tutanağı"dır; aynı turnuva için girilen bilgiler tutanağa da aktarılır. Tüm alanlar tek tabloda düzenlenir, her alanın kaynağı yanında yazar. Kendi .docx şablonunuzu yükleyebilirsiniz: `{{IL}}`, `{{YER}}` gibi işaretler, Word yer imleri, içerik denetimleri ya da "İLİ | …" biçimindeki etiketli tablo hücreleri doldurulur; belgenin biçimi, tabloları ve kilitli alanları korunur. "Önizle ve Yazdır" belgeyi yazdırmadan önce sayfa sayfa gösterir; yazıcıya gönderme ve PDF kaydetme önizlemeden yapılır. PDF'e dönüştürme, önizleme ve yazdırma için Microsoft Word gerekir; Word yoksa belge .docx olarak kaydedilir.

**🏷 Masa Kartları** her kategori için A4 (varsayılan yatay) kart basar. Her kategoriye ayrı bir renk atanır ve hatırlanır; zemin ve yazı rengi tablodan değiştirilebilir.

**🪪 Yaka Kartları** görevlileri chess-results'tan alır; unvan IA/FA/NA bilgisinden önerilir, TSF derecesi (Aday, İl, Ulusal, FIDE, Uluslararası) seçilebilir ve hatırlanır.

**Ayarlar → Yazıcı** bölümünden hedef yazıcı, kağıt kaynağı ve kopya sayısı ayarlanır. **Yazıcı tercihleri…** yazıcının kendi ayar penceresini açar (sessiz mod, baskı kalitesi gibi üreticiye özel seçenekler); seçilenler her baskıda uygulanır. **Doğrudan yazdır** açıkken önizleme ve yazıcı penceresi açılmadan basılır.

Yazılar hazır kâğıttaki kutulara göre kayıksa **Ayarlar → Yazıcı hizalama** bölümünden milimetre cinsinden düzeltin ve **Hizalama testi** ile tek kâğıt basarak kontrol edin; şablonu değiştirmeniz gerekmez. Turnuva yeri, hakem ve zaman kontrolü bilgileri bilinçli olarak kâğıda basılmaz.

Şablon düzenleyicisinde turnuva, tarih, kategori, tur, masa, oyuncu ve takım alanlarının konumu ayrı ayrı ayarlanabilir. Varsayılan şablon "Ana Örnek 2"dir; önceki yerleşim "Ana Örnek" adıyla durur. **Ayarlar → Sisteme göre şablon** ile İsviçre, Berger ve takım kategorileri için farklı şablon seçilebilir.

<p align="center">
  <img src="docs/images/template-designer.png" alt="Hazır kâğıt şablonu tasarımcısı" width="720">
</p>

## İndirme

Windows x64 paketi [Releases](https://github.com/TunahanDilercan/chess-scoresheet-printer/releases/latest) sayfasından indirilebilir. ZIP arşivini çıkardıktan sonra `ChessScoresheetPrinter.exe` çalıştırılabilir. Paket .NET çalışma zamanını içerir ve Windows 10 ile Windows 11 için hazırlanmıştır.

Yayımlanan dosya kod imzalı değildir. Windows ilk çalıştırmada SmartScreen uyarısı gösterebilir.
