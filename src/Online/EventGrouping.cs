using System.Text;
using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// Federasyon listesinde aynı etkinliğin farklı kategorileri ayrı turnuva (tnr) olarak görünür.
/// Bunları tek girişe indirir; kategori seçimi etkinlik açılınca zaten otomatik bulunur.
/// </summary>
public static class EventGrouping
{
    // "… 8 Yaş ve Altı …", "… 10_yaş_ve_altı_ kategorisi": yaş ekiyle başlayan her şey sondan atılır.
    private static readonly Regex AgeSuffix = new(@"[\s_]*[-–]?[\s_]*\d+[\s_]*ya[sş](?!\p{L}).*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex GroupSuffix = new(@"[\s_]*[-–]?[\s_]*grup[\s_]*[a-z0-9]{1,3}[\s_]*$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>"Kategori" kelimesinden hemen önce gelip ekin parçası sayılan kelimeler (katlanmış yazım).</summary>
    private static readonly HashSet<string> CategoryWords = new(StringComparer.Ordinal)
    {
        "acik", "genel", "kadin", "kiz", "kizlar", "erkek", "erkekler", "yas", "ve", "alti", "ustu",
        "elo", "ukd", "veteran", "yildiz", "yildizlar", "minik", "minikler", "kucuk", "kucukler",
        "genc", "gencler", "buyuk", "buyukler", "open", "women", "girls", "boys", "ozel", "engelli"
    };

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
        var stripped = AgeSuffix.Replace(trimmed, "").Trim();
        stripped = StripKategori(stripped);
        stripped = GroupSuffix.Replace(stripped, "").Trim(' ', '_', '-', '–');
        return stripped.Length >= 4 ? stripped : trimmed; // çok kısaldıysa orijinali koru
    }

    /// <summary>
    /// "… AÇIK YAŞ KADIN KATEGORİSİ (2010-2016)" → "…". Sondaki parantez/köşeli notu, "Kategori(si)"
    /// kelimesini ve hemen önündeki kategori adı kelimelerini (A, B, AÇIK, GENEL, KADIN …) atar.
    /// Turnuva adının asıl kelimeleri (ör. "Okul Ligi") sözlükte olmadığı için korunur.
    /// </summary>
    private static string StripKategori(string s)
    {
        var words = Regex.Split(s, @"[\s_]+").Where(w => w.Length > 0).ToList();

        // Sondaki "(…)" / "[…]" notları
        int end = words.Count;
        while (end > 0 && (words[end - 1].EndsWith(')') || words[end - 1].EndsWith(']')))
        {
            int open = end - 1;
            while (open >= 0 && !(words[open].StartsWith('(') || words[open].StartsWith('['))) open--;
            if (open < 0) break;
            end = open;
        }

        // "Kategori 2" / "Kategori B" biçimi: sondaki kısa etiketi de say.
        int k = end - 1;
        if (k >= 1 && words[k].Length <= 2 && IsKategori(words[k - 1])) k--;
        if (k < 0 || !IsKategori(words[k])) return s;

        int start = k;
        while (start > 0 && IsCategoryLabel(words[start - 1])) start--;
        return string.Join(" ", words.Take(start));
    }

    /// <summary>
    /// Kategori menüsü olmayan (tek gruplu) turnuvada adın sonundaki kategori eki:
    /// "… TURNUVASI AÇIK YAŞ KATEGORİSİ" → "AÇIK YAŞ", "… GENEL KATEGORİ" → "GENEL"; ek yoksa "".
    /// </summary>
    public static string CategoryFromEventName(string name)
    {
        var full = (name ?? "").Trim();
        var baseName = BaseName(full);
        if (baseName.Length >= full.Length || !full.StartsWith(baseName, StringComparison.Ordinal)) return "";
        var suffix = full[baseName.Length..].Trim(' ', '_', '-', '–');
        return IsKategori(suffix) ? "" : ShortCategory(suffix);
    }

    /// <summary>Kategori butonu/kağıdı için kısa ad: "A Kategorisi" → "A", "AÇIK KATEGORİ" → "AÇIK".</summary>
    public static string ShortCategory(string name)
    {
        var words = (name ?? "").Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1 && IsKategori(words[^1])) words.RemoveAt(words.Count - 1);
        var s = string.Join(" ", words);
        return s.Length == 0 ? (name ?? "") : s;
    }

    private static bool IsKategori(string w)
        => Fold(w) is "kategori" or "kategorisi" or "category" or "kateqori" or "kat.";

    private static bool IsCategoryLabel(string w)
    {
        var f = Fold(w.Trim('-', '–', ',', '.'));
        if (f.Length == 0) return false;
        if (f.Length <= 2 && f.All(char.IsLetterOrDigit)) return true; // A, B, U8, 12
        return CategoryWords.Contains(f);
    }

    /// <summary>Türkçe harfleri ASCII'ye indirip küçültür (kültürden bağımsız karşılaştırma için).</summary>
    public static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            sb.Append(ch switch
            {
                'ç' or 'Ç' => 'c', 'ğ' or 'Ğ' => 'g', 'ı' or 'I' or 'İ' or 'i' => 'i',
                'ö' or 'Ö' => 'o', 'ş' or 'Ş' => 's', 'ü' or 'Ü' => 'u',
                'â' or 'Â' => 'a', 'î' or 'Î' => 'i', 'û' or 'Û' => 'u',
                _ => char.ToLowerInvariant(ch)
            });
        }
        return sb.ToString();
    }
}
