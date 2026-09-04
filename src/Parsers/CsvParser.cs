using System.Text;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>CSV / TXT eşleştirme dosyalarını okur (Swiss-Manager text export).</summary>
public sealed class CsvParser : IPairingParser
{
    public bool CanParse(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext is ".csv" or ".txt" or ".tsv";
    }

    public Tournament Parse(string filePath, AppConfig config)
    {
        try
        {
            // UTF-8 (BOM dahil) oku — Türkçe karakterler için kritik.
            var text = File.ReadAllText(filePath, Encoding.UTF8);
            var rows = CsvReader.Parse(text);

            if (rows.Count < 2)
                throw new ParseException("CSV dosyasında başlık + veri satırı bulunamadı.");

            var headers = rows[0];
            var mapper = new TabularMapper(headers, config);
            if (!mapper.HasMinimumColumns)
                throw new ParseException(
                    "CSV başlıklarında Beyaz oyuncu sütunu eşlenemedi. " +
                    "config.json içindeki 'mapping' bölümünü dosyanızın başlıklarına göre düzenleyin. " +
                    $"Bulunan başlıklar: {string.Join(", ", headers)}");

            var pairings = new List<Pairing>();
            for (int i = 1; i < rows.Count; i++)
                pairings.Add(mapper.MapRow(rows[i], fallbackBoard: i));

            return ParserUtil.BuildTournament(config, pairings);
        }
        catch (ParseException) { throw; }
        catch (Exception ex)
        {
            throw new ParseException($"CSV okunamadı: {ex.Message}", ex);
        }
    }
}
