using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Varsayılan hazır kağıt şablonunu (kullanıcının "Ana Örnek" notasyon kağıdı) hazırlar.
/// Görsel gömülü kaynaktan exe yanına çıkarılır; böylece başka bilgisayarda da çalışır.
/// Kullanıcı hiç ayar yapmazsa bu şablon geçerli olur; düzenleyebilir veya başka ekleyebilir.
/// </summary>
public static class OverlayDefaults
{
    public const string DefaultName = "Ana Örnek";
    private const string ResourceName = "NotasyonOtomasyonu.App.AnaOrnek.png";

    /// <summary>
    /// İlk açılışta çağrılır. Şablon kütüphanesi boşsa Ana Örnek'i ekler ve etkin yapar.
    /// Var olan kullanıcı ayarlarına dokunmaz (sadece arka plan dosyası kaybolmuşsa yeniden çıkarır).
    /// </summary>
    public static void EnsureDefaults(AppConfig cfg, string baseDir)
    {
        var bgPath = ExtractBackground(baseDir);

        // Hiç şablon yoksa kütüphaneyi mevcut Overlay'den ya da varsayılandan kur.
        if (cfg.Templates.Count == 0)
        {
            if (cfg.Overlay.IsConfigured)
            {
                if (string.IsNullOrWhiteSpace(cfg.Overlay.Name)) cfg.Overlay.Name = "Şablonum";
                cfg.Templates.Add(cfg.Overlay.DeepClone());
            }
            else
            {
                var def = BuildAnaOrnek(bgPath);
                cfg.Templates.Add(def);
                cfg.Overlay = def.DeepClone();
            }
        }

        // Etkin şablonun arka plan yolu kırıksa (taşınmış exe) Ana Örnek görselini geri bağla.
        if (cfg.Overlay.IsConfigured &&
            (string.IsNullOrWhiteSpace(cfg.Overlay.BackgroundImagePath) ||
             !File.Exists(cfg.Overlay.BackgroundImagePath)) &&
            bgPath is not null &&
            cfg.Overlay.Name == DefaultName)
        {
            cfg.Overlay.BackgroundImagePath = bgPath;
        }
    }

    /// <summary>Gömülü Ana Örnek görselini exe yanındaki templates klasörüne çıkarır.</summary>
    public static string? ExtractBackground(string baseDir)
    {
        try
        {
            var dir = Path.Combine(baseDir, "templates");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "AnaOrnek.png");

            var asm = System.Reflection.Assembly.GetExecutingAssembly();
            using var s = asm.GetManifestResourceStream(ResourceName);
            if (s is null) return File.Exists(path) ? path : null;

            // Boyut farklıysa (güncel sürüm) yeniden yaz; aynıysa dokunma.
            if (!File.Exists(path) || new FileInfo(path).Length != s.Length)
            {
                using var fs = File.Create(path);
                s.CopyTo(fs);
            }
            return path;
        }
        catch { return null; }
    }

    /// <summary>Yeni boş bir Ana Örnek kopyası (sıfırlama için).</summary>
    public static OverlayTemplate BuildAnaOrnek(string? backgroundPath) => new()
    {
        Name = DefaultName,
        BackgroundImagePath = backgroundPath,
        PrintBackground = false, // hazır kağıda basılır, arka plan çizilmez
        PerPage = 1,
        Fields = DefaultFields()
    };

    /// <summary>
    /// AnaOrnek.png düzenine göre alan kutuları (normalize 0..1). Kullanıcı tasarımcıdan
    /// ince ayar yapabilir; bunlar makul başlangıç konumlarıdır.
    /// </summary>
    // Kullanıcının hazırladığı ve onayladığı yerleşim (publish/config.json'dan gömüldü).
    private static List<OverlayField> DefaultFields() => new()
    {
        // Turnuva Adı (üst, "Turnuva Adı / Tournament Name" hücresi)
        F(FieldKind.TournamentName, 0.5047619, 0.1187291, 0.4380952, 0.0284281, 11, false, HAlign.Left),
        // Tarih ("Tarih / Date")
        F(FieldKind.Date,           0.1500000, 0.1538462, 0.1952381, 0.0284281, 11, false, HAlign.Left),
        // Kategori ("Kategori / Category") — A / B / 8 Yaş …
        F(FieldKind.Category,       0.4333333, 0.1555184, 0.1761905, 0.0284281, 11, true,  HAlign.Left),
        // Tur (Round)
        F(FieldKind.RoundNo,        0.6809524, 0.1555184, 0.1238095, 0.0284281, 11, true,  HAlign.Left),
        // Masa (Board) — sağ üst, büyük
        F(FieldKind.BoardNo,        0.8619048, 0.1538462, 0.0761905, 0.0301003, 20, true,  HAlign.Left),

        // Beyaz oyuncu (Adı S. + Rating)
        F(FieldKind.WhiteName,      0.1500000, 0.1923077, 0.4571429, 0.0284281, 11, false, HAlign.Left),
        F(FieldKind.WhiteRating,    0.6809524, 0.1906355, 0.1285714, 0.0284281, 11, false, HAlign.Left),

        // Siyah oyuncu (Adı S. + Rating)
        F(FieldKind.BlackName,      0.1500000, 0.2625418, 0.4595238, 0.0301003, 11, false, HAlign.Left),
        F(FieldKind.BlackRating,    0.6785714, 0.2608696, 0.1309524, 0.0317726, 11, false, HAlign.Left),
    };

    private static OverlayField F(FieldKind kind, double x, double y, double w, double h,
                                  double font, bool bold, HAlign align) => new()
    {
        Kind = kind, X = x, Y = y, W = w, H = h,
        FontSize = font, Bold = bold, Align = align,
        Overflow = OverflowMode.ShrinkThenAbbreviate, MinFontSize = 6
    };
}
