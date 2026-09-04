using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>
/// Başlık satırlı tablolar (CSV/XLSX) için ortak eşleme mantığı.
/// config.Mapping'teki başlık adaylarını kullanarak sütunları modele bağlar.
/// </summary>
public sealed class TabularMapper
{
    private readonly Dictionary<string, int> _index = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyDictionary<string, List<string>> _mapping;

    public TabularMapper(IReadOnlyList<string> headers, AppConfig config)
    {
        _mapping = config.Mapping;

        // Her hedef alan için, başlıklar arasında ilk eşleşen adayın sütun indeksini bul.
        foreach (var (field, aliases) in _mapping)
        {
            for (int c = 0; c < headers.Count; c++)
            {
                var h = (headers[c] ?? string.Empty).Trim();
                if (h.Length == 0) continue;
                if (aliases.Any(a => string.Equals(a.Trim(), h, StringComparison.OrdinalIgnoreCase)))
                {
                    _index[field] = c;
                    break;
                }
            }
        }
    }

    /// <summary>En az Beyaz ad sütunu bulunabildiyse tablo anlamlıdır.</summary>
    public bool HasMinimumColumns => _index.ContainsKey("whiteName");

    /// <summary>Bir veri satırını Pairing'e çevirir. Eşleme bulunamayan alanlar boş geçilir.</summary>
    public Pairing MapRow(IReadOnlyList<string> cells, int fallbackBoard)
    {
        int board = ParseInt(Get(cells, "board")) ?? fallbackBoard;

        var white = new Player(
            StartNo: ParseInt(Get(cells, "whiteStartNo")),
            Name: CleanName(Get(cells, "whiteName")),
            Title: Clean(Get(cells, "whiteTitle")),
            Rating: ParseRating(Get(cells, "whiteRating")),
            Federation: Clean(Get(cells, "whiteFederation")),
            Club: Clean(Get(cells, "whiteClub")));

        var blackName = Get(cells, "blackName");
        Player? black;
        if (IsBye(blackName))
        {
            black = null; // BAY
        }
        else
        {
            black = new Player(
                StartNo: ParseInt(Get(cells, "blackStartNo")),
                Name: CleanName(blackName),
                Title: Clean(Get(cells, "blackTitle")),
                Rating: ParseRating(Get(cells, "blackRating")),
                Federation: Clean(Get(cells, "blackFederation")),
                Club: Clean(Get(cells, "blackClub")));
        }

        return new Pairing(board, white, black, Clean(Get(cells, "result")));
    }

    private string? Get(IReadOnlyList<string> cells, string field)
    {
        if (!_index.TryGetValue(field, out var i)) return null;
        if (i < 0 || i >= cells.Count) return null;
        return cells[i];
    }

    // BAY/bye tespiti: boş ad ya da bye anlamına gelen kelimeler.
    private static bool IsBye(string? name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) return true;
        var low = n.ToLowerInvariant();
        return low is "bay" or "bye" or "-" or "(bye)" or "spielfrei" or "boş"
            || low.Contains("bye") || low.Contains("bay (");
    }

    private static string CleanName(string? s)
    {
        var v = Clean(s);
        return string.IsNullOrEmpty(v) ? "—" : v!;
    }

    private static string? Clean(string? s)
    {
        var v = s?.Trim();
        return string.IsNullOrEmpty(v) ? null : v;
    }

    private static int? ParseInt(string? s)
    {
        var v = s?.Trim();
        if (string.IsNullOrEmpty(v)) return null;
        // Sadece baştaki sayısal kısmı al ("12." veya "12 (w)" gibi).
        int end = 0;
        if (v[0] == '-' || v[0] == '+') end = 1;
        while (end < v.Length && char.IsDigit(v[end])) end++;
        if (end == 0) return null;
        return int.TryParse(v[..end], out var n) ? n : null;
    }

    // Rating: 0 veya boş -> null (kağıtta boş kalsın).
    private static int? ParseRating(string? s)
    {
        var n = ParseInt(s);
        return n is null or 0 ? null : n;
    }
}
