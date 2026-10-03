using System.Drawing.Text;
using NotasyonOtomasyonu.App.Overlay;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>
/// Kart ve yaka kartlarında kullanılan gömülü yazı tipleri (açık kaynak, SIL OFL; yalnız kalın kesim
/// ve Latin/Türkçe karakterler — exe'yi büyütmez). "DejaVu Sans" notasyon kağıdıyla aynı fonttur.
/// </summary>
public static class CardFonts
{
    public const string Default = "Montserrat";
    public static readonly string[] Embedded = { "Montserrat", "Oswald", "Inter", "DejaVu Sans" };

    /// <summary>Seçilebilir yazı tipleri.</summary>
    public static IReadOnlyList<string> Names => Embedded;

    private static readonly Dictionary<string, FontFamily?> Families = new(StringComparer.OrdinalIgnoreCase);
    private static readonly List<PrivateFontCollection> Keep = new(); // koleksiyonlar uygulama boyunca yaşamalı
    private static readonly Bitmap MeasureBmp = new(2, 2);

    private static FontFamily? Family(string name)
    {
        lock (Families)
        {
            if (Families.TryGetValue(name, out var f)) return f;
            f = null;
            var res = $"NotasyonOtomasyonu.App.fonts.{name.Replace(" ", "")}-Bold.ttf";
            using var s = typeof(CardFonts).Assembly.GetManifestResourceStream(res);
            if (s is not null)
            {
                var data = new byte[s.Length];
                s.ReadExactly(data);
                var pfc = new PrivateFontCollection();
                var ptr = System.Runtime.InteropServices.Marshal.AllocCoTaskMem(data.Length);
                System.Runtime.InteropServices.Marshal.Copy(data, 0, ptr, data.Length);
                pfc.AddMemoryFont(ptr, data.Length);
                Keep.Add(pfc);
                f = pfc.Families.FirstOrDefault();
            }
            Families[name] = f;
            return f;
        }
    }

    /// <summary>Kalın font (punto); bilinmeyen ad ya da DejaVu için notasyon kağıdının kalın fontu.</summary>
    public static Font Create(string? name, float pt)
    {
        var fam = string.IsNullOrWhiteSpace(name) || name == "DejaVu Sans" ? null : Family(name);
        if (fam is null) return TextFitter.CreateFont(pt, bold: true);
        var style = fam.IsStyleAvailable(FontStyle.Bold) ? FontStyle.Bold : FontStyle.Regular;
        return new Font(fam, pt, style, GraphicsUnit.Point);
    }

    private const float RefPt = 100f;
    private static readonly Dictionary<(string Font, string Text), float> Widths = new();
    private static readonly Dictionary<string, float> Spacings = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Metin genişliği (punto). Genişlik puntoyla doğrusal olduğundan her metin 100 puntoda bir kez
    /// ölçülüp ölçeklenir (yazı sığdırma yüzlerce ölçüm yapar; her seferinde font üretmek yavaştı).
    /// </summary>
    public static float MeasureWidth(string? name, string text, float pt)
    {
        var key = (name ?? "", text);
        lock (Widths)
        {
            if (!Widths.TryGetValue(key, out var w100))
            {
                using var g = Graphics.FromImage(MeasureBmp);
                g.PageUnit = GraphicsUnit.Point;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                using var font = Create(name, RefPt);
                w100 = g.MeasureString(text, font, int.MaxValue, StringFormat.GenericTypographic).Width;
                if (Widths.Count > 20000) Widths.Clear();
                Widths[key] = w100;
            }
            return w100 * pt / RefPt;
        }
    }

    /// <summary>Satır yüksekliği / punto oranı (fontun kendi aralığı).</summary>
    public static float LineSpacing(string? name)
    {
        lock (Spacings)
        {
            if (Spacings.TryGetValue(name ?? "", out var s)) return s;
            using var font = Create(name, RefPt);
            var fam = font.FontFamily;
            return Spacings[name ?? ""] = fam.GetLineSpacing(font.Style) / (float)fam.GetEmHeight(font.Style);
        }
    }
}
