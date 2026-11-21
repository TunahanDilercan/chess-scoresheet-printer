using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>
/// Sporcu listesini Excel'den okur (.xlsx / .xlsm). Önce "Veri_Listesi" sayfası, yoksa ad sütunu bulunan ilk
/// sayfa. Sütunlar başlıktan tanınır: ad soyad, kategori, okul / kulüp / il, ELO, UKD. Başlık tanınmazsa
/// eski yazdırma makrolarındaki sıra kullanılır: A ad, B kategori, C okul/il, D ELO, E UKD.
/// </summary>
public static class AthleteImport
{
    public sealed record Result(List<Athlete> Athletes, string Sheet, string Columns);

    public static Result Read(string path)
    {
        using var wb = new XLWorkbook(path);
        var sheets = wb.Worksheets.ToList();
        var ordered = sheets.Where(s => Norm(s.Name) is "verilistesi" or "veri" or "liste" or "sporcular")
                            .Concat(sheets).Distinct().ToList();
        foreach (var ws in ordered)
        {
            var range = ws.RangeUsed();
            if (range is null) continue;
            var rows = range.RowsUsed().ToList();
            if (rows.Count == 0) continue;
            int first = range.FirstColumn().ColumnNumber(), last = range.LastColumn().ColumnNumber();
            var head = rows[0].Cells(first, last).Select(c => c.GetString()).ToList();
            var map = Map(head);
            bool positional = map.Name < 0;
            if (positional)
            {
                // başlık tanınmadı: yalnız veri sayfasında ("Veri_Listesi") ya da tek sayfalı dosyada sıra ile oku
                if (Norm(ws.Name) != "verilistesi" && sheets.Count > 1) continue;
                map = new Columns(0, 1, 2, 3, 4);
            }
            var list = new List<Athlete>();
            foreach (var row in rows.Skip(positional && !LooksLikeHeader(head) ? 0 : 1))
            {
                var cells = row.Cells(first, last).Select(c => c.GetString()).ToList();
                string At(int i) => i >= 0 && i < cells.Count ? Clean(cells[i]) : "";
                var name = At(map.Name);
                if (name.Length == 0) continue;
                list.Add(new Athlete(name, At(map.Category), At(map.Line3), Rating(At(map.Elo)), Rating(At(map.Ukd))));
            }
            if (list.Count == 0) continue;
            string Col(int i) => i >= 0 && i < head.Count ? $"{(char)('A' + i)}: {Clean(head[i])}" : "—";
            var desc = positional ? "sıra ile (A ad, B kategori, C okul/il, D ELO, E UKD)"
                                  : $"ad {Col(map.Name)}, kategori {Col(map.Category)}, okul/il {Col(map.Line3)}, ELO {Col(map.Elo)}, UKD {Col(map.Ukd)}";
            return new Result(list, ws.Name, desc);
        }
        throw new ParseException("Excel dosyasında sporcu listesi bulunamadı (ad soyad sütunu olan bir sayfa yok).");
    }

    private sealed record Columns(int Name, int Category, int Line3, int Elo, int Ukd);

    /// <summary>Başlıklardan sütun eşlemesi (bulunamayan -1).</summary>
    private static Columns Map(IReadOnlyList<string> head)
    {
        int name = -1, cat = -1, line3 = -1, elo = -1, ukd = -1, line3Rank = 99;
        for (int i = 0; i < head.Count; i++)
        {
            var h = Norm(head[i]);
            if (h.Length == 0) continue;
            if (name < 0 && (h.Contains("adsoyad") || h.Contains("adisoyadi") || h.Contains("sporcu") && !h.Contains("no") || h is "ad" or "adi" or "isim" or "oyuncu" or "name"))
                name = i;
            else if (cat < 0 && (h.Contains("kategori") || h is "grup" or "category"))
                cat = i;
            else if (ukd < 0 && h.Contains("ukd"))
                ukd = i;
            else if (elo < 0 && (h.Contains("elo") || h is "fide" or "rating" or "rtg" or "puan"))
                elo = i;
            else
            {
                int rank = h.Contains("okul") ? 0 : h.Contains("kulup") || h.Contains("takim") || h == "club" ? 1
                         : h.Contains("yurd") || h == "il" || h.Contains("sehir") || h.Contains("ili") ? 2 : 99;
                if (rank < line3Rank) { line3 = i; line3Rank = rank; }
            }
        }
        return new Columns(name, cat, line3, elo, ukd);
    }

    private static bool LooksLikeHeader(IReadOnlyList<string> head)
        => head.Any(h => Norm(h) is var n && (n.Contains("ad") || n.Contains("kategori") || n.Contains("elo") || n.Contains("ukd") || n.Contains("okul")));

    private static int? Rating(string s)
        => int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) && r > 0 ? r
         : double.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d >= 1 ? (int)Math.Round(d) : null;

    /// <summary>Hücre metni: fazla boşluklar atılır.</summary>
    private static string Clean(string s) => string.Join(' ', (s ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Başlık karşılaştırması için: küçük harf, Türkçe karakterler sadeleşir, harf dışı atılır.</summary>
    public static string Norm(string s)
    {
        var t = (s ?? "").ToLower(new CultureInfo("tr-TR"))
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var sb = new StringBuilder();
        foreach (var ch in t.Normalize(NormalizationForm.FormD))
            if (char.IsLetterOrDigit(ch)) sb.Append(ch);
        return sb.ToString();
    }
}
