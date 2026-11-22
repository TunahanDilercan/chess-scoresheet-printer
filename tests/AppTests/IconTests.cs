using NotasyonOtomasyonu.App;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Kendi çizdiğimiz simgeler: her biri çizilir, çerçeveden taşmaz, düğmeye doğru yerleşir.</summary>
public class IconTests
{
    public static IEnumerable<object[]> All => Enum.GetValues<Ico>().Select(i => new object[] { i });

    [Theory]
    [MemberData(nameof(All))]
    public void EveryIcon_DrawsInsideItsSquare(Ico icon)
    {
        const int px = 48;
        var bmp = new Bitmap(px, px);
        using (var g = Graphics.FromImage(bmp)) Icons.Draw(g, icon, new RectangleF(0, 0, px, px), Color.Black);
        int ink = 0, edge = 0;
        for (int y = 0; y < px; y++)
            for (int x = 0; x < px; x++)
            {
                if (bmp.GetPixel(x, y).A < 40) continue;
                ink++;
                if (x == 0 || y == 0 || x == px - 1 || y == px - 1) edge++;
            }
        Assert.True(ink > 20, $"{icon}: boş");
        Assert.True(edge == 0, $"{icon}: kenara taşıyor ({edge} px)");
    }

    [Fact]
    public void Render_IsCachedPerSizeAndColor()
    {
        Assert.Same(Icons.Render(Ico.Print, 16, Color.White), Icons.Render(Ico.Print, 16, Color.White));
        Assert.NotSame(Icons.Render(Ico.Print, 16, Color.White), Icons.Render(Ico.Print, 20, Color.White));
        Assert.NotSame(Icons.Render(Ico.Print, 16, Color.White), Icons.Render(Ico.Print, 16, Color.Black));
    }

    [Fact]
    public void Render_DoesNotShareBitmapsAcrossThreads()
    {
        var here = Icons.Render(Ico.Badge, 18, Color.Black);
        Bitmap? there = null;
        var t = new Thread(() => there = Icons.Render(Ico.Badge, 18, Color.Black));
        t.Start(); t.Join();
        Assert.NotNull(there);
        Assert.NotSame(here, there);
    }

    [Fact]
    public void Button_GetsIconBeforeText_ColouredByStyle()
    {
        using var light = Icons.Set(new Button { Text = "Ayarlar", ForeColor = Color.Black, Font = new Font("Segoe UI", 10f) }, Ico.Settings);
        Assert.NotNull(light.Image);
        // sabit genişlik: yerleşimi biz hesaplarız (yazı simge payı kadar içeriden); kendi boyunu alan düğmede WinForms
        Assert.Equal(TextImageRelation.Overlay, light.TextImageRelation);
        Assert.True(light.Padding.Left > light.Image!.Height);
        using (var auto = Icons.Set(new Button { Text = "Ayarlar", AutoSize = true }, Ico.Settings))
        {
            Assert.Equal(TextImageRelation.ImageBeforeText, auto.TextImageRelation);
            Assert.Equal(0, auto.Padding.Left);
        }
        Assert.Equal(Icons.SizeFor(light.Font), light.Image!.Height);
        Assert.True(light.Image.Width > light.Image.Height); // yazıyla arasında boşluk
        Assert.Equal(Icons.Accent.ToArgb(), Ink((Bitmap)light.Image));

        using var primary = Icons.Set(new Button { Text = "Yazdır", BackColor = Color.Green, ForeColor = Color.White }, Ico.Print);
        Assert.Equal(Color.White.ToArgb(), Ink((Bitmap)primary.Image!));

        // yazı rengi sonradan değişince simge de değişir; yazı tipi büyüyünce simge büyür
        primary.ForeColor = Color.Black;
        Assert.Equal(Icons.Accent.ToArgb(), Ink((Bitmap)primary.Image!));
        int before = primary.Image!.Height;
        primary.Font = new Font("Segoe UI", 16f);
        Assert.True(primary.Image!.Height > before);

        using var only = Icons.Set(new Button { Text = "" }, Ico.Refresh);
        Assert.Equal(TextImageRelation.Overlay, only.TextImageRelation);
        Assert.Equal(ContentAlignment.MiddleCenter, only.ImageAlign);
        Assert.Equal(only.Image!.Width, only.Image.Height); // yazısız düğmede boşluk yok
    }

