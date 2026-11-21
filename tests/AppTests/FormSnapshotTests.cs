using NotasyonOtomasyonu.App;
using NotasyonOtomasyonu.App.Athletes;
using NotasyonOtomasyonu.App.Badges;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>
/// Pencerelerin ekran görüntüsü (göz kontrolü için): CSP_FORM_SHOTS klasörü verilirse pencereler ekran dışında
/// açılıp PNG olarak kaydedilir. Klasör verilmezse yalnız açılıp kapanır (kurucu ve simgeler hata vermiyor mu).
/// </summary>
public class FormSnapshotTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-shots-" + Guid.NewGuid().ToString("N"));
    public FormSnapshotTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void ToolWindows_OpenWithIcons()
    {
        var outDir = Environment.GetEnvironmentVariable("CSP_FORM_SHOTS");
        var cfg = new AppConfig();
        var cats = new[] { new CategoryRef(1, "12 Yaş", true), new CategoryRef(2, "14 Yaş", false) };
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                Snap(outDir, "athletes", new AthletesForm(new AthletesContext("Deneme Turnuvası", null, cats, null, cfg, () => { }, _dir, _dir)));
                Snap(outDir, "badges", new BadgesForm(new BadgesContext("Deneme Turnuvası",
                    () => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>()), cfg, () => { }, _dir, _dir)));
                Snap(outDir, "cards", new CardsForm(new CardsContext("Deneme Turnuvası", cats,
                    _ => Task.FromResult((8, "deneme")), cfg, () => { }, _dir, _dir)));
                Snap(outDir, "settings", new SettingsForm(cfg, _ => { }, _ => { }));
                Snap(outDir, "templates", new TemplatesForm(cfg));
                Snap(outDir, "log", new LogWindow(() => "Basıldı: deneme\r\nUyarı: deneme", () => { }, () => "ayrıntı"));
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Exception("pencere açılamadı", error);
    }

    private static void Snap(string? outDir, string name, Form f)
    {
        using (f)
        {
            f.StartPosition = FormStartPosition.Manual;
            f.Location = new Point(-5000, -5000);
            f.ShowInTaskbar = false;
            f.Show();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(25); }
            var withIcons = AllButtons(f).Count(b => Icons.IconOf(b) is not null && b.Image is not null);
            Assert.True(withIcons > 0, name + ": simgeli düğme yok");
            if (!string.IsNullOrEmpty(outDir))
            {
                using var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
                bmp.Save(Path.Combine(outDir, name + ".png"));
            }
            f.Close();
        }
    }

    private static IEnumerable<ButtonBase> AllButtons(Control c)
    {
        foreach (Control k in c.Controls)
        {
            if (k is ButtonBase b) yield return b;
            foreach (var d in AllButtons(k)) yield return d;
        }
    }
}
