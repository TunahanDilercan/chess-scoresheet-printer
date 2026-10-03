using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using RenderInit = NotasyonOtomasyonu.Render.QuestPdfRenderer;
using Color = System.Drawing.Color;
using ImageFormat = System.Drawing.Imaging.ImageFormat;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Yazıcıya giden GDI çizimini aynen PDF'e aktarır (sayfa yüksek çözünürlükte resme çizilir):
/// önizleme, baskı ve PDF birebir aynı olur. Aynı içerikli sayfalar PDF'e bir kez gömülür.
/// </summary>
public static class GdiPdf
{
    /// <param name="pageKeys">Her sayfanın içerik anahtarı (aynı anahtar = aynı resim, tekrar çizilmez).</param>
    /// <param name="draw">Sayfayı punto koordinatlarında çizer (0,0 = kağıdın sol üstü).</param>
    public static void Save<TKey>(string path, IReadOnlyList<TKey> pageKeys, float pageWpt, float pageHpt,
                                  Action<Graphics, TKey> draw, int dpi = 200) where TKey : notnull
    {
        RenderInit.EnsureInitialized();
        var images = new Dictionary<TKey, QuestPDF.Infrastructure.Image>();
        QuestPDF.Infrastructure.Image Render(TKey key)
        {
            if (images.TryGetValue(key, out var img)) return img;
            int w = (int)Math.Round(pageWpt / 72f * dpi), h = (int)Math.Round(pageHpt / 72f * dpi);
            using var bmp = new Bitmap(w, h);
            bmp.SetResolution(dpi, dpi);
            using (var g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                g.PageUnit = GraphicsUnit.Point;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                draw(g, key);
            }
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return images[key] = QuestPDF.Infrastructure.Image.FromBinaryData(ms.ToArray());
        }

        Document.Create(doc =>
        {
            foreach (var key in pageKeys)
            {
                var img = Render(key);
                doc.Page(page =>
                {
                    page.Size(pageWpt, pageHpt, Unit.Point);
                    page.Margin(0);
                    page.Content().Image(img).FitArea();
                });
            }
        }).GeneratePdf(path);
    }
}
