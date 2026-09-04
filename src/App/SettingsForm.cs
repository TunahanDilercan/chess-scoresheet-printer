using NotasyonOtomasyonu.App.TemplateDesigner;
using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Sık değişmeyen ama kalıcı olması gereken ayarlar: hakem, ülke, düzen seçenekleri.
/// OK'e basınca verilen <see cref="AppConfig"/> nesnesine yazar (çağıran kaydeder).
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _cfg;

    private readonly TextBox _name = new();
    private readonly TextBox _time = new();
    private readonly TextBox _arbiter = new();
    private readonly ComboBox _pageSize = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    public SettingsForm(AppConfig cfg)
    {
        _cfg = cfg;

        Text = "⚙ Ayarlar";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new System.Drawing.Size(440, 360);
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        BackColor = System.Drawing.Color.FromArgb(245, 245, 240);

        int y = 16;
        AddRow("Varsayılan turnuva adı:", _name, ref y);
        AddRow("Zaman kontrolü (ops.):", _time, ref y);
        AddRow("Hakem adı (ops.):", _arbiter, ref y);

        _pageSize.Items.AddRange(new object[] { "A5 (önerilen)", "A4" });
        AddRow("Sayfa boyutu:", _pageSize, ref y);

        Controls.Add(new Label
        {
            Text = "Zaman kontrolü/hakem yalnızca özel şablonda o alanlar varsa basılır.",
            AutoSize = false, Size = new System.Drawing.Size(408, 30), Location = new System.Drawing.Point(16, y),
            ForeColor = System.Drawing.Color.Gray
        });
        y += 34;

        // Hazır kağıt (overlay) şablonu tasarımcısı
        var btnTemplate = new Button
        {
            Text = "🎨 Hazır kağıt şablonları (ekle / düzenle)…",
            Size = new System.Drawing.Size(408, 32),
            Location = new System.Drawing.Point(16, y)
        };
        btnTemplate.Click += OpenTemplates;
        Controls.Add(btnTemplate);
        y += 40;

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
        Controls.Add(new Label
        {
            AutoSize = true, Location = new System.Drawing.Point(16, y),
            ForeColor = System.Drawing.Color.FromArgb(95, 122, 70),
            Text = "♞ Geliştiren: Tunahan Dilercan — satranç oyuncusu ve hakemi."
        });
        Controls.Add(link);
        y += 44;

        var ok = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, Size = new System.Drawing.Size(100, 32) };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Size = new System.Drawing.Size(100, 32) };
        ok.Location = new System.Drawing.Point(218, y);
        cancel.Location = new System.Drawing.Point(326, y);
        ok.Click += (_, _) => WriteBack();
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        LoadFrom();
    }

    private void OpenTemplates(object? sender, EventArgs e)
    {
        using var dlg = new TemplatesForm(_cfg);
        dlg.ShowDialog(this);
        // Etkin şablon değişmiş olabilir; çağıran (MainForm) Kaydet'te config'i yazar.
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
        _time.Text = _cfg.Tournament.TimeControl;
        _arbiter.Text = _cfg.Tournament.Arbiter ?? "";
        _pageSize.SelectedIndex = NotasyonOtomasyonu.Core.PageGeometry.IsA5(_cfg.Layout.PageSize) ? 0 : 1;
    }

    private void WriteBack()
    {
        _cfg.Tournament.Name = _name.Text.Trim();
        _cfg.Tournament.TimeControl = _time.Text.Trim();
        _cfg.Tournament.Arbiter = Empty2Null(_arbiter.Text);
        _cfg.Layout.PageSize = _pageSize.SelectedIndex == 0 ? "A5" : "A4";
    }

    private static string? Empty2Null(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
