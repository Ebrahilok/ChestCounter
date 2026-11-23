using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ChestCounter;

/// <summary>
/// Lista de partidas de la ventana principal, agrupada por día y la más reciente arriba. Cada día empieza con una
/// cabecera («Hoy · 6 partidas · 381 cofres»); cada partida muestra la hora y el modo (izquierda), el puesto y las
/// eliminaciones (centro) y tus cofres en grande (derecha). La partida en curso va arriba, en verde.
/// Se desplaza con la rueda del ratón. Todas las medidas salen de la fuente, que la ventana escala al redimensionar.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MatchList : Control
{
    /// <summary>Un elemento de la lista: cabecera de día (Row null) o partida.</summary>
    private sealed record Item(MatchRow? Row, DateTime Day, int Matches, int Chests);

    private List<Item> _items = new();
    private float _scroll;    // píxeles desplazados
    private int _hover = -1;  // índice en _items (solo partidas)
    private bool _countsTeam;
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

    /// <summary>Clic en una partida (abre su detalle).</summary>
    public event Action<MatchRow>? RowClicked;

    /// <summary>Texto cuando no hay partidas (sin partidas, o el filtro no deja ninguna).</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string EmptyText { get; set; } = "";

    /// <param name="countsTeam">«Sumar sus cofres a tu total»: la cabecera del día lo dice.</param>
    public void SetRows(IReadOnlyList<MatchRow> rows, bool countsTeam, bool resetScroll = false)
    {
        _countsTeam = countsTeam;
        var items = new List<Item>();
        foreach (var day in rows.GroupBy(r => r.Start.Date)) // las filas ya vienen de la más nueva a la más antigua
        {
            var list = day.ToList();
            items.Add(new Item(null, day.Key, list.Count(r => !r.Pending), list.Sum(r => r.Counted))); // la partida sin cifras aún no cuenta
            items.AddRange(list.Select(r => new Item(r, day.Key, 0, 0)));
        }
        _items = items;
        if (resetScroll) _scroll = 0;
        _scroll = Math.Clamp(_scroll, 0, MaxScroll);
        _hover = -1;
        Invalidate();
    }

    private float RowHeight => Font.Height * 2.75f;
    private float HeaderHeight => Font.Height * 1.9f;
    private float HeightOf(Item i) => i.Row == null ? HeaderHeight : RowHeight;
    private float ContentHeight => _items.Sum(HeightOf);
    private float MaxScroll => Math.Max(0, ContentHeight - Height);

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        var scroll = Math.Clamp(_scroll - Math.Sign(e.Delta) * RowHeight * 1.5f, 0, MaxScroll);
        if (Math.Abs(scroll - _scroll) > 0.5f)
        {
            _scroll = scroll;
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

    private int _pressed = -1;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        _pressed = e.Button == MouseButtons.Left ? ItemAt(e.Y) : -1;
        base.OnMouseDown(e);
    }

    /// <summary>Clic = pulsar y soltar sobre la misma partida.</summary>
    protected override void OnMouseUp(MouseEventArgs e)
    {
        var i = ItemAt(e.Y);
        if (e.Button == MouseButtons.Left && i >= 0 && i == _pressed && _items[i].Row is { Pending: false } row)
        {
            _tip.Hide(this);
            RowClicked?.Invoke(row);
        }
        _pressed = -1;
        base.OnMouseUp(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _tip.Dispose();
        }
        base.Dispose(disposing);
    }

    private int ItemAt(float y)
    {
        var top = -_scroll;
        for (var i = 0; i < _items.Count; i++)
        {
            var h = HeightOf(_items[i]);
            if (y >= top && y < top + h) return i;
            top += h;
        }
        return -1;
    }

    private void UpdateHover(Point p)
    {
        var i = ItemAt(p.Y);
        var hover = i >= 0 && _items[i].Row is { Pending: false } ? i : -1;
        Cursor = hover >= 0 ? Cursors.Hand : Cursors.Default;
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

    /// <summary>"Hoy", "Ayer", "Domingo" (esta semana) o "Domingo 9/11".</summary>
    private static string DayName(DateTime day)
    {
        var days = (DateTime.Now.Date - day.Date).Days;
        var name = L.Culture.DateTimeFormat.GetDayName(day.DayOfWeek);
        name = name.Length > 0 ? char.ToUpper(name[0], L.Culture) + name[1..] : name;
        return days switch
        {
            0 => L.T("Hoy", "Today"),
            1 => L.T("Ayer", "Yesterday"),
            > 1 and < 7 => name,
            _ => $"{name} {day.ToString(L.En ? "M/d" : "d/M", L.Culture)}",
        };
    }

    private static string Place(MatchRow r)
    {
        if (r.Placement is not { } p) return "";
        if (p == 1) return L.T("Victoria", "Victory");
        var text = "#" + L.N(p);
        if (r.Players is { } n) text += "/" + L.N(n);
        return text;
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(BackColor);

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_items.Count == 0)
        {
            TextRenderer.DrawText(g, EmptyText, Font, ClientRectangle, Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }

        using var fontMain = new Font(Font, FontStyle.Bold);
        using var fontSmall = new Font(Font.FontFamily, Font.SizeInPoints * 0.88f, FontStyle.Regular, GraphicsUnit.Point);
        using var fontHead = new Font(Font.FontFamily, Font.SizeInPoints * 0.88f, FontStyle.Bold, GraphicsUnit.Point);
        using var fontBig = new Font(Font.FontFamily, Font.SizeInPoints * 1.65f, FontStyle.Bold, GraphicsUnit.Point);
        using var line = new Pen(Theme.CardEdge, 1f);

        const TextFormatFlags one = TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        var rowH = RowHeight;
        var lineH = Font.Height;
        var pad = lineH * 0.6f;
        var w = Width - (MaxScroll > 0 ? lineH * 0.5f : 0f); // hueco para la barra de desplazamiento
        var bigW = TextRenderer.MeasureText("8888", fontBig).Width;
        var chestRight = (int)(w - pad);
        var chestLeft = chestRight - bigW;
        var midW = (int)(w * 0.24f);
        var midLeft = chestLeft - (int)pad - midW;
        var leftW = midLeft - (int)pad * 2;
        var bestChests = _items.Where(i => i.Row is { Live: false }).Select(i => i.Row!.Chests).DefaultIfEmpty(0).Max();

        var y = -_scroll;
        for (var i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            var h = HeightOf(item);
            if (y > Height) break;
            if (y + h < 0)
            {
                y += h;
                continue;
            }

            if (item.Row == null)
            {
                // Cabecera del día: nombre a la izquierda, partidas y cofres a la derecha.
                var top = (int)(y + h - lineH * 1.25f);
                TextRenderer.DrawText(g, DayName(item.Day).ToUpper(L.Culture), fontHead, new Rectangle((int)pad, top, (int)(w / 2), lineH + 2), Theme.Cyan, TextFormatFlags.Left | one);
                var summary = L.Count(item.Matches, "partida", "partidas", "match", "matches") + "  ·  "
                    + L.Count(item.Chests, "cofre", "cofres", "chest", "chests")
                    + (_countsTeam ? "*" : ""); // * = con los del equipo (lo explica el tooltip de la ventana)
                TextRenderer.DrawText(g, summary, fontSmall, new Rectangle((int)(w / 2), top, chestRight - (int)(w / 2), lineH + 2), Theme.Muted, TextFormatFlags.Right | one);
                y += h;
                continue;
            }

            var r = item.Row;
            if (i == _hover || r.Live)
            {
                using var hl = new SolidBrush(r.Live ? Color.FromArgb(26, Theme.Mint) : Theme.Panel);
                using var path = Shape.Rounded(new RectangleF(pad * 0.4f, y + 2, w - pad * 0.8f, rowH - 4), lineH * 0.5f);
                g.FillPath(hl, path);
            }
            else if (i > 0 && _items[i - 1].Row != null)
            {
                g.DrawLine(line, pad, y, w - pad, y);
            }

            var top1 = (int)(y + rowH / 2 - lineH * 1.05f);
            var top2 = (int)(y + rowH / 2 + lineH * 0.05f);

            // Izquierda: hora (o «En curso») y modo.
            var when = r.Live ? "● " + L.T("En curso", "In progress") : r.Start.ToString("HH:mm", L.Culture);
            TextRenderer.DrawText(g, when, fontMain, new Rectangle((int)pad, top1, leftW, lineH + 2), r.Live ? Theme.Mint : Theme.Ink, TextFormatFlags.Left | one);
            TextRenderer.DrawText(g, r.Mode, fontSmall, new Rectangle((int)pad, top2, leftW, lineH + 2), Theme.Muted, TextFormatFlags.Left | one);

            // Centro: puesto (pastilla) y eliminaciones.
            if (r.Placement is { } place)
            {
                var text = Place(r);
                var size = TextRenderer.MeasureText(g, text, fontHead, Size.Empty, TextFormatFlags.NoPadding);
                var badge = new RectangleF(midLeft, top1 - 1, Math.Min(midW, size.Width + lineH), lineH + 2);
                var (bg, fg) = place == 1 ? (Color.FromArgb(60, Theme.Amber), Theme.Amber)
                    : place <= 10 ? (Color.FromArgb(40, Theme.Cyan), Theme.Cyan)
                    : (Theme.Panel, Theme.Ink);
                using (var b = new SolidBrush(bg))
                using (var path = Shape.Rounded(badge, badge.Height / 2))
                {
                    g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, text, fontHead, Rectangle.Round(badge), fg, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | one);
            }
            if (r.Eliminations is { } elim)
            {
                TextRenderer.DrawText(g, L.F("{0} elim.", "{0} elim.", L.N(elim)), fontSmall, new Rectangle(midLeft, top2, midW, lineH + 2), Theme.Muted, TextFormatFlags.Left | one);
            }

            // Derecha: cofres en grande (con compañeros, debajo el total del equipo); tesoros y token pirata delante.
            var chestColor = r.Live ? Theme.Mint : r.Chests == bestChests && r.Chests > 0 ? Theme.Amber : Theme.Ink;
            var teamText = r.TeamTotal is { } team ? L.F("equipo {0}+", "team {0}+", L.N(team)) : null;
            var bigRect = teamText == null
                ? new Rectangle(chestLeft, (int)y, bigW, (int)rowH)
                : new Rectangle(chestLeft - lineH * 2, (int)(y + rowH * 0.04f), bigW + lineH * 2, (int)(rowH * 0.62f));
            if (teamText != null)
            {
                TextRenderer.DrawText(g, teamText, fontSmall, new Rectangle(chestLeft - lineH * 3, (int)(y + rowH * 0.6f), bigW + lineH * 3, lineH + 2),
                    Theme.Muted, TextFormatFlags.Right | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            TextRenderer.DrawText(g, r.Pending ? "…" : L.N(r.Chests), fontBig, bigRect, chestColor, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            var marks = (r.PirateToken ? "★ " : "") + (r.Treasures > 0 ? OverlayModel.TreasureIcon + (r.Treasures > 1 ? L.N(r.Treasures) : "") : "");
            if (marks.Length > 0)
            {
                var numW = TextRenderer.MeasureText(g, L.N(r.Chests), fontBig, Size.Empty, TextFormatFlags.NoPadding).Width;
                var markRect = new Rectangle(chestRight - numW - lineH * 4, bigRect.Y, lineH * 4 - lineH / 3, bigRect.Height);
                TextRenderer.DrawText(g, marks, fontMain, markRect, r.PirateToken ? Theme.Amber : Theme.Rose,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
            }
            y += h;
        }

        // Barra de desplazamiento fina cuando no cabe todo.
        if (MaxScroll > 0)
        {
            var content = ContentHeight;
            var trackX = Width - lineH * 0.35f;
            var thumbH = Math.Max(lineH * 1.5f, Height * Height / content);
            var thumbY = (Height - thumbH) * _scroll / MaxScroll;
            using var thumb = new SolidBrush(Theme.Line);
            using var path = Shape.Rounded(new RectangleF(trackX - 2, thumbY + 2, 4, thumbH - 4), 2);
            g.FillPath(thumb, path);
        }
    }
}
