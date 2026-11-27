using NotasyonOtomasyonu.App;
using Xunit;

namespace NotasyonOtomasyonu.AppTests;

/// <summary>Tablodaki değer hatası teknik "DataGridView Varsayılan Hata İletişim Kutusu" açmamalı.</summary>
public class GridSafetyTests
{
    private sealed class BrokenForm : AppForm
    {
        public readonly DataGridView Grid = new() { Dock = DockStyle.Fill, AccessibleName = "deneme" };
        public BrokenForm()
        {
            Grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "Bas" });
            Grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Ad" });
            Controls.Add(Grid);
        }
    }

    [Fact]
    public void BrokenCell_DoesNotShowDialog_AndIsRecorded()
    {
        Exception? error = null;
        var t = new Thread(() =>
        {
            try
            {
                Application.EnableVisualStyles();   // gerçek programdaki gibi
                using var f = new BrokenForm { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false };
                f.Show();
                int i = f.Grid.Rows.Add(true, "satır");
                f.Grid.Rows[i].Cells["Bas"] = new DataGridViewTextBoxCell();   // onay kutusu sütununda tür uyuşmazlığı
                for (int k = 0; k < 10; k++) { Application.DoEvents(); Thread.Sleep(20); }
                using var bmp = new Bitmap(f.Width, f.Height);
                f.DrawToBitmap(bmp, new Rectangle(0, 0, f.Width, f.Height));
            }
            catch (Exception ex) { error = ex; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(30)), "hata kutusu açılıp testi kilitledi");
        Assert.Null(error);
        Assert.NotNull(GridSafety.LastError);
        Assert.Contains("Bas", GridSafety.LastError);
    }
}
