using System.Reflection;

namespace NotasyonOtomasyonu.Render;

/// <summary>
/// Gömülü Türkçe fontlara erişim. Hem QuestPDF (PDF) hem GDI+ (ekran/yazdırma ölçümü)
/// aynı fontu kullansın diye bayt olarak da sunulur.
/// </summary>
public static class EmbeddedFonts
{
    public const string FamilyName = "DejaVu Sans";

    public static byte[] Regular() => Read("NotasyonOtomasyonu.Render.Fonts.DejaVuSans.ttf");
    public static byte[] Bold() => Read("NotasyonOtomasyonu.Render.Fonts.DejaVuSans-Bold.ttf");

    private static byte[] Read(string resource)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var s = asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Gömülü font bulunamadı: {resource}");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }
}
