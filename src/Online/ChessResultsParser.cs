using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// chess-results.com HTML sayfalarını ayrıştırır (AngleSharp). Türkçe karakterler
/// HTML entity (&amp;#304;) olarak gelir; AngleSharp bunları otomatik çözer.
/// </summary>
public static class ChessResultsParser
{
    private static readonly HtmlParser Parser = new();

    // ---- 1) Federasyon turnuva listesi (fed.aspx) ----
    public static IReadOnlyList<TournamentRef> ParseFederationList(string html)
    {
        var doc = Parser.ParseDocument(html);
        var result = new List<TournamentRef>();
        var seen = new HashSet<int>();

        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var href = a.GetAttribute("href") ?? "";
            var m = Regex.Match(href, @"[Tt]nr(\d+)\.aspx", RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            var name = Clean(a.TextContent);
            if (name.Length < 5) continue;                 // nav/ikon linklerini ele
            if (!int.TryParse(m.Groups[1].Value, out var tnr)) continue;
            if (!seen.Add(tnr)) continue;

            result.Add(new TournamentRef(tnr, name));
        }
        return result;
    }

    // ---- 2) Etkinlik üst bilgisi: ad, tarih, tur sayısı, kategoriler ----
    // Yer/hakem/zaman kontrolü bilinçli olarak okunmaz: notasyon kağıdına basılmıyorlar.
    public static EventInfo ParseEventInfo(string html, int tnr)
    {
        var doc = Parser.ParseDocument(html);

        var name = Clean(doc.QuerySelector("h2")?.TextContent
                          ?? doc.QuerySelector("title")?.TextContent
                          ?? $"Turnuva {tnr}");

        var dates = InfoField(doc, "Tarih", "Date");

        var (maxRound, currentRound) = ParseRounds(doc);
        var categories = ParseCategories(doc, tnr, name);

        return new EventInfo(tnr, name, dates, maxRound, currentRound, categories);
    }

    // ---- 3) Eşleştirme tablosu -> Tournament ----
    /// <summary>Eşleştirme sayfasını okuyup tam Tournament üretir (üst bilgi + masalar).</summary>
    public static Tournament ParsePairings(string html, int tnr, int round)
    {
        var doc = Parser.ParseDocument(html);
        var info = ParseEventInfo(html, tnr); // aynı sayfada üst bilgi de var

        // Veri satırları chess-results'ta CRng1/CRng2 sınıflarıyla işaretli.
        var dataRows = doc.QuerySelectorAll("tr.CRng1, tr.CRng2").ToList();
        var table = dataRows.FirstOrDefault()?.Closest("table");
        if (table is null)
            throw new InvalidOperationException(
                "Eşleştirme tablosu bulunamadı. Tur numarası geçerli mi? (Henüz oluşturulmamış olabilir.)");

        var headers = FindHeaderCells(table);
        var map = new PairingColumnMap(headers);

        var pairings = new List<Pairing>();
        int fallback = 0;
        foreach (var row in dataRows)
        {
            fallback++;
            var cells = RowCells(row);
            if (cells.Count == 0) continue;
            var pairing = map.MapRow(cells, fallback);
            if (pairing is not null) pairings.Add(pairing);
        }

        return new Tournament(
            Name: info.Name,
            RoundNo: round,
            Pairings: pairings,
            Date: info.Dates);
    }

