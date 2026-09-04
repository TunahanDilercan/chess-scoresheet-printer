using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// Federasyon listesinde aynı etkinliğin farklı kategorileri ayrı turnuva (tnr) olarak görünür.
/// Bunları tek girişe indirir; kategori seçimi etkinlik açılınca zaten otomatik bulunur.
/// </summary>
public static class EventGrouping
{
    // "… A Kategorisi", "… 8 Yaş Kategorisi", "… Kategori 2", "… Grup B" gibi son ekler.
    private static readonly Regex CategorySuffix = new(
        @"\s*[-–]?\s*(\d+\s*Yaş.*|[A-ZÇĞİÖŞÜa-zçğıöşü0-9]+\s*Kategori(si)?|Kategori(si)?\s*[A-Z0-9]*|Grup\s*[A-Z0-9]+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Aynı temel ada sahip turnuvaları tek girişe indirger (ilk tnr korunur).</summary>
    public static List<TournamentRef> Dedupe(IReadOnlyList<TournamentRef> items)
    {
        var result = new List<TournamentRef>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var t in items)
        {
            var baseName = BaseName(t.Name);
            if (seen.Add(baseName))
                result.Add(t with { Name = baseName });
        }
        return result;
    }

    /// <summary>Ad sonundaki kategori/yaş/grup ekini atar.</summary>
    public static string BaseName(string name)
    {
        var trimmed = (name ?? "").Trim();
        var stripped = CategorySuffix.Replace(trimmed, "").Trim();
        return stripped.Length >= 4 ? stripped : trimmed; // çok kısaldıysa orijinali koru
    }
}
