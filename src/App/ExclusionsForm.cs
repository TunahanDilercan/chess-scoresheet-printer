using NotasyonOtomasyonu.Core;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// "Hariç Tut" ekranı: hangi kağıtların BASILMAYACAĞINI seçtiren küçük pencere.
/// Eşlenmeyen (BAY) masalar ve elle girilen masa numaraları hariç tutulabilir.
/// Seçimler config'e yazılır ve kalıcı olur.
/// </summary>
public sealed class ExclusionsForm : Form
{
    private readonly AppConfig _cfg;
    private readonly CheckBox _excludeByes = new() { Text = "Eşlenmeyen (BAY) masalarını basma" };
    private readonly TextBox _boards = new();
    private readonly Label _preview = new();
    private readonly IReadOnlyList<int> _available;

    /// <param name="availableBoards">Şu an çekili masalar (önizleme için, boş olabilir).</param>
    public ExclusionsForm(AppConfig cfg, IReadOnlyList<int>? availableBoards = null)
    {
        _cfg = cfg;
        _available = availableBoards ?? Array.Empty<int>();

        Text = "🚫 Hariç Tut (basılmayacaklar)";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = MinimizeBox = false;
        BackColor = System.Drawing.Color.FromArgb(245, 245, 240);
        Font = new System.Drawing.Font("Segoe UI", 9.75F);
        ClientSize = new System.Drawing.Size(460, 290);

        int y = 16;
        Controls.Add(new Label
        {
            Text = "Bu ekranda BASILMAYACAK kağıtları seçin. Seçimler kayıtlı kalır.",
            AutoSize = false, Size = new System.Drawing.Size(428, 20),
            Location = new System.Drawing.Point(16, y), ForeColor = System.Drawing.Color.Gray
        });
        y += 30;

        _excludeByes.SetBounds(16, y, 420, 24);
        Controls.Add(_excludeByes);
        y += 34;

        Controls.Add(new Label
        {
            Text = "Basılmayacak masa no'ları (örn: 3, 7, 12-15):",
            AutoSize = true, Location = new System.Drawing.Point(16, y + 4)
        });
        y += 26;
        _boards.SetBounds(16, y, 428, 25);
        _boards.TextChanged += (_, _) => UpdatePreview();
        Controls.Add(_boards);
        y += 34;

        _preview.SetBounds(16, y, 428, 56);
        _preview.ForeColor = System.Drawing.Color.FromArgb(95, 122, 70);
        Controls.Add(_preview);
        y += 64;

        var ok = new Button { Text = "Kaydet", DialogResult = DialogResult.OK, Size = new System.Drawing.Size(110, 32), Location = new System.Drawing.Point(228, y), BackColor = System.Drawing.Color.FromArgb(118, 150, 86), ForeColor = System.Drawing.Color.White, FlatStyle = FlatStyle.Flat };
        var cancel = new Button { Text = "Vazgeç", DialogResult = DialogResult.Cancel, Size = new System.Drawing.Size(110, 32), Location = new System.Drawing.Point(344, y) };
        ok.Click += (_, _) => WriteBack();
        Controls.Add(ok); Controls.Add(cancel);
        AcceptButton = ok; CancelButton = cancel;

        // mevcut değerleri yükle
        _excludeByes.Checked = !_cfg.Layout.PrintByeSheets;
        _boards.Text = _cfg.Layout.ExcludedBoards;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var set = BoardRange.Parse(_boards.Text);
        if (set.Count == 0)
        {
            _preview.Text = _available.Count > 0
                ? $"Çekili {_available.Count} masanın hepsi basılacak."
                : "Tüm masalar basılacak.";
            return;
        }
        var sorted = string.Join(", ", set.OrderBy(x => x));
        if (_available.Count > 0)
        {
            int kalan = _available.Count(b => !set.Contains(b));
            _preview.Text = $"Hariç: {sorted}\nÇekili {_available.Count} masadan {kalan} tanesi basılacak.";
        }
        else _preview.Text = $"Hariç tutulacak masalar: {sorted}";
    }

    private void WriteBack()
    {
        _cfg.Layout.PrintByeSheets = !_excludeByes.Checked;
        _cfg.Layout.ExcludedBoards = (_boards.Text ?? "").Trim();
    }
}
