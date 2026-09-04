using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Turnuva üst bilgisini (ad, zaman kontrolü, hakem, yer, tarih) elle düzenleme ekranı.
/// Kaydedince değerler config'e yazılır ve kalıcı olur; çağıran ManualOverride'ı işaretler.
/// </summary>
public sealed class MetaEditForm : Form
{
    private readonly AppConfig _cfg;
    private readonly TextBox _name = new();
    private readonly ComboBox _date = new() { DropDownStyle = ComboBoxStyle.DropDown }; // turnuva günleri + serbest

    public MetaEditForm(AppConfig cfg)
    {
        _cfg = cfg;
        Text = "✏ Turnuva Bilgisini Düzenle";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        BackColor = System.Drawing.Color.FromArgb(245, 245, 240);
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        ClientSize = new System.Drawing.Size(440, 180);

        int y = 16;
        Row("Turnuva adı:", _name, ref y);
        Row("Tarih:", _date, ref y);

        Controls.Add(new Label
        {
            Text = "Tarih: turnuva günlerinden seçin (varsayılan bugün). Listede yoksa elle yazabilirsiniz.\nZaman kontrolü/hakem ⚙ Ayarlar'da.",
            AutoSize = false, Size = new System.Drawing.Size(412, 40), Location = new System.Drawing.Point(16, y),
            ForeColor = System.Drawing.Color.Gray
        });
        y += 46;

        var ok = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, Size = new System.Drawing.Size(110, 32), Location = new System.Drawing.Point(208, y), BackColor = System.Drawing.Color.FromArgb(118, 150, 86), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Size = new System.Drawing.Size(110, 32), Location = new System.Drawing.Point(324, y) };
        ok.Click += (_, _) => WriteBack();
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;

        LoadValues();
    }

    private void Row(string label, Control input, ref int y)
    {
        Controls.Add(new Label { Text = label, AutoSize = true, Location = new System.Drawing.Point(16, y + 4) });
        input.SetBounds(140, y, 284, 25);
        Controls.Add(input);
        y += 32;
    }

    private void LoadValues()
    {
        _name.Text = _cfg.Tournament.Name;

        // Tarih seçenekleri: turnuva günleri + bugün
        _date.Items.Clear();
        foreach (var d in TournamentDates.Parse(_cfg.Tournament.DateRange))
            _date.Items.Add(TournamentDates.Format(d));
        var today = TournamentDates.Format(DateTime.Today);
        if (!_date.Items.Contains(today)) _date.Items.Insert(0, today);
        _date.Text = string.IsNullOrWhiteSpace(_cfg.Tournament.Date) ? today : _cfg.Tournament.Date!;
    }

    private void WriteBack()
    {
        _cfg.Tournament.Name = _name.Text.Trim();
        _cfg.Tournament.Date = Nz(_date.Text);
    }

    private static string? Nz(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}
