using NotasyonOtomasyonu.Core;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RenderInit = NotasyonOtomasyonu.Render.QuestPdfRenderer;
using Image = QuestPDF.Infrastructure.Image;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Overlay şablonunu PDF olarak üretir (önizleme/kayıt). Doğrudan yazdırmayla AYNI
/// <see cref="OverlayLayout"/> ve <see cref="SheetPages"/> hesabını kullanır; arka planı
/// (şablonda açıksa) basar.
/// </summary>
public static class OverlayPdfRenderer
{
    private const string FontName = "DejaVu Sans";

    public static void Render(OverlayTemplate tpl, Tournament t, string outputPath, string pageSize = "A4")
        => Render(_ => tpl, t, outputPath, pageSize);

    /// <param name="templateFor">Her kağıdın şablonu (sisteme göre farklı kağıt olabilir).</param>
    public static void Render(Func<Pairing, OverlayTemplate> templateFor, Tournament t, string outputPath, string pageSize = "A4")
    {
        RenderInit.EnsureInitialized(); // QuestPDF lisansı + gömülü fontlar

        var pages = SheetPages.Build(t.Pairings, templateFor);
        bool a5 = NotasyonOtomasyonu.Core.PageGeometry.IsA5(pageSize);

        // Görsel başına tek Image nesnesi: PDF'e bir kez gömülür, her sayfada yeniden kullanılır
        // (byte[] ile her sayfaya ayrı kopya gömülüyor, 25 kağıt ≈ 30 MB oluyordu).
        var images = new Dictionary<string, Image>();
        Image? Bg(OverlayTemplate tpl)
        {
            var path = tpl.BackgroundImagePath;
            if (!tpl.PrintBackground || string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            if (!images.TryGetValue(path, out var img)) images[path] = img = Image.FromFile(path);
            return img;
        }

        Document.Create(doc =>
        {
            foreach (var pg in pages)
            {
                var tpl = pg.Template;
                var sheets = OverlayLayout.SheetRects(tpl.PerPage is 1 or 2 ? tpl.PerPage : 1, pageSize);
                var bg = Bg(tpl);
                doc.Page(page =>
                {
                    page.Size(a5 ? PageSizes.A5 : PageSizes.A4);
                    page.Margin(0);
                    page.DefaultTextStyle(s => s.FontFamily(FontName).FontColor(Colors.Black));

                    page.Content().Layers(layers =>
                    {
                        layers.PrimaryLayer().Extend(); // tam sayfa boş tuval

                        for (int i = 0; i < pg.Pairings.Count && i < sheets.Count; i++)
                        {
                            var sheet = sheets[i];

                            // Mutlak konum: Unconstrained + Translate. (Padding+Width sayfayı bir
                            // kıl payı aşınca QuestPDF içeriği SESSİZCE atıyordu → arka plan hiç
                            // çıkmıyor, kenara yakın alanlar kayboluyordu.)
                            if (bg is not null)
                                layers.Layer().Unconstrained().TranslateX(sheet.X).TranslateY(sheet.Y)
                                      .Width(sheet.Width).Height(sheet.Height)
                                      .Image(bg).FitUnproportionally(); // GDI baskısı gibi kağıda gerdir

                            foreach (var placed in OverlayLayout.Place(tpl, t, pg.Pairings[i], sheet))
                            {
                                if (string.IsNullOrEmpty(placed.Text)) continue;
                                var box = layers.Layer().Unconstrained()
                                    .TranslateX(placed.RectPt.X).TranslateY(placed.RectPt.Y)
                                    .Width(placed.RectPt.Width).Height(placed.RectPt.Height)
                                    .AlignMiddle();

                                var aligned = placed.Align switch
                                {
                                    HAlign.Center => box.AlignCenter(),
                                    HAlign.Right => box.AlignRight(),
                                    _ => box.AlignLeft()
                                };

                                var span = aligned.Text(placed.Text).FontSize(placed.FontPt);
                                if (placed.Bold) span.Bold();
                            }
                        }
                    });
                });
            }
        }).GeneratePdf(outputPath);
    }
}
