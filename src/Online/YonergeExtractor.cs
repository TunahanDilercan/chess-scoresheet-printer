using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// Yayımlanmış bir turnuva yönergesinden (TSF il sitesindeki PDF ya da Word belgesi) bilgileri okur:
/// organizasyon, il/ilçe, tarih, yer, sistem, düşünme süresi, son başvuru, başvuru adresi, direktör,
/// telefon, e-posta ve açılış programı saatleri. TSF'nin güncel (iki sütunlu, düz metin) yönerge
/// düzeni ile eski tablo düzeni desteklenir.
/// </summary>
public static class YonergeExtractor
{
    // ================= Metin çıkarma =================

    /// <summary>PDF metni; iki sütunlu sayfalarda önce sol, sonra sağ sütun okunur.</summary>
    public static string PdfText(byte[] pdf)
    {
        var sb = new StringBuilder();
        using var doc = PdfDocument.Open(pdf);
        foreach (var page in doc.GetPages())
        {
            var words = page.GetWords().Where(w => !string.IsNullOrWhiteSpace(w.Text)).ToList();
            if (words.Count == 0) continue;
            double split = ColumnSplit(words, page.Width);
            if (split <= 0) { AppendLines(sb, words); continue; }
            // Sütun çizgisini kesen satırlar (ortalı başlık) tam genişlik okunur, sonra sol ve sağ sütun.
            var full = words.GroupBy(LineKey).Where(g => IsFullWidth(g.ToList(), split)).SelectMany(g => g).ToHashSet();
            AppendLines(sb, full.ToList());
            var rest = words.Where(w => !full.Contains(w)).ToList();
            AppendLines(sb, rest.Where(w => w.BoundingBox.Left < split).ToList());
            AppendLines(sb, rest.Where(w => w.BoundingBox.Left >= split).ToList());
        }
        return sb.ToString();
    }

    /// <summary>
    /// Sayfayı ikiye bölen dikey boşluk: en az sözcüğün kestiği x'lerden oluşan en uzun aralığın
    /// ortası (sütun arası boşluğun ortası); sütun yoksa 0.
    /// </summary>
    private static double ColumnSplit(List<Word> words, double width)
    {
        var xs = new List<(double X, int Cross)>();
        for (double x = width * 0.35; x <= width * 0.65; x += 1)
            xs.Add((x, words.Count(w => w.BoundingBox.Left < x && w.BoundingBox.Right > x)));
        int min = xs.Min(p => p.Cross);
        // Ortalı başlık sütun arasını kesebilir: en az kesilen değerin biraz üstüne kadar izin verilir.
        int limit = Math.Max(min, Math.Max(1, words.Count / 40));
        double best = 0, runStart = -1, bestLen = -1;
        for (int i = 0; i <= xs.Count; i++)
        {
            bool inRun = i < xs.Count && xs[i].Cross <= limit;
            if (inRun && runStart < 0) runStart = xs[i].X;
            if (!inRun && runStart >= 0)
            {
                double end = xs[i - 1].X, len = end - runStart;
                if (len > bestLen) { bestLen = len; best = (runStart + end) / 2; }
                runStart = -1;
            }
        }
        min = xs.Where(p => Math.Abs(p.X - best) < 1).Select(p => p.Cross).DefaultIfEmpty(int.MaxValue).Min();
        int left = words.Count(w => w.BoundingBox.Right <= best), right = words.Count(w => w.BoundingBox.Left >= best);
        // Başlık gibi tek tük satırlar sütunu kesebilir; iki tarafta da yeterince sözcük varsa sütunludur.
        return min <= Math.Max(3, words.Count / 25) && left > words.Count / 10 && right > words.Count / 10 ? best : 0;
    }

    /// <summary>
    /// Sütun çizgisini kesen ya da iki yanında normal sözcük aralığıyla devam eden satır (ortalı başlık):
    /// iki sütunun yan yana satırları arasında ise geniş bir boşluk vardır.
    /// </summary>
    private static bool IsFullWidth(List<Word> line, double split)
    {
        if (line.Any(w => w.BoundingBox.Left < split && w.BoundingBox.Right > split)) return true;
        var l = line.Where(w => w.BoundingBox.Right <= split).Select(w => w.BoundingBox.Right).DefaultIfEmpty(double.NaN).Max();
        var r = line.Where(w => w.BoundingBox.Left >= split).Select(w => w.BoundingBox.Left).DefaultIfEmpty(double.NaN).Min();
        return !double.IsNaN(l) && !double.IsNaN(r) && r - l < 9;
    }

