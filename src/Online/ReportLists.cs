using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// Tutanak ve raporlardaki tekrarlanan tablolar ({{önek.sütun}} satırı her öğe için çoğaltılır):
/// <list type="bullet">
/// <item><c>hakem</c>: sira, adi, gorev, unvan — chess-results görevlileri (yaka kartı dereceleriyle).</item>
/// <item><c>sporcu</c>: sira, adi, unvan, elo, kulup, il, kategori — kategorilerin başlangıç listeleri.</item>
/// <item><c>eslesme</c>: kategori, tur, masa, beyaz, siyah, sonuc — kategorilerin eşleştirmeleri.</item>
/// <item><c>ceza</c>: tarih, saat, tur, kategori, masa, sporcu, hamle, neden, ceza, hakem.</item>
/// <item><c>itiraz</c>: tarih, saat, tur, kategori, masa, eden, konu, karar, hakem.</item>
/// </list>
/// Ağ gerektirmez (veriler çağırandan gelir); her tablo ve sütun test edilir.
/// </summary>
public static class ReportLists
{
    /// <summary>Ceza / itiraz tablosunda kayıt olmasa da elle yazmak için bırakılan en az satır.</summary>
    public const int MinIncidentRows = 3;

    /// <summary>Tablo önekleri ve sütunları (alan listesi ve düzenleme için).</summary>
    public static readonly IReadOnlyDictionary<string, (string Title, (string Column, string Title)[] Columns)> Catalog =
        new Dictionary<string, (string, (string, string)[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["kategori"] = ("Kategoriler", new[] { ("adi", "Kategori adı"), ("kriteri", "Kategori kriteri") }),
            ["program"] = ("Tur programı", new[] { ("tarih", "Tarih"), ("saat", "Saat"), ("etkinlik", "Program") }),
            ["katilim"] = ("Katılım (turnuva sonu)", new[] { ("kategori", "Kategori"), ("sporcu", "Sporcu sayısı"), ("tur", "Oynanan tur") }),
            ["il"] = ("İl dağılımı (turnuva sonu)", new[] { ("adi", "İl"), ("sayi", "Sporcu sayısı") }),
            ["derece"] = ("Dereceye girenler (turnuva sonu)", new[] { ("kategori", "Kategori"), ("derece", "Derece"), ("ad", "Ad soyad"), ("il", "İl"), ("rtg", "Rating"), ("puan", "Puan") }),
            ["siralama"] = ("Final sıralaması (turnuva sonu)", new[] { ("kategori", "Kategori"), ("sira", "Sıra"), ("ad", "Ad soyad"), ("il", "İl"), ("rtg", "Rating"), ("puan", "Puan") }),
            ["hakem"] = ("Hakemler ve görevleri", new[] { ("sira", "Sıra"), ("adi", "Ad soyad"), ("gorev", "Görev"), ("unvan", "Unvan / derece") }),
            ["sporcu"] = ("Sporcu listesi (kategorilere göre)", new[] { ("sira", "Sıra"), ("adi", "Ad soyad"), ("unvan", "Unvan"), ("elo", "Rating"), ("kulup", "Kulüp"), ("il", "İl"), ("kategori", "Kategori") }),
            ["eslesme"] = ("Eşleştirmeler (her kategorinin son eşlenen turu)", new[] { ("kategori", "Kategori"), ("tur", "Tur"), ("masa", "Masa"), ("beyaz", "Beyaz"), ("siyah", "Siyah"), ("sonuc", "Sonuç") }),
            ["ceza"] = ("Verilen cezalar (Ceza / itiraz kayıtları)", new[] { ("tarih", "Tarih"), ("saat", "Saat"), ("tur", "Tur"), ("kategori", "Kategori"), ("masa", "Masa"), ("sporcu", "Sporcu"), ("hamle", "Hamle no"), ("neden", "Ceza nedeni"), ("ceza", "Verilen ceza"), ("hakem", "Hakem") }),
            ["itiraz"] = ("İtirazlar (Ceza / itiraz kayıtları)", new[] { ("tarih", "Tarih"), ("saat", "Saat"), ("tur", "Tur"), ("kategori", "Kategori"), ("masa", "Masa"), ("eden", "İtiraz eden"), ("konu", "İtirazın konusu"), ("karar", "Karar"), ("hakem", "Hakem") }),
        };

    /// <summary>Görevliler: başhakem, yardımcı, hakemler, direktör; unvan elle seçilen dereceden ya da FIDE unvanından.</summary>
    public static List<Dictionary<string, string>> Arbiters(IReadOnlyDictionary<string, string> info, IReadOnlyDictionary<string, string>? grades = null)
    {
        var rows = new List<Dictionary<string, string>>();
        int i = 0;
        foreach (var a in Online.Arbiters.FromInfo(info))
        {
            var key = EventGrouping.Fold(a.Name).Replace(" ", "");
            var grade = grades is not null && grades.TryGetValue(key, out var g) ? g : Online.Arbiters.GradeFromTitle(a.FideTitle) ?? "";
            rows.Add(new() { ["sira"] = (++i).ToString(), ["adi"] = a.Name, ["gorev"] = a.Role, ["unvan"] = grade });
        }
        return rows;
    }

    /// <summary>Kategorilerin başlangıç listeleri (kategori sırasıyla; il, kayıt listesinden çözülür).</summary>
    public static List<Dictionary<string, string>> Players(IEnumerable<(string Category, IReadOnlyList<Player> Players)> lists, Func<string, string?>? province = null)
    {
        var rows = new List<Dictionary<string, string>>();
        foreach (var (cat, players) in lists)
        {
            int i = 0;
            foreach (var p in players)
            {
                i++;
                rows.Add(new()
                {
                    ["sira"] = (p.StartNo ?? i).ToString(),
                    ["adi"] = p.Name,
                    ["unvan"] = p.Title ?? "",
                    ["elo"] = p.Rating is > 0 ? p.Rating.Value.ToString() : "",
                    ["kulup"] = p.Club ?? "",
                    ["il"] = p.Province ?? province?.Invoke(p.Name) ?? "",
                    ["kategori"] = cat
                });
            }
        }
        return rows;
    }

    /// <summary>Kategorilerin eşleştirmeleri; BAY "BAY" olarak yazılır.</summary>
    public static List<Dictionary<string, string>> Pairings(IEnumerable<(string Category, Tournament Round)> rounds)
    {
        var rows = new List<Dictionary<string, string>>();
        foreach (var (cat, t) in rounds)
            foreach (var p in t.Pairings.OrderBy(p => p.IsBye).ThenBy(p => p.Board))
                rows.Add(new()
                {
                    ["kategori"] = cat,
                    ["tur"] = (p.Round ?? t.RoundNo).ToString(),
                    ["masa"] = p.IsBye ? "" : p.BoardText,
                    ["beyaz"] = p.White.Name,
                    ["siyah"] = p.IsBye ? "BAY" : p.Black!.Name,
                    ["sonuc"] = p.Result ?? ""
                });
        return rows;
    }

    /// <summary>
    /// Ceza ya da itiraz tablosu: o türün kayıtları tarih/saat sırasıyla; en az <see cref="MinIncidentRows"/>
    /// satır (boş satırlar elle yazmak için).
    /// </summary>
    public static List<Dictionary<string, string>> Incidents(IEnumerable<IncidentRecord>? records, string kind, int minRows = MinIncidentRows)
    {
        bool penalty = kind == IncidentRecord.Penalty;
        var rows = (records ?? Enumerable.Empty<IncidentRecord>())
            .Where(r => r.Kind == kind && !r.IsEmpty)
            .OrderBy(r => SortDate(r.Date)).ThenBy(r => r.Time, StringComparer.Ordinal)
            .Select(r =>
            {
                var d = new Dictionary<string, string>
                {
                    ["tarih"] = r.Date, ["saat"] = r.Time, ["tur"] = r.Round, ["kategori"] = r.Category, ["masa"] = r.Board, ["hakem"] = r.Arbiter
                };
                if (penalty) { d["sporcu"] = r.Player; d["hamle"] = r.Move; d["neden"] = r.Reason; d["ceza"] = r.Decision; }
                else { d["eden"] = r.Player; d["konu"] = r.Reason; d["karar"] = r.Decision; }
                return d;
            })
            .ToList();
        var cols = Catalog[penalty ? "ceza" : "itiraz"].Columns.Select(c => c.Column).ToList();
        while (rows.Count < minRows) rows.Add(cols.ToDictionary(c => c, _ => ""));
        return rows;
    }

    /// <summary>Tabloyu en az <paramref name="minRows"/> satıra boş satırlarla tamamlar (elle yazmak için).</summary>
    public static List<Dictionary<string, string>> Padded(List<Dictionary<string, string>> rows, string prefix, int minRows)
    {
        var result = rows.ToList();
        var cols = Catalog.TryGetValue(prefix, out var c) ? c.Columns.Select(x => x.Column).ToList() : rows.FirstOrDefault()?.Keys.ToList() ?? new();
        while (result.Count < minRows) result.Add(cols.ToDictionary(k => k, _ => ""));
        return result;
    }

    /// <summary>Kayıt sayıları ve değerlendirme için özet değerler (CEZA_SAYISI, ITIRAZ_SAYISI).</summary>
    public static Dictionary<string, string> IncidentValues(IEnumerable<IncidentRecord>? records)
    {
        var list = (records ?? Enumerable.Empty<IncidentRecord>()).Where(r => !r.IsEmpty).ToList();
        return new(StringComparer.OrdinalIgnoreCase)
        {
            ["CEZA_SAYISI"] = list.Count(r => r.Kind == IncidentRecord.Penalty).ToString(),
            ["ITIRAZ_SAYISI"] = list.Count(r => r.Kind == IncidentRecord.Appeal).ToString()
        };
    }

    /// <summary>"03.10.2026" → sıralanabilir tarih; okunamazsa en sona.</summary>
    private static DateTime SortDate(string s)
        => DateTime.TryParseExact(s.Trim(), new[] { "dd.MM.yyyy", "d.M.yyyy", "dd.MM.yy", "yyyy-MM-dd" },
                                  System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d)
            ? d : DateTime.MaxValue;
}
