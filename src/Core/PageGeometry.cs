namespace NotasyonOtomasyonu.Core;

/// <summary>Sayfa boyutlarının punto (1/72 inç) karşılıkları. A4 ve A5 dikey.</summary>
public static class PageGeometry
{
    public const float A4WidthPt = 595.28f;
    public const float A4HeightPt = 841.89f;
    public const float A5WidthPt = 419.53f;
    public const float A5HeightPt = 595.28f;

    /// <summary>"A4" / "A5" → (genişlik, yükseklik) punto. Bilinmeyen → A4.</summary>
    public static (float Width, float Height) Points(string? size)
        => string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase)
            ? (A5WidthPt, A5HeightPt)
            : (A4WidthPt, A4HeightPt);

    public static bool IsA5(string? size) => string.Equals(size, "A5", StringComparison.OrdinalIgnoreCase);
}
