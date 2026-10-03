using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App.Reports;

/// <summary>
/// Rapordaki eksik bilgileri tek tek, sohbet eder gibi soran adım adım pencere.
/// Her cevap doğrudan alana yazılır; "Boş bırak" ile geçilebilir, Esc ile kalanlar sonraya bırakılır.
/// </summary>
public sealed class MissingFieldsDialog : Form
{
    private static readonly Color Green = Color.FromArgb(118, 150, 86);
    private static readonly Color GreenDark = Color.FromArgb(95, 122, 70);

    private readonly IReadOnlyList<ReportField> _fields;
    private int _index;

    private readonly Label _lblStep = new() { AutoSize = true, ForeColor = Color.Gray, Left = 24, Top = 18 };
    private readonly ProgressBar _bar = new() { Left = 24, Top = 42, Height = 6, Style = ProgressBarStyle.Continuous };
    private readonly Label _lblQuestion = new() { Left = 24, Top = 64, Height = 30, Font = new Font("Segoe UI", 13f, FontStyle.Bold), ForeColor = GreenDark };
    private readonly Label _lblHint = new() { Left = 24, Top = 96, Height = 22, ForeColor = Color.DimGray };
    private readonly TextBox _txt = new() { Left = 24, Top = 124, Font = new Font("Segoe UI", 11f) };
    private readonly Button _btnBack = new() { Text = "◀ Geri", Width = 96, Height = 36 };
    private readonly Button _btnSkip = new() { Text = "Boş bırak", Width = 110, Height = 36 };
    private readonly Button _btnNext = new() { Width = 130, Height = 36, BackColor = Green, ForeColor = Color.White, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };

    public MissingFieldsDialog(IReadOnlyList<ReportField> fields)
    {
        _fields = fields;
        Text = "Birkaç bilgi eksik";
        Font = new Font("Segoe UI", 9.75f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(620, 300);
        KeyPreview = true;
        BackColor = Color.White;

        int w = ClientSize.Width - 48;
        _bar.Width = w; _lblQuestion.Width = w; _lblHint.Width = w; _txt.Width = w;
        _bar.Maximum = Math.Max(1, fields.Count);

        foreach (var b in new[] { _btnBack, _btnSkip, _btnNext })
        {
            b.FlatStyle = FlatStyle.Flat;
            b.Cursor = Cursors.Hand;
            b.Top = ClientSize.Height - b.Height - 18;
            b.FlatAppearance.BorderColor = b == _btnNext ? GreenDark : Color.FromArgb(208, 208, 198);
        }
        _btnNext.Left = ClientSize.Width - _btnNext.Width - 24;
        _btnSkip.Left = _btnNext.Left - _btnSkip.Width - 8;
        _btnBack.Left = 24;

        var intro = new Label
        {
            Left = 24, Top = 186, Width = w, Height = 40, ForeColor = Color.Gray,
            Text = "Chess-results'ta bulunamayan bilgileri soruyorum. Verdiğiniz iletişim/organizasyon bilgileri bir sonraki raporda hazır gelir."
        };

        Controls.AddRange(new Control[] { _lblStep, _bar, _lblQuestion, _lblHint, _txt, intro, _btnBack, _btnSkip, _btnNext });

        _btnBack.Click += (_, _) => Go(-1);
        _btnSkip.Click += (_, _) => { _txt.Text = ""; Go(+1); };
        _btnNext.Click += (_, _) => Go(+1);
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && !(_txt.Multiline && e.Shift)) { e.SuppressKeyPress = true; Go(+1); }
            else if (e.KeyCode == Keys.Escape) { Save(); DialogResult = DialogResult.Cancel; }
        };

        ShowStep(0);
    }

    private void ShowStep(int i)
    {
        _index = Math.Clamp(i, 0, _fields.Count - 1);
        var f = _fields[_index];
        _lblStep.Text = $"Adım {_index + 1} / {_fields.Count}" + (f.Optional ? "  •  isteğe bağlı" : "");
        _bar.Value = _index + 1;
        _lblQuestion.Text = f.Label + "?";
        _lblHint.Text = f.Hint;
        _txt.Multiline = f.Multiline;
        _txt.Height = f.Multiline ? 52 : 28;
        _txt.ScrollBars = f.Multiline ? ScrollBars.Vertical : ScrollBars.None;
        _txt.Text = f.Value;
        _txt.SelectAll();
        _btnBack.Enabled = _index > 0;
        _btnNext.Text = _index == _fields.Count - 1 ? "Bitir ✓" : "İleri ▶";
        _txt.Focus();
    }

    private void Save()
    {
        var f = _fields[_index];
        f.Value = _txt.Text.Trim();
        if (f.Value.Length > 0 && f.Source == ReportFieldSource.Missing) f.Source = ReportFieldSource.Suggested;
    }

    private void Go(int delta)
    {
        Save();
        if (delta > 0 && _index == _fields.Count - 1) { DialogResult = DialogResult.OK; return; }
        ShowStep(_index + delta);
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); _txt.Focus(); }
}
