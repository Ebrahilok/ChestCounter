using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ChestCounter;

/// <summary>
/// Ventana «Party»: crear una party o unirse con un código y ver, de cada miembro, sus cofres exactos (hoy y en total)
/// y en cuántas partidas jugasteis juntos. Se repinta sola cuando termina una sincronización.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class PartyDialog : Form
{
    private const int W = 440, Pad = 18, RowH = 50, MaxListH = 300;
    private readonly Monitor _monitor;
    private readonly Panel _body;
    private string? _error;
    private bool _busy;
    private PartyView? _view;
    private string _code = "";

    private PartyDialog(Monitor monitor, PartyView? view)
    {
        _monitor = monitor;
        _view = view;
        Text = "Party";
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
        _body = new Panel { Location = Point.Empty, Width = W, BackColor = Theme.Bg };
        Controls.Add(_body);
        Rebuild();
    }

    /// <summary>Abre la ventana. <paramref name="subscribe"/> recibe una función para pasarle cada estado nuevo mientras está abierta.</summary>
    public static void Open(IWin32Window owner, Monitor monitor, PartyView? view, Action<Action<PartyView?>?> subscribe)
    {
        using var dialog = new PartyDialog(monitor, view);
        subscribe(v =>
        {
            if (dialog.IsDisposed || !dialog.IsHandleCreated) return;
            dialog.BeginInvoke(() =>
            {
                if (dialog.IsDisposed || dialog._busy) return;
                if (Signature(v) == Signature(dialog._view)) return; // nada nuevo: sin parpadeos
                dialog._view = v;
                dialog.Rebuild();
            });
        });
        try
        {
            dialog.ShowDialog(owner);
        }
        finally
        {
            subscribe(null);
        }
    }

    private static string Signature(PartyView? v) => v == null ? ""
        : string.Join("|", v.Members.Select(m => $"{m.Name},{m.Today},{m.Total},{m.Matches},{m.Together},{m.LastSync:O}"))
            + $"|{v.Code}|{v.LastSyncUtc:O}|{v.Error}|{v.Syncing}";

    private void Rebuild()
    {
        SuspendLayout();
        var focused = _body.Controls.OfType<TextBox>().FirstOrDefault()?.Focused == true;
        foreach (Control c in _body.Controls.Cast<Control>().ToList())
        {
            c.Dispose();
        }
        _body.Controls.Clear();
        var y = Pad;
        if (_view == null) y = BuildJoin(y, focused);
        else y = BuildParty(y, _view);
        _body.Height = y;
        ClientSize = new Size(W, y);
        ResumeLayout();
    }

    // ---- Sin party: crear o unirse ---------------------------------------------------------------------------------

    private int BuildJoin(int y, bool focusCode)
    {
        y = Title(y, L.T("Jugar en party", "Play as a party"));
        y = Paragraph(y, L.T("Juega con tus amigos y ved exactamente cuántos cofres abrió cada uno. Cada miembro sube los cofres de su propio replay, así que el número es exacto en vez del «N+» que se ve desde el tuyo. Los compañeros que no están en la party siguen con su mínimo observado.",
            "Play with your friends and see exactly how many chests each of you opened. Every member uploads the chests from their own replay, so the number is exact instead of the “N+” seen from yours. Teammates outside the party keep their observed minimum."), Theme.Muted);

        var key = _monitor.ShownProfileKey;
        var name = _monitor.ShownProfileName;
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(name))
        {
            y = Paragraph(y + 4, L.T("Primero juega una partida con el contador abierto: hace falta saber cuál es tu cuenta de Epic.",
                "Play a match with the counter open first: it needs to know your Epic account."), Theme.Amber);
            return Buttons(y + 8, null);
        }
        y = Paragraph(y, L.F("Entras como {0}.", "You join as {0}.", name), Theme.Ink);
        y += 6;

        // Crear
        var create = new Card { Location = new Point(Pad, y), Size = new Size(W - 2 * Pad, 64), Radius = 14, BackColor = Theme.Card };
        create.Controls.Add(CardLabel(L.T("Crear una party nueva", "Create a new party"), 9.5f, FontStyle.Bold, Theme.Ink, new Point(14, 12), create));
        create.Controls.Add(CardLabel(L.T("Te da un código para tus amigos", "Gives you a code for your friends"), 8f, FontStyle.Regular, Theme.Muted, new Point(14, 34), create));
        var createButton = new PillButton
        {
            Text = L.T("Crear", "Create"), Location = new Point(create.Width - 14 - 96, 14), Size = new Size(96, 36), Radius = 11,
            BackColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.AccentInk, Enabled = !_busy,
        };
        createButton.Click += async (_, _) => await Run(() => _monitor.Party.CreateAsync(key, name));
        create.Controls.Add(createButton);
        _body.Controls.Add(create);
        y += create.Height + 10;

        // Unirse
        var join = new Card { Location = new Point(Pad, y), Size = new Size(W - 2 * Pad, 64), Radius = 14, BackColor = Theme.Card };
        join.Controls.Add(CardLabel(L.T("Unirse con un código", "Join with a code"), 9.5f, FontStyle.Bold, Theme.Ink, new Point(14, 6), join));
        var box = new TextBox
        {
            Text = _code, CharacterCasing = CharacterCasing.Upper, MaxLength = 8, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel, ForeColor = Theme.Ink, Font = Theme.Text(12f, FontStyle.Bold),
            Location = new Point(14, 28), Size = new Size(join.Width - 14 - 96 - 12 - 14, 28), PlaceholderText = "ABC123",
        };
        box.TextChanged += (_, _) => _code = box.Text;
        var joinButton = new PillButton { Text = L.T("Unirse", "Join"), Location = new Point(join.Width - 14 - 96, 14), Size = new Size(96, 36), Radius = 11, Enabled = !_busy };
        joinButton.Click += async (_, _) =>
        {
            var code = _code.Trim();
            if (code.Length < 4)
            {
                _error = L.T("Escribe el código que te pasó tu amigo.", "Type the code your friend gave you.");
                BeginInvoke(Rebuild);
                return;
            }
            await Run(() => _monitor.Party.JoinAsync(code, key, name));
        };
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            joinButton.PerformClick();
        };
        join.Controls.Add(box);
        join.Controls.Add(joinButton);
        _body.Controls.Add(join);
        if (focusCode) BeginInvoke(() => { if (!box.IsDisposed) { box.Focus(); box.SelectionStart = box.TextLength; } });
        y += join.Height + 10;

        y = Paragraph(y, L.T("Todos tenéis que usar esta versión del contador. Si un amigo juega con el contador cerrado, sus partidas se suben la próxima vez que lo abra (mientras Fortnite conserve los replays).",
            "Everyone needs this version of the counter. If a friend plays with the counter closed, their matches are uploaded the next time they open it (while Fortnite keeps the replays)."), Theme.Muted);
        return Buttons(y + 4, null);
    }

    // ---- En una party: miembros y sus cofres --------------------------------------------------------------------------

    private int BuildParty(int y, PartyView view)
    {
        var title = new Label
        {
            Text = "Party  " + view.Code, Font = Theme.Text(14f, FontStyle.Bold), ForeColor = Theme.Cyan, BackColor = Theme.Bg,
            Location = new Point(Pad, y), AutoSize = true, UseMnemonic = false,
        };
        _body.Controls.Add(title);
        var copy = new PillButton { Text = L.T("Copiar código", "Copy code"), Location = new Point(W - Pad - 120, y), Size = new Size(120, 30), Radius = 10, Font = Theme.Text(8.5f, FontStyle.Bold) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(view.Code); copy.Text = L.T("¡Copiado!", "Copied!"); }
            catch { /* portapapeles ocupado */ }
        };
        _body.Controls.Add(copy);
        y += Math.Max(title.PreferredHeight, 30) + 4;
        y = Paragraph(y, L.T("Pasa este código a tus amigos para que se unan. Los cofres de quien esté aquí salen exactos (✓) en tus partidas con ellos.",
            "Give this code to your friends so they can join. The chests of everyone here show as exact (✓) in your matches with them."), Theme.Muted);
        y += 4;

        var card = new Card { Location = new Point(Pad, y), Width = W - 2 * Pad, Radius = 14, BackColor = Theme.Card };
        _body.Controls.Add(card);
        var listH = Math.Min(view.Members.Count * RowH + 8, MaxListH);
        card.Height = listH + 8;
        var list = new Panel { Location = new Point(4, 4), Size = new Size(card.Width - 8, listH), BackColor = card.BackColor, AutoScroll = view.Members.Count * RowH + 8 > MaxListH };
        card.Controls.Add(list);
        var rowW = list.Width - (list.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0);
        var ry = 4;
        foreach (var m in view.Members)
        {
            var name = m.Name + (m.IsMe ? "  " + L.T("(tú)", "(you)") : "");
            list.Controls.Add(new Label
            {
                Text = name, Font = Theme.Text(9.5f, FontStyle.Bold), ForeColor = m.IsMe ? Theme.Amber : Theme.Ink, BackColor = list.BackColor,
                Location = new Point(10, ry + 4), Size = new Size(rowW - 130, 20), AutoEllipsis = true, UseMnemonic = false,
            });
            var detail = L.F("{0} en total · {1}", "{0} in total · {1}", L.N(m.Total), L.Count(m.Matches, "partida", "partidas", "match", "matches"));
            if (!m.IsMe) detail += " · " + L.F("{0} contigo", "{0} with you", L.N(m.Together));
            detail += " · " + (m.LastSync is { } at ? Ago(at) : L.T("aún no se ha conectado", "not connected yet"));
            list.Controls.Add(new Label
            {
                Text = detail, Font = Theme.Text(7.5f), ForeColor = Theme.Muted, BackColor = list.BackColor,
                Location = new Point(10, ry + 25), Size = new Size(rowW - 20, 17), AutoEllipsis = true, UseMnemonic = false,
            });
            list.Controls.Add(new Label
            {
                Text = L.N(m.Today), Font = Theme.Text(15f, FontStyle.Bold), ForeColor = m.Today > 0 ? Theme.Cyan : Theme.Muted, BackColor = list.BackColor,
                Location = new Point(rowW - 120, ry), Size = new Size(70, 26), TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false,
            });
            list.Controls.Add(new Label
            {
                Text = L.T("hoy", "today"), Font = Theme.Text(7.5f), ForeColor = Theme.Muted, BackColor = list.BackColor,
                Location = new Point(rowW - 48, ry + 8), Size = new Size(40, 16), UseMnemonic = false,
            });
            ry += RowH;
        }
        y += card.Height + 8;

        var status = view.Error ?? (view.Syncing ? L.T("Sincronizando…", "Syncing…")
            : view.LastSyncUtc is { } last ? L.F("Sincronizado {0}. Se actualiza cada minuto y al terminar cada partida.", "Synced {0}. Updates every minute and after every match.", Ago(last))
            : L.T("Conectando…", "Connecting…"));
        y = Paragraph(y, status, view.Error != null ? Theme.Coral : Theme.Muted);
        y = Paragraph(y, L.T("Cofres de partidas de Battle Royale. «Contigo» = partidas tuyas en las que su número ya es exacto.",
            "Battle Royale chests. “With you” = your matches where their number is already exact."), Theme.Muted);

        var leave = new PillButton { Text = L.T("Salir de la party", "Leave party"), Size = new Size(140, 36), Radius = 11, ForeColor = Theme.Coral, Enabled = !_busy };
        leave.Click += async (_, _) =>
        {
            var sure = MessageBox.Show(this, L.T("¿Salir de la party? Tus partidas subidas se borran del servidor; los números exactos que ya tienes se quedan.",
                "Leave the party? Your uploaded matches are deleted from the server; the exact numbers you already have stay."),
                "Party", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (sure == DialogResult.Yes) await Run(() => _monitor.Party.LeaveAsync());
        };
        return Buttons(y + 6, leave);
    }

    // ---- Piezas ------------------------------------------------------------------------------------------------------

    private async Task Run(Func<Task<string?>> action)
    {
        await Task.Yield(); // el botón pulsado se rehace: primero que termine su clic
        _busy = true;
        _error = null;
        Rebuild();
        string? error;
        try { error = await action(); }
        catch (Exception ex) { error = ex.Message; }
        if (IsDisposed) return;
        _busy = false;
        _error = error;
        _view = _monitor.Party.View();
        Rebuild();
        _monitor.RequestPublish();
    }

    private int Buttons(int y, PillButton? left)
    {
        if (_error != null)
        {
            y = Paragraph(y, _error, Theme.Coral) + 2;
        }
        if (_busy)
        {
            y = Paragraph(y, L.T("Conectando con el servidor…", "Contacting the server…"), Theme.AccentSoft) + 2;
        }
        if (left != null)
        {
            left.Location = new Point(Pad, y);
            _body.Controls.Add(left);
        }
        var close = new PillButton
        {
            Text = L.T("Cerrar", "Close"), Location = new Point(W - Pad - 110, y), Size = new Size(110, 36), Radius = 11,
            BackColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.AccentInk, DialogResult = DialogResult.OK,
        };
        _body.Controls.Add(close);
        CancelButton = close;
        return y + 36 + Pad;
    }

    private int Title(int y, string text)
    {
        var title = new Label
        {
            Text = text, Font = Theme.Text(12f, FontStyle.Bold), ForeColor = Theme.Ink, BackColor = Theme.Bg,
            Location = new Point(Pad, y), AutoSize = true, UseMnemonic = false,
        };
        _body.Controls.Add(title);
        return y + title.PreferredHeight + 4;
    }

    private int Paragraph(int y, string text, Color color)
    {
        var label = new Label
        {
            Text = text, Font = Theme.Text(8.5f), ForeColor = color, BackColor = Theme.Bg, Location = new Point(Pad, y),
            MaximumSize = new Size(W - 2 * Pad, 0), AutoSize = true, UseMnemonic = false,
        };
        _body.Controls.Add(label);
        return y + label.PreferredHeight + 6;
    }

    private static Label CardLabel(string text, float size, FontStyle style, Color color, Point at, Control card) => new()
    {
        Text = text, Font = Theme.Text(size, style), ForeColor = color, BackColor = card.BackColor,
        Location = at, Size = new Size(card.Width - 14 - 96 - 20 - at.X, (int)(size * 2.3f)), AutoEllipsis = true, UseMnemonic = false,
    };

    private static string Ago(DateTime utc)
    {
        var span = DateTime.UtcNow - utc.ToUniversalTime();
        if (span < TimeSpan.FromMinutes(1)) return L.T("hace un momento", "just now");
        if (span < TimeSpan.FromHours(1)) return L.F("hace {0} min", "{0} min ago", (int)span.TotalMinutes);
        if (span < TimeSpan.FromDays(1)) return L.F("hace {0} h", "{0} h ago", (int)span.TotalHours);
        return L.F("hace {0} d", "{0} d ago", (int)span.TotalDays);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Chrome(Handle);
    }
}
