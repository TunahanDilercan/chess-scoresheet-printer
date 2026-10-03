using System.Globalization;
using NotasyonOtomasyonu.Online;

namespace NotasyonOtomasyonu.App;

/// <summary>
/// Açılır listelerde klavyeyle seçim: bir harfe basınca o harfle başlayan ilk öğe, aynı harfe tekrar
/// basınca sıradaki gelir (Isparta → Iğdır …). Hızlı yazınca ("isp") başı eşleşen öğe seçilir.
/// Türkçe harfler önce kendisiyle, sonra eşdeğeriyle eşleşir: "I" → Iğdır, Isparta, sonra İstanbul,
/// İzmir; "S" → S ile başlayanlar, sonra Ş ile başlayanlar.
/// </summary>
internal static class ComboKeySearch
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private const int TypingPauseMs = 900;

    private sealed class State { public string Buffer = ""; public DateTime Last; }

    /// <summary>Kontrol ağacındaki tüm açılır listelere (yalnız seçmeli) ekler.</summary>
    public static void AttachAll(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is ComboBox { DropDownStyle: ComboBoxStyle.DropDownList } cbo) Attach(cbo);
            if (c.HasChildren) AttachAll(c);
        }
    }

    public static void Attach(ComboBox cbo)
    {
        if (cbo.Tag as string == nameof(ComboKeySearch)) return;
        var st = new State();
        cbo.KeyPress += (_, e) => OnKeyPress(cbo, st, e);
    }

    private static void OnKeyPress(ComboBox cbo, State st, KeyPressEventArgs e)
    {
        if (char.IsControl(e.KeyChar) || cbo.Items.Count == 0) return;
        var now = DateTime.Now;
        if ((now - st.Last).TotalMilliseconds > TypingPauseMs) st.Buffer = "";
        st.Last = now;

        var texts = cbo.Items.Cast<object>().Select(o => cbo.GetItemText(o) ?? "").ToList();
        bool repeat = st.Buffer.Length > 0 && st.Buffer.All(ch => Fold(ch) == Fold(e.KeyChar));
        st.Buffer += e.KeyChar;

        int target = -1;
        if (st.Buffer.Length > 1 && !repeat)
        {
            // Hızlı yazım: baştan eşleşen öğe ("kü" → Kütahya)
            var prefix = Fold(st.Buffer);
            target = texts.FindIndex(t => Fold(t).StartsWith(prefix, StringComparison.Ordinal));
            if (target < 0) st.Buffer = e.KeyChar.ToString(); // eşleşme yok: yeni harfle baştan başla
        }
        if (target < 0)
        {
            if (repeat) st.Buffer = e.KeyChar.ToString();   // aynı harf: sıradakine geç
            var group = LetterGroup(texts, e.KeyChar);
            if (group.Count > 0)
            {
                int pos = group.IndexOf(cbo.SelectedIndex);
                target = group[(pos + 1) % group.Count];
            }
        }
        if (target >= 0 && target != cbo.SelectedIndex) cbo.SelectedIndex = target;
        e.Handled = true; // Windows'un varsayılan (Türkçe harfleri ayırmayan) aramasını engelle
    }

    /// <summary>Harfle başlayan öğeler: önce birebir aynı harf, sonra eşdeğeri (I/İ, S/Ş, C/Ç …).</summary>
    public static List<int> LetterGroup(IReadOnlyList<string> texts, char letter)
    {
        char up = char.ToUpper(letter, Tr);
        var exact = new List<int>();
        var folded = new List<int>();
        for (int i = 0; i < texts.Count; i++)
        {
            var t = texts[i];
            if (t.Length == 0) continue;
            if (char.ToUpper(t[0], Tr) == up) exact.Add(i);
            else if (Fold(t[0]) == Fold(letter)) folded.Add(i);
        }
        return exact.Concat(folded).ToList();
    }

    private static string Fold(string s) => EventGrouping.Fold(s);
    private static char Fold(char c) => EventGrouping.Fold(c.ToString()) is { Length: > 0 } f ? f[0] : c;
}
