using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.App.Lists;
using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Çıktıların görüntüsü (göz kontrolü için; CSP_FORM_SHOTS klasörü verilince yazılır).</summary>
public class OutputSnapshotTests
{
    [Fact]
    public void Save()
    {
        var dir = Environment.GetEnvironmentVariable("CSP_FORM_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        var sheet = new ArbiterSheet("12 Yaş Kızlar", 3, "22.11.2026 10:00", ArbiterSheetTests.Pairings(8, bye: true));
        var p = new ArbiterSheetPrinter(new[] { sheet }, "Isparta Deneme Turnuvası", SheetLayout.Separate, penalty: PenaltyForm.Below);
        using (var bmp = p.RenderPage(0, 1100)) bmp.Save(Path.Combine(dir, "cikti_hakem_kagidi.png"));

        var cats = new Dictionary<string, string> { ["12 Yaş"] = "#D32F2F", ["14 Yaş"] = "#1565C0", ["16 Yaş"] = "#F9A825", ["Açık"] = "#2E7D32" };
        var athletes = new[]
        {
            new Athlete("Deniz Örnek", "12 Yaş", "Isparta Fen Lisesi", 1500, 1400), new Athlete("Ali Sınama", "14 Yaş", "Burdur"),
            new Athlete("Elif Deneme", "16 Yaş", "Antalya", null, 1320), new Athlete("Can Örnekli", "Açık", "Isparta"),
        };
        using var ap = new AthleteSheetPrinter(athletes, new AthleteCardConfig(), new AthleteImages(null, null, null, null), "Deneme", c => cats.GetValueOrDefault(c));
        using var page = new Bitmap(1240, 1754);
        page.SetResolution(150, 150);
        using (var g = Graphics.FromImage(page)) { g.Clear(Color.White); g.PageUnit = GraphicsUnit.Point; PagedOutput.Draw(ap, g, 0); }
        page.Save(Path.Combine(dir, "cikti_sporcu_kategori_renkleri.png"));
    }
}
