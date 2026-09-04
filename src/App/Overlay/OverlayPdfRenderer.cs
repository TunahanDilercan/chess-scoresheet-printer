using NotasyonOtomasyonu.Core;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using RenderInit = NotasyonOtomasyonu.Render.QuestPdfRenderer;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>
/// Overlay şablonunu PDF olarak üretir (önizleme/kayıt). Doğrudan yazdırmayla AYNI
/// <see cref="OverlayLayout"/> hesabını kullanır; arka planı (varsa) opsiyonel basar.
/// </summary>
public static class OverlayPdfRenderer
{
    private const string FontName = "DejaVu Sans";

    public static void Render(OverlayTemplate tpl, Tournament t, string outputPath, string pageSize = "A4")
    {
        RenderInit.EnsureInitialized(); // QuestPDF lisansı + gömülü fontlar

        int perPage = tpl.PerPage is 1 or 2 ? tpl.PerPage : 1;
        var sheets = OverlayLayout.SheetRects(perPage, pageSize);
        var pages = Chunk(t.Pairings, perPage);
        bool a5 = NotasyonOtomasyonu.Core.PageGeometry.IsA5(pageSize);

        byte[]? bg = null;
        if (tpl.PrintBackground && !string.IsNullOrWhiteSpace(tpl.BackgroundImagePath)
            && File.Exists(tpl.BackgroundImagePath))
            bg = File.ReadAllBytes(tpl.BackgroundImagePath);

        Document.Create(doc =>
        {
            foreach (var pagePairings in pages)
            {
                doc.Page(page =>
                {
                    page.Size(a5 ? PageSizes.A5 : PageSizes.A4);
                    page.Margin(0);
                    page.DefaultTextStyle(s => s.FontFamily(FontName).FontColor(Colors.Black));

                    page.Content().Layers(layers =>
                    {
                        layers.PrimaryLayer().Extend(); // tam sayfa boş tuval

                        for (int i = 0; i < pagePairings.Count && i < sheets.Count; i++)
                        {
                            var sheet = sheets[i];

                            if (bg is not null)
                                layers.Layer().PaddingLeft(sheet.X).PaddingTop(sheet.Y)
                                      .Width(sheet.Width).Height(sheet.Height)
                                      .Image(bg).FitArea();

                            foreach (var placed in OverlayLayout.Place(tpl, t, pagePairings[i], sheet))
                            {
                                if (string.IsNullOrEmpty(placed.Text)) continue;
                                var box = layers.Layer()
                                    .PaddingLeft(placed.RectPt.X).PaddingTop(placed.RectPt.Y)
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

    private static List<List<Pairing>> Chunk(IReadOnlyList<Pairing> items, int size)
    {
        var result = new List<List<Pairing>>();
        for (int i = 0; i < items.Count; i += size)
            result.Add(items.Skip(i).Take(size).ToList());
        if (result.Count == 0) result.Add(new List<Pairing>());
        return result;
    }
}