    /// <summary>Simge + yazı bloğu düğmenin ortasında durur (ana penceredeki düğme ölçüleriyle).</summary>
    [Theory]
    [InlineData("Yazdır", 366, 80, 12f, true)]
    [InlineData("Yazdır — 8 kağıt", 366, 80, 12f, true)]
    [InlineData("Ara", 120, 27, 9.75f, false)]
    [InlineData("PDF", 104, 44, 9.75f, false)]
    [InlineData("Tümü", 90, 26, 9.75f, false)]
    [InlineData("Önizle ve Yazdır", 200, 40, 9.75f, true)]
    [InlineData("Hizalama testi yazdır (örnek tek kağıt)…", 408, 32, 9.75f, false)]
    public void IconAndText_AreCenteredTogether(string text, int w, int h, float pt, bool bold)
    {
        Exception? err = null;
        var t = new Thread(() =>
        {
            try
            {
                using var f = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
                var green = Color.FromArgb(118, 150, 86);
                var b = new Button
                {
                    Text = text, Size = new Size(w, h), FlatStyle = FlatStyle.Flat, BackColor = green, ForeColor = Color.White,
                    Font = new Font("Segoe UI", pt, bold ? FontStyle.Bold : FontStyle.Regular)
                };
                b.FlatAppearance.BorderColor = green;
                f.Controls.Add(b);
                Icons.Set(b, Ico.Print);
                f.Show();
                using var bmp = new Bitmap(w, h);
                b.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                int left = int.MaxValue, right = -1;
                for (int x = 2; x < w - 2; x++)
                    for (int y = 2; y < h - 2; y++)
                        if (bmp.GetPixel(x, y).GetBrightness() > 0.75f) { left = Math.Min(left, x); right = Math.Max(right, x); }
                Assert.True(right > left, "çizim yok");
                double off = (left + right) / 2.0 - w / 2.0;
                Assert.True(Math.Abs(off) <= 3, $"'{text}': blok {left}..{right}, ortadan {off:0.#} px kayık");
                // simge yazının solunda: en soldaki beyaz piksel simgeye ait (simge payı kadar boşluk var)
                Assert.True(left < w / 2 - 10);
            }
            catch (Exception ex) { err = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw err;
    }

    /// <summary>Sola dayalı düğmede simge solda, dikeyde ortada ve tam görünür; yazı simgenin sağından başlar.</summary>
    [Theory]
    [InlineData("Brother DCP-T536DW Printer • Tepsi 2 • 2 kopya · değiştir", 232, 24, 8.75f)]   // sığmaz: "…" ile kısalır
    [InlineData("Varsayılana Sıfırla", 152, 30, 9.75f)]
    [InlineData("Sil", 152, 30, 9.75f)]
    public void LeftAligned_IconAtLeftEdge_TextAfterIt(string text, int w, int h, float pt)
    {
        Exception? err = null;
        var t = new Thread(() =>
        {
            try
            {
                using var f = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
                var b = new Button
                {
                    Text = text, Size = new Size(w, h), FlatStyle = FlatStyle.Flat, BackColor = Color.White, ForeColor = Color.Black,
                    Font = new Font("Segoe UI", pt), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
                };
                b.FlatAppearance.BorderColor = Color.White;
                f.Controls.Add(b);
                Icons.Set(b, Ico.Print);
                f.Show();
                using var bmp = new Bitmap(w, h);
                b.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
                bool Ink(int x, int y) => bmp.GetPixel(x, y).GetBrightness() < 0.6f;
                bool Col(int x) => Enumerable.Range(1, h - 2).Any(y => Ink(x, y));
                int iconL = Enumerable.Range(1, w - 2).First(Col);
                int iconR = iconL; while (Col(iconR + 1)) iconR++;
                int textL = iconR + 1; while (textL < w - 1 && !Col(textL)) textL++;
                int px = b.Image!.Height;
                Assert.True(iconL <= 10, $"simge solda değil ({iconL})");
                Assert.InRange(iconR - iconL + 1, px - 4, px + 1);                 // simge tam (kesilmemiş)
                Assert.InRange(textL - iconR, 3, 14);                             // yazıyla arasında boşluk var
                var rows = Enumerable.Range(0, h).Where(y => Enumerable.Range(iconL, iconR - iconL + 1).Any(x => Ink(x, y))).ToList();
                Assert.True(rows.Min() >= 1 && rows.Max() <= h - 2, "simge kesik");
                Assert.True(Math.Abs((rows.Min() + rows.Max()) / 2.0 - h / 2.0) <= 2, $"simge dikeyde ortada değil ({rows.Min()}..{rows.Max()})");
            }
            catch (Exception ex) { err = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw err;
    }

    /// <summary>Simgeli açıklamada simge yazının ilk harfinin üstüne binmez (aralarında boş sütun kalır).</summary>
    [Fact]
    public void Caption_IconDoesNotOverlapText()
    {
        Exception? err = null;
        var t = new Thread(() =>
        {
            try
            {
                using var f = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), BackColor = Color.White };
                var l = Icons.Caption(Ico.Ok, Color.Black, "chess-results", new Font("Segoe UI", 9.75f));
                l.BackColor = Color.White; l.ForeColor = Color.Black;
                f.Controls.Add(l);
                f.Show();
                using var bmp = new Bitmap(l.Width, l.Height);
                l.DrawToBitmap(bmp, new Rectangle(0, 0, l.Width, l.Height));
                bool Ink(int x) => Enumerable.Range(0, l.Height).Any(y => bmp.GetPixel(x, y).GetBrightness() < 0.6f);
                int px = Icons.SizeFor(l.Font);
                int iconEnd = Enumerable.Range(0, l.Width).Last(x => x < px + 2 && Ink(x));
                int textStart = Enumerable.Range(iconEnd + 1, l.Width - iconEnd - 1).First(Ink);
                Assert.True(Ink(px / 2), "simge çizilmemiş");
                Assert.True(textStart - iconEnd >= 3, $"simge ({iconEnd}) ile yazı ({textStart}) arasında boşluk yok");
            }
            catch (Exception ex) { err = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start(); t.Join();
        if (err is not null) throw err;
    }

    [Theory]
    [InlineData("✓", Ico.Ok)]
    [InlineData("ℹ", Ico.Info)]
    [InlineData("⚠", Ico.Warning)]
    [InlineData("✖", Ico.Error)]
    [InlineData("•", Ico.Dot)]
    [InlineData("✎", Ico.Edit)]
    public void GridMarks_MapToIcons(string mark, Ico icon) => Assert.Equal(icon, Icons.MarkIcon(mark)!.Value.Icon);

    /// <summary>Simgenin en koyu (tam örtülü) piksellerinin rengi.</summary>
    private static int Ink(Bitmap b)
    {
        for (int y = 0; y < b.Height; y++)
            for (int x = 0; x < b.Width; x++)
                if (b.GetPixel(x, y).A == 255) return b.GetPixel(x, y).ToArgb();
        return 0;
    }

    /// <summary>CSP_ICON_SHEET verilirse bütün simgeleri 16/20/24/48 px'te tek bir PNG'ye çizer (göz kontrolü için).</summary>
    [Fact]
    public void ContactSheet()
    {
        var path = Environment.GetEnvironmentVariable("CSP_ICON_SHEET");
        if (string.IsNullOrEmpty(path)) return;
        var icons = Enum.GetValues<Ico>();
        int[] sizes = { 16, 20, 24, 48 };
        const int cell = 64, label = 110;
        using var bmp = new Bitmap(label + sizes.Length * cell + cell, icons.Length * cell / 1);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        using var f = new Font("Segoe UI", 9f);
        for (int i = 0; i < icons.Length; i++)
        {
            int y = i * cell;
            g.DrawString(icons[i].ToString(), f, Brushes.Black, 4, y + cell / 2 - 8);
            for (int s = 0; s < sizes.Length; s++)
                g.DrawImage(Icons.Render(icons[i], sizes[s], Icons.Accent), label + s * cell + (cell - sizes[s]) / 2, y + (cell - sizes[s]) / 2, sizes[s], sizes[s]);
            // yeşil düğme üstünde beyaz
            int gx = label + sizes.Length * cell;
            g.FillRectangle(new SolidBrush(Color.FromArgb(118, 150, 86)), gx + 8, y + 12, cell - 16, cell - 24);
            g.DrawImage(Icons.Render(icons[i], 20, Color.White), gx + 22, y + 22, 20, 20);
        }
        bmp.Save(path);
    }
}
