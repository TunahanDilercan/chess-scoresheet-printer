using System.Reflection;
using NotasyonOtomasyonu.App.Cards;
using NotasyonOtomasyonu.Core;
using NotasyonOtomasyonu.Online;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Masa kartları penceresindeki salon levhası satırları.</summary>
public class CardsFormSignTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "csp-cardsform-" + Guid.NewGuid().ToString("N"));
    public CardsFormSignTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private static void Sta(Action a)
    {
        Exception? error = null;
        var t = new Thread(() => { try { a(); } catch (Exception ex) { error = ex; } });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (error is not null) throw new Exception("test başarısız", error);
    }

    private CardsForm Open(AppConfig cfg, Action save)
    {
        var cats = new[] { new CategoryRef(1, "12 Yaş", true), new CategoryRef(2, "14 Yaş", false) };
        var f = new CardsForm(new CardsContext("Deneme", cats, _ => Task.FromResult((8, "deneme")), cfg, save, _dir, _dir))
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false
        };
        f.Show();
        Application.DoEvents();
        return f;
    }

    private static T Find<T>(Control root, Func<T, bool>? where = null) where T : Control
    {
        foreach (Control c in root.Controls)
        {
            if (c is T t && (where is null || where(t))) return t;
            try { return Find(c, where); } catch (InvalidOperationException) { }
        }
        throw new InvalidOperationException(typeof(T).Name + " bulunamadı");
    }

    private static List<CardSpec> Selected(CardsForm f) =>
        ((IEnumerable<CardSpec>)typeof(CardsForm).GetMethod("Selected", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(f, null)!).ToList();

    [Fact]
    public void SignRows_AreListedBelowCategories_AndPrintedOnceByDefault()
    {
        Sta(() =>
        {
            var cfg = new AppConfig();
            using var f = Open(cfg, () => { });
            var grid = Find<DataGridView>(f);
            var names = grid.Rows.Cast<DataGridViewRow>().Select(r => r.Cells["Kategori"].Value?.ToString() ?? "").ToList();
            Assert.Equal(new[] { "12 YAŞ", "14 YAŞ", "Salon levhaları", "İNCELEME MASASI", "KAYIT KONTROL", "WC", "SPORCU GİRİŞİ", "SPORCU ÇIKIŞI", "YÖNERGE", "YASAK LEVHASI", "" }, names);
            var sel = Selected(f);
            Assert.Equal(6, sel.Count(s => s.Kind == CardKind.Sign));
            Assert.All(sel.Where(s => s.Kind == CardKind.Sign), s => { Assert.Equal(1, s.Copies); Assert.Equal(CardArrow.Right, s.Arrow); });
            Assert.Single(sel, s => s.Kind == CardKind.Ban && s.Copies == 1);
            Assert.DoesNotContain(sel, s => s.Category.Length == 0);   // yazısız boş kart basılmaz
        });
    }

    [Fact]
    public void Edits_AreSaved_AndCustomCardPrintsWhenTyped()
    {
        Sta(() =>
        {
            var cfg = new AppConfig();
            int saves = 0;
            using (var f = Open(cfg, () => saves++))
            {
                var grid = Find<DataGridView>(f);
                var wc = grid.Rows.Cast<DataGridViewRow>().First(r => r.Cells["Kategori"].Value as string == "WC");
                wc.Cells["Ok"].Value = "← Sol";
                var custom = grid.Rows[grid.Rows.Count - 1];
                custom.Cells["Kategori"].Value = "hakem odası";
                custom.Cells["Ok"].Value = "↓ Aşağı";
                var sel = Selected(f);
                Assert.Contains(sel, s => s.Category == "WC" && s.Arrow == CardArrow.Left);
                Assert.Contains(sel, s => s.Category == "HAKEM ODASI" && s.Arrow == CardArrow.Down && s.Kind == CardKind.Sign);
            }
            Assert.True(saves > 0);
            Assert.Equal("Left", cfg.Cards.Signs[2].Arrow);
            Assert.Equal("HAKEM ODASI", cfg.Cards.Custom.Text);
            Assert.Equal("Down", cfg.Cards.Custom.Arrow);

            // yeniden açılınca aynı gelir
            using var g = Open(cfg, () => { });
            var grid2 = Find<DataGridView>(g);
            Assert.Equal("HAKEM ODASI", grid2.Rows[grid2.Rows.Count - 1].Cells["Kategori"].Value);
            Assert.Equal("← Sol", grid2.Rows.Cast<DataGridViewRow>().First(r => r.Cells["Kategori"].Value as string == "WC").Cells["Ok"].Value);
        });
    }

    [Fact]
    public void SelectAllButtons_AreAboveTheList_AndClearEverything()
    {
        Sta(() =>
        {
            var cfg = new AppConfig();
            using var f = Open(cfg, () => { });
            var grid = Find<DataGridView>(f);
            var none = Find<Button>(f, b => b.Text == "Tümünü kaldır");
            var all = Find<Button>(f, b => b.Text == "Tümünü seç");
            Assert.True(f.PointToClient(none.Parent!.PointToScreen(none.Location)).Y < f.PointToClient(grid.Parent!.PointToScreen(grid.Location)).Y,
                        "düğmeler listenin üstünde olmalı");
            none.PerformClick();
            Assert.Empty(Selected(f));
            Assert.All(cfg.Cards.Signs, s => Assert.False(s.Print));
            Assert.False(cfg.Cards.BanPrint);
            all.PerformClick();
            Assert.Equal(6, Selected(f).Count(s => s.Kind == CardKind.Sign));
            Assert.True(cfg.Cards.BanPrint);
        });
    }

    [Fact]
    public void OldSignList_GetsYonergeOnce_WithoutTouchingUserEdits()
    {
        // 1.7.4'te kaydedilmiş liste: YÖNERGE yok, kullanıcı WC'yi değiştirmiş
        var cfg = new AppConfig();
        cfg.Cards.Signs = new[] { "İNCELEME MASASI", "KAYIT KONTROL", "TUVALET", "SPORCU GİRİŞİ", "SPORCU ÇIKIŞI" }
            .Select(t => new SignCardConfig { Text = t }).ToList();
        cfg.Cards.SignSet = 0;
        Assert.True(cfg.Cards.UpgradeSigns());
        Assert.Equal(new[] { "İNCELEME MASASI", "KAYIT KONTROL", "TUVALET", "SPORCU GİRİŞİ", "SPORCU ÇIKIŞI", "YÖNERGE" }, cfg.Cards.Signs.Select(s => s.Text));
        Assert.Equal(1, cfg.Cards.Signs[^1].Copies);
        // kullanıcı sonra YÖNERGE'yi silerse geri gelmez
        cfg.Cards.Signs.RemoveAt(5);
        Assert.False(cfg.Cards.UpgradeSigns());
        Assert.Equal(5, cfg.Cards.Signs.Count);
        // yeni ayarda zaten var, iki kez eklenmez
        var fresh = new AppConfig();
        fresh.Cards.UpgradeSigns();
        Assert.Single(fresh.Cards.Signs, s => s.Text == "YÖNERGE");
    }

    [Fact]
    public void SelectingSignRow_ShowsSignPreview()
    {
        Sta(() =>
        {
            var cfg = new AppConfig();
            using var f = Open(cfg, () => { });
            var grid = Find<DataGridView>(f);
            var pic = Find<PictureBox>(f, p => p.Dock == DockStyle.Fill);
            var before = pic.Image;
            grid.Rows.Cast<DataGridViewRow>().First(r => r.Cells["Kategori"].Value as string == "YASAK LEVHASI").Selected = true;
            Application.DoEvents();
            Assert.NotNull(pic.Image);
            Assert.NotSame(before, pic.Image);
            // yasak levhasının önizlemesinde kırmızı yasak işaretleri var
            using var bmp = new Bitmap(pic.Image!);
            int red = 0;
            for (int y = 0; y < bmp.Height; y += 2) for (int x = 0; x < bmp.Width; x += 2) { var c = bmp.GetPixel(x, y); if (c.R > 160 && c.G < 90 && c.B < 90) red++; }
            Assert.True(red > 50);
        });
    }
}
