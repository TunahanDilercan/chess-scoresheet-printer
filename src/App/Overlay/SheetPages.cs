using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App.Overlay;

/// <summary>Bir yazdırma sayfası: kullanılacak şablon ve o sayfaya düşen kağıtlar (1 veya 2).</summary>
public sealed record SheetPage(OverlayTemplate Template, IReadOnlyList<Pairing> Pairings);

/// <summary>
/// Kağıtları sayfalara böler. Her kağıt kendi sistemine göre şablon alabilir (ör. toplu baskıda
/// Berger kategorisi ya da takım maçları farklı kağıda); şablon değişince yeni sayfaya geçilir ve
/// sayfa başına kağıt sayısı o şablonun ayarından gelir.
/// </summary>
public static class SheetPages
{
    public static List<SheetPage> Build(IReadOnlyList<Pairing> pairings, Func<Pairing, OverlayTemplate> templateFor)
    {
        var pages = new List<SheetPage>();
        OverlayTemplate? tpl = null;
        var current = new List<Pairing>();
        foreach (var p in pairings)
        {
            var t = templateFor(p);
            int perPage = t.PerPage is 1 or 2 ? t.PerPage : 1;
            if (!ReferenceEquals(t, tpl) || current.Count >= perPage)
            {
                if (current.Count > 0) pages.Add(new SheetPage(tpl!, current));
                current = new List<Pairing>();
                tpl = t;
            }
            current.Add(p);
        }
        if (current.Count > 0) pages.Add(new SheetPage(tpl!, current));
        if (pages.Count == 0 && pairings.Count == 0) { /* boş belge: çağıran engeller */ }
        return pages;
    }
}
