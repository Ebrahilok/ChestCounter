using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ChestCounter;

/// <summary>
/// Estadísticas dentro de la app, sobre las partidas de la lista (con su filtro, si hay): tus cofres según el puesto,
/// por modo y con tus compañeros. Clic en un compañero filtra la lista por las partidas jugadas con él.
/// Usa tus cofres (sin los de los compañeros), los mismos que la cifra grande de cada partida.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StatsDialog : Form
{
    private const int W = 500, Pad = 18, CardPad = 14;
    private readonly Panel _scroll;
    private int _y;

    private sealed class Block
    {
        public required Card Card;
        public int Y;
    }

    private StatsDialog(IReadOnlyList<MatchRow> all, string? filter, Action<string> filterByMate)
    {
        Text = L.T("Estadísticas", "Statistics");
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

        _scroll = new Panel { Location = new Point(0, 0), Width = W, BackColor = Theme.Bg };
        Controls.Add(_scroll);
        _y = Pad;

        var rows = all.Where(r => !r.Live).ToList();
        Text1(L.T("Estadísticas", "Statistics"), 12f, FontStyle.Bold, Theme.Ink);
        Text1((filter != null ? L.F("Filtro: {0} · ", "Filter: {0} · ", filter) : "")
            + L.Count(rows.Count, "partida", "partidas", "match", "matches") + " · "
            + L.T("tus cofres, sin los de los compañeros", "your chests, without your teammates'"), 9f, FontStyle.Regular, Theme.Muted);
        _y += 10;

        if (rows.Count == 0)
        {
            Text1(L.T("No hay partidas terminadas para calcular.", "No finished matches to work with."), 9f, FontStyle.Regular, Theme.Muted);
        }
        else
        {
            AddLuck(rows);
            AddPlacement(rows);
            AddModes(rows);
            AddMates(rows, filterByMate);
        }

        _y += 4;
        var close = new PillButton
        {
            Text = L.T("Cerrar", "Close"), Location = new Point(W - Pad - 110, _y), Size = new Size(110, 36), Radius = 11,
            BackColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.AccentInk, DialogResult = DialogResult.OK,
        };
        _scroll.Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;
        _y += 36 + Pad;

        var h = Math.Min(_y, 720);
        _scroll.Height = h;
        if (_y > h)
        {
            _scroll.AutoScroll = true;
            _scroll.Width = W + SystemInformation.VerticalScrollBarWidth;
        }
        ClientSize = new Size(_scroll.Width, h);
        Shown += (_, _) => _scroll.AutoScrollPosition = new Point(0, 0);
    }

    public static void Open(IWin32Window owner, IReadOnlyList<MatchRow> rows, string? filter, Action<string> filterByMate)
    {
        string? chosen = null;
        using (var dialog = new StatsDialog(rows, filter, mate => chosen = mate))
        {
            dialog.ShowDialog(owner);
        }
        if (chosen != null) filterByMate(chosen);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Chrome(Handle);
    }

    // ---- Secciones -------------------------------------------------------------------------------------------

    /// <summary>
    /// Probabilidad de tesoro y de token pirata por cofre, calculada con tus propios cofres (Epic no publica las
    /// cifras). Sin ningún token todavía, se da un límite: con 0 casos en N intentos, la probabilidad real es casi
    /// seguro (95 %) menor que 3/N («regla del tres»).
    /// </summary>
    private void AddLuck(List<MatchRow> rows)
    {
        var chests = rows.Sum(r => r.OwnChestCount);
        var treasures = rows.Sum(r => r.OwnTreasures);
        var tokens = rows.Sum(r => r.OwnTokens);
        var perMatch = rows.Count > 0 ? rows.Average(r => r.OwnChestCount) : 0;
        static string OneIn(double p) => L.F("1 de cada {0}", "1 in {0}", L.N((int)Math.Round(1 / p)));
        static string Pct(double p) => (p * 100).ToString(p < 0.01 ? "0.00" : "0.0", L.Culture) + " %";
        static double AtLeastOne(double p, double n) => 1 - Math.Pow(1 - p, n);

        var b = Section(L.T("Tu suerte con los cofres", "Your chest luck"), L.F("{0} cofres tuyos", "{0} of your chests", L.N(chests)));
        if (chests < 20)
        {
            Note(b, L.T("Todavía hay muy pocos cofres para calcular nada.", "Too few chests yet to work anything out."), Theme.Muted);
            End(b);
            return;
        }

        Header(b, "", L.T("Cada cofre", "Each chest"), L.T("Cada partida", "Each match"));
        if (treasures > 0)
        {
            var p = (double)treasures / chests;
            Line(b, L.F("Algún tesoro ({0} en total)", "Any treasure ({0} in total)", L.N(treasures)), Pct(p), Pct(AtLeastOne(p, perMatch)), Theme.Rose);
            Note(b, L.F("Te sale un tesoro en {0} cofres.", "You get a treasure in {0} chests.", OneIn(p).ToLower(L.Culture)), Theme.Muted);
        }
        if (tokens > 0)
        {
            var p = (double)tokens / chests;
            Line(b, L.F("Token pirata ({0})", "Pirate token ({0})", L.N(tokens)), Pct(p), Pct(AtLeastOne(p, perMatch)), Theme.Amber);
            Note(b, L.F("Unos {0} cofres por token. Probabilidad de que te salga al menos uno en los próximos 100 cofres: {1}.",
                "About {0} chests per token. Chance of getting at least one in your next 100 chests: {1}.",
                L.N((int)Math.Round(1 / p)), Pct(AtLeastOne(p, 100))), Theme.Ink);
        }
        else
        {
            var upper = 3.0 / chests;
            Line(b, L.T("Token pirata (ninguno aún)", "Pirate token (none yet)"), "< " + Pct(upper), "< " + Pct(AtLeastOne(upper, perMatch)), Theme.Amber);
            Note(b, L.F("No te ha salido ninguno en {0} cofres. Con tus datos, la probabilidad por cofre es casi seguro menor que {1}: hacen falta tokens para afinar más.",
                "You haven't got one in {0} chests. From your data, the chance per chest is almost certainly below {1}: it takes some tokens to narrow it down.",
                L.N(chests), OneIn(upper).ToLower(L.Culture)), Theme.Ink);
        }
        Note(b, L.T("Calculado con tus cofres y tus tesoros: Epic no publica las probabilidades. Más cofres = estimación más fiable. En la Hora del tesoro, el acceso a la Tienda pirata es ×1,5.",
            "Worked out from your own chests and treasures: Epic doesn't publish the odds. More chests = a more reliable estimate. During Treasure Hour, Pirate Shop access is ×1.5."), Theme.Muted);
        End(b);
    }

    /// <summary>¿Más cofres = mejor puesto? Media de tus cofres por tramo de puesto, con una barra.</summary>
    private void AddPlacement(List<MatchRow> rows)
    {
        var buckets = new (string Name, Func<int?, bool> Test, Color Color)[]
        {
            (L.T("Victoria", "Victory"), p => p == 1, Theme.Amber),
            (L.T("Puesto 2–10", "Placed 2–10"), p => p is >= 2 and <= 10, Theme.Cyan),
            (L.T("Puesto 11–25", "Placed 11–25"), p => p is >= 11 and <= 25, Theme.Ink),
            (L.T("Puesto 26 o peor", "Placed 26 or lower"), p => p is >= 26, Theme.Ink),
        };
        var b = Section(L.T("Cofres según tu puesto", "Chests by placement"), L.T("media por partida", "average per match"));
        Header(b, L.T("Puesto", "Placement"), L.T("Partidas", "Matches"), L.T("Media", "Average"));
        var data = buckets.Select(x => (x.Name, x.Color, Matches: rows.Where(r => x.Test(r.Placement)).ToList())).ToList();
        var max = data.Where(d => d.Matches.Count > 0).Select(d => d.Matches.Average(r => r.Chests)).DefaultIfEmpty(1).Max();
        foreach (var (name, color, matches) in data)
        {
            var avg = matches.Count > 0 ? matches.Average(r => r.Chests) : (double?)null;
            Line(b, name, L.N(matches.Count), avg?.ToString("0.0", L.Culture) ?? "—", color, avg is { } a ? (float)(a / max) : 0f, color);
        }

        // Una frase con la conclusión: top 10 contra el resto.
        var top = rows.Where(r => r.Placement is <= 10).ToList();
        var rest = rows.Where(r => r.Placement is > 10).ToList();
        if (top.Count >= 3 && rest.Count >= 3)
        {
            var diff = top.Average(r => r.Chests) - rest.Average(r => r.Chests);
            b.Y += 4;
            Note(b, Math.Abs(diff) < 1
                ? L.T("En el top 10 abres prácticamente los mismos cofres que en el resto de partidas.", "In the top 10 you open about the same number of chests as in other matches.")
                : diff > 0
                    ? L.F("Cuando quedas en el top 10 abres de media {0} cofres más que cuando no.", "When you place top 10 you open {0} more chests on average than when you don't.", diff.ToString("0.0", L.Culture))
                    : L.F("Cuando quedas en el top 10 abres de media {0} cofres menos: no hace falta saquear tanto para llegar lejos.", "When you place top 10 you open {0} fewer chests on average: you don't need that much loot to go far.", (-diff).ToString("0.0", L.Culture)),
                Theme.Ink);
        }
        End(b);
    }

    /// <summary>Por modo: partidas, media, récord y victorias.</summary>
    private void AddModes(List<MatchRow> rows)
    {
        var modes = rows.GroupBy(r => r.Mode).OrderByDescending(g => g.Count()).ToList();
        var b = Section(L.T("Por modo", "By mode"), L.Count(modes.Count, "modo", "modos", "mode", "modes"));
        Header(b, L.T("Modo", "Mode"), L.T("Partidas", "Matches"), L.T("Media", "Average"), L.T("Récord", "Best"), L.T("Victorias", "Wins"));
        foreach (var g in modes)
        {
            var wins = g.Count(r => r.Placement == 1);
            Line(b, g.Key, L.N(g.Count()), g.Average(r => r.Chests).ToString("0.0", L.Culture), Theme.Ink, extra: new[]
            {
                L.N(g.Max(r => r.Chests)),
                wins > 0 ? L.N(wins) : "—",
            });
        }
        End(b);
    }

    /// <summary>Compañeros: partidas juntos, victorias y cofres que se les vio abrir. Clic = filtrar la lista.</summary>
    private void AddMates(List<MatchRow> rows, Action<string> filterByMate)
    {
        var mates = rows
            .SelectMany(r => (r.Detail.Mates ?? (IReadOnlyList<MateDetail>)Array.Empty<MateDetail>()).Select(m => (Row: r, Mate: m)))
            .GroupBy(x => x.Mate.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().Mate.Name, Matches: g.Count(), Wins: g.Count(x => x.Row.Placement == 1),
                Avg: g.Average(x => x.Mate.Chests), Mine: g.Average(x => x.Row.Chests)))
            .OrderByDescending(m => m.Matches).ThenByDescending(m => m.Wins)
            .Take(12)
            .ToList();
        var b = Section(L.T("Compañeros", "Teammates"), mates.Count == 0 ? "" : L.T("clic = ver sus partidas", "click = see your matches"));
        if (mates.Count == 0)
        {
            Note(b, L.T("No hay partidas con compañeros guardados.", "No matches with saved teammates."), Theme.Muted);
            End(b);
            return;
        }
        Header(b, L.T("Compañero", "Teammate"), L.T("Juntos", "Together"), L.T("Suyos", "Theirs"), L.T("Tuyos", "Yours"), L.T("Victorias", "Wins"));
        foreach (var m in mates)
        {
            var labels = Line(b, m.Name, L.N(m.Matches), m.Avg.ToString("0.0", L.Culture) + "+", Theme.Ink, extra: new[]
            {
                m.Mine.ToString("0.0", L.Culture),
                m.Wins > 0 ? L.N(m.Wins) : "—",
            });
            var name = m.Name;
            foreach (var l in labels)
            {
                l.Cursor = Cursors.Hand;
                l.Click += (_, _) => { filterByMate(name); Close(); };
                l.MouseEnter += (_, _) => labels[0].ForeColor = Theme.Cyan;
                l.MouseLeave += (_, _) => labels[0].ForeColor = Theme.Ink;
            }
        }
        b.Y += 4;
        Note(b, L.T("«Suyos» y «Tuyos» son cofres de media por partida juntos. Los suyos son un mínimo (+): tu replay solo tiene lo que pasa cerca de ti.",
            "“Theirs” and “Yours” are average chests per match together. Theirs are a minimum (+): your replay only has what happens near you."), Theme.Muted);
        End(b);
    }

    // ---- Piezas ----------------------------------------------------------------------------------------------

    private void Text1(string text, float size, FontStyle style, Color color)
    {
        var l = new Label
        {
            Text = text, Font = Theme.Text(size, style), ForeColor = color, BackColor = Theme.Bg,
            Location = new Point(Pad, _y), MaximumSize = new Size(W - 2 * Pad, 0), AutoSize = true, UseMnemonic = false,
        };
        _scroll.Controls.Add(l);
        _y += l.PreferredHeight + 2;
    }

    private Block Section(string title, string summary)
    {
        var card = new Card { Location = new Point(Pad, _y), Width = W - 2 * Pad, Radius = 14 };
        _scroll.Controls.Add(card);
        var b = new Block { Card = card, Y = CardPad - 2 };
        card.Controls.Add(new Label
        {
            Text = title, Font = Theme.Text(9.5f, FontStyle.Bold), ForeColor = Theme.Ink, BackColor = card.BackColor,
            Location = new Point(CardPad, b.Y), Size = new Size(card.Width / 2, 20), UseMnemonic = false,
        });
        card.Controls.Add(new Label
        {
            Text = summary, Font = Theme.Text(8f), ForeColor = Theme.Muted, BackColor = card.BackColor,
            Location = new Point(card.Width / 2, b.Y + 2), Size = new Size(card.Width / 2 - CardPad, 18),
            TextAlign = ContentAlignment.TopRight, UseMnemonic = false,
        });
        b.Y += 28;
        return b;
    }

    // Columnas: nombre (resto del ancho) y hasta 4 cifras de 62 px, alineadas a la derecha.
    private const int ColW = 74;

    private static int NameWidth(Block b, int columns) => b.Card.Width - 2 * CardPad - columns * ColW;

    private static void Header(Block b, string name, params string[] columns)
    {
        var x = CardPad;
        Add(b, name, x, NameWidth(b, columns.Length), Theme.Muted, false, 7.5f);
        x += NameWidth(b, columns.Length);
        foreach (var c in columns)
        {
            Add(b, c, x, ColW, Theme.Muted, false, 7.5f, right: true);
            x += ColW;
        }
        b.Y += 18;
    }

    /// <summary>Una fila: nombre, cifras y, si se pide, una barra fina bajo el nombre. Devuelve sus etiquetas.</summary>
    private static List<Label> Line(Block b, string name, string c1, string c2, Color nameColor, float bar = -1, Color? barColor = null, string[]? extra = null)
    {
        var columns = new List<string> { c1, c2 };
        if (extra != null) columns.AddRange(extra);
        var nameW = NameWidth(b, columns.Count);
        var labels = new List<Label> { Add(b, name, CardPad, nameW - 8, nameColor, false, 9f) };
        var x = CardPad + nameW;
        for (var i = 0; i < columns.Count; i++)
        {
            labels.Add(Add(b, columns[i], x, ColW, i == 1 ? Theme.Ink : Theme.Muted, i == 1, 9f, right: true));
            x += ColW;
        }
        b.Y += 19;
        if (bar >= 0)
        {
            var barW = nameW - 16;
            b.Card.Controls.Add(new GoalBar { Location = new Point(CardPad, b.Y), Size = new Size(barW, 4), Value = bar, BackColor = b.Card.BackColor, ForeColor = barColor ?? Theme.Cyan, GreenWhenFull = false });
            b.Y += 9;
        }
        return labels;
    }

    private static Label Add(Block b, string text, int x, int w, Color color, bool bold, float size, bool right = false)
    {
        var l = new Label
        {
            Text = text, Font = Theme.Text(size, bold ? FontStyle.Bold : FontStyle.Regular), ForeColor = color, BackColor = b.Card.BackColor,
            AutoEllipsis = true, Location = new Point(x, b.Y), Size = new Size(w, 18), UseMnemonic = false,
            TextAlign = right ? ContentAlignment.TopRight : ContentAlignment.TopLeft,
        };
        b.Card.Controls.Add(l);
        return l;
    }

    private static void Note(Block b, string text, Color color)
    {
        var l = new Label
        {
            Text = text, Font = Theme.Text(8f), ForeColor = color, BackColor = b.Card.BackColor, UseMnemonic = false,
            Location = new Point(CardPad, b.Y), MaximumSize = new Size(b.Card.Width - 2 * CardPad, 0), AutoSize = true,
        };
        b.Card.Controls.Add(l);
        b.Y += l.PreferredHeight + 4;
    }

    private void End(Block b)
    {
        b.Card.Height = b.Y + CardPad - 4;
        _y += b.Card.Height + 10;
    }
}
