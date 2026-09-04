using System.Reflection;
using QuestPDF.Drawing;
using QuestPDF.Infrastructure;

namespace NotasyonOtomasyonu.Render;

/// <summary>
/// QuestPDF lisansı + gömülü Türkçe fontların tek seferlik kaydı.
/// (Overlay PDF üretimi bu init'i kullanır; uygulama yalnızca hazır kağıda bindirme yapar.)
/// </summary>
public static class QuestPdfRenderer
{
    private static bool _initialized;
    private static readonly object InitLock = new();

    /// <summary>Lisans ayarı + gömülü Türkçe fontları yalnızca bir kez kaydeder.</summary>
    public static void EnsureInitialized()
    {
        if (_initialized) return;
        lock (InitLock)
        {
            if (_initialized) return;
            QuestPDF.Settings.License = LicenseType.Community;

            var asm = Assembly.GetExecutingAssembly();
            RegisterEmbeddedFont(asm, "NotasyonOtomasyonu.Render.Fonts.DejaVuSans.ttf");
            RegisterEmbeddedFont(asm, "NotasyonOtomasyonu.Render.Fonts.DejaVuSans-Bold.ttf");
            _initialized = true;
        }
    }

    private static void RegisterEmbeddedFont(Assembly asm, string resourceName)
    {
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream is null) return; // font yoksa QuestPDF sistem fontuna düşer
        FontManager.RegisterFont(stream);
    }
}
