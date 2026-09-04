using System.Text.Json;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Parsers;

/// <summary>
/// JSON eşleştirme dosyalarını okur. En temiz kaynak budur.
/// İki şekli destekler:
///  1) Yapısal nesne: { name, roundNo, pairings:[ { board, white, black, result } ] }
///  2) Düz dizi:      [ { Board, White, WhiteRating, Black, ... }, ... ] (mapping ile)
/// </summary>
public sealed class JsonParser : IPairingParser
{
    public bool CanParse(string filePath)
        => Path.GetExtension(filePath).ToLowerInvariant() == ".json";

    public Tournament Parse(string filePath, AppConfig config)
    {
        try
        {
            var text = File.ReadAllText(filePath, System.Text.Encoding.UTF8);
            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;

            return root.ValueKind switch
            {
                JsonValueKind.Object => ParseStructured(root, config),
                JsonValueKind.Array => ParseArray(root, config),
                _ => throw new ParseException("Beklenmeyen JSON kökü (nesne ya da dizi olmalı).")
            };
        }
        catch (ParseException) { throw; }
        catch (JsonException ex)
        {
            throw new ParseException($"JSON biçimi bozuk: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new ParseException($"JSON okunamadı: {ex.Message}", ex);
        }
    }

    // --- Yapısal nesne ---
    private static Tournament ParseStructured(JsonElement root, AppConfig config)
    {
        var pairingsEl = FindArray(root, "pairings", "games", "boards", "eslesmeler");
        if (pairingsEl is null)
            throw new ParseException("JSON nesnesinde 'pairings' dizisi bulunamadı.");

        var pairings = new List<Pairing>();
        int idx = 0;
        foreach (var pe in pairingsEl.Value.EnumerateArray())
        {
            idx++;
            int board = GetInt(pe, "board", "masa", "br", "bo") ?? idx;

            var white = ReadPlayer(pe, "white", "beyaz")
                        ?? new Player(null, "—");
            Player? black = ReadPlayer(pe, "black", "siyah");
            // Açıkça null veya "bye" işaretli ise BAY.
            if (black is not null && IsByeName(black.Name)) black = null;

            var result = GetString(pe, "result", "sonuc", "sonuç");
            pairings.Add(new Pairing(board, white, black, result));
        }

        var name = GetString(root, "name", "tournament", "turnuva", "ad") ?? config.Tournament.Name;
        var round = GetInt(root, "roundNo", "round", "tur", "rnd") ?? config.Tournament.LastRound;
        var loc = GetString(root, "location", "yer", "city") ?? config.Tournament.Location;
        var date = GetString(root, "date", "tarih") ?? config.Tournament.Date;
        var tc = GetString(root, "timeControl", "tempo", "zaman") ?? config.Tournament.TimeControl;
        var arb = GetString(root, "arbiter", "hakem") ?? config.Tournament.Arbiter;

        return new Tournament(name, round, pairings, loc, date, tc, arb);
    }

    // --- Düz dizi (satırlar) ---
    private static Tournament ParseArray(JsonElement root, AppConfig config)
    {
        var pairings = new List<Pairing>();
        int idx = 0;
        foreach (var row in root.EnumerateArray())
        {
            idx++;
            int board = GetInt(row, "board", "masa", "br", "bo") ?? idx;
            var white = new Player(
                StartNo: GetInt(row, "whiteStartNo", "sno", "no"),
                Name: GetString(row, "whiteName", "white", "beyaz", "name") ?? "—",
                Title: GetString(row, "whiteTitle", "title"),
                Rating: NullIfZero(GetInt(row, "whiteRating", "rtg", "elo", "rating")));

            var blackName = GetString(row, "blackName", "black", "siyah");
            Player? black = IsByeName(blackName)
                ? null
                : new Player(
                    StartNo: GetInt(row, "blackStartNo"),
                    Name: blackName ?? "—",
                    Title: GetString(row, "blackTitle"),
                    Rating: NullIfZero(GetInt(row, "blackRating", "rtg2", "elo2")));

            pairings.Add(new Pairing(board, white, black, GetString(row, "result", "sonuc")));
        }
        return ParserUtil.BuildTournament(config, pairings);
    }

    // --- yardımcılar ---
    private static Player? ReadPlayer(JsonElement parent, params string[] keys)
    {
        foreach (var k in keys)
            if (TryGet(parent, k, out var el))
            {
                if (el.ValueKind == JsonValueKind.Null) return null;
                if (el.ValueKind == JsonValueKind.String) // sadece isim verilmiş
                {
                    var nm = el.GetString();
                    return string.IsNullOrWhiteSpace(nm) ? null : new Player(null, nm!.Trim());
                }
                if (el.ValueKind == JsonValueKind.Object)
                {
                    var name = GetString(el, "name", "ad", "isim") ?? "—";
                    return new Player(
                        StartNo: GetInt(el, "startNo", "sno", "no"),
                        Name: name,
                        Title: GetString(el, "title", "unvan"),
                        Rating: NullIfZero(GetInt(el, "rating", "rtg", "elo")),
                        Federation: GetString(el, "federation", "fed"),
                        Club: GetString(el, "club", "kulup", "kulüp"));
                }
            }
        return null;
    }

    private static bool IsByeName(string? name)
    {
        var n = (name ?? "").Trim().ToLowerInvariant();
        return n.Length == 0 || n is "bay" or "bye" or "-" || n.Contains("bye") || n.Contains("bay");
    }

    private static int? NullIfZero(int? v) => v is null or 0 ? null : v;

    private static JsonElement? FindArray(JsonElement obj, params string[] keys)
    {
        foreach (var k in keys)
            if (TryGet(obj, k, out var el) && el.ValueKind == JsonValueKind.Array)
                return el;
        return null;
    }

    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        if (obj.ValueKind == JsonValueKind.Object)
            foreach (var p in obj.EnumerateObject())
                if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = p.Value;
                    return true;
                }
        value = default;
        return false;
    }

    private static string? GetString(JsonElement obj, params string[] keys)
    {
        foreach (var k in keys)
            if (TryGet(obj, k, out var el))
            {
                var s = el.ValueKind switch
                {
                    JsonValueKind.String => el.GetString(),
                    JsonValueKind.Number => el.ToString(),
                    _ => null
                };
                if (!string.IsNullOrWhiteSpace(s)) return s!.Trim();
            }
        return null;
    }

    private static int? GetInt(JsonElement obj, params string[] keys)
    {
        foreach (var k in keys)
            if (TryGet(obj, k, out var el))
            {
                if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out var n)) return n;
                if (el.ValueKind == JsonValueKind.String && int.TryParse(el.GetString(), out var m)) return m;
            }
        return null;
    }
}
