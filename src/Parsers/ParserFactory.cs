using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>Dosya uzantısına göre uygun parser'ı seçer ve çalıştırır.</summary>
public static class ParserFactory
{
    private static readonly IPairingParser[] Parsers =
    {
        new JsonParser(),
        new XlsxParser(),
        new CsvParser(),
    };

    /// <summary>Dosyaya uygun parser'ı bul. Uzantı tanınmazsa içerikten sezer.</summary>
    public static IPairingParser? Create(string filePath)
        => Parsers.FirstOrDefault(p => p.CanParse(filePath)) ?? SniffByContent(filePath);

    /// <summary>
    /// Uzantı bilinmiyorsa dosya içeriğinden biçimi anla:
    /// ZIP imzası (PK) → XLSX; ilk anlamlı karakter { veya [ → JSON; diğer → CSV/TXT.
    /// </summary>
    private static IPairingParser? SniffByContent(string filePath)
    {
        try
        {
            if (!File.Exists(filePath)) return null;
            using var fs = File.OpenRead(filePath);
            Span<byte> head = stackalloc byte[16];
            int n = fs.Read(head);

            int start = 0;
            if (n >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) start = 3; // UTF-8 BOM

            if (n - start >= 2 && head[start] == 0x50 && head[start + 1] == 0x4B) // "PK" -> xlsx/zip
                return Parsers.OfType<XlsxParser>().First();

            // İlk boşluk olmayan karakter
            for (int i = start; i < n; i++)
            {
                char c = (char)head[i];
                if (c is ' ' or '\t' or '\r' or '\n' or '﻿') continue;
                if (c == '{' || c == '[') return Parsers.OfType<JsonParser>().First();
                break;
            }
            return Parsers.OfType<CsvParser>().First();
        }
        catch { return null; }
    }

    /// <summary>Kısa yol: parser'ı seç ve dosyayı ayrıştır.</summary>
    public static Tournament Parse(string filePath, AppConfig config)
    {
        if (!File.Exists(filePath))
            throw new ParseException($"Dosya bulunamadı: {filePath}");

        var parser = Create(filePath)
            ?? throw new ParseException(
                $"Dosya türü tanınamadı: {Path.GetExtension(filePath)}. " +
                "Desteklenenler: .json, .csv, .txt, .xlsx");

        // Swiss-Manager'ın kendi turnuva dosyası (.TUNX/.TUN) ikili biçimdedir; metin sanılıp
        // okunursa hata penceresine anlamsız karakterler dökülüyordu.
        if (parser is CsvParser && LooksBinary(filePath))
            throw new ParseException(
                "Bu dosya Swiss-Manager'ın kendi turnuva dosyası (ikili TUNX biçimi) ve doğrudan okunamıyor.\n" +
                "Swiss-Manager'da eşleştirme listesini Excel/CSV olarak dışa aktarın ya da " +
                "Online (chess-results) kaynağını kullanın.");

        return parser.Parse(filePath, config);
    }

    /// <summary>İlk 4 KB'ta NUL baytı varsa metin değil ikili dosyadır.</summary>
    private static bool LooksBinary(string filePath)
    {
        try
        {
            using var fs = File.OpenRead(filePath);
            var buf = new byte[4096];
            int n = fs.Read(buf, 0, buf.Length);
            // UTF-16 metinlerde de NUL olur; BOM'lu UTF-16'yı ikili sayma.
            if (n >= 2 && ((buf[0] == 0xFF && buf[1] == 0xFE) || (buf[0] == 0xFE && buf[1] == 0xFF))) return false;
            return Array.IndexOf(buf, (byte)0, 0, n) >= 0;
        }
        catch { return false; }
    }

    /// <summary>Desteklenen uzantılar için OpenFileDialog filtresi.</summary>
    public const string FileDialogFilter =
        "Eşleştirme dosyaları (*.json;*.csv;*.txt;*.xlsx)|*.json;*.csv;*.txt;*.xlsx|" +
        "JSON (*.json)|*.json|CSV/TXT (*.csv;*.txt)|*.csv;*.txt|Excel (*.xlsx)|*.xlsx|" +
        "Tümü (*.*)|*.*";
}
