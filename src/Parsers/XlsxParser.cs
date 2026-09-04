using ClosedXML.Excel;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>XLSX (Excel) eşleştirme dosyalarını okur. İlk dolu çalışma sayfasını kullanır.</summary>
public sealed class XlsxParser : IPairingParser
{
    public bool CanParse(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() is ".xlsx" or ".xlsm";

    public Tournament Parse(string filePath, AppConfig config)
    {
        try
        {
            using var wb = new XLWorkbook(filePath);
            var ws = wb.Worksheets.FirstOrDefault()
                     ?? throw new ParseException("Excel dosyasında çalışma sayfası yok.");

            var range = ws.RangeUsed();
            if (range is null)
                throw new ParseException("Excel sayfası boş.");

            var rows = range.RowsUsed().ToList();
            if (rows.Count < 2)
                throw new ParseException("Excel'de başlık + veri satırı bulunamadı.");

            // Başlık satırı
            var headers = rows[0].Cells(range.FirstColumn().ColumnNumber(), range.LastColumn().ColumnNumber())
                                 .Select(c => c.GetString().Trim())
                                 .ToList();

            var mapper = new TabularMapper(headers, config);
            if (!mapper.HasMinimumColumns)
                throw new ParseException(
                    "Excel başlıklarında Beyaz oyuncu sütunu eşlenemedi. " +
                    "config.json 'mapping' bölümünü düzenleyin. " +
                    $"Bulunan başlıklar: {string.Join(", ", headers)}");

            int firstCol = range.FirstColumn().ColumnNumber();
            int lastCol = range.LastColumn().ColumnNumber();

            var pairings = new List<Pairing>();
            for (int i = 1; i < rows.Count; i++)
            {
                var cells = rows[i].Cells(firstCol, lastCol).Select(c => c.GetString()).ToList();
                pairings.Add(mapper.MapRow(cells, fallbackBoard: i));
            }

            return ParserUtil.BuildTournament(config, pairings);
        }
        catch (ParseException) { throw; }
        catch (Exception ex)
        {
            throw new ParseException($"Excel okunamadı: {ex.Message}", ex);
        }
    }
}
