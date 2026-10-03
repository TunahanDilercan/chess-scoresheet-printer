namespace NotasyonOtomasyonu.App.Cards;

/// <summary>Basılacak bir kategori kartı (adet kadar sayfa); <paramref name="Color"/> kategorinin tema rengi.</summary>
public sealed record CardSpec(string Category, Color Color, int Copies);

/// <summary>
/// A4 masa kartının ölçüleri (punto). Varsayılan yatay. Üst %60: turnuva afişi/logosu (oran korunur;
/// yoksa turnuva adı); alt %40: büyük kategori adı — stile göre beyaz zeminde tema renginde yazı ya da
/// tema renginde zeminde beyaz yazı. Önizleme, yazıcı ve PDF aynı hesabı kullanır.
/// </summary>
public sealed class CardLayout
{
    public const float A4Short = 595.28f, A4Long = 841.89f;
    public const float Margin = 28f;
    public const float BandRatio = 0.40f;

    public bool Landscape { get; }
    public float PageW => Landscape ? A4Long : A4Short;
    public float PageH => Landscape ? A4Short : A4Long;

    public CardLayout(bool landscape) => Landscape = landscape;

    public RectangleF BandRect => new(0, PageH * (1 - BandRatio), PageW, PageH * BandRatio);
    public RectangleF LogoRect => new(Margin, Margin, PageW - 2 * Margin, PageH * (1 - BandRatio) - 2 * Margin);

    /// <summary>Bant içindeki yazı alanı (kenarlardan pay bırakılır).</summary>
    public RectangleF BandTextRect
    {
        get { var b = BandRect; return new RectangleF(b.X + 36, b.Y + 22, b.Width - 72, b.Height - 44); }
    }

    /// <summary>Üst alanın tamamı (kenardan kenara afiş için).</summary>
    public RectangleF HeaderRect => new(0, 0, PageW, PageH * (1 - BandRatio));

    /// <summary>Afiş yuvasının en-boy oranı (kırpma aracı bu orana kilitlenir).</summary>
    public float LogoAspect => LogoRect.Width / LogoRect.Height;

    /// <summary>Tam genişlik modunda afiş alanının en-boy oranı.</summary>
    public float HeaderAspect => HeaderRect.Width / HeaderRect.Height;

    /// <summary>
    /// Afişi çizer. Normal: kenar boşluklu yuvaya oranı korunarak sığdırılır. Tam genişlik: sayfanın tüm
    /// genişliğine yayılır (oran korunur); yüksekliği alanı aşarsa ortadan kırpılır, kısaysa dikeyde ortalanır.
    /// </summary>
    public void DrawLogo(Graphics g, Image logo, bool fullWidth)
    {
        if (!fullWidth)
        {
            g.DrawImage(logo, FitImage(new SizeF(logo.Width, logo.Height), LogoRect));
            return;
        }
        var area = HeaderRect;
        float h = logo.Height * (area.Width / logo.Width);
        var dest = new RectangleF(area.X, area.Y + (area.Height - h) / 2, area.Width, h);
        var state = g.Save();
        g.SetClip(area);
        g.DrawImage(logo, dest);
        g.Restore(state);
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
    public static (List<string> Lines, float Pt) Fit(string text, RectangleF box, float maxPt, string? font,
                                                      float minPt = 12, int maxLines = 3)
    {
        var words = (text ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return (new List<string>(), maxPt);
        float spacing = CardFonts.LineSpacing(font);
        for (float pt = maxPt; pt >= minPt; pt -= Math.Max(1f, pt * 0.04f))
        {
            var lines = Wrap(words, box.Width, pt, font);
            if (lines is null || lines.Count > maxLines) continue;
            if (lines.Count * pt * spacing <= box.Height) return (lines, pt);
        }
        return (Wrap(words, float.MaxValue, minPt, font)!, minPt);
    }

    private static List<string>? Wrap(string[] words, float width, float pt, string? font)
    {
        var lines = new List<string>();
        var cur = "";
        foreach (var w in words)
        {
            if (CardFonts.MeasureWidth(font, w, pt) > width) return null; // tek sözcük sığmıyor
            var cand = cur.Length == 0 ? w : cur + " " + w;
            if (CardFonts.MeasureWidth(font, cand, pt) <= width) cur = cand;
            else { lines.Add(cur); cur = w; }
        }
        if (cur.Length > 0) lines.Add(cur);
        return lines;
    }

    /// <summary>Satırları kutuda dikey ortalı, yatay ortalı çizer.</summary>
    public static void DrawLines(Graphics g, (List<string> Lines, float Pt) fit, RectangleF box, Color color, string? font)
    {
        using var f = CardFonts.Create(font, fit.Pt);
        using var brush = new SolidBrush(color);
        using var fmt = new StringFormat(StringFormat.GenericTypographic)
        { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
        float lh = fit.Pt * CardFonts.LineSpacing(font);
        float y = box.Y + (box.Height - lh * fit.Lines.Count) / 2;
        foreach (var line in fit.Lines)
        {
            g.DrawString(line, f, brush, new RectangleF(box.X - 50, y, box.Width + 100, lh), fmt);
            y += lh;
        }
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