    // ---- kategori navigasyonu ----
    private static IReadOnlyList<CategoryRef> ParseCategories(IDocument doc, int tnr, string eventName)
    {
        var cats = new List<CategoryRef>();

        // "Turnuva seçimi" etiketli hücrenin yanındaki kap.
        var label = doc.QuerySelectorAll("td")
            .FirstOrDefault(td => Clean(td.TextContent).StartsWith("Turnuva seçimi", StringComparison.OrdinalIgnoreCase));
        var container = label?.NextElementSibling;

        if (container is not null)
        {
            foreach (var node in container.Children)
            {
                if (node.TagName.Equals("A", StringComparison.OrdinalIgnoreCase))
                {
                    var href = node.GetAttribute("href") ?? "";
                    var m = Regex.Match(href, @"[Tt]nr(\d+)\.aspx", RegexOptions.IgnoreCase);
                    if (m.Success && int.TryParse(m.Groups[1].Value, out var t))
                        cats.Add(new CategoryRef(t, Clean(node.TextContent), IsCurrent: false));
                }
                else
                {
                    // Mevcut grup: link olmayan (bold/italik) metin.
                    var txt = Clean(node.TextContent);
                    if (txt.Length > 0 && node.QuerySelector("a") is null)
                        cats.Add(new CategoryRef(tnr, txt, IsCurrent: true));
                }
            }
        }

        // Hiç kategori bulunamadıysa (tek gruplu turnuva) mevcut etkinliği ekle. Ad olarak
        // turnuva adının tamamı değil yalnızca kategori eki ("… AÇIK KATEGORİSİ" → "AÇIK") kullanılır;
        // ek yoksa boş kalır (kağıttaki Kategori kutusuna turnuva adı basılmasın).
        if (cats.Count == 0)
            cats.Add(new CategoryRef(tnr, EventGrouping.CategoryFromEventName(eventName), IsCurrent: true));

        // Mevcut işaretli yoksa, tnr eşleşeni mevcut say.
        if (!cats.Any(c => c.IsCurrent))
            cats = cats.Select(c => c.Tnr == tnr ? c with { IsCurrent = true } : c).ToList();

        return cats;
    }

    /// <summary>Tur sayısı bilinmediğinde gösterilecek makul üst sınır.</summary>
    public const int UnknownMaxRound = 11;

    /// <summary>
    /// Toplam tur sayısı ve eşlenmiş son tur. chess-results menüsünde eşlenmiş turlar
    /// "Tur1, Tur2, Tur3/7" diye link olarak listelenir; "/7" son eşlenmiş turun yanındadır.
    /// Sayfadaki program metni ("6. Tur 16.30") tur sayısı sanılmasın diye yalnızca bu
    /// bitişik yazımlı ("Tur3") link metinlerine bakılır.
    /// </summary>
    private static (int Max, int Current) ParseRounds(IDocument doc)
    {
        int max = 0, current = 0;
        foreach (var a in doc.QuerySelectorAll("a[href]"))
        {
            var m = Regex.Match(Clean(a.TextContent), @"^(?:Tur|Rd\.|Rnd\.?|Round)\s?(\d{1,2})(?:\s*/\s*(\d{1,2}))?$",
                RegexOptions.IgnoreCase);
            if (!m.Success) continue;
            int r = int.Parse(m.Groups[1].Value);
            current = Math.Max(current, r);
            max = Math.Max(max, r);
            if (m.Groups[2].Success) max = Math.Max(max, int.Parse(m.Groups[2].Value));
        }

        // Görüntülenen tur link olmayabilir; "Tur5/5" düz metin olarak da geçer.
        var text = doc.Body?.TextContent ?? "";
        foreach (Match m in Regex.Matches(text, @"\bTur(\d{1,2})/(\d{1,2})\b"))
        {
            current = Math.Max(current, int.Parse(m.Groups[1].Value));
            max = Math.Max(max, int.Parse(m.Groups[2].Value));
        }

        return (max == 0 ? UnknownMaxRound : max, current);
    }

    // ---- yardımcılar ----
    private static string? InfoField(IDocument doc, params string[] labels)
    {
        foreach (var td in doc.QuerySelectorAll("td"))
        {
            var t = Clean(td.TextContent);
            foreach (var lbl in labels)
                if (t.Equals(lbl, StringComparison.OrdinalIgnoreCase) ||
                    t.StartsWith(lbl + ":", StringComparison.OrdinalIgnoreCase))
                {
                    var val = Clean(td.NextElementSibling?.TextContent ?? "");
                    if (val.Length > 0) return val;
                }
        }
        return null;
    }

    private static List<string> FindHeaderCells(IElement table)
    {
        foreach (var tr in table.QuerySelectorAll("tr"))
        {
            var cells = RowCells(tr);
            var joined = string.Join("|", cells).ToLowerInvariant();
            if (joined.Contains("beyaz") || joined.Contains("white") ||
                joined.Contains("masa") || joined.Contains("bo.") || joined.Contains("sonuç"))
                return cells;
        }
        return new List<string>();
    }

    private static List<string> RowCells(IElement row) =>
        row.Children
           .Where(c => c.TagName is "TD" or "TH")
           .Select(c => Clean(c.TextContent))
           .ToList();

    private static string Clean(string? s)
        => string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, @"\s+", " ").Trim();
}
