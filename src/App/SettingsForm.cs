using NotasyonOtomasyonu.App.Overlay;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Sık değişmeyen ama kalıcı olması gereken ayarlar: varsayılan turnuva adı, sayfa boyutu,
/// yazıcı hizalaması ve şablonlar. OK'e basınca verilen <see cref="AppConfig"/> nesnesine yazar (çağıran kaydeder).
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;

    private readonly TextBox _name = new();
    private readonly ComboBox _pageSize = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _offX = MmBox();
    private readonly NumericUpDown _offY = MmBox();

    public SettingsForm(AppConfig cfg)
    {
        _cfg = cfg;

        Text = "⚙ Ayarlar";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        BackColor = System.Drawing.Color.FromArgb(245, 245, 240);

        int y = 16;
        AddRow("Varsayılan turnuva adı:", _name, ref y);

        _pageSize.Items.AddRange(new object[] { "A5 (önerilen)", "A4" });
        AddRow("Sayfa boyutu:", _pageSize, ref y);

        // ---- Yazıcı hizalama ----
        y += 4;
        Controls.Add(new Label
        {
            Text = "Yazıcı hizalama (mm)", AutoSize = true, Location = new System.Drawing.Point(16, y),
            Font = new System.Drawing.Font("Segoe UI", 9.75F, System.Drawing.FontStyle.Bold),
            ForeColor = System.Drawing.Color.FromArgb(95, 122, 70)
        });
        y += 24;
        Controls.Add(new Label { Text = "Sağa (+) / sola (−):", AutoSize = true, Location = new System.Drawing.Point(16, y + 4) });
        _offX.SetBounds(150, y, 70, 25); Controls.Add(_offX);
        Controls.Add(new Label { Text = "Aşağı (+) / yukarı (−):", AutoSize = true, Location = new System.Drawing.Point(230, y + 4) });
        _offY.SetBounds(374, y, 50, 25); Controls.Add(_offY);
        y += 32;
        Controls.Add(new Label
        {
            Text = "Yazılar hazır kağıttaki kutulara göre kayıksa buradan düzeltin; şablonu değiştirmeniz gerekmez. " +
                   "Önce hizalama testini boş bir hazır kağıda basıp ölçün.",
            AutoSize = false, Size = new System.Drawing.Size(408, 54), Location = new System.Drawing.Point(16, y),
            ForeColor = System.Drawing.Color.Gray
        });
        y += 58;
        var btnTest = new Button
        {
            Text = "🖨 Hizalama testi yazdır (örnek tek kağıt)…",
            Size = new System.Drawing.Size(408, 32), Location = new System.Drawing.Point(16, y)
        };
        btnTest.Click += PrintAlignmentTest;
        Controls.Add(btnTest);
        y += 44;

        // Hazır kağıt (overlay) şablonu tasarımcısı
        var btnTemplate = new Button
        {
            Text = "🎨 Hazır kağıt şablonları (ekle / düzenle)…",
            Size = new System.Drawing.Size(408, 32),
            Location = new System.Drawing.Point(16, y)
        };
        btnTemplate.Click += OpenTemplates;
        Controls.Add(btnTemplate);
        y += 44;

        // Credit
        var sep = new Label { BorderStyle = BorderStyle.Fixed3D, Location = new System.Drawing.Point(16, y), Size = new System.Drawing.Size(408, 2) };
        Controls.Add(sep); y += 8;
        var link = new LinkLabel
        {
            Text = "github.com/TunahanDilercan/chess-scoresheet-printer",
            AutoSize = true,
            Location = new System.Drawing.Point(16, y + 18)
        };
        link.LinkClicked += (_, _) =>
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/TunahanDilercan/chess-scoresheet-printer") { UseShellExecute = true }); }
            catch { /* yok say */ }
        };
        var version = typeof(SettingsForm).Assembly.GetName().Version;
        Controls.Add(new Label
        {
            AutoSize = true, Location = new System.Drawing.Point(16, y),
            ForeColor = System.Drawing.Color.FromArgb(95, 122, 70),
            Text = $"♞ Geliştiren: Tunahan Dilercan — satranç oyuncusu ve hakemi.  (v{version?.ToString(3)})"
        });
        Controls.Add(link);
        y += 48;

        var ok = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, Size = new System.Drawing.Size(100, 32) };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Size = new System.Drawing.Size(100, 32) };
        ok.Location = new System.Drawing.Point(218, y);
        cancel.Location = new System.Drawing.Point(326, y);
        ok.Click += (_, _) => WriteBack();
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new System.Drawing.Size(440, y + 46);

        LoadFrom();
    }

    private static NumericUpDown MmBox() => new()
    {
        Minimum = -15, Maximum = 15, DecimalPlaces = 1, Increment = 0.5m, TextAlign = HorizontalAlignment.Right
    };

    private void OpenTemplates(object? sender, EventArgs e)
    {
        using var dlg = new TemplatesForm(_cfg);
        dlg.ShowDialog(this);
        // Etkin şablon değişmiş olabilir; çağıran (MainForm) Kaydet'te config'i yazar.
    }

    /// <summary>Örnek verili tek kağıt basar: kutu hizasını ve kaydırma ayarını gerçek kağıtta görmek için.</summary>
    private void PrintAlignmentTest(object? sender, EventArgs e)
    {
        if (!_cfg.Overlay.IsConfigured)
        {
            MessageBox.Show(this, "Önce bir hazır kağıt şablonu tanımlayın.", "Şablon yok", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var (t, p) = OverlayLayout.Sample();
        t = t with { Pairings = new[] { p } };
        var size = _pageSize.SelectedIndex == 0 ? "A5" : "A4";
        new SheetPrinter(_cfg.Overlay, t, size, (double)_offX.Value, (double)_offY.Value).PrintWithPreview(this);
    }

    private void AddRow(string label, Control input, ref int y)
    {
        Controls.Add(new Label { Text = label, AutoSize = true, Location = new System.Drawing.Point(16, y + 4) });
        input.SetBounds(150, y, 274, 25);
        Controls.Add(input);
        y += 34;
    }

    private void LoadFrom()
    {
        _name.Text = _cfg.Tournament.Name;
        _pageSize.SelectedIndex = NotasyonOtomasyonu.Core.PageGeometry.IsA5(_cfg.Layout.PageSize) ? 0 : 1;
        _offX.Value = Clamp(_cfg.Layout.PrintOffsetXmm, _offX);
        _offY.Value = Clamp(_cfg.Layout.PrintOffsetYmm, _offY);
    }

    private static decimal Clamp(double v, NumericUpDown n)
        => Math.Min(n.Maximum, Math.Max(n.Minimum, (decimal)v));

    private void WriteBack()
    {
        _cfg.Tournament.Name = _name.Text.Trim();
        _cfg.Layout.PageSize = _pageSize.SelectedIndex == 0 ? "A5" : "A4";
        _cfg.Layout.PrintOffsetXmm = (double)_offX.Value;
        _cfg.Layout.PrintOffsetYmm = (double)_offY.Value;
    }
}
