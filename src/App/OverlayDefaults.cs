using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Yerleşik hazır kağıt şablonları ("Ana Örnek" notasyon kağıdı). Görsel gömülü kaynaktan exe
/// yanına çıkarılır; böylece başka bilgisayarda da çalışır.
///  • <see cref="DefaultName"/> ("Ana Örnek 2"): sahada ayarlanmış güncel yerleşim — VARSAYILAN.
///  • <see cref="LegacyName"/> ("Ana Örnek"): v1.0/v1.1 yerleşimi, ikinci seçenek olarak durur.
/// İkisinde de takım turnuvası için "Kulüp" kutularına takım adı alanları vardır (bireyselde boş).
/// </summary>
public static class OverlayDefaults
{
    public const string DefaultName = "Ana Örnek 2";
    public const string LegacyName = "Ana Örnek";
    private const string ResourceName = "NotasyonOtomasyonu.App.AnaOrnek.png";

    /// <summary>Bu sürümün şablon seti; config'teki değer küçükse yeni varsayılan bir kez eklenir.</summary>
    public const int CurrentTemplateSetVersion = 2;

    /// <summary>
    /// Açılışta çağrılır. Kütüphane boşsa iki yerleşik şablonu ekler ("Ana Örnek 2" etkin).
    /// Eski config'e (şablon seti &lt; 2) yeni varsayılanı bir kez ekleyip etkin yapar ve nüshayı 4'e
    /// çeker; kullanıcının kendi şablonlarına dokunmaz. Arka plan dosyası kaybolmuşsa yeniden bağlar.
    /// </summary>
    public static void EnsureDefaults(AppConfig cfg, string baseDir)
    {
        var bgPath = ExtractBackground(baseDir);

        if (cfg.Templates.Count == 0)
        {
            if (cfg.Overlay.IsConfigured)
            {
                if (string.IsNullOrWhiteSpace(cfg.Overlay.Name)) cfg.Overlay.Name = "Şablonum";
                cfg.Templates.Add(cfg.Overlay.DeepClone());
            }
            else cfg.Templates.Add(BuildLegacy(bgPath));
        }

        if (cfg.TemplateSetVersion < CurrentTemplateSetVersion)
        {
            var def = cfg.Templates.FirstOrDefault(t => t.Name == DefaultName);
            if (def is null)
            {
                def = BuildDefault(bgPath);
                cfg.Templates.Insert(0, def);
            }
            cfg.Overlay = def.DeepClone();               // yeni yerleşim varsayılan
            cfg.Layout.CopiesPerBoard = LayoutConfig.DefaultCopies;
            cfg.TemplateSetVersion = CurrentTemplateSetVersion;
        }

        // Yerleşik şablonların arka plan yolu kırıksa (taşınmış exe) gömülü görseli geri bağla.
        if (bgPath is not null)
        {
            foreach (var t in cfg.Templates.Append(cfg.Overlay))
                if (t.Name is DefaultName or LegacyName &&
                    (string.IsNullOrWhiteSpace(t.BackgroundImagePath) || !File.Exists(t.BackgroundImagePath)))
                    t.BackgroundImagePath = bgPath;
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

    /// <summary>Varsayılan şablonun yeni bir kopyası ("Ana Örnek 2", sıfırlama için).</summary>
    public static OverlayTemplate BuildDefault(string? backgroundPath) => new()
    {
        Name = DefaultName,
        BackgroundImagePath = backgroundPath,
        PrintBackground = false, // hazır kağıda basılır, arka plan çizilmez
        PerPage = 1,
        Fields = DefaultFields()
    };

    /// <summary>Eski yerleşimin yeni bir kopyası ("Ana Örnek").</summary>
    public static OverlayTemplate BuildLegacy(string? backgroundPath) => new()
    {
        Name = LegacyName,
        BackgroundImagePath = backgroundPath,
        PrintBackground = false,
        PerPage = 1,
        Fields = LegacyFields()
    };

    /// <summary>Adına göre yerleşik şablonu yeniden kurar (sıfırlama); yerleşik değilse null.</summary>
    public static OverlayTemplate? BuildBuiltIn(string name, string? backgroundPath) => name switch
    {
        DefaultName => BuildDefault(backgroundPath),
        LegacyName => BuildLegacy(backgroundPath),
        _ => null
    };

    /// <summary>
    /// Sahada (Isparta, Ekim 2026) hazır kağıda göre ayarlanmış yerleşim: yazılar kutuların
    /// etiket sütunundan biraz içeride başlar. Normalize (0..1) koordinatlar.
    /// </summary>
    private static List<OverlayField> DefaultFields() => new()
    {
        F(FieldKind.TournamentName, 0.5333333, 0.1220736, 0.4095238, 0.0267559, 11, false),
        F(FieldKind.Date,           0.1761905, 0.1588629, 0.1714286, 0.0234114, 11, false),
        F(FieldKind.Category,       0.4547619, 0.1605351, 0.1547619, 0.0301003, 11, true),
        F(FieldKind.RoundNo,        0.7071429, 0.1588629, 0.1000000, 0.0301003, 11, true),
        F(FieldKind.BoardNo,        0.8833333, 0.1571906, 0.0571429, 0.0284281, 20, true),
        F(FieldKind.WhiteName,      0.1880952, 0.1939799, 0.4190476, 0.0284281, 11, false),
        F(FieldKind.WhiteRating,    0.7000000, 0.1939799, 0.1071429, 0.0301003, 11, false),
        F(FieldKind.BlackName,      0.1952381, 0.2608696, 0.4142857, 0.0317726, 11, false),
        F(FieldKind.BlackRating,    0.7047619, 0.2642140, 0.1023810, 0.0317726, 11, false),
        // Takım turnuvası: "Kulüp / Club" kutularına takım adı (bireyselde boş kalır)
        F(FieldKind.WhiteTeam,      0.1880952, 0.2250000, 0.4190476, 0.0284281, 9, false, OverflowMode.ShrinkThenEllipsis),
        F(FieldKind.BlackTeam,      0.1952381, 0.2950000, 0.4142857, 0.0284281, 9, false, OverflowMode.ShrinkThenEllipsis),
    };

    /// <summary>v1.0/v1.1 yerleşimi (kullanıcının ilk onayladığı).</summary>
    private static List<OverlayField> LegacyFields() => new()
    {
        F(FieldKind.TournamentName, 0.5047619, 0.1187291, 0.4380952, 0.0284281, 11, false),
        F(FieldKind.Date,           0.1500000, 0.1538462, 0.1952381, 0.0284281, 11, false),
        F(FieldKind.Category,       0.4333333, 0.1555184, 0.1761905, 0.0284281, 11, true),
        F(FieldKind.RoundNo,        0.6809524, 0.1555184, 0.1238095, 0.0284281, 11, true),
        F(FieldKind.BoardNo,        0.8619048, 0.1538462, 0.0761905, 0.0301003, 20, true),
        F(FieldKind.WhiteName,      0.1500000, 0.1923077, 0.4571429, 0.0284281, 11, false),
        F(FieldKind.WhiteRating,    0.6809524, 0.1906355, 0.1285714, 0.0284281, 11, false),
        F(FieldKind.BlackName,      0.1500000, 0.2625418, 0.4595238, 0.0301003, 11, false),
        F(FieldKind.BlackRating,    0.6785714, 0.2608696, 0.1309524, 0.0317726, 11, false),
        F(FieldKind.WhiteTeam,      0.1500000, 0.2250000, 0.4571429, 0.0284281, 9, false, OverflowMode.ShrinkThenEllipsis),
        F(FieldKind.BlackTeam,      0.1500000, 0.2950000, 0.4595238, 0.0284281, 9, false, OverflowMode.ShrinkThenEllipsis),
    };

    private static OverlayField F(FieldKind kind, double x, double y, double w, double h,
                                  double font, bool bold, OverflowMode overflow = OverflowMode.ShrinkThenAbbreviate) => new()
    {
        Kind = kind, X = x, Y = y, W = w, H = h,
        FontSize = font, Bold = bold, Align = HAlign.Left,
        Overflow = overflow, MinFontSize = 6
    };
}
