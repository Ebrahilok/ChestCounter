using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ChestCounter;

/// <summary>
/// Lista de partidas de la ventana principal, la más reciente arriba. Cada fila: cuándo y en qué modo (izquierda),
/// puesto y eliminaciones (centro) y los cofres en grande (derecha). La partida en curso va arriba, en verde.
/// Se desplaza con la rueda del ratón. Todas las medidas salen de la fuente, que la ventana escala al redimensionar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MatchList : Control
{
    private IReadOnlyList<MatchRow> _rows = Array.Empty<MatchRow>();
    private int _top;     // primera fila visible
    private int _hover = -1;
    private readonly ToolTip _tip = new();
    private string? _tipText;

    public MatchList()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint
            | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        BackColor = Theme.Card;
        ForeColor = Theme.Ink;
        Font = Theme.Text(9f);
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.List;
        AccessibleName = L.T("Partidas", "Matches");
    }

    /// <summary>Clic en una fila (abre el detalle de esa partida).</summary>
    public event Action<MatchRow>? RowClicked;

    public void SetRows(IReadOnlyList<MatchRow> rows)
    {
        var keepTop = _rows.Count > 0 && rows.Count > 0 && _top > 0;
        _rows = rows;
        if (!keepTop)
        {
            _top = 0;
        }
        _top = Math.Clamp(_top, 0, MaxTop);
        Invalidate();
    }

    private float RowHeight => Font.Height * 2.75f;
    private int VisibleRows => Math.Max(1, (int)(Height / RowHeight));
    private int MaxTop => Math.Max(0, _rows.Count - VisibleRows);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var top = Math.Clamp(_top - Math.Sign(e.Delta) * 2, 0, MaxTop);
        if (top != _top)
        {
            _top = top;
            UpdateHover(e.Location);
            Invalidate();
        }
        base.OnMouseWheel(e);
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        Focus(); // para recibir la rueda del ratón
        base.OnMouseEnter(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        UpdateHover(e.Location);
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        _hover = -1;
        _tip.Hide(this);
        _tipText = null;
        Invalidate();
        base.OnMouseLeave(e);
    }

    protected override void OnClick(EventArgs e)
    {
        if (_hover >= 0 && _hover < _rows.Count)
        {
            _tip.Hide(this);
            RowClicked?.Invoke(_rows[_hover]);
        }
        base.OnClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }

    private void UpdateHover(Point p)
    {
        var i = _top + (int)(p.Y / RowHeight);
        var hover = p.Y >= 0 && i < _rows.Count && i < _top + VisibleRows ? i : -1;
        if (hover != _hover)
        {
            _hover = hover;
            Invalidate();
            var text = hover >= 0 ? L.T("Clic para ver quién abrió qué", "Click to see who opened what") : null;
            if (text != _tipText)
            {
                _tipText = text;
                if (text != null) _tip.SetToolTip(this, text); else _tip.Hide(this);
            }
        }
    }

    /// <summary>"Hoy 21:34", "Ayer 21:34", "dom 21:34" (esta semana) o "09/11 21:34".</summary>
    private static string When(DateTime start)
    {
        var today = DateTime.Now.Date;
        var time = start.ToString("HH:mm", L.Culture);
        var days = (today - start.Date).Days;
        return days switch
        {
            0 => $"{L.T("Hoy", "Today")} {time}",
            1 => $"{L.T("Ayer", "Yesterday")} {time}",
            > 1 and < 7 => $"{L.Culture.DateTimeFormat.GetAbbreviatedDayName(start.DayOfWeek)} {time}",
            _ => L.DateTimeShort(start),
        };
    }

    private static string Place(MatchRow r, bool long_ = false)
    {
        if (r.Placement is not { } p) return "";
        if (p == 1) return L.T("Victoria", "Victory");
        var text = "#" + L.N(p);
        if (r.Players is { } n) text += long_ ? " " + L.F("de {0}", "of {0}", L.N(n)) : "/" + L.N(n);
        return text;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_rows.Count == 0)
        {
            TextRenderer.DrawText(g,
                L.T("Aún no hay partidas. Juega una con los replays activados y aparecerá aquí.",
                    "No matches yet. Play one with replays turned on and it will show up here."),
                Font, ClientRectangle, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        using var fontMain = new Font(Font, FontStyle.Bold);
        using var fontSmall = new Font(Font.FontFamily, Font.SizeInPoints * 0.88f, FontStyle.Regular, GraphicsUnit.Point);
        using var fontBig = new Font(Font.FontFamily, Font.SizeInPoints * 1.65f, FontStyle.Bold, GraphicsUnit.Point);
        using var fontBadge = new Font(Font.FontFamily, Font.SizeInPoints * 0.88f, FontStyle.Bold, GraphicsUnit.Point);
        using var line = new Pen(Theme.CardEdge, 1f);

        var rowH = RowHeight;
        var pad = Font.Height * 0.6f;
        var w = Width - (MaxTop > 0 ? Font.Height * 0.5f : 0f); // hueco para la barra de desplazamiento
        var bigW = TextRenderer.MeasureText("8888", fontBig).Width;
        var chestRight = (int)(w - pad);
        var chestLeft = chestRight - bigW;
        var midW = (int)(w * 0.24f);
        var midLeft = chestLeft - (int)pad - midW;
        var leftW = midLeft - (int)pad * 2;
        var bestChests = _rows.Where(r => !r.Live).Select(r => r.Chests).DefaultIfEmpty(0).Max();

        for (var i = _top; i < _rows.Count && i < _top + VisibleRows + 1; i++)
        {
            var r = _rows[i];
            var y = (i - _top) * rowH;
            if (y > Height) break;
            var rect = new RectangleF(0, y, w, rowH);

            if (i == _hover || r.Live)
            {
                using var hl = new SolidBrush(r.Live ? Color.FromArgb(26, Theme.Mint) : Theme.Panel);
                using var path = Shape.Rounded(new RectangleF(pad * 0.4f, y + 2, w - pad * 0.8f, rowH - 4), Font.Height * 0.5f);
                g.FillPath(hl, path);
            }
            else if (i > _top)
            {
                g.DrawLine(line, pad, y, w - pad, y);
            }

            var lineH = Font.Height;
            var top1 = (int)(y + rowH / 2 - lineH * 1.05f);
            var top2 = (int)(y + rowH / 2 + lineH * 0.05f);

            // Izquierda: cuándo (o «En curso») y el modo.
            var when = r.Live ? "● " + L.T("En curso", "In progress") : When(r.Start);
            TextRenderer.DrawText(g, when, fontMain, new Rectangle((int)pad, top1, leftW, lineH + 2),
                r.Live ? Theme.Mint : Theme.Ink, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, r.Mode, fontSmall, new Rectangle((int)pad, top2, leftW, lineH + 2),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // Centro: puesto (pastilla) y eliminaciones / tesoros.
            if (r.Placement is { } place)
            {
                var text = Place(r);
                var size = TextRenderer.MeasureText(g, text, fontBadge, Size.Empty, TextFormatFlags.NoPadding);
                var bw = Math.Min(midW, size.Width + lineH);
                var badge = new RectangleF(midLeft, top1 - 1, bw, lineH + 2);
                var (bg, fg) = place == 1 ? (Color.FromArgb(60, Theme.Amber), Theme.Amber)
                    : place <= 10 ? (Color.FromArgb(40, Theme.Cyan), Theme.Cyan)
                    : (Theme.Panel, Theme.Ink);
                using (var b = new SolidBrush(bg))
                using (var path = Shape.Rounded(badge, badge.Height / 2))
                {
                    g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, text, fontBadge, Rectangle.Round(badge), fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            var extras = new List<string>();
            if (r.Eliminations is { } elim) extras.Add(L.F("{0} elim.", "{0} elim.", L.N(elim)));
            TextRenderer.DrawText(g, string.Join(" · ", extras), fontSmall, new Rectangle(midLeft, top2, midW, lineH + 2),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            // Derecha: cofres en grande; tesoros y token pirata delante.
            var chestColor = r.Live ? Theme.Mint : r.Chests == bestChests && r.Chests > 0 ? Theme.Amber : Theme.Ink;
            // Con compañeros, la cifra sube un poco y debajo va el total del equipo (un mínimo: «72+»).
            var teamText = r.TeamTotal is { } team ? L.F("equipo {0}+", "team {0}+", L.N(team)) : null;
            var bigRect = teamText == null
                ? new Rectangle(chestLeft, (int)y, bigW, (int)rowH)
                : new Rectangle(chestLeft - lineH * 2, (int)(y + rowH * 0.04f), bigW + lineH * 2, (int)(rowH * 0.62f));
            if (teamText != null)
            {
                TextRenderer.DrawText(g, teamText, fontSmall, new Rectangle(chestLeft - lineH * 3, (int)(y + rowH * 0.6f), bigW + lineH * 3, lineH + 2),
                    Theme.Muted, TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            TextRenderer.DrawText(g, L.N(r.Chests), fontBig, bigRect, chestColor,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            var marks = (r.PirateToken ? "★ " : "") + (r.Treasures > 0 ? OverlayModel.TreasureIcon + (r.Treasures > 1 ? L.N(r.Treasures) : "") : "");
            if (marks.Length > 0)
            {
                var numW = TextRenderer.MeasureText(g, L.N(r.Chests), fontBig, Size.Empty, TextFormatFlags.NoPadding).Width;
                var markRect = new Rectangle(chestRight - numW - lineH * 4, bigRect.Y, lineH * 4 - lineH / 3, bigRect.Height);
                TextRenderer.DrawText(g, marks, fontMain, markRect, r.PirateToken ? Theme.Amber : Theme.Rose,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
        }

        // Barra de desplazamiento fina cuando no caben todas.
        if (MaxTop > 0)
        {
            var trackX = Width - Font.Height * 0.35f;
            var thumbH = Math.Max(Font.Height * 1.5f, Height * VisibleRows / (float)_rows.Count);
            var thumbY = (Height - thumbH) * _top / MaxTop;
            using var thumb = new SolidBrush(Theme.Line);
            using var path = Shape.Rounded(new RectangleF(trackX - 2, thumbY + 2, 4, thumbH - 4), 2);
            g.FillPath(thumb, path);
        }
    }
}
