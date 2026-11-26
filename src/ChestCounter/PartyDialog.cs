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
    private float _f = 1;
    private int _tab; // 0 = estado de la party, 1 = partidas juntos

    /// <summary>Medida de diseño (a 96 ppp) en píxeles del monitor actual.</summary>
    private int S(float v) => (int)Math.Round(v * _f);
    private Point P(float x, float y) => new(S(x), S(y));
    private Size Z(float w, float h) => new(S(w), S(h));

    private PartyDialog(Monitor monitor, PartyView? view)
    {
        _monitor = monitor;
        _view = view;
        Text = "Party";
        // Escalado propio (no AutoScale): la ventana se rehace con cada sincronización y AutoScale solo escala la primera vez.
        AutoScaleMode = AutoScaleMode.None;
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
            + $"|{v.Code}|{v.LastSyncUtc:O}|{v.Error}|{v.Syncing}|"
            + string.Join(";", v.Games.Select(g => $"{g.StartUtc:O},{string.Join(",", g.Players.Select(p => $"{p.Name}={p.Chests}{p.Exact}"))}"));

    private void Rebuild()
    {
        SuspendLayout();
        var focused = _body.Controls.OfType<TextBox>().FirstOrDefault()?.Focused == true;
        foreach (Control c in _body.Controls.Cast<Control>().ToList())
        {
            c.Dispose();
        }
        _body.Controls.Clear();
        _f = DeviceDpi / 96f;
        _body.Width = S(W);
        var y = S(Pad);
        if (_view == null) y = BuildJoin(y, focused);
        else y = BuildParty(y, _view);
        _body.Height = y;
        ClientSize = new Size(S(W), y);
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
            y = Paragraph(y + S(4), L.T("Primero juega una partida con el contador abierto: hace falta saber cuál es tu cuenta de Epic.",
                "Play a match with the counter open first: it needs to know your Epic account."), Theme.Amber);
            return Buttons(y + S(8), null);
        }
        y = Paragraph(y, L.F("Entras como {0}.", "You join as {0}.", name), Theme.Ink);
        y += S(6);

        // Crear
        var create = new Card { Location = new Point(S(Pad), y), Size = Z(W - 2 * Pad, 64), Radius = S(14), BackColor = Theme.Card };
        create.Controls.Add(CardLabel(L.T("Crear una party nueva", "Create a new party"), 9.5f, FontStyle.Bold, Theme.Ink, P(14, 12), create));
        create.Controls.Add(CardLabel(L.T("Te da un código para tus amigos", "Gives you a code for your friends"), 8f, FontStyle.Regular, Theme.Muted, P(14, 34), create));
        var createButton = new PillButton
        {
            Text = L.T("Crear", "Create"), Location = new Point(create.Width - S(14 + 96), S(14)), Size = Z(96, 36), Radius = S(11),
            BackColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.AccentInk, Enabled = !_busy,
        };
        createButton.Click += async (_, _) => await Run(() => _monitor.Party.CreateAsync(key, name));
        create.Controls.Add(createButton);
        _body.Controls.Add(create);
        y += create.Height + S(10);

        // Unirse
        var join = new Card { Location = new Point(S(Pad), y), Size = Z(W - 2 * Pad, 64), Radius = S(14), BackColor = Theme.Card };
        join.Controls.Add(CardLabel(L.T("Unirse con un código", "Join with a code"), 9.5f, FontStyle.Bold, Theme.Ink, P(14, 6), join));
        var box = new TextBox
        {
            Text = _code, CharacterCasing = CharacterCasing.Upper, MaxLength = 8, BorderStyle = BorderStyle.FixedSingle,
            BackColor = Theme.Panel, ForeColor = Theme.Ink, Font = Theme.Text(12f, FontStyle.Bold),
            Location = P(14, 28), Size = new Size(join.Width - S(14 + 96 + 12 + 14), S(28)), PlaceholderText = "ABC123",
        };
        box.TextChanged += (_, _) => _code = box.Text;
        var joinButton = new PillButton { Text = L.T("Unirse", "Join"), Location = new Point(join.Width - S(14 + 96), S(14)), Size = Z(96, 36), Radius = S(11), Enabled = !_busy };
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
        y += join.Height + S(10);

        y = Paragraph(y, L.T("Todos tenéis que usar esta versión del contador. Si un amigo juega con el contador cerrado, sus partidas se suben la próxima vez que lo abra (mientras Fortnite conserve los replays).",
            "Everyone needs this version of the counter. If a friend plays with the counter closed, their matches are uploaded the next time they open it (while Fortnite keeps the replays)."), Theme.Muted);
        return Buttons(y + S(4), null);
    }

    // ---- En una party: miembros y sus cofres --------------------------------------------------------------------------

    private int BuildParty(int y, PartyView view)
    {
        var title = new Label
        {
            Text = "Party  " + view.Code, Font = Theme.Text(14f, FontStyle.Bold), ForeColor = Theme.Cyan, BackColor = Theme.Bg,
            Location = new Point(S(Pad), y), AutoSize = true, UseMnemonic = false,
        };
        _body.Controls.Add(title);
        var copy = new PillButton { Text = L.T("Copiar código", "Copy code"), Location = new Point(S(W - Pad - 120), y), Size = Z(120, 30), Radius = S(10), Font = Theme.Text(8.5f, FontStyle.Bold) };
        copy.Click += (_, _) =>
        {
            try { Clipboard.SetText(view.Code); copy.Text = L.T("¡Copiado!", "Copied!"); }
            catch { /* portapapeles ocupado */ }
        };
        _body.Controls.Add(copy);
        y += Math.Max(title.PreferredHeight, S(30)) + S(8);

        // Pestañas: estado de la party / partidas jugadas juntos.
        var tabs = new[] { L.T("Estado de la party", "Party status"), L.F("Partidas juntos ({0})", "Matches together ({0})", L.N(view.Games.Count)) };
        var tabW = (W - 2 * Pad - 8) / 2f;
        for (var i = 0; i < tabs.Length; i++)
        {
            var index = i;
            var selected = _tab == i;
            var tab = new PillButton
            {
                Text = tabs[i], Location = new Point(S(Pad + i * (tabW + 8)), y), Size = Z(tabW, 32), Radius = S(10),
                Font = Theme.Text(9f, FontStyle.Bold),
                BackColor = selected ? Theme.Accent : Theme.Panel, HoverColor = selected ? Theme.AccentHover : Theme.PanelHover,
                ForeColor = selected ? Theme.AccentInk : Theme.Ink,
            };
            tab.Click += (_, _) =>
            {
                if (_tab == index) return;
                _tab = index;
                BeginInvoke(Rebuild); // el botón pulsado se rehace: después de su clic
            };
            _body.Controls.Add(tab);
        }
        y += S(32 + 12);
        if (_tab == 1)
        {
            return BuildGames(y, view);
        }

        y = Paragraph(y, L.T("Pasa este código a tus amigos para que se unan. Los cofres de quien esté aquí salen exactos (✓) en tus partidas con ellos.",
            "Give this code to your friends so they can join. The chests of everyone here show as exact (✓) in your matches with them."), Theme.Muted);
        y += S(4);

        var card = new Card { Location = new Point(S(Pad), y), Width = S(W - 2 * Pad), Radius = S(14), BackColor = Theme.Card };
        _body.Controls.Add(card);
        var listH = S(Math.Min(view.Members.Count * RowH + 8, MaxListH));
        card.Height = listH + S(8);
        var list = new Panel { Location = P(4, 4), Size = new Size(card.Width - S(8), listH), BackColor = card.BackColor, AutoScroll = view.Members.Count * RowH + 8 > MaxListH };
        card.Controls.Add(list);
        var rowW = list.Width - (list.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0);
        var ry = S(4);
        foreach (var m in view.Members)
        {
            var name = m.Name + (m.IsMe ? "  " + L.T("(tú)", "(you)") : "");
            list.Controls.Add(new Label
            {
                Text = name, Font = Theme.Text(9.5f, FontStyle.Bold), ForeColor = m.IsMe ? Theme.Amber : Theme.Ink, BackColor = list.BackColor,
                Location = new Point(S(10), ry + S(4)), Size = new Size(rowW - S(130), S(20)), AutoEllipsis = true, UseMnemonic = false,
            });
            var detail = L.F("{0} en total · {1}", "{0} in total · {1}", L.N(m.Total), L.Count(m.Matches, "partida", "partidas", "match", "matches"));
            if (!m.IsMe) detail += " · " + L.F("{0} contigo", "{0} with you", L.N(m.Together));
            detail += " · " + (m.LastSync is { } at ? Ago(at) : L.T("aún no se ha conectado", "not connected yet"));
            list.Controls.Add(new Label
            {
                Text = detail, Font = Theme.Text(7.5f), ForeColor = Theme.Muted, BackColor = list.BackColor,
                Location = new Point(S(10), ry + S(25)), Size = new Size(rowW - S(20), S(17)), AutoEllipsis = true, UseMnemonic = false,
            });
            list.Controls.Add(new Label
            {
                Text = L.N(m.Today), Font = Theme.Text(15f, FontStyle.Bold), ForeColor = m.Today > 0 ? Theme.Cyan : Theme.Muted, BackColor = list.BackColor,
                Location = new Point(rowW - S(120), ry), Size = Z(70, 26), TextAlign = ContentAlignment.MiddleRight, UseMnemonic = false,
            });
            list.Controls.Add(new Label
            {
                Text = L.T("hoy", "today"), Font = Theme.Text(7.5f), ForeColor = Theme.Muted, BackColor = list.BackColor,
                Location = new Point(rowW - S(48), ry + S(8)), Size = Z(40, 16), UseMnemonic = false,
            });
            ry += S(RowH);
        }
        y += card.Height + S(8);

        var status = view.Error ?? (view.Syncing ? L.T("Sincronizando…", "Syncing…")
            : view.LastSyncUtc is { } last ? L.F("Sincronizado {0}. Se actualiza cada minuto, al terminar cada partida y entero al abrir el contador.", "Synced {0}. Updates every minute, after every match and in full when the counter opens.", Ago(last))
            : L.T("Conectando…", "Connecting…"));
        y = Paragraph(y, status, view.Error != null ? Theme.Coral : Theme.Muted);
        y = Paragraph(y, L.T("Cofres de partidas de Battle Royale. «Contigo» = partidas tuyas en las que su número ya es exacto.",
            "Battle Royale chests. “With you” = your matches where their number is already exact."), Theme.Muted);

        var leave = new PillButton { Text = L.T("Salir de la party", "Leave party"), Size = Z(140, 36), Radius = S(11), ForeColor = Theme.Coral, Enabled = !_busy };
        leave.Click += async (_, _) =>
        {
            var sure = MessageBox.Show(this, L.T("¿Salir de la party? Tus partidas subidas se borran del servidor; los números exactos que ya tienes se quedan.",
                "Leave the party? Your uploaded matches are deleted from the server; the exact numbers you already have stay."),
                "Party", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (sure == DialogResult.Yes) await Run(() => _monitor.Party.LeaveAsync());
        };
        // Vuelve a subir todas tus partidas y a bajar todas las de la party: corrige cualquier número que no coincida.
        var resync = new PillButton { Text = "⟳ " + L.T("Resincronizar", "Resync all"), Size = Z(130, 36), Radius = S(11), Enabled = !_busy && !view.Syncing };
        resync.Click += (_, _) =>
        {
            _monitor.Party.ResyncAll();
            _monitor.RequestPublish();
            resync.Enabled = false;
            resync.Text = L.T("Sincronizando…", "Syncing…");
        };
        return Buttons(y + S(6), leave, resync);
    }

    // ---- Partidas juntos: cada partida con los cofres de cada uno -----------------------------------------------------

    private const int GamesShown = 60, GamesListH = 380, LineH = 19;

    private int BuildGames(int y, PartyView view)
    {
        var games = view.Games;
        if (games.Count == 0)
        {
            y = Paragraph(y, L.T("Todavía no hay partidas juntos. Aparecen aquí cuando al menos dos miembros de la party juegan la misma partida y la suben (al terminarla, con el contador abierto).",
                "No matches together yet. They show up here when at least two party members play the same match and upload it (when it ends, with the counter open)."), Theme.Muted);
            return Buttons(y + S(4), null);
        }

        // Resumen: cofres de cada miembro sumando todas las partidas juntos (solo los exactos).
        var totals = games.SelectMany(g => g.Players.Where(p => p.Exact))
            .GroupBy(p => p.Name)
            .Select(g => (Name: g.Key, IsMe: g.First().IsMe, Chests: g.Sum(p => p.Chests), Matches: g.Count()))
            .OrderByDescending(t => t.Chests)
            .ToList();
        y = Paragraph(y, L.Count(games.Count, "partida juntos", "partidas juntos", "match together", "matches together") + ":  "
            + string.Join("  ·  ", totals.Select(t => $"{(t.IsMe ? L.T("tú", "you") : t.Name)} {L.N(t.Chests)}")), Theme.Ink);

        var card = new Card { Location = new Point(S(Pad), y), Width = S(W - 2 * Pad), Radius = S(14), BackColor = Theme.Card };
        _body.Controls.Add(card);
        var shown = games.Take(GamesShown).ToList();
        var contentH = shown.Sum(g => 26 + g.Players.Count * LineH + 12) + 8;
        var listH = S(Math.Min(contentH, GamesListH));
        card.Height = listH + S(8);
        var list = new Panel { Location = P(4, 4), Size = new Size(card.Width - S(8), listH), BackColor = card.BackColor, AutoScroll = contentH > GamesListH };
        card.Controls.Add(list);
        var rowW = list.Width - (list.AutoScroll ? SystemInformation.VerticalScrollBarWidth : 0);
        var ry = S(6);
        var first = true;
        foreach (var g in shown)
        {
            if (!first)
            {
                list.Controls.Add(new Panel { BackColor = Theme.CardEdge, Location = new Point(S(10), ry - S(6)), Size = new Size(rowW - S(20), 1) });
            }
            first = false;
            var local = g.StartUtc == DateTime.MinValue ? (DateTime?)null : g.StartUtc.ToLocalTime();
            var when = local is not { } at ? "—"
                : at.Date == DateTime.Today ? L.T("Hoy", "Today") + " " + at.ToString("HH:mm", L.Culture)
                : at.Date == DateTime.Today.AddDays(-1) ? L.T("Ayer", "Yesterday") + " " + at.ToString("HH:mm", L.Culture)
                : at.ToString(L.En ? "MMM d · HH:mm" : "d/MM · HH:mm", L.Culture);
            var head = when + "  ·  " + g.Mode + (g.Placement is { } place
                ? "  ·  " + (place == 1 ? L.T("Victoria", "Victory") : "#" + L.N(place)) : "");
            list.Controls.Add(new Label
            {
                Text = head, Font = Theme.Text(9f, FontStyle.Bold), ForeColor = g.Placement == 1 ? Theme.Amber : Theme.Ink, BackColor = list.BackColor,
                Location = new Point(S(10), ry), Size = new Size(rowW - S(130), S(20)), AutoEllipsis = true, UseMnemonic = false,
            });
            list.Controls.Add(new Label
            {
                Text = L.F("equipo {0}", "team {0}", L.N(g.Total)) + (g.Exact ? "" : "+"), Font = Theme.Text(9f, FontStyle.Bold),
                ForeColor = Theme.Cyan, BackColor = list.BackColor, TextAlign = ContentAlignment.TopRight,
                Location = new Point(rowW - S(120), ry), Size = new Size(S(110), S(20)), UseMnemonic = false,
            });
            ry += S(26);
            foreach (var p in g.Players)
            {
                var color = p.IsMe ? Theme.Amber : p.Exact ? Theme.Ink : Theme.Muted;
                var name = p.IsMe ? p.Name + "  " + L.T("(tú)", "(you)")
                    : p.InParty ? p.Name + (p.Exact ? "" : "  " + L.T("(aún no la subió)", "(not uploaded yet)"))
                    : p.Name + "  " + L.T("(random)", "(random)");
                list.Controls.Add(new Label
                {
                    Text = name, Font = Theme.Text(8.5f), ForeColor = color, BackColor = list.BackColor,
                    Location = new Point(S(22), ry), Size = new Size(rowW - S(22 + 90 + 10), S(LineH)), AutoEllipsis = true, UseMnemonic = false,
                });
                // Sin AutoEllipsis: con él, «✓» no se pinta.
                list.Controls.Add(new Label
                {
                    Text = p.IsMe ? L.N(p.Chests) : p.Exact ? "✓ " + L.N(p.Chests) : L.N(p.Chests) + "+",
                    Font = Theme.Text(8.5f, FontStyle.Bold), ForeColor = p.IsMe ? Theme.Amber : p.Exact ? Theme.Mint : Theme.Muted,
                    BackColor = list.BackColor, TextAlign = ContentAlignment.TopRight,
                    Location = new Point(rowW - S(100), ry), Size = new Size(S(90), S(LineH)), UseMnemonic = false,
                });
                ry += S(LineH);
            }
            ry += S(12);
        }
        list.Controls.Add(new Panel { BackColor = list.BackColor, Location = new Point(0, ry), Size = new Size(1, 1) }); // margen final al desplazar
        y += card.Height + S(8);
        y = Paragraph(y, (games.Count > GamesShown ? L.F("Las {0} más recientes. ", "The {0} most recent. ", GamesShown) : "")
            + L.T("✓ = exacto (su propio replay). «+» = mínimo visto desde tu replay: randoms o miembros que aún no la subieron.",
                "✓ = exact (their own replay). “+” = minimum seen from your replay: randoms or members who haven't uploaded it yet."), Theme.Muted);
        return Buttons(y + S(4), null);
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

    private int Buttons(int y, PillButton? left, PillButton? middle = null)
    {
        if (_error != null)
        {
            y = Paragraph(y, _error, Theme.Coral) + S(2);
        }
        if (_busy)
        {
            y = Paragraph(y, L.T("Conectando con el servidor…", "Contacting the server…"), Theme.AccentSoft) + S(2);
        }
        if (left != null)
        {
            left.Location = new Point(S(Pad), y);
            _body.Controls.Add(left);
        }
        if (middle != null)
        {
            middle.Location = new Point(S(W - Pad - 110 - 12 - 130), y);
            _body.Controls.Add(middle);
        }
        var close = new PillButton
        {
            Text = L.T("Cerrar", "Close"), Location = new Point(S(W - Pad - 110), y), Size = Z(110, 36), Radius = S(11),
            BackColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.AccentInk, DialogResult = DialogResult.OK,
        };
        _body.Controls.Add(close);
        CancelButton = close;
        return y + S(36 + Pad);
    }

    private int Title(int y, string text)
    {
        var title = new Label
        {
            Text = text, Font = Theme.Text(12f, FontStyle.Bold), ForeColor = Theme.Ink, BackColor = Theme.Bg,
            Location = new Point(S(Pad), y), AutoSize = true, UseMnemonic = false,
        };
        _body.Controls.Add(title);
        return y + title.PreferredHeight + S(4);
    }

    private int Paragraph(int y, string text, Color color)
    {
        var label = new Label
        {
            Text = text, Font = Theme.Text(8.5f), ForeColor = color, BackColor = Theme.Bg, Location = new Point(S(Pad), y),
            MaximumSize = new Size(S(W - 2 * Pad), 0), AutoSize = true, UseMnemonic = false,
        };
        _body.Controls.Add(label);
        return y + label.PreferredHeight + S(6);
    }

    private Label CardLabel(string text, float size, FontStyle style, Color color, Point at, Control card) => new()
    {
        Text = text, Font = Theme.Text(size, style), ForeColor = color, BackColor = card.BackColor,
        Location = at, Size = new Size(card.Width - S(14 + 96 + 20) - at.X, S(size * 2.3f)), AutoEllipsis = true, UseMnemonic = false,
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
        if (DeviceDpi / 96f != _f) Rebuild(); // se abrió en un monitor con otra escala
    }

    /// <summary>Movida a un monitor con otra escala: se rehace a la nueva medida.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        Rebuild();
    }
}
