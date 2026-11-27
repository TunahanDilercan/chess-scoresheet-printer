using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;
using Xunit.Abstractions;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Görsel stiller açıkken (gerçek programdaki gibi) tablo çizilirken veri hatası kutusu çıkmamalı.</summary>
public class CardsFormDataErrorTests
{
    private readonly ITestOutputHelper _o;
    public CardsFormDataErrorTests(ITestOutputHelper o) => _o = o;

    [Fact]
    public void Grid_PaintsWithoutDataErrors()
    {
        var errors = new List<string>();
        var t = new Thread(() =>
        {
            Application.EnableVisualStyles();
            var dir = Path.Combine(Path.GetTempPath(), "csp-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var cats = new[] { new CategoryRef(1, "12 Yaş", true) };
            using var f = new CardsForm(new CardsContext("Deneme", cats, _ => Task.FromResult((8, "deneme")), new AppConfig(), () => { }, dir, dir))
            { StartPosition = FormStartPosition.Manual, Location = new Point(0, 0), ShowInTaskbar = false };
            DataGridView? grid = null;
            void Walk(Control c) { foreach (Control k in c.Controls) { if (k is DataGridView g) grid = g; Walk(k); } }
            Walk(f);
            grid!.DataError += (_, e) => errors.Add($"satır {e.RowIndex} sütun {grid.Columns[e.ColumnIndex].Name} ({e.Context}): {e.Exception?.GetType().Name}: {e.Exception?.Message}");
            f.Show();
            for (int i = 0; i < 20; i++) { Application.DoEvents(); Thread.Sleep(50); }
            using var bmp = new Bitmap(f.Width, f.Height);
            f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        foreach (var e in errors.Distinct()) _o.WriteLine(e);
        Assert.Empty(errors);
    }
}
