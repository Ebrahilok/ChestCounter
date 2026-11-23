using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ChestCounter;

/// <summary>
/// Línea de tiempo de una partida: cofres acumulados (escalones) a lo largo de los minutos, una marca abajo por cada
/// cofre (color según el tipo; rojo si lo rompiste) y un rombo rosa en cada tesoro.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TimelineChart : Control
{
    private readonly IReadOnlyList<ChestMoment> _chests;
    private readonly IReadOnlyList<TreasureMoment> _treasures;
    private readonly float _duration;

    public TimelineChart(IReadOnlyList<ChestMoment> chests, IReadOnlyList<TreasureMoment> treasures, float duration)
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        _chests = chests;
        _treasures = treasures;
        var last = Math.Max(chests.Select(c => c.Seconds).DefaultIfEmpty(0).Max(), treasures.Select(t => t.Seconds).DefaultIfEmpty(0).Max());
        _duration = Math.Max(60, Math.Max(duration, last + 10));
        BackColor = Theme.Card;
        Font = Theme.Text(7.5f);
    }

    /// <summary>Color de la marca de un cofre según su tipo.</summary>
    public static Color KindColor(string kind, bool destroyed) => destroyed ? Theme.Coral : kind switch
    {
        ChestKinds.Normal => Theme.Ink,
        ChestKinds.Rare => Theme.Cyan,
        _ => Theme.Amber, // enterrado, extracción, cartel…
    };

    /// <summary>"3:07".</summary>
    public static string Clock(float seconds) => $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var labelH = Font.Height + 2;
        var plot = new RectangleF(2, 6, Width - 4, Height - labelH - 18); // zona de la curva
        var tickY = plot.Bottom + 4;                                      // marcas por cofre
        float X(float s) => plot.Left + plot.Width * Math.Clamp(s / _duration, 0, 1);

        // Rejilla y etiquetas de minutos (cada 5 min; cada 2 si la partida es corta).
        var step = _duration > 12 * 60 ? 300 : 120;
        using (var grid = new Pen(Theme.CardEdge, 1f))
        {
            for (var s = 0; s <= _duration; s += step)
            {
                var x = X(s);
                g.DrawLine(grid, x, plot.Top, x, plot.Bottom);
                var text = L.F("{0} min", "{0} min", s / 60);
                var size = TextRenderer.MeasureText(text, Font);
                var lx = (int)Math.Clamp(x - size.Width / 2f, 0, Width - size.Width);
                TextRenderer.DrawText(g, text, Font, new Point(lx, Height - labelH), Theme.Muted);
            }
            g.DrawLine(grid, plot.Left, plot.Bottom, plot.Right, plot.Bottom);
        }

        if (_chests.Count > 0)
        {
            // Escalones: cofres acumulados.
            var points = new List<PointF> { new(plot.Left, plot.Bottom) };
            for (var i = 0; i < _chests.Count; i++)
            {
                var x = X(_chests[i].Seconds);
                var yPrev = plot.Bottom - plot.Height * i / _chests.Count;
                var y = plot.Bottom - plot.Height * (i + 1) / _chests.Count;
                points.Add(new PointF(x, yPrev));
                points.Add(new PointF(x, y));
            }
            points.Add(new PointF(X(_duration), plot.Top));
            using (var area = new GraphicsPath())
            {
                area.AddLines(points.ToArray());
                area.AddLine(X(_duration), plot.Top, X(_duration), plot.Bottom);
                area.CloseFigure();
                using var fill = new SolidBrush(Color.FromArgb(40, Theme.Amber));
                g.FillPath(fill, area);
            }
            using (var pen = new Pen(Theme.Amber, 2f) { LineJoin = LineJoin.Round })
            {
                g.DrawLines(pen, points.ToArray());
            }

            // Una marca por cofre.
            foreach (var c in _chests)
            {
                using var pen = new Pen(KindColor(c.Kind, c.Destroyed), 2f);
                var x = X(c.Seconds);
                g.DrawLine(pen, x, tickY, x, tickY + 9);
            }
        }

        // Tesoros: rombo rosa sobre la curva.
        using var rose = new SolidBrush(Theme.Rose);
        foreach (var t in _treasures)
        {
            var x = X(t.Seconds);
            var done = _chests.Count(c => c.Seconds <= t.Seconds);
            var y = _chests.Count > 0 ? plot.Bottom - plot.Height * done / _chests.Count : plot.Bottom;
            const float r = 5f;
            g.FillPolygon(rose, new[] { new PointF(x, y - r), new PointF(x + r, y), new PointF(x, y + r), new PointF(x - r, y) });
        }
    }
}
