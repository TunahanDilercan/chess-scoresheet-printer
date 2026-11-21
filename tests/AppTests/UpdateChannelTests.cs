using NotasyonOtomasyonu.App.Update;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Güncelleme kanalı: TunahanDilercan/chess-scoresheet-printer (eski lgndist deposu yalnız köprü).</summary>
[Collection("updater")] // CSP_UPDATE_API ortam değişkeni güncelleyici testleriyle paylaşılıyor: sırayla
public class UpdateChannelTests
{
    [Fact]
    public void Channel_IsTunahanDilercanRepository()
    {
        Assert.Equal("TunahanDilercan/chess-scoresheet-printer", Updater.Repo);
        Assert.Equal("https://github.com/TunahanDilercan/chess-scoresheet-printer/releases", Updater.ReleasesPage);
    }

    [Fact]
    public void DefaultApi_PointsToLatestReleaseOfChannel()
    {
        var saved = Environment.GetEnvironmentVariable("CSP_UPDATE_API");
        try
        {
            Environment.SetEnvironmentVariable("CSP_UPDATE_API", null);
            Assert.Equal("https://api.github.com/repos/TunahanDilercan/chess-scoresheet-printer/releases/latest", Updater.ApiUrl);
        }
        finally { Environment.SetEnvironmentVariable("CSP_UPDATE_API", saved); }
    }
}
