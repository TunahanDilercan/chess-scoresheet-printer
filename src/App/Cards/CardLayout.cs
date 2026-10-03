using NotasyonOtomasyonu.App.Overlay;

namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Basılacak bir kategori kartı (adet kadar sayfa).</summary>
public sealed record CardSpec(string Category, Color Color, int Copies);

/// <summary>
/// A4 dikey masa kartının ölçüleri (punto). Üst %60: turnuva afişi/logosu (oran korunur; yoksa
/// turnuva adı); alt %40: kategori renginde bant ve büyük kategori adı. GDI baskısı ve PDF aynı
/// hesabı kullanır.
/// </summary>
public static class CardLayout
{
    public const float PageW = 595.28f, PageH = 841.89f;
    public const float Margin = 28f;
    public const float BandRatio = 0.40f;
    public const float LineSpacing = 1.17f;  // DejaVu Sans satır yüksekliği / punto

    public static RectangleF BandRect => new(0, PageH * (1 - BandRatio), PageW, PageH * BandRatio);
    public static RectangleF LogoRect => new(Margin, Margin, PageW - 2 * Margin, PageH * (1 - BandRatio) - 2 * Margin);

    /// <summary>Bant içindeki yazı alanı (kenarlardan pay bırakılır).</summary>
    public static RectangleF BandTextRect
    {
        get { var b = BandRect; return new RectangleF(b.X + 36, b.Y + 30, b.Width - 72, b.Height - 60); }
    }

    /// <summary>Resmi oranını koruyarak kutuya ortalar.</summary>
    public static RectangleF FitImage(SizeF img, RectangleF box)
    {
        if (img.Width <= 0 || img.Height <= 0) return box;
        float s = Math.Min(box.Width / img.Width, box.Height / img.Height);
        float w = img.Width * s, h = img.Height * s;
        return new RectangleF(box.X + (box.Width - w) / 2, box.Y + (box.Height - h) / 2, w, h);
    }

    /// <summary>
    /// Metni kutuya en büyük puntoda sığdırır (sözcük sınırında en çok <paramref name="maxLines"/> satır).
    /// </summary>
    public static (List<string> Lines, float Pt) Fit(string text, RectangleF box, float maxPt, float minPt = 14, int maxLines = 3)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return (new List<string>(), maxPt);
        for (float pt = maxPt; pt >= minPt; pt -= Math.Max(1f, pt * 0.04f))
        {
            var lines = Wrap(words, box.Width, pt);
            if (lines is null || lines.Count > maxLines) continue;
            if (lines.Count * pt * LineSpacing <= box.Height) return (lines, pt);
        }
        return (Wrap(words, float.MaxValue, minPt)!, minPt);
    }

    private static List<string>? Wrap(string[] words, float width, float pt)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var w in words)
        {
            if (TextFitter.MeasureWidth(w, pt, bold: true) > width) return null; // tek sözcük sığmıyor
            var cand = cur.Length == 0 ? w : cur + " " + w;
            if (TextFitter.MeasureWidth(cand, pt, bold: true) <= width) cur = cand;
            else { lines.Add(cur); cur = w; }
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    /// <summary>Zemin rengine göre okunaklı yazı rengi (açık zeminde siyah, koyuda beyaz).</summary>
    public static Color TextColorFor(Color bg)
    {
        double lum = (0.299 * bg.R + 0.587 * bg.G + 0.114 * bg.B) / 255.0;
        return lum > 0.6 ? Color.FromArgb(20, 20, 20) : Color.White;
    }

    public static Color ParseColor(string? hex, Color fallback)
    {
        try { return string.IsNullOrWhiteSpace(hex) ? fallback : ColorTranslator.FromHtml(hex); }
        catch { return fallback; }
    }

    public static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
}
