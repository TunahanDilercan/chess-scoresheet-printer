using System.Text;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// chess-results üzerinden turnuva arama, kategori/tur keşfi ve eşleştirme çekme.
/// UI bu cepheyi kullanır; tüm çağrılar async'tir.
/// </summary>
public sealed class ChessResultsService : IDisposable
{
    private readonly ChessResultsClient _client;
    private readonly bool _ownsClient;

    public ChessResultsService(ChessResultsClient? client = null)
    {
        _ownsClient = client is null;
        _client = client ?? new ChessResultsClient();
    }

    /// <summary>
    /// Türkiye turnuvalarını getirir. İl ve/veya metin verilirse ad içinde (Türkçe-duyarsız) süzer;
    /// varsayılan olarak aynı etkinliğin kategorilerini tek girişe indirir.
    /// </summary>
    public async Task<IReadOnlyList<TournamentRef>> SearchTurkeyAsync(
        string? text, string? province = null, bool dedupe = true, CancellationToken ct = default)
    {
        var html = await _client.FetchAsync(ChessResultsClient.FederationUrl("TUR"), ct).ConfigureAwait(false);
        IReadOnlyList<TournamentRef> list = ChessResultsParser.ParseFederationList(html);

        if (!string.IsNullOrWhiteSpace(province))
        {
            var pf = Fold(province);
            list = list.Where(t => Fold(t.Name).Contains(pf)).ToList();
        }
        if (!string.IsNullOrWhiteSpace(text))
        {
            var f = Fold(text);
            list = list.Where(t => Fold(t.Name).Contains(f)).ToList();
        }
        return dedupe ? EventGrouping.Dedupe(list) : list.ToList();
    }

    /// <summary>Bir turnuvanın üst bilgisini + kategorilerini + tur sayısını getirir.</summary>
    public async Task<EventInfo> GetEventAsync(int tnr, CancellationToken ct = default)
    {
        var html = await _client.FetchAsync(ChessResultsClient.EventUrl(tnr), ct).ConfigureAwait(false);
        return ChessResultsParser.ParseEventInfo(html, tnr);
    }

    /// <summary>Seçili kategori (tnr) + turun güncel eşleştirmelerini çeker.</summary>
    public async Task<Tournament> GetPairingsAsync(int tnr, int round, CancellationToken ct = default)
    {
        var html = await _client.FetchAsync(ChessResultsClient.PairingsUrl(tnr, round), ct).ConfigureAwait(false);
        return ChessResultsParser.ParsePairings(html, tnr, round);
    }

    /// <summary>Türkçe karakterleri ASCII'ye indirip küçük harfe çevirir (arama için).</summary>
    private static string Fold(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim())
        {
            char c = char.ToLowerInvariant(ch);
            c = c switch
            {
                'ç' => 'c', 'ğ' => 'g', 'ı' => 'i', 'İ' => 'i', 'ö' => 'o', 'ş' => 's', 'ü' => 'u',
                'â' => 'a', 'î' => 'i', 'û' => 'u', _ => c
            };
            // 'I' küçük harfte 'i' olur; 'İ' yukarıda ele alındı.
            sb.Append(c);
        }
        return sb.ToString();
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}
