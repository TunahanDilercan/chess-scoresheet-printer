using System.Net;
using System.Text.RegularExpressions;

namespace NotasyonOtomasyonu.Online;

/// <summary>
/// chess-results.com'a HTTP istekleri yapar ve URL'leri kurar.
/// Sayfalar UTF-8 + HTML entity döner; gövde olduğu gibi okunur (parser entity çözer).
/// </summary>
public sealed class ChessResultsClient : IDisposable
{
    private const string BaseHost = "https://chess-results.com";
    private readonly HttpClient _http;

    public ChessResultsClient(HttpClient? http = null)
    {
        if (http is not null) { _http = http; return; }

        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            AllowAutoRedirect = true
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(25) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("tr,en;q=0.8");
    }

    public Task<string> FetchAsync(string url, CancellationToken ct = default)
        => _http.GetStringAsync(url, ct);

    // ---- URL kurucular (lan=8 = Türkçe) ----
    public static string FederationUrl(string fed = "TUR") => $"{BaseHost}/fed.aspx?lan=8&fed={fed}";

    /// <summary>Belirli tur eşleştirmeleri (art=2 = eşleştirme listesi).</summary>
    public static string PairingsUrl(int tnr, int round)
        => $"{BaseHost}/tnr{tnr}.aspx?lan=8&art=2&rd={round}&turdet=YES";

    /// <summary>Etkinlik genel sayfası (başlangıç sıralaması) — kategori/tur bilgisi içerir.</summary>
    public static string EventUrl(int tnr)
        => $"{BaseHost}/tnr{tnr}.aspx?lan=8&art=1&turdet=YES";

    /// <summary>
    /// Kullanıcı girdisinden turnuva numarasını çıkarır.
    /// "1437263", "tnr1437263", tam URL (…/Tnr1437263.aspx?…) kabul eder.
    /// </summary>
    public static int? ParseTnr(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        input = input.Trim();

        var m = Regex.Match(input, @"[Tt]nr(\d+)");
        if (m.Success && int.TryParse(m.Groups[1].Value, out var t1)) return t1;

        // Düz sayı
        if (Regex.IsMatch(input, @"^\d+$") && int.TryParse(input, out var t2)) return t2;

        return null;
    }

    public void Dispose() => _http.Dispose();
}
