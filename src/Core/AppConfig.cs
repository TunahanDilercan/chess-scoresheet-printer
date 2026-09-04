using System.Text.Json;
using System.Text.Json.Serialization;

namespace NotasyonOtomasyonu.Core;

/// <summary>
/// Uygulama yapılandırması. config.json'dan okunur, son kullanılan değerleri hatırlamak için
/// geri yazılabilir. Tüm alanların makul varsayılanları vardır; config.json olmasa da çalışır.
/// </summary>
public sealed class AppConfig
{
    public TournamentConfig Tournament { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();

    /// <summary>chess-results entegrasyonu ve son seçili turnuva/kategori (kalıcı).</summary>
    public OnlineConfig Online { get; set; } = new();

    /// <summary>Hazır notasyon kağıdına bindirme (overlay) şablonu — ETKİN olan.</summary>
    public OverlayTemplate Overlay { get; set; } = new();

    /// <summary>Kayıtlı hazır kağıt şablonları kütüphanesi (kullanıcı birden çok ekleyebilir).</summary>
    public List<OverlayTemplate> Templates { get; set; } = new();

    /// <summary>
    /// CSV/XLSX sütun başlığı eşlemesi. Her hedef alan için olası başlık adları (büyük/küçük harf duyarsız).
    /// Sürüme/dile göre değişen Swiss-Manager başlıklarını tek modele bağlar.
    /// </summary>
    public Dictionary<string, List<string>> Mapping { get; set; } = DefaultMapping();

    // ---- JSON ayarları ----
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>Dosyadan yükle; yoksa veya bozuksa varsayılanları döndür (asla istisna fırlatmaz).</summary>
    public static AppConfig Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AppConfig();
            var json = File.ReadAllText(path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
            // Mapping boş geldiyse varsayılanlarla doldur ki parser çalışsın.
            if (cfg.Mapping is null || cfg.Mapping.Count == 0)
                cfg.Mapping = DefaultMapping();
            else
                MergeDefaults(cfg.Mapping);
            return cfg;
        }
        catch
        {
            return new AppConfig();
        }
    }

    /// <summary>Diske kaydet (hata olursa sessizce yutulur, çökme yok).</summary>
    public void Save(string path)
    {
        try
        {
            var json = JsonSerializer.Serialize(this, JsonOpts);
            File.WriteAllText(path, json);
        }
        catch { /* config kaydı kritik değil */ }
    }

    /// <summary>Config'te tanımlı olmayan eşleme anahtarlarını varsayılanlardan tamamla.</summary>
    private static void MergeDefaults(Dictionary<string, List<string>> map)
    {
        foreach (var (k, v) in DefaultMapping())
            if (!map.ContainsKey(k)) map[k] = v;
    }

    /// <summary>Türkçe + İngilizce Swiss-Manager başlık adayları.</summary>
    public static Dictionary<string, List<string>> DefaultMapping() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["board"]            = new() { "Br.", "Br", "Masa", "Board", "Bo.", "Bo", "Table", "No" },
        ["whiteStartNo"]     = new() { "SNo", "SNr", "Sıra", "StartNo", "No1", "WhiteNo" },
        ["whiteName"]        = new() { "Beyaz", "White", "Name", "Ad Soyad", "İsim", "Player1", "WhiteName" },
        ["whiteTitle"]       = new() { "Unvan", "Title", "Ttl", "WhiteTitle" },
        ["whiteRating"]      = new() { "Rtg", "RtgI", "ELO", "Rating", "WRtg", "WhiteRating", "Elo1" },
        ["whiteFederation"]  = new() { "Fed", "Federation", "Ülke", "WhiteFed" },
        ["whiteClub"]        = new() { "Kulüp", "Club", "Takım", "WhiteClub" },
        ["blackStartNo"]     = new() { "SNo2", "No2", "BlackNo" },
        ["blackName"]        = new() { "Siyah", "Black", "Player2", "BlackName", "Rakip" },
        ["blackTitle"]       = new() { "Unvan2", "Title2", "BlackTitle" },
        ["blackRating"]      = new() { "Rtg2", "ELO2", "BRtg", "BlackRating", "Elo2", "RtgII" },
        ["blackFederation"]  = new() { "Fed2", "BlackFed" },
        ["blackClub"]        = new() { "Kulüp2", "Club2", "BlackClub" },
        ["result"]           = new() { "Sonuç", "Result", "Res.", "Res" }
    };
}

public sealed class TournamentConfig
{
    public string Name { get; set; } = "Turnuva Adı";
    public string? Location { get; set; }
    public string TimeControl { get; set; } = "90 dk + 30 sn";
    public string? Arbiter { get; set; }
    /// <summary>Notasyona basılan TEK tarih (örn. "14.06.2026").</summary>
    public string? Date { get; set; }
    /// <summary>Turnuvanın ham tarih ifadesi/aralığı (tarih seçimi listesi için).</summary>
    public string? DateRange { get; set; }
    /// <summary>UI'da son seçilen tur no'yu hatırlamak için.</summary>
    public int LastRound { get; set; } = 1;

    /// <summary>
    /// Kullanıcı ad/zaman/hakem bilgisini elle değiştirdi mi? true ise senkronda çekilen
    /// veriyle ezilmez. Farklı turnuva seçilince false'a döner (çekilen veri kullanılsın).
    /// </summary>
    public bool ManualOverride { get; set; }
}

/// <summary>
/// chess-results.com ayarları ve kullanıcının son seçimi. Tümü kalıcıdır; kullanıcı
/// başka turnuva/kategori seçene kadar burada saklı kalır.
/// </summary>
public sealed class OnlineConfig
{
    /// <summary>Ülke sabit (Türkiye = TUR federasyonu).</summary>
    public string Country { get; set; } = "Türkiye";
    public string Federation { get; set; } = "TUR";

    /// <summary>Arama kutusunda en son yazılan metin.</summary>
    public string CityFilter { get; set; } = "";

    /// <summary>Seçili il (filtre). "(Tüm iller)" = filtre yok.</summary>
    public string Province { get; set; } = "(Tüm iller)";

    // Son seçili turnuva (etkinlik) ve kategori
    public int? SelectedTnr { get; set; }
    public string? SelectedTournamentName { get; set; }
    public int? SelectedCategoryTnr { get; set; }
    public string? SelectedCategoryName { get; set; }

    /// <summary>Son kullanılan veri kaynağı: "Dosya" veya "Online".</summary>
    public string Source { get; set; } = "Online";
}

public sealed class LayoutConfig
{
    /// <summary>Sayfa başına kağıt: 1 veya 2.</summary>
    public int PerPage { get; set; } = 2;
    /// <summary>Toplam hamle kapasitesi (2 bloğa bölünür).</summary>
    public int MoveRows { get; set; } = 60;
    public bool ShowRefereeSignature { get; set; } = true;
    /// <summary>BAY masaları için kağıt basılsın mı?</summary>
    public bool PrintByeSheets { get; set; } = true;

    /// <summary>Her masa için nüsha sayısı (iki oyuncuya da aynı notasyon → varsayılan 2).</summary>
    public int CopiesPerBoard { get; set; } = 2;

    /// <summary>Sayfa boyutu: "A4" veya "A5". Çıktılar genelde A5.</summary>
    public string PageSize { get; set; } = "A5";

    /// <summary>
    /// Basılmayacak (hariç tutulacak) masa numaraları. "3,7,12-15" gibi. Boş = hepsini bas.
    /// Kullanıcı "Hariç Tut" ekranından seçer; kalıcıdır.
    /// </summary>
    public string ExcludedBoards { get; set; } = "";
}
