using System.Drawing.Imaging;
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
/// <remarks>
/// Sayfa resimleri PDF'e olduğu gibi gömülür (QuestPDF yeniden kodlamaz): yeniden kodlama sayfa başına
/// ~2 sn sürüyordu (120 sporculuk yaka kartı PDF'i 70 sn'yi aşıyordu). Afiş/fotoğraf içeren sayfalar
/// yüksek kaliteli JPEG (küçük ve hızlı), yazı ağırlıklı sayfalar kayıpsız PNG olarak gömülür.
/// </remarks>
public static class GdiPdf
{
    /// <summary>Sayfa resminin biçimi: <see cref="Png"/> kayıpsız (listeler), <see cref="Photo"/> JPEG %95 (afişli kartlar).</summary>
    public enum PageFormat { Png, Photo }

    /// <param name="pageKeys">Her sayfanın içerik anahtarı (aynı anahtar = aynı resim, tekrar çizilmez).</param>
    /// <param name="draw">Sayfayı punto koordinatlarında çizer (0,0 = kağıdın sol üstü).</param>
    /// <param name="progress">Hazırlanan sayfa sayısı ve toplam (arka planda çağrılabilir).</param>
    public static void Save<TKey>(string path, IReadOnlyList<TKey> pageKeys, float pageWpt, float pageHpt,
                                  Action<Graphics, TKey> draw, int dpi = 200, PageFormat format = PageFormat.Png,
                                  IProgress<(int Done, int Total)>? progress = null, CancellationToken ct = default) where TKey : notnull
    {
        RenderInit.EnsureInitialized();
        var unique = pageKeys.Distinct().ToList();
        var images = new Dictionary<TKey, QuestPDF.Infrastructure.Image>();
        int w = (int)Math.Round(pageWpt / 72f * dpi), h = (int)Math.Round(pageHpt / 72f * dpi);
        var jpeg = format == PageFormat.Photo ? ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg") : null;
        progress?.Report((0, unique.Count));
        using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
        {
            bmp.SetResolution(dpi, dpi);
            for (int i = 0; i < unique.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                using (var g = Graphics.FromImage(bmp))
                {
                    g.Clear(Color.White);
                    g.PageUnit = GraphicsUnit.Point;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    draw(g, unique[i]);
                }
                using var ms = new MemoryStream();
                if (jpeg is not null)
                {
                    using var ep = new EncoderParameters(1);
                    ep.Param[0] = new EncoderParameter(Encoder.Quality, 95L);
                    bmp.Save(ms, jpeg, ep);
                }
                else bmp.Save(ms, ImageFormat.Png);
                images[unique[i]] = QuestPDF.Infrastructure.Image.FromBinaryData(ms.ToArray());
                progress?.Report((i + 1, unique.Count));
            }
        }
        ct.ThrowIfCancellationRequested();

        // Önce geçici dosyaya: iptal ya da hata olursa yarım PDF kalmaz, açık eski dosya bozulmaz.
        var tmp = path + ".tmp";
        Document.Create(doc =>
        {
            foreach (var key in pageKeys)
            {
                var img = images[key];
                doc.Page(page =>
                {
                    page.Size(pageWpt, pageHpt, Unit.Point);
                    page.Margin(0);
                    page.Content().Image(img).FitArea().UseOriginalImage();
                });
            }
        }).GeneratePdf(tmp);
        try { File.Move(tmp, path, overwrite: true); }
        catch { try { File.Delete(tmp); } catch { } throw; }
    }
}
