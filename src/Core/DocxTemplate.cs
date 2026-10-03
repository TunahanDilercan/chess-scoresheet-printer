using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace NotasyonOtomasyonu.Core;

/// <summary>Bir rapor/yönerge belgesine yazılacak veriler.</summary>
public sealed class ReportData
{
    /// <summary>Tekil alanlar: "IL", "YER", "TARIH_ARALIGI" … (büyük/küçük harf duyarsız).</summary>
    public Dictionary<string, string> Values { get; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Kategori tablosu satırları: "adi", "kriteri".</summary>
    public List<Dictionary<string, string>> Categories { get; } = new();
    /// <summary>Program tablosu satırları: "tarih", "saat", "etkinlik".</summary>
    public List<Dictionary<string, string>> Program { get; } = new();
}

/// <summary>Belgede bulunan doldurulabilir yerler.</summary>
public sealed record TemplateAnalysis(
    IReadOnlyList<string> Fields,          // {{ALAN}} işaretleri ve etiketten tanınan alanlar
    bool HasCategoryTable,                 // {{kategori.…}} satırı
    bool HasProgramTable,                  // {{program.…}} satırı
    IReadOnlyList<string> LabelFields)     // işaretsiz, "İLİ | …" gibi etiketten tanınanlar
{
    /// <summary>Etiketli hücrelerde belgede zaten yazan değerler (kullanıcının kendi yönergesi).</summary>
    public IReadOnlyDictionary<string, string> Existing { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// .docx şablon motoru (Word gerekmez; belge XML'i doğrudan işlenir).
///  • <c>{{ALAN}}</c> işaretleri doldurulur. Word işareti birkaç parçaya bölmüş olsa da paragraf
///    birleştirilerek tanınır.
///  • <c>{{kategori.adi}}</c> / <c>{{kategori.kriteri}}</c> içeren tablo satırı her kategori için,
///    <c>{{program.tarih}}</c> / <c>{{program.saat}}</c> / <c>{{program.etkinlik}}</c> içeren satır her
///    program kalemi için çoğaltılır; aynı günün tarih hücreleri dikey birleştirilir.
///  • İşaret içermeyen belgelerde "İLİ", "YERİ", "SİSTEM", "DÜŞÜNME SÜRESİ" gibi etiket hücrelerinin
///    yanındaki hücre bilinen değerle doldurulur (kullanıcının kendi yönergesi).
/// </summary>
public static class DocxTemplate
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly Regex Token = new(@"\{\{\s*([A-Za-z0-9_.]+)\s*\}\}");

    /// <summary>Etiket (katlanmış yazım) → alan. Etiket hücresinin sağındaki hücre doldurulur.</summary>
    public static readonly IReadOnlyDictionary<string, string> LabelMap = new Dictionary<string, string>
    {
        ["turnuva adi"] = "TURNUVA_ADI", ["ili"] = "IL", ["il"] = "IL", ["ilcesi"] = "ILCE", ["ilce"] = "ILCE",
        ["baslama-bitis tarihi"] = "TARIH_ARALIGI", ["baslama - bitis tarihi"] = "TARIH_ARALIGI",
        ["tarih"] = "TARIH_ARALIGI", ["tarihi"] = "TARIH_ARALIGI", ["turnuva tarihi"] = "TARIH_ARALIGI",
        ["yeri"] = "YER", ["yer"] = "YER", ["son basvuru tarihi"] = "SON_BASVURU",
        ["sistem"] = "SISTEM", ["sistemi"] = "SISTEM", ["dusunme suresi"] = "DUSUNME_SURESI",
        ["organizasyon"] = "ORGANIZASYON", ["turnuva direktoru"] = "DIREKTOR", ["yarisma direktoru"] = "DIREKTOR",
        ["bashakem"] = "BASHAKEM", ["bas hakem"] = "BASHAKEM", ["hakemler"] = "HAKEMLER", ["hakem"] = "HAKEMLER",
        ["tur sayisi"] = "TUR_SAYISI",
    };

    /// <summary>Tablo başlıklarında geçen, değer olmayan sözcükler.</summary>
    private static readonly HashSet<string> HeaderWords = new()
    {
        "saat", "program", "kategori adi", "kategori kriteri", "kategori", "tur", "sira", "no"
    };

    private static IEnumerable<string> Parts(ZipArchive zip)
        => zip.Entries.Select(e => e.FullName)
              .Where(n => n == "word/document.xml" || Regex.IsMatch(n, @"^word/(header|footer)\d*\.xml$"));

    public static TemplateAnalysis Analyze(byte[] docx)
    {
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var fields = new List<string>();
        var labels = new List<string>();
        var existing = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool cats = false, prog = false;
        foreach (var name in Parts(zip))
        {
            var x = Load(zip, name);
            NormalizeParagraphs(x);
            foreach (var t in x.Descendants(W + "t"))
                foreach (Match m in Token.Matches(t.Value))
                {
                    var key = m.Groups[1].Value;
                    if (key.StartsWith("kategori.", StringComparison.OrdinalIgnoreCase)) cats = true;
                    else if (key.StartsWith("program.", StringComparison.OrdinalIgnoreCase)) prog = true;
                    else if (!fields.Contains(key, StringComparer.OrdinalIgnoreCase)) fields.Add(key);
                }
            foreach (var (key, cell) in LabelCells(x))
            {
                if (!labels.Contains(key) && !fields.Contains(key, StringComparer.OrdinalIgnoreCase)) labels.Add(key);
                var v = CellText(cell);
                if (!existing.ContainsKey(key) && Regex.IsMatch(v, @"[\p{L}\d]{2,}")) existing[key] = v; // "…", "-" değer sayılmaz
            }
        }
        return new TemplateAnalysis(fields.Concat(labels).ToList(), cats, prog, labels) { Existing = existing };
    }

    public static byte[] Fill(byte[] docx, ReportData data)
    {
        var input = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var output = new MemoryStream();
        using (var outZip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            var parts = Parts(input).ToHashSet();
            foreach (var entry in input.Entries)
            {
                var target = outZip.CreateEntry(entry.FullName, CompressionLevel.Optimal);
                using var ws = target.Open();
                if (parts.Contains(entry.FullName))
                {
                    var x = Load(input, entry.FullName);
                    NormalizeParagraphs(x);
                    ExpandRows(x, "kategori.", data.Categories, mergeDateKey: null);
                    ExpandRows(x, "program.", data.Program, mergeDateKey: "tarih");
                    ReplaceTokens(x, data.Values);
                    FillLabelCells(x, data.Values);
                    using var w = new StreamWriter(ws, new UTF8Encoding(false));
                    x.Save(w, SaveOptions.DisableFormatting);
                }
                else
                {
                    using var rs = entry.Open();
                    rs.CopyTo(ws);
                }
            }
        }
        input.Dispose();
        return output.ToArray();
    }

    private static XDocument Load(ZipArchive zip, string name)
    {
        using var s = zip.GetEntry(name)!.Open();
        return XDocument.Load(s, LoadOptions.PreserveWhitespace);
    }

    /// <summary>Bir paragrafta parçalara bölünmüş {{…}} işaretini tek metin düğümünde birleştirir.</summary>
    private static void NormalizeParagraphs(XDocument x)
    {
        foreach (var p in x.Descendants(W + "p").ToList())
        {
            var ts = p.Descendants(W + "t").ToList();
            if (ts.Count < 2) continue;
            var all = string.Concat(ts.Select(t => t.Value));
            if (!all.Contains("{{")) continue;
            bool split = Token.Matches(all).Cast<Match>().Any(m => !ts.Any(t => t.Value.Contains(m.Value)));
            if (!split && !Regex.IsMatch(all, @"\{\{[^}]*$|^[^{]*\}\}")) continue;
            ts[0].Value = all;
            Preserve(ts[0]);
            foreach (var t in ts.Skip(1)) t.Value = "";
        }
    }

    private static void Preserve(XElement t) => t.SetAttributeValue(XNamespace.Xml + "space", "preserve");

    /// <summary>"{{prefix…}}" içeren tablo satırını her öğe için çoğaltır.</summary>
    private static void ExpandRows(XDocument x, string prefix, List<Dictionary<string, string>> items, string? mergeDateKey)
    {
        foreach (var row in x.Descendants(W + "tr").ToList())
        {
            var text = string.Concat(row.Descendants(W + "t").Select(t => t.Value));
            if (!text.Contains("{{" + prefix, StringComparison.OrdinalIgnoreCase)) continue;

            string? prevDate = null;
            XElement anchor = row;
            foreach (var item in items)
            {
                var clone = new XElement(row);
                string? date = mergeDateKey is not null && item.TryGetValue(mergeDateKey, out var d) ? d : null;
                bool continueDay = date is not null && date == prevDate;
                foreach (var tc in clone.Elements(W + "tc"))
                {
                    var cellText = string.Concat(tc.Descendants(W + "t").Select(t => t.Value));
                    bool isDateCell = mergeDateKey is not null && cellText.Contains("{{" + prefix + mergeDateKey, StringComparison.OrdinalIgnoreCase);
                    if (isDateCell)
                    {
                        var vm = tc.Element(W + "tcPr")?.Element(W + "vMerge");
                        if (vm is not null)
                        {
                            if (continueDay) vm.SetAttributeValue(W + "val", null);
                            else vm.SetAttributeValue(W + "val", "restart");
                        }
                    }
                    foreach (var t in tc.Descendants(W + "t"))
                        t.Value = Token.Replace(t.Value, m =>
                        {
                            var key = m.Groups[1].Value;
                            if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return m.Value;
                            if (isDateCell && continueDay) return "";
                            return item.TryGetValue(key[prefix.Length..], out var v) ? v : "";
                        });
                }
                anchor.AddAfterSelf(clone);
                anchor = clone;
                prevDate = date;
            }
            row.Remove();
        }
    }

    private static void ReplaceTokens(XDocument x, IReadOnlyDictionary<string, string> values)
    {
        foreach (var t in x.Descendants(W + "t"))
        {
            if (!t.Value.Contains("{{")) continue;
            t.Value = Token.Replace(t.Value, m => values.TryGetValue(m.Groups[1].Value, out var v) ? v : "");
            Preserve(t);
        }
    }

    /// <summary>Tablolarda "etiket | değer" çiftleri: etiket bilinen bir alansa (alan, değer hücresi).</summary>
    private static IEnumerable<(string Key, XElement ValueCell)> LabelCells(XDocument x)
    {
        foreach (var row in x.Descendants(W + "tr"))
        {
            var cells = row.Elements(W + "tc").ToList();
            for (int i = 0; i + 1 < cells.Count; i++)
            {
                var label = Fold(CellText(cells[i])).Trim(' ', ':', ' ');
                if (!LabelMap.TryGetValue(label, out var key)) continue;
                var next = CellText(cells[i + 1]);
                if (next.Contains("{{")) continue; // işaretli şablon: işaret doldurur
                // Tablo başlık satırı ("TARİH | SAAT | PROGRAM"): yan hücre de başlıksa değer değildir.
                var nextFold = Fold(next).Trim(' ', ':');
                if (LabelMap.ContainsKey(nextFold) || HeaderWords.Contains(nextFold)) continue;
                yield return (key, cells[i + 1]);
            }
        }
    }

    private static void FillLabelCells(XDocument x, IReadOnlyDictionary<string, string> values)
    {
        foreach (var (key, cell) in LabelCells(x).ToList())
        {
            if (!values.TryGetValue(key, out var v) || string.IsNullOrWhiteSpace(v)) continue;
            var ts = cell.Descendants(W + "t").ToList();
            if (ts.Count == 0)
            {
                var p = cell.Element(W + "p") ?? new XElement(W + "p");
                if (p.Parent is null) cell.Add(p);
                var t = new XElement(W + "t", v); Preserve(t);
                p.Add(new XElement(W + "r", t));
                continue;
            }
            ts[0].Value = v; Preserve(ts[0]);
            foreach (var t in ts.Skip(1)) t.Value = "";
        }
    }

    private static string CellText(XElement tc) => string.Concat(tc.Descendants(W + "t").Select(t => t.Value)).Trim();

    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
            sb.Append(ch switch
            {
                'ç' or 'Ç' => 'c', 'ğ' or 'Ğ' => 'g', 'ı' or 'I' or 'İ' or 'i' => 'i',
                'ö' or 'Ö' => 'o', 'ş' or 'Ş' => 's', 'ü' or 'Ü' => 'u', _ => char.ToLowerInvariant(ch)
            });
        return Regex.Replace(sb.ToString(), @"\s+", " ");
    }
}
