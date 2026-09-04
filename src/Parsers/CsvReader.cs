using System.Text;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>
/// Basit ama sağlam RFC4180 tarzı CSV ayrıştırıcı. Tırnaklı alanları ("...") ve
/// alan içi ayırıcı/yeni satırı destekler. Ayırıcı otomatik algılanır (tab, ;, ,).
/// </summary>
public static class CsvReader
{
    /// <summary>Metni satır/hücre matrisine çevirir. İlk anlamlı satır başlık kabul edilebilir.</summary>
    public static List<List<string>> Parse(string text, char? delimiter = null)
    {
        // BOM temizle
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        char delim = delimiter ?? DetectDelimiter(text);
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];

            if (inQuotes)
            {
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(ch);
                continue;
            }

            if (ch == '"') { inQuotes = true; }
            else if (ch == delim) { row.Add(field.ToString()); field.Clear(); }
            else if (ch == '\r') { /* yoksay; \n işleyecek */ }
            else if (ch == '\n')
            {
                row.Add(field.ToString()); field.Clear();
                rows.Add(row); row = new List<string>();
            }
            else field.Append(ch);
        }

        // Son alan/satır
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        // Tamamen boş satırları at
        return rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
    }

    /// <summary>İlk satırda en çok sütun üreten ayırıcıyı seç.</summary>
    private static char DetectDelimiter(string text)
    {
        var firstLine = text.Split('\n').FirstOrDefault(l => !string.IsNullOrWhiteSpace(l)) ?? "";
        char[] candidates = { '\t', ';', ',', '|' };
        char best = ',';
        int bestCount = 0;
        foreach (var d in candidates)
        {
            int count = firstLine.Count(c => c == d);
            if (count > bestCount) { bestCount = count; best = d; }
        }
        return best;
    }
}
