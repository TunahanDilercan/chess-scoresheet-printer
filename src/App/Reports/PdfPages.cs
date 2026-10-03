using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>
/// PDF sayfalarını Windows'un yerleşik PDF motoruyla (Windows.Data.Pdf) resme çizer: rapor önizlemesi
/// ve önizlemeden yazdırma için. Ek kütüphane gerekmez.
/// </summary>
public static class PdfPages
{
    /// <summary>Sayfa sayısı ve her sayfanın boyutu (punto).</summary>
    public static async Task<List<SizeF>> SizesAsync(string path)
    {
        var doc = await Load(path);
        var list = new List<SizeF>();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            using var p = doc.GetPage(i);
            // Windows.Data.Pdf boyutu DIP (1/96 inç) verir → punto
            list.Add(new SizeF((float)p.Size.Width * 72f / 96f, (float)p.Size.Height * 72f / 96f));
        }
        return list;
    }

    /// <summary>Tüm sayfaları verilen çözünürlükte (dpi) çizer.</summary>
    public static async Task<List<Bitmap>> RenderAsync(string path, int dpi)
    {
        var doc = await Load(path);
        var pages = new List<Bitmap>();
        for (uint i = 0; i < doc.PageCount; i++)
        {
            using var page = doc.GetPage(i);
            uint w = (uint)Math.Round(page.Size.Width / 96.0 * dpi), h = (uint)Math.Round(page.Size.Height / 96.0 * dpi);
            using var stream = new InMemoryRandomAccessStream();
            await page.RenderToStreamAsync(stream, new PdfPageRenderOptions { DestinationWidth = w, DestinationHeight = h });
            stream.Seek(0);
            using var net = stream.AsStream();
            using var img = Image.FromStream(net);
            var bmp = new Bitmap(img);
            bmp.SetResolution(dpi, dpi);
            pages.Add(bmp);
        }
        return pages;
    }

    private static async Task<PdfDocument> Load(string path)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        return await PdfDocument.LoadFromFileAsync(file);
    }
}
