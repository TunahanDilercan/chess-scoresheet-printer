using System.Drawing.Drawing2D;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Kartlarda kullanılan görselleri yükler. Dosya kilitlenmesin diye bellekte kopyalanır; çok büyük görseller
/// (telefon fotoğrafı, 6000 piksellik afiş) baskıda gerekenden fazlasıyla küçültülür: her kart çiziminde
/// yeniden ölçeklemek önizlemeyi ve baskıyı birkaç kat yavaşlatıyordu.
/// </summary>
public static class ImageFiles
{
    /// <summary>Varsayılan üst sınır: 190 mm'lik isimlik bile 300 dpi'da ~2250 piksel ister.</summary>
    public const int DefaultMaxPx = 2400;

    public static Bitmap? Load(string? path, int maxPx = DefaultMaxPx)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            using var src = Image.FromStream(fs);
            return Bounded(src, maxPx);
        }
        catch { return null; }
    }

    /// <summary>Uzun kenarı <paramref name="maxPx"/>'i aşıyorsa oran korunarak küçültülmüş kopya; değilse birebir kopya.</summary>
    public static Bitmap Bounded(Image src, int maxPx = DefaultMaxPx)
    {
        int longest = Math.Max(src.Width, src.Height);
        if (maxPx <= 0 || longest <= maxPx) return new Bitmap(src);
        double k = maxPx / (double)longest;
        int w = Math.Max(1, (int)Math.Round(src.Width * k)), h = Math.Max(1, (int)Math.Round(src.Height * k));
        var dst = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        dst.SetResolution(src.HorizontalResolution, src.VerticalResolution);
        using var g = Graphics.FromImage(dst);
        g.CompositingMode = CompositingMode.SourceCopy;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.SmoothingMode = SmoothingMode.HighQuality;
        using var wrap = new System.Drawing.Imaging.ImageAttributes();
        wrap.SetWrapMode(WrapMode.TileFlipXY);   // kenarlarda ince koyu çizgi olmasın
        g.DrawImage(src, new Rectangle(0, 0, w, h), 0, 0, src.Width, src.Height, GraphicsUnit.Pixel, wrap);
        return dst;
    }
}