    /// <summary>Satır anahtarı: sözcüğün taban çizgisi ("Ç", "ş" gibi alt çıkıntılar satırı bölmesin).</summary>
    private static double LineKey(Word w)
        => Math.Round((w.Letters.Count > 0 ? w.Letters[0].StartBaseLine.Y : w.BoundingBox.Bottom) / 2.0);

    private static void AppendLines(StringBuilder sb, List<Word> words)
    {
        foreach (var line in words.GroupBy(LineKey).OrderByDescending(g => g.Key))
            sb.AppendLine(string.Join(" ", line.OrderBy(w => w.BoundingBox.Left).Select(w => w.Text)));
    }

    /// <summary>Word belgesinin düz metni (paragraf ve tablo hücreleri satır satır).</summary>
    public static string DocxText(byte[] docx)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip = new ZipArchive(new MemoryStream(docx), ZipArchiveMode.Read);
        var sb = new StringBuilder();
        foreach (var name in new[] { "word/header1.xml", "word/document.xml" })
        {
            var e = zip.GetEntry(name);
            if (e is null) continue;
            using var s = e.Open();
            var x = XDocument.Load(s);
            foreach (var p in x.Descendants(w + "p"))
                sb.AppendLine(string.Concat(p.Descendants(w + "t").Select(t => t.Value)));
        }
        return sb.ToString();
    }

    /// <summary>Belge başlığı ("… YÖNERGESİ" satırı); bulunamazsa ilk dolu satır.</summary>
    public static string Title(string text)
    {
        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        // Başlık büyük harfle yazılır; metin içindeki "yönergeleri" sözcüğü başlık sayılmaz.
        static bool Upper(string l) { var letters = l.Where(char.IsLetter).ToList(); return letters.Count > 0 && letters.Count(char.IsUpper) >= letters.Count * 0.8; }
        return lines.FirstOrDefault(l => Upper(l) && Regex.IsMatch(Fold(l), @"yonergesi|tutanagi"))
               ?? lines.FirstOrDefault(Upper) ?? lines.FirstOrDefault() ?? "";
    }

    // ================= Alanlar =================

    /// <summary>Metinden bulunabilen alanlar (anahtarlar ReportBuilder kataloğuyla aynı).</summary>
    public static Dictionary<string, string> Extract(string text)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var t = Regex.Replace(text.Replace('’', '\'').Replace('‘', '\''), @"[ \t]+", " ");
        var flat = Regex.Replace(t, @"\s*\n\s*", " ");          // satır sonları boşluk
        void Set(string key, string? v)
        {
            v = v?.Trim().Trim(',', ';', ' ');
            if (!string.IsNullOrWhiteSpace(v) && !d.ContainsKey(key)) d[key] = v!;
        }
        Match M(string pattern, string? s = null) => Regex.Match(s ?? flat, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        var title = Title(t);
        var tm = Regex.Match(title, @"^(.*?)\s+Y[ÖO]NERGES[İI]", RegexOptions.IgnoreCase);
        if (tm.Success) Set("TURNUVA_ADI", tm.Groups[1].Value);

        // --- TSF güncel düzen (düz metin) ---
        Set("ORGANIZASYON", M(@"1\.1\.\s*(.{5,120}?)\s+tarafından").Groups[1].Value);
        var il = M(@"(?:^|[,.\s])(\p{L}+)\s+ili\s*,\s*(\p{L}+(?:\s\p{L}+)?)\s+ilçesinde");
        if (il.Success) { Set("IL", il.Groups[1].Value); Set("ILCE", il.Groups[2].Value); }
        Set("TARIH_ARALIGI", M(@"(\d{1,2}(?:[./]\d{1,2}(?:[./]\d{4})?)?\s*[-–]\s*\d{1,2}[./]\d{1,2}[./]\d{4}|\d{1,2}[./]\d{1,2}[./]\d{4})\s+tarihleri").Groups[1].Value
                                .Replace(" ", "").Replace('–', '-'));
        Set("YER", M(@"tarihleri\s+arasında\s*,\s*(.{3,100}?)\s*'(?:n?[dt][ae])\b").Groups[1].Value);
        var sys = M(@"(İsviçre|Isvicre|Berger|Döner|Lig)\s+sistem");
        if (sys.Success) Set("SISTEM", Fold(sys.Groups[1].Value) switch
        {
            "berger" or "doner" => "BİREYSEL BERGER SİSTEMİ",
            "lig" => "LİG SİSTEMİ",
            _ => "BİREYSEL İSVİÇRE SİSTEMİ"
        });
        var tc = M(@"(\d{1,3})\s*(?:dakika|dk\.?|')\s*(?:\+|ve)?\s*(?:hamle\s+başına\s*)?(\d{1,2})\s*(?:saniye|sn\.?|'')\s*(?:eklemeli|ilaveli|artışlı)?");
        if (tc.Success) Set("DUSUNME_SURESI", TimeControls.Format(int.Parse(tc.Groups[1].Value), int.Parse(tc.Groups[2].Value)));
        else
        {
            var tm2 = M(@"(\d{1,3})\s*dakika\s+(?:zaman\s+)?tempo");
            if (tm2.Success) Set("DUSUNME_SURESI", TimeControls.Format(int.Parse(tm2.Groups[1].Value), 0));
        }
        var sb = M(@"başvurular[ıi]?\s+(\d{1,2}[./]\d{1,2}[./]\d{4})\s*(?:tarihinde\s*)?(?:saat\s*)?(\d{1,2}[:.]\d{2})?");
        if (sb.Success) Set("SON_BASVURU", (sb.Groups[1].Value + " " + sb.Groups[2].Value.Replace(':', '.')).Trim());
        var adr = M(@"Başvurular\s+((?:https?://)?[\w.-]+\.tsf\.org\.tr[^\s,]*)\s+adres");
        if (adr.Success && !adr.Groups[1].Value.Contains("xxx")) Set("BASVURU_ADRESI", adr.Groups[1].Value.StartsWith("http") ? adr.Groups[1].Value : "https://" + adr.Groups[1].Value);
        Set("DIREKTOR", M(@"(?:Yarışma|Turnuva)\s+Direktörü\s*:\s*(.{3,60}?)(?=\s+(?:Telefon|Tel\b|E-?posta|Başhakem|\d+\.\d*\.?\s|[1-9]\.\s)|$)").Groups[1].Value);
        Set("BASHAKEM", M(@"Başhakem\s*:\s*(.{3,60}?)(?=\s+(?:Telefon|Tel\b|E-?posta|\d+\.\d*\.?\s|[1-9]\.\s)|$)").Groups[1].Value);
        Set("TELEFON", M(@"Telefon(?:\s+Numarası)?\s*:\s*((?:\+?90[\s-]*)?\(?0?\d{3}\)?[\s-]*\d{3}[\s-]*\d{2}[\s-]*\d{2})(?!\d)").Groups[1].Value);
        var mail = M(@"E-?posta\s*:\s*(\S+)");
        if (mail.Success) Set("EPOSTA", FixEmail(mail.Groups[1].Value));
        var yr = M(@"vizesi\s+yapılmış\s+(\d{4})\s*(?:-|ve)\s*(\d{4})\s+yılları");
        if (yr.Success) Set("DOGUM_YILLARI", $"{yr.Groups[1].Value}-{yr.Groups[2].Value}");

        // Açılış programı (satır bazında: "09.30 Teknik Toplantı")
        Set("PROGRAM_KAYIT", M(@"(\d{1,2}[.:]\d{2}\s*[-–]\s*\d{1,2}[.:]\d{2})\s+Kayıt", t).Groups[1].Value.Replace(':', '.').Replace(" ", ""));
        Set("PROGRAM_TEKNIK", M(@"(\d{1,2}[.:]\d{2})\s+Teknik\s+Toplantı", t).Groups[1].Value.Replace(':', '.'));
        Set("PROGRAM_ILAN", M(@"(\d{1,2}[.:]\d{2})\s+1\.\s*Tur\s+Eşleştirme", t).Groups[1].Value.Replace(':', '.'));

        // --- eski tablo düzeni ("İLİ | Isparta | İLÇESİ | Merkez") ---
        var oil = M(@"\bİLİ\s+(\p{L}+)\s+İLÇESİ\s+(\p{L}+)", t);
        if (oil.Success) { Set("IL", oil.Groups[1].Value); Set("ILCE", oil.Groups[2].Value); }
        Set("YER", M(@"\bYERİ\s+(.+)", t).Groups[1].Value);
        Set("ORGANIZASYON", M(@"\bORGANİZASYON\s+(.+)", t).Groups[1].Value);
        Set("DUSUNME_SURESI", M(@"\bDÜŞÜNME SÜRESİ\s+(.+)", t).Groups[1].Value);

        return d;
    }

    /// <summary>PDF'ten "@" düşmüş e-postayı onarır: "aydintsf.org.tr" → "aydin@tsf.org.tr".</summary>
    public static string FixEmail(string s)
    {
        s = s.Trim().TrimEnd('.', ',', ';');
        if (s.Contains('@')) return s;
        var m = Regex.Match(s, @"^(.+?)(tsf\.org\.tr|gmail\.com|hotmail\.com|outlook\.com|yahoo\.com|icloud\.com|yandex\.com)$", RegexOptions.IgnoreCase);
        return m.Success ? $"{m.Groups[1].Value}@{m.Groups[2].Value}" : s;
    }

    private static string Fold(string s) => EventGrouping.Fold(s);
}
