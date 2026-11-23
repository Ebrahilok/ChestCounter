using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ChestCounter;

/// <summary>
/// «Ya tienen el token»: los compañeros con los que juegas, cada uno con un interruptor. Los marcados ya tienen el token
/// de la Tienda pirata y no pueden volver a sacarlo, así que sus cofres no suman a tu probabilidad.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TokenOwnersDialog : Form
{
    private const int W = 400, Pad = 18, RowH = 42, MaxListH = 360;
    private readonly List<(string Key, ToggleSwitch Toggle)> _rows = new();

    private TokenOwnersDialog(IReadOnlyList<(string Key, string Name, int Matches)> mates, IReadOnlyCollection<string> owners)
    {
        Text = L.T("Ya tienen el token", "Already have the token");
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = Theme.Text();
        BackColor = Theme.Bg;
        ForeColor = Theme.Ink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Icon = AppIcon.Load();

        var y = Pad;
        var title = new Label
        {
            Text = L.T("¿Quién ya tiene el token?", "Who already has the token?"), Font = Theme.Text(12f, FontStyle.Bold),
            ForeColor = Theme.Ink, BackColor = Theme.Bg, Location = new Point(Pad, y), AutoSize = true, UseMnemonic = false,
        };
        Controls.Add(title);
        y += title.PreferredHeight + 4;
        var intro = new Label
        {
            Text = L.T("Quien ya tiene el token no puede volver a sacarlo: sus cofres dejan de sumar a tu probabilidad.",
                "Someone who already has the token can't get it again: their chests stop adding to your chance."),
            Font = Theme.Text(9f), ForeColor = Theme.Muted, BackColor = Theme.Bg, Location = new Point(Pad, y),
            MaximumSize = new Size(W - 2 * Pad, 0), AutoSize = true, UseMnemonic = false,
        };
        Controls.Add(intro);
        y += intro.PreferredHeight + 12;

        var card = new Card { Location = new Point(Pad, y), Width = W - 2 * Pad, Radius = 14, BackColor = Theme.Card };
        Controls.Add(card);
        if (mates.Count == 0)
        {
            card.Height = 60;
            card.Controls.Add(new Label
            {
                Text = L.T("Aún no hay compañeros en tus partidas.", "No teammates in your matches yet."),
                Font = Theme.Text(9f), ForeColor = Theme.Muted, BackColor = card.BackColor,
                Location = new Point(14, 0), Size = new Size(card.Width - 28, 60), TextAlign = ContentAlignment.MiddleLeft,
            });
        }
        else
        {
            var listH = Math.Min(mates.Count * RowH + 8, MaxListH);
            card.Height = listH + 8;
            var list = new Panel { Location = new Point(4, 4), Size = new Size(card.Width - 8, listH), BackColor = card.BackColor, AutoScroll = mates.Count * RowH + 8 > MaxListH };
            card.Controls.Add(list);
            var rowW = list.Width - (list.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0);
            var ry = 4;
            foreach (var (key, name, matches) in mates)
            {
                var toggle = new ToggleSwitch
                {
                    Location = new Point(rowW - 10 - 40, ry + 10),
                    Checked = owners.Contains(key, StringComparer.OrdinalIgnoreCase) || owners.Contains(name, StringComparer.OrdinalIgnoreCase),
                };
                var label = new Label
                {
                    Text = name, Font = Theme.Text(9.5f, FontStyle.Bold), ForeColor = Theme.Ink, BackColor = list.BackColor,
                    Location = new Point(10, ry + 2), Size = new Size(rowW - 70, 20), AutoEllipsis = true, UseMnemonic = false, Cursor = Cursors.Hand,
                };
                var sub = new Label
                {
                    Text = L.Count(matches, "partida juntos", "partidas juntos", "match together", "matches together"),
                    Font = Theme.Text(7.5f), ForeColor = Theme.Muted, BackColor = list.BackColor,
                    Location = new Point(10, ry + 21), Size = new Size(rowW - 70, 17), UseMnemonic = false, Cursor = Cursors.Hand,
                };
                label.Click += (_, _) => toggle.Checked = !toggle.Checked;
                sub.Click += (_, _) => toggle.Checked = !toggle.Checked;
                list.Controls.Add(label);
                list.Controls.Add(sub);
                list.Controls.Add(toggle);
                _rows.Add((key, toggle));
                ry += RowH;
            }
        }
        y += card.Height + 16;

        var cancel = new PillButton { Text = L.T("Cancelar", "Cancel"), Location = new Point(W - Pad - 110 - 8 - 110, y), Size = new Size(110, 36), Radius = 11, DialogResult = DialogResult.Cancel };
        var save = new PillButton
        {
            Text = L.T("Guardar", "Save"), Location = new Point(W - Pad - 110, y), Size = new Size(110, 36), Radius = 11,
            BackColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.AccentInk, DialogResult = DialogResult.OK,
        };
        Controls.Add(cancel);
        Controls.Add(save);
        AcceptButton = save;
        CancelButton = cancel;
        ClientSize = new Size(W, y + 36 + Pad);
    }

    /// <summary>
    /// Muestra la lista y devuelve las claves marcadas, o null si se cancela. Las claves guardadas de compañeros que ya
    /// no salen en la lista se conservan.
    /// </summary>
    public static List<string>? Choose(IWin32Window owner, IReadOnlyList<(string Key, string Name, int Matches)> mates, IReadOnlyCollection<string> owners)
    {
        using var dialog = new TokenOwnersDialog(mates, owners);
        if (dialog.ShowDialog(owner) != DialogResult.OK)
        {
            return null;
        }
        var listed = new HashSet<string>(mates.SelectMany(m => new[] { m.Key, m.Name }), StringComparer.OrdinalIgnoreCase);
        return owners.Where(o => !listed.Contains(o))
            .Concat(dialog._rows.Where(r => r.Toggle.Checked).Select(r => r.Key))
            .ToList();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Chrome(Handle);
    }
}
