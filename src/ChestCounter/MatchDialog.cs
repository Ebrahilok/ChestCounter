using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ChestCounter;

/// <summary>
/// Detalle de una partida, dentro de la app (clic en una fila de la lista): tus cofres por tipo, otros contenedores,
/// lo que se vio abrir a cada compañero y los tesoros con quién los consiguió. El informe HTML solo se abre con su botón.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MatchDialog : Form
{
    private const int W = 440, Pad = 18, CardPad = 14;
    private readonly Panel _scroll;
    private int _y;

    private MatchDialog(MatchRow row, string reportPath)
    {
        var d = row.Detail;
        Text = L.T("Detalle de la partida", "Match details");
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

        // ---- Cabecera: cuándo, modo, puesto ----------------------------------------------------------------
        var when = row.Live ? L.T("Partida en curso", "Match in progress") : row.Start.ToString(L.En ? "dddd, MMM d · HH:mm" : "dddd d/MM · HH:mm", L.Culture);
        Text1(Cap(when), 12f, FontStyle.Bold, row.Live ? Theme.Mint : Theme.Ink);
        var facts = new List<string> { row.Mode };
        if (row.Placement is { } p) facts.Add(p == 1 ? "🏆 " + L.T("Victoria", "Victory") : row.Players is { } n ? L.F("Puesto {0} de {1}", "Placed {0} of {1}", L.N(p), L.N(n)) : L.F("Puesto {0}", "Placed {0}", L.N(p)));
        if (row.Eliminations is { } e) facts.Add(L.Count(e, "eliminación", "eliminaciones", "elimination", "eliminations"));
        if (d.DurationSeconds is { } secs) facts.Add(L.F("{0} min", "{0} min", secs / 60));
        Text1(string.Join("  ·  ", facts), 9f, FontStyle.Regular, Theme.Muted);
        _y += 10;

        // ---- Línea de tiempo: cuándo abriste cada cofre ------------------------------------------------------
        if (d.Timeline.Count > 0)
        {
            var times = d.Timeline.Select(c => c.Seconds).ToList();
            var busiest = times.GroupBy(t => (int)(t / 60)).OrderByDescending(gr => gr.Count()).ThenBy(gr => gr.Key).First();
            var tl = Section(L.T("Línea de tiempo", "Timeline"),
                L.F("1.º a los {0}", "1st at {0}", TimelineChart.Clock(times[0])), Theme.Amber);
            var chart = new TimelineChart(d.Timeline, d.TreasureTimes, d.DurationSeconds ?? 0)
            {
                Location = new Point(CardPad - 4, tl.Y), Size = new Size(tl.Card.Width - 2 * CardPad + 8, 104),
            };
            tl.Card.Controls.Add(chart);
            tl.Y += 110;
            Row(tl, L.F("La mitad ({0}) ya a los {1}", "Half of them ({0}) by {1}", L.N((times.Count + 1) / 2), TimelineChart.Clock(times[(times.Count - 1) / 2])), "", Theme.Ink);
            Row(tl, L.F("Minuto más activo: {0}–{1} ({2})", "Busiest minute: {0}–{1} ({2})", busiest.Key, busiest.Key + 1,
                L.Count(busiest.Count(), "cofre", "cofres", "chest", "chests")), "", Theme.Ink);
            Row(tl, L.F("Último cofre a los {0}", "Last chest at {0}", TimelineChart.Clock(times[^1])), "", Theme.Ink);
            Row(tl, L.T("Marcas: blanco normal · cian raro · ámbar especial", "Ticks: white normal · cyan rare · amber special"), "", Theme.Muted);
            Row(tl, L.T("rojo = roto en vez de abierto · ◆ tesoro", "red = broken instead of opened · ◆ treasure"), "", Theme.Muted);
            EndSection(tl);
        }

        // ---- Tú ----------------------------------------------------------------------------------------
        var you = Section(L.T("Tú", "You"), L.Count(row.Chests, "cofre", "cofres", "chest", "chests"), Theme.Amber);
        if (d.MyChests.Count == 0)
        {
            Row(you, L.T("No abriste ningún cofre.", "You didn't open any chests."), "", Theme.Muted);
        }
        foreach (var c in d.MyChests) Row(you, c.Name, L.N(c.Count), Theme.Ink);
        var opened = d.MyChests.Sum(c => c.Count);
        if (d.Destroyed > 0)
        {
            Row(you, L.F("Rotos, no abiertos (de {0})", "Broken, not opened (of {0})", L.N(opened)), L.N(d.Destroyed), Theme.Muted);
        }
        if (d.MyOthers.Count > 0)
        {
            Divider(you);
            foreach (var c in d.MyOthers)
            {
                Row(you, c.Counted ? "+ " + c.Name : c.Name + "  " + L.T("(no suma)", "(not counted)"), L.N(c.Count), c.Counted ? Theme.Ink : Theme.Muted);
            }
            if (d.MyOthers.Any(c => c.Counted))
            {
                Row(you, L.F("= {0} cofres + {1} que sumas en Ajustes", "= {0} chests + {1} you count in Settings", L.N(opened), L.N(d.MyOthers.Where(c => c.Counted).Sum(c => c.Count))), "", Theme.Muted);
            }
        }
        EndSection(you);

        // ---- Compañeros ---------------------------------------------------------------------------------
        if (d.Mates == null)
        {
            Note(L.T("Esta partida se analizó con una versión antigua: no tiene datos de compañeros.",
                "This match was analyzed by an older version: it has no teammate data."));
        }
        else if (d.Mates.Count == 0)
        {
            Note(L.T("Partida en solitario: sin compañeros.", "Solo match: no teammates."));
        }
        else
        {
            var teamTotal = row.Chests + d.Mates.Sum(m => m.Chests);
            var team = Section(L.T("Compañeros", "Teammates"), L.F("equipo: {0}+ cofres", "team: {0}+ chests", L.N(teamTotal)), Theme.Cyan);
            var first = true;
            foreach (var m in d.Mates)
            {
                if (!first) Divider(team);
                first = false;
                Row(team, m.Name, m.Chests > 0 ? L.N(m.Chests) + "+" : "0", Theme.Ink, bold: true);
                if (m.Kinds.Count == 0)
                {
                    Row(team, L.T("no se le vio abrir nada", "not seen opening anything"), "", Theme.Muted, indent: true);
                }
                foreach (var k in m.Kinds) Row(team, k.Name, L.N(k.Count), Theme.Muted, indent: true);
            }
            EndSection(team);
            Note(L.T("«+» = al menos. Tu replay solo guarda lo que pasa cerca de ti: si un compañero abre cofres lejos, el juego no los envía a tu partida y no se pueden contar.",
                "“+” = at least. Your replay only stores what happens near you: if a teammate opens chests far away, the game doesn't send them to you and they can't be counted."));
        }

        // ---- Tesoros ---------------------------------------------------------------------------------------
        if (d.Treasures.Count > 0)
        {
            var t = Section(L.T("Tesoros", "Treasures"), L.N(d.Treasures.Count), Theme.Rose);
            foreach (var f in d.Treasures)
            {
                Row(t, (f.Golden ? "★★ " : "★ ") + f.Name, f.Who, f.Mine ? Theme.Rose : Theme.Ink);
            }
            EndSection(t);
        }

        // ---- Botones -------------------------------------------------------------------------------------
        _y += 4;
        var report = new PillButton { Text = L.T("Ver en el informe", "View in report"), Location = new Point(Pad, _y), Size = new Size(150, 36), Radius = 11 };
        report.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(new Uri(reportPath).AbsoluteUri + "#partidas") { UseShellExecute = true })?.Dispose(); }
            catch { /* sin informe o sin navegador */ }
        };
        var close = new PillButton
        {
            Text = L.T("Cerrar", "Close"), Location = new Point(W - Pad - 110, _y), Size = new Size(110, 36), Radius = 11,
            BackColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.AccentInk, DialogResult = DialogResult.OK,
        };
        _scroll.Controls.Add(report);
        _scroll.Controls.Add(close);
        AcceptButton = close;
        CancelButton = close;
        _y += 36 + Pad;

        // Alto: el contenido, hasta 600 px; más allá se desplaza.
        var h = Math.Min(_y, 720);
        _scroll.Height = h;
        if (_y > h)
        {
            _scroll.AutoScroll = true; // solo si no cabe: si no, Windows a veces pone una barra inútil
            _scroll.Width = W + SystemInformation.VerticalScrollBarWidth;
        }
        ClientSize = new Size(_scroll.Width, h);
        Shown += (_, _) => _scroll.AutoScrollPosition = new Point(0, 0); // empieza arriba, no en el botón con foco
    }

    /// <summary>Abre el detalle de una partida.</summary>
    public static void Open(IWin32Window owner, MatchRow row, string reportPath)
    {
        using var dialog = new MatchDialog(row, reportPath);
        dialog.ShowDialog(owner);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Chrome(Handle);
    }

    private static string Cap(string s) => s.Length > 0 ? char.ToUpper(s[0], L.Culture) + s[1..] : s;

    // ---- Piezas -------------------------------------------------------------------------------------------

    private sealed class SectionState
    {
        public required Card Card;
        public int Y;
    }

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

    private void Note(string text)
    {
        var l = new Label
        {
            Text = text, Font = Theme.Text(8f), ForeColor = Theme.Muted, BackColor = Theme.Bg,
            Location = new Point(Pad + 2, _y), MaximumSize = new Size(W - 2 * Pad - 4, 0), AutoSize = true, UseMnemonic = false,
        };
        _scroll.Controls.Add(l);
        _y += l.PreferredHeight + 12;
    }

    private SectionState Section(string title, string summary, Color summaryColor)
    {
        var card = new Card { Location = new Point(Pad, _y), Width = W - 2 * Pad, Radius = 14 };
        _scroll.Controls.Add(card);
        var s = new SectionState { Card = card, Y = CardPad - 2 };
        card.Controls.Add(new Label
        {
            Text = title, Font = Theme.Text(9.5f, FontStyle.Bold), ForeColor = Theme.Ink, BackColor = card.BackColor,
            Location = new Point(CardPad, s.Y), Size = new Size(card.Width / 2, 20), UseMnemonic = false,
        });
        card.Controls.Add(new Label
        {
            Text = summary, Font = Theme.Text(9.5f, FontStyle.Bold), ForeColor = summaryColor, BackColor = card.BackColor,
            Location = new Point(card.Width / 2, s.Y), Size = new Size(card.Width / 2 - CardPad, 20),
            TextAlign = ContentAlignment.TopRight, UseMnemonic = false,
        });
        s.Y += 26;
        return s;
    }

    private static void Row(SectionState s, string name, string value, Color color, bool indent = false, bool bold = false)
    {
        var x = CardPad + (indent ? 14 : 0);
        var valueW = value.Length == 0 ? 0 : 120; // sin cifra, el texto usa todo el ancho
        var font = Theme.Text(9f, bold ? FontStyle.Bold : FontStyle.Regular);
        s.Card.Controls.Add(new Label
        {
            Text = name, Font = font, ForeColor = color, BackColor = s.Card.BackColor, AutoEllipsis = true,
            Location = new Point(x, s.Y), Size = new Size(s.Card.Width - x - valueW - CardPad, 19), UseMnemonic = false,
        });
        if (valueW == 0)
        {
            s.Y += 20;
            return;
        }
        s.Card.Controls.Add(new Label
        {
            Text = value, Font = Theme.Text(9f, FontStyle.Bold), ForeColor = color == Theme.Muted ? Theme.Muted : Theme.Ink,
            BackColor = s.Card.BackColor, AutoEllipsis = true, TextAlign = ContentAlignment.TopRight,
            Location = new Point(s.Card.Width - CardPad - valueW, s.Y), Size = new Size(valueW, 19), UseMnemonic = false,
        });
        s.Y += 20;
    }

    private static void Divider(SectionState s)
    {
        s.Y += 4;
        s.Card.Controls.Add(new Panel { BackColor = Theme.CardEdge, Location = new Point(CardPad, s.Y), Size = new Size(s.Card.Width - 2 * CardPad, 1) });
        s.Y += 6;
    }

    private void EndSection(SectionState s)
    {
        s.Card.Height = s.Y + CardPad - 4;
        _y += s.Card.Height + 10;
    }
}
