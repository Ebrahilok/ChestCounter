using System.Diagnostics;
using System.Drawing;
using System.Runtime.Versioning;
using System.Windows.Forms;

namespace ChestCounter;

/// <summary>
/// Ventana principal (640×460). Arriba: título, perfil, cuándo se actualizó y el botón «Actualizar». Debajo, una franja
/// con cuatro cifras (total, hoy, media, tesoros). El centro es la lista de partidas, la más reciente arriba; a su
/// derecha, los últimos 7 días y los récords (o el equipo, con «Compañeros» activado). Abajo, la franja del token
/// pirata / Hora del tesoro y los botones.
/// Todo lo demás (escaneo, overlay, actualizaciones) lo hace <see cref="Monitor"/> en segundo plano.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MainForm : Form
{
    private readonly Monitor _monitor;
    private readonly Store _store;
    private readonly Card _summary;
    private readonly Label _title;
    private bool? _treasureShown;
    private readonly Card _countCard;
    private readonly Label _countLabel;
    private readonly ToolTip _tips = new();
    private readonly System.Windows.Forms.Timer _clock = new() { Interval = 1000 };
    private readonly Label _profileName;
    private readonly ComboBox _profile;
    private readonly Label _status;
    private readonly PillButton _refresh;
    private DateTime? _refreshClickedUtc;
    private readonly Label _total, _today, _average, _treasures;
    private readonly Label _todayCap, _totalCap;
    private readonly GoalBar _goalBar;
    private readonly Label _matchesCount;
    private readonly PillButton _filterButton;
    private readonly PillButton _statsButton;
    // Filtro de la lista (null / false = sin filtrar).
    private string? _filterMode, _filterMate;
    private bool _filterWins, _filterTop10;
    // Avisos de récord y de meta cumplida (en la franja de abajo, unos segundos).
    private string? _flash;
    private DateTime _flashUntil;
    private int? _prevBest, _prevToday;
    private DateTime? _prevNewest;
    private readonly MatchList _matches;
    private readonly DailyChart _daily;
    private readonly Card _sideCard;
    private readonly Label _sideTitle;
    private readonly Label[] _sideNames = new Label[SideRows], _sideValues = new Label[SideRows];
    private const int SideRows = 4; // récords (3 filas) o equipo (tú + hasta 3 compañeros)
    private readonly Card _noticeCard;
    private readonly Label _notice;
    private readonly PillButton _update;
    private readonly PillButton _overlayButton, _reportButton, _settingsButton;
    private readonly Card _pirateCard;
    private readonly Label _pirateLabel;
    private MonitorState? _last;
    private string? _profileSig;
    private bool _ignoreProfileChange;
    private bool _autoCloseOffer;

    // Disposición base 640×460: todo escala proporcionalmente al redimensionar.
    private const int DesignW = 640, DesignH = 460;
    private const int Pad = 16, Gap = 10;
    private const int ListW = 382;                      // columna de la lista de partidas
    private const int SideX = Pad + ListW + Gap;        // columna derecha
    private const int SideW = DesignW - Pad - SideX;
    private const int BodyY = 146, BodyH = 216;
    private const int PirateY = BodyY + BodyH + 8, BottomY = PirateY + 40 + 8;
    private const int ButtonW = 84, ButtonGap = 8, Buttons = 4;

    public MainForm(Monitor monitor, Store store)
    {
        _monitor = monitor;
        _store = store;

        Text = L.T("Contador de cofres", "Chest Counter") + " v" + AppInfo.VersionText;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Font = Theme.Text();
        BackColor = Theme.Bg;
        ForeColor = Theme.Ink;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimumSize = new Size(480, 345);
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(DesignW, DesignH);
        Icon = AppIcon.Load();

        // ---- Cabecera: título y perfil a la izquierda; estado, «Actualizar» y corazón a la derecha ---------------
        _title = Lbl(this, L.T("CONTADOR DE COFRES", "CHEST COUNTER"), 8.5f, FontStyle.Bold, Theme.Cyan, new Point(Pad, 12), new Size(300, 18));
        _profileName = Lbl(this, "", 8.0f, FontStyle.Regular, Theme.Muted, new Point(Pad, 33), new Size(210, 18));
        _profile = new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            FlatStyle = FlatStyle.Flat,
            BackColor = Theme.Panel,
            ForeColor = Theme.Ink,
            Font = Theme.Text(),
            Location = new Point(Pad, 31),
            Size = new Size(200, 24),
            Visible = false,
        };
        _profile.SelectedIndexChanged += (_, _) =>
        {
            if (!_ignoreProfileChange && _profile.SelectedItem is ProfileItem item)
            {
                _monitor.SetProfile(item.Key);
            }
        };
        Controls.Add(_profile);

        // Cuenta atrás de la Hora del tesoro de hoy (solo el día que la hay y antes de que empiece).
        _countCard = new Card { Location = new Point(230, 31), Size = new Size(128, 22), Radius = 11, BackColor = Theme.TreasurePanel, Visible = false };
        _countLabel = Lbl(_countCard, "", 8.0f, FontStyle.Bold, Theme.Amber, new Point(0, 0), new Size(128, 22), ContentAlignment.MiddleCenter);
        Controls.Add(_countCard);

        _status = Lbl(this, "", 8.0f, FontStyle.Regular, Theme.Muted, new Point(364, 14), new Size(156, 34), ContentAlignment.MiddleRight);
        _refresh = new PillButton
        {
            Text = "⟳  " + L.T("Actualizar", "Refresh"), Location = new Point(526, 14), Size = new Size(90, 34), Radius = 10,
            BackColor = Theme.Panel, Font = Theme.Text(8.5f, FontStyle.Bold),
        };
        _refresh.Click += (_, _) =>
        {
            _refreshClickedUtc = DateTime.UtcNow;
            UpdateStatus();
            _monitor.RefreshNow();
        };
        _tips.SetToolTip(_refresh, L.T("Busca ahora partidas nuevas en tus replays", "Look for new matches in your replays now"));
        Controls.Add(_refresh);

        // Corazón: vuelve a abrir el mensaje de agradecimiento (por si se cerró sin leerlo).
        var thanks = new Label
        {
            Text = "♥", Location = new Point(DesignW - 22, 0), Size = new Size(22, 22), Font = Theme.Text(10f, FontStyle.Bold),
            ForeColor = Theme.Rose, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            AccessibleName = L.T("Un mensaje de Doofy", "A message from Doofy"), AccessibleRole = AccessibleRole.PushButton,
        };
        thanks.MouseEnter += (_, _) => thanks.ForeColor = Color.FromArgb(255, 180, 205);
        thanks.MouseLeave += (_, _) => thanks.ForeColor = Theme.Rose;
        thanks.Click += (_, _) => ThanksDialog.Open(this);
        _tips.SetToolTip(thanks, L.T("Un mensaje de Doofy", "A message from Doofy"));
        Controls.Add(thanks);

        // ---- Franja de resumen: cuatro cifras, la primera (total) más grande ------------------------------------
        _summary = new Card { Location = new Point(Pad, 62), Size = new Size(DesignW - 2 * Pad, 74), Radius = 14 };
        Controls.Add(_summary);
        var colW = _summary.Width / 4;
        _total = Lbl(_summary, "0", 22f, FontStyle.Bold, Theme.Cyan, new Point(14, 4), new Size(colW - 18, 42));
        _totalCap = Lbl(_summary, L.T("cofres en total", "chests in total"), 8.0f, FontStyle.Regular, Theme.Muted, new Point(17, 47), new Size(colW - 20, 16));
        _today = SummaryFigure(1, colW, Theme.Ink, L.T("hoy", "today"), out _todayCap);
        _goalBar = new GoalBar { Location = new Point(colW + 18, 64), Size = new Size(colW - 34, 5), Visible = false };
        _summary.Controls.Add(_goalBar);
        _average = SummaryFigure(2, colW, Theme.Ink, L.T("media por partida", "average per match"), out _);
        _treasures = SummaryFigure(3, colW, Theme.Rose, L.T("tesoros", "treasures"), out _);
        for (var i = 1; i < 4; i++)
        {
            _summary.Controls.Add(new Panel { BackColor = Theme.CardEdge, Location = new Point(i * colW, 16), Size = new Size(1, 42) });
        }

        // ---- Lista de partidas (lo principal) --------------------------------------------------------------
        var list = new Card { Location = new Point(Pad, BodyY), Size = new Size(ListW, BodyH), Radius = 14 };
        Lbl(list, L.T("Partidas", "Matches"), 9f, FontStyle.Bold, Theme.Ink, new Point(14, 9), new Size(80, 18));
        _matchesCount = Lbl(list, "", 8.0f, FontStyle.Regular, Theme.Muted, new Point(94, 10), new Size(ListW - 14 - 118 - 8 - 94, 16), ContentAlignment.TopRight);
        _filterButton = new PillButton { Text = L.T("Filtrar ▾", "Filter ▾"), Location = new Point(ListW - 14 - 118, 6), Size = new Size(118, 24), Radius = 9, Font = Theme.Text(8f, FontStyle.Bold) };
        _filterButton.Click += (_, _) => ShowFilterMenu();
        list.Controls.Add(_filterButton);
        _matches = new MatchList { Location = new Point(4, 32), Size = new Size(ListW - 8, BodyH - 38) };
        _matches.RowClicked += row => MatchDialog.Open(this, row, _store.ReportPath);
        list.Controls.Add(_matches);
        Controls.Add(list);

        // ---- Columna derecha: últimos 7 días y récords / equipo -----------------------------------------------
        var daily = new Card { Location = new Point(SideX, BodyY), Size = new Size(SideW, 112), Radius = 14 };
        Lbl(daily, L.T("Últimos 7 días", "Last 7 days"), 8.0f, FontStyle.Regular, Theme.Muted, new Point(13, 7), new Size(SideW - 26, 14));
        _daily = new DailyChart { Location = new Point(8, 24), Size = new Size(SideW - 16, 84), BackColor = Theme.Card, Font = Theme.Text(7f) };
        daily.Controls.Add(_daily);
        Controls.Add(daily);

        _sideCard = new Card { Location = new Point(SideX, BodyY + 120), Size = new Size(SideW, BodyH - 120), Radius = 14 };
        _sideTitle = Lbl(_sideCard, L.T("Récords", "Records"), 8.0f, FontStyle.Regular, Theme.Muted, new Point(13, 7), new Size(SideW - 26, 14));
        for (var i = 0; i < SideRows; i++)
        {
            var y = 26 + i * 16;
            _sideNames[i] = Lbl(_sideCard, "", 8.5f, FontStyle.Regular, Theme.Muted, new Point(13, y), new Size(SideW - 26 - 56, 16), ContentAlignment.MiddleLeft);
            _sideValues[i] = Lbl(_sideCard, "", 8.5f, FontStyle.Bold, Theme.Ink, new Point(SideW - 13 - 56, y), new Size(56, 16), ContentAlignment.MiddleRight);
        }
        Controls.Add(_sideCard);

        // ---- Token pirata / Hora del tesoro (franja) ------------------------------------------------------
        _pirateCard = new Card { Location = new Point(Pad, PirateY), Size = new Size(DesignW - 2 * Pad, 40), Radius = 12, BackColor = Theme.Panel };
        _pirateLabel = Lbl(_pirateCard, "", 8.0f, FontStyle.Bold, Theme.Muted, new Point(13, 0), new Size(_pirateCard.Width - 26, 40), ContentAlignment.MiddleCenter);
        _pirateCard.Cursor = _pirateLabel.Cursor = Cursors.Hand;
        _pirateCard.Click += (_, _) => Open(new Uri(_store.ReportPath).AbsoluteUri + "#tesoros");
        _pirateLabel.Click += (_, _) => Open(new Uri(_store.ReportPath).AbsoluteUri + "#tesoros");
        Controls.Add(_pirateCard);

        // ---- Fila inferior: aviso o actualización a la izquierda, botones a la derecha -------------------------
        var noticeW = DesignW - 2 * Pad - Buttons * ButtonW - Buttons * ButtonGap;
        _noticeCard = new Card { Location = new Point(Pad, BottomY), Size = new Size(noticeW, 38), Radius = 12, BackColor = Theme.Card, Visible = false };
        _notice = Lbl(_noticeCard, "", 8.0f, FontStyle.Regular, Theme.AccentSoft, new Point(12, 0), new Size(noticeW - 24, 38), ContentAlignment.MiddleLeft);
        _noticeCard.Click += (_, _) => { if (_autoCloseOffer) _monitor.KeepOpen(); };
        _notice.Click += (_, _) => { if (_autoCloseOffer) _monitor.KeepOpen(); };
        Controls.Add(_noticeCard);

        _update = new PillButton { Location = new Point(Pad, BottomY), Size = new Size(noticeW, 38), Radius = 12, Visible = false, BackColor = Theme.Accent, HoverColor = Theme.AccentHover, ForeColor = Theme.AccentInk };
        _update.Click += (_, _) => _monitor.InstallUpdate();
        Controls.Add(_update);

        _statsButton = Action(L.T("Estadísticas", "Stats"), DesignW - Pad - 4 * ButtonW - 3 * ButtonGap);
        _statsButton.Click += (_, _) => StatsDialog.Open(this, Filtered(_last?.Recent ?? Array.Empty<MatchRow>()), FilterText(), mate =>
        {
            ClearFilter();
            _filterMate = mate;
            ApplyFilter();
        });
        _tips.SetToolTip(_statsButton, L.T("Cofres según el puesto, por modo y con tus compañeros", "Chests by placement, by mode and with your teammates"));
        _overlayButton = Action(L.T("Overlay", "Overlay"), DesignW - Pad - 3 * ButtonW - 2 * ButtonGap);
        _overlayButton.Click += (_, _) => _monitor.ToggleOverlay();
        _reportButton = Action(L.T("Informe", "Report"), DesignW - Pad - 2 * ButtonW - ButtonGap);
        _reportButton.Click += (_, _) => Open(_store.ReportPath);
        _settingsButton = Action(L.T("Ajustes", "Settings"), DesignW - Pad - ButtonW);
        _settingsButton.Click += (_, _) =>
        {
            using var dialog = new SettingsForm(_monitor, _store);
            dialog.ShowDialog(this);
        };

        _clock.Tick += (_, _) =>
        {
            UpdateTreasureClock();
            UpdateStatus();
            if (_flash != null && DateTime.UtcNow > _flashUntil && _last != null)
            {
                _flash = null;
                Render(_last);
            }
        };
        _clock.Start();
        FormClosed += (_, _) => { _clock.Dispose(); _tips.Dispose(); };

        CaptureLayout();
        _scaleTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _scaleTimer.Tick += (_, _) => { _scaleTimer.Stop(); ApplyLayoutScale(); };

        monitor.Changed += OnChanged;
        Shown += (_, _) =>
        {
            if (_monitor.AutoMode)
            {
                WindowState = FormWindowState.Minimized; // abierto con Fortnite: el juego conserva la pantalla
            }
        };
    }

    /// <summary>
    /// Runs the action once, the first time the window is active and not minimized (right after showing, or when the
    /// player restores it if it was opened minimized with Fortnite). Deferred so the main window is drawn first.
    /// </summary>
    public void ShowOnceWhenVisible(Action action)
    {
        var done = false;
        void Try(object? sender, EventArgs e)
        {
            if (done || WindowState == FormWindowState.Minimized || !Visible)
            {
                return;
            }
            done = true;
            Activated -= Try;
            BeginInvoke(action);
        }
        Activated += Try;
    }

    private readonly Dictionary<Control, LayoutEntry> _layout = new();
    private System.Windows.Forms.Timer? _scaleTimer;
    private float _lastFx = 1, _lastFy = 1;

    private sealed class LayoutEntry
    {
        public Point Location;
        public Size Size;
        public Font? Font;
        public Font? Scaled;
    }

    private void CaptureLayout()
    {
        Capture(this);
        void Capture(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                _layout[c] = new LayoutEntry { Location = c.Location, Size = c.Size, Font = c.Font };
                Capture(c);
            }
        }
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (WindowState == FormWindowState.Minimized)
        {
            return;
        }
        _scaleTimer?.Stop();
        _scaleTimer?.Start();
    }

    /// <summary>Escala posiciones, tamaños y fuentes desde el diseño base. Las fuentes van en puntos: se dividen por el DPI para no contarlas dos veces.</summary>
    private void ApplyLayoutScale()
    {
        if (_layout.Count == 0 || ClientSize.Width <= 0 || ClientSize.Height <= 0)
        {
            return;
        }
        var fx = ClientSize.Width / (float)DesignW;
        var fy = ClientSize.Height / (float)DesignH;
        if (Math.Abs(fx - _lastFx) < 0.005 && Math.Abs(fy - _lastFy) < 0.005)
        {
            return;
        }
        _lastFx = fx;
        _lastFy = fy;
        // Durante restaurar/maximizar el tamaño puede ser transitorio: sin factor válido no se tocan las fuentes.
        var k = Math.Min(fx / (DeviceDpi / 96f), fy / (DeviceDpi / 96f));
        var scaleFonts = float.IsFinite(k) && k > 0.2 && k < 10;
        foreach (var (c, l) in _layout)
        {
            c.Location = new Point((int)Math.Round(l.Location.X * fx), (int)Math.Round(l.Location.Y * fy));
            c.Size = new Size((int)Math.Round(l.Size.Width * fx), (int)Math.Round(l.Size.Height * fy));
            if (scaleFonts && l.Font != null)
            {
                var nf = new Font(l.Font.FontFamily, l.Font.SizeInPoints * k, l.Font.Style);
                l.Scaled?.Dispose();
                l.Scaled = nf;
                c.Font = nf;
            }
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Theme.Chrome(Handle); // esquinas redondeadas de Windows 11 (el sistema las dibuja sin tocar botones ni bordes)
        _treasureShown = null; // ventana nueva: vuelve a aplicar el borde
        if (_last != null)
        {
            Render(_last);
        }
    }

    private void OnChanged(MonitorState state)
    {
        _last = state;
        if (!IsHandleCreated || IsDisposed)
        {
            return;
        }
        try
        {
            BeginInvoke(new Action(() => Render(state)));
        }
        catch (ObjectDisposedException)
        {
            // la ventana se está cerrando
        }
        catch (InvalidOperationException)
        {
            // todavía sin ventana
        }
    }

    private void Render(MonitorState s)
    {
        var st = s.Stats;
        var live = s.Recent.FirstOrDefault()?.Live == true;

        CheckCelebrations(s);

        // Franja de resumen: todo, o solo las partidas del filtro.
        var rows = Filtered(s.Recent);
        var filtered = FilterActive;
        var today = filtered ? rows.Where(r => r.Start.Date == DateTime.Now.Date).Sum(r => r.Counted) : st.Today;
        var finished = rows.Where(r => !r.Live).ToList();
        _total.Text = filtered ? L.N(rows.Sum(r => r.Counted)) : s.View.Total;
        _totalCap.Text = filtered ? L.T("cofres (filtro)", "chests (filtered)") : L.T("cofres en total", "chests in total");
        _total.ForeColor = filtered ? Theme.AccentSoft : Theme.Cyan;
        _today.Text = L.N(today);
        _average.Text = filtered
            ? finished.Count > 0 ? finished.Average(r => r.Counted).ToString("0.0", L.Culture) : "—"
            : st.Matches > 0 ? st.Average.ToString("0.0", L.Culture) : "—";
        _treasures.Text = $"{OverlayModel.TreasureIcon} {L.N(filtered ? rows.Sum(r => r.Treasures) : st.Treasures)}";

        // Meta diaria: barra bajo «hoy» (verde al cumplirla).
        var goal = s.DailyGoal;
        _goalBar.Visible = goal > 0 && !filtered;
        if (goal > 0 && !filtered)
        {
            _goalBar.Value = Math.Min(1f, (float)st.Today / goal);
            _todayCap.Text = st.Today >= goal
                ? L.F("hoy · ✓ meta {0}", "today · ✓ goal {0}", L.N(goal))
                : L.F("hoy · meta {0}", "today · goal {0}", L.N(goal));
            _todayCap.ForeColor = st.Today >= goal ? Theme.Mint : Theme.Muted;
        }
        else
        {
            _todayCap.Text = live ? L.T("hoy (con la actual)", "today (incl. current)") : L.T("hoy", "today");
            _todayCap.ForeColor = Theme.Muted;
        }

        // Lista de partidas.
        _matches.EmptyText = filtered
            ? L.T("Ninguna partida con este filtro.", "No matches with this filter.")
            : L.T("Aún no hay partidas. Juega una con los replays activados y aparecerá aquí.", "No matches yet. Play one with replays turned on and it will show up here.");
        _matches.SetRows(rows, s.CountsTeammates);
        _matchesCount.Text = filtered
            ? L.F("{0} de {1}", "{0} of {1}", L.N(rows.Count), L.N(s.Recent.Count))
            : st.HistoryMatches == 0 ? ""
            : L.Count(st.HistoryMatches, "partida", "partidas", "match", "matches");
        _tips.SetToolTip(_matchesCount, (st.HistorySince is { } since
            ? L.F("Guardadas desde el {0:dd/MM/yy}. Aunque Fortnite borre los replays, aquí no se pierden.", "Saved since {0:dd/MM/yy}. Even when Fortnite deletes the replays, they stay here.", since)
            : "") + (s.CountsTeammates
            ? "\n" + L.T("* Los cofres de cada día incluyen los de tus compañeros («Sumar sus cofres a tu total»).", "* Each day's chests include your teammates' (“Add their chests to your total”).")
            : ""));
        var filterText = FilterText();
        _filterButton.Text = filterText != null ? filterText + "  ✕" : L.T("Filtrar ▾", "Filter ▾");
        _filterButton.BackColor = filterText != null ? Theme.Accent : Theme.Panel;
        _filterButton.HoverColor = filterText != null ? Theme.AccentHover : Theme.PanelHover;
        _filterButton.ForeColor = filterText != null ? Theme.AccentInk : Theme.Ink;
        _tips.SetToolTip(_filterButton, filterText != null ? L.T("Clic para quitar el filtro", "Click to clear the filter") : L.T("Filtrar por modo, resultado o compañero", "Filter by mode, result or teammate"));

        _daily.SetDays(st.LastDays);
        if (s.Team != null) RenderTeam(s.Team); else RenderRecords(st);

        _overlayButton.Enabled = _monitor.OverlayEnabled;
        _overlayButton.Indicator = _monitor.OverlayVisible ? Theme.Green : Theme.Red;

        // Token pirata / Hora del tesoro.
        var pirateAlert = s.PirateTokenLive || st.LastMatch?.PirateToken == true;
        var now = DateTime.UtcNow;
        var treasureHour = TreasureHours.Active(now);
        _pirateLabel.Text = _flash ?? (pirateAlert
            ? (s.PirateTokenLive ? L.T("★ TOKEN PIRATA · Detectado en esta partida", "★ PIRATE TOKEN · Detected in this match")
                : L.T("★ TOKEN PIRATA · Detectado en la partida anterior", "★ PIRATE TOKEN · Detected in the previous match"))
                + "  ·  " + (s.TokenDetails ?? L.T("Ver detalles en Tesoros →", "View details in Treasures →"))
            : treasureHour != null
                ? L.T("★ HORA DEL TESORO · Más probabilidad de tesoros", "★ TREASURE HOUR · Better treasure odds")
                    + "  ·  " + L.T("Tienda pirata ×1,5 · Traje dorado ×7,5 · Vehículo ×10", "Pirate Shop ×1.5 · Gold outfit ×7.5 · Vehicle ×10")
            : TreasureHours.Next(now) is { } next
                ? L.F("Próxima Hora del tesoro: {0} · Ver Tesoros →", "Next Treasure Hour: {0} · View Treasures →", TreasureHours.When(next))
                : L.T("Sin token pirata detectado · Ver Tesoros →", "No pirate token detected · View Treasures →"));
        _pirateLabel.ForeColor = _flash != null ? Theme.Mint : pirateAlert || treasureHour != null ? Theme.Amber : Theme.Muted;
        RenderTreasureHour(treasureHour);

        // Perfil: nombre fijo con una sola cuenta, selector con varias.
        var signature = string.Join("|", s.Profiles.Select(p => p.Key));
        var several = s.Profiles.Count >= 2;
        _profile.Visible = several;
        _profileName.Visible = !several;
        _profileName.Text = s.Profiles.FirstOrDefault(p => p.Key == s.CurrentKey) is { } current
            ? $"{L.T("Perfil", "Profile")}: {current.Name}"
            : "";
        if (signature != _profileSig)
        {
            _profileSig = signature;
            _ignoreProfileChange = true;
            _profile.Items.Clear();
            foreach (var p in s.Profiles)
            {
                _profile.Items.Add(new ProfileItem(p.Key, Profiles.Describe(p)));
            }
            _ignoreProfileChange = false;
        }
        _ignoreProfileChange = true;
        for (var i = 0; i < _profile.Items.Count; i++)
        {
            if (_profile.Items[i] is ProfileItem item && item.Key == s.CurrentKey)
            {
                _profile.SelectedIndex = i;
                break;
            }
        }
        _ignoreProfileChange = false;

        // Aviso o actualización: uno u otro, abajo a la izquierda (los botones siguen visibles).
        _autoCloseOffer = s.AutoCloseOffer;
        _update.Visible = s.Update != null;
        if (s.Update != null)
        {
            _update.Text = L.F("Instalar la versión {0} (tienes la {1})", "Install version {0} (you have {1})",
                s.Update.Version.ToString(3), AppInfo.VersionText);
        }
        _notice.Text = s.Notice ?? "";
        _notice.Cursor = s.AutoCloseOffer ? Cursors.Hand : Cursors.Default;
        _noticeCard.Visible = s.Update == null && !string.IsNullOrEmpty(s.Notice);

        UpdateStatus();
    }

    // ---- Filtro de la lista ------------------------------------------------------------------------------

    private bool FilterActive => _filterMode != null || _filterMate != null || _filterWins || _filterTop10;

    private IReadOnlyList<MatchRow> Filtered(IReadOnlyList<MatchRow> rows) => !FilterActive ? rows : rows.Where(r =>
        (_filterMode == null || r.Mode == _filterMode)
        && (!_filterWins || r.Placement == 1)
        && (!_filterTop10 || r.Placement is <= 10)
        && (_filterMate == null || r.Detail.Mates?.Any(m => string.Equals(m.Name, _filterMate, StringComparison.OrdinalIgnoreCase)) == true)).ToList();

    /// <summary>Descripción corta del filtro («Escuadrón · victorias · con X»), o null sin filtro.</summary>
    private string? FilterText()
    {
        if (!FilterActive) return null;
        var parts = new List<string>();
        if (_filterWins) parts.Add(L.T("victorias", "wins"));
        if (_filterTop10) parts.Add(L.T("top 10", "top 10"));
        if (_filterMode != null) parts.Add(_filterMode);
        if (_filterMate != null) parts.Add(L.F("con {0}", "with {0}", _filterMate));
        return string.Join(" · ", parts);
    }

    private void ClearFilter()
    {
        _filterMode = _filterMate = null;
        _filterWins = _filterTop10 = false;
    }

    private void ApplyFilter()
    {
        if (_last != null) Render(_last);
    }

    /// <summary>Con filtro, el botón lo quita; sin filtro, abre el menú: resultado, modo y compañero.</summary>
    private void ShowFilterMenu()
    {
        if (FilterActive)
        {
            ClearFilter();
            ApplyFilter();
            return;
        }
        var rows = _last?.Recent ?? Array.Empty<MatchRow>();
        var menu = new ContextMenuStrip { Renderer = new DarkMenuRenderer(), BackColor = Theme.Panel, ForeColor = Theme.Ink, ShowImageMargin = false, Font = Theme.Text(9f) };
        ToolStripMenuItem Item(string text, Action act, bool enabled = true)
        {
            var it = new ToolStripMenuItem(text) { ForeColor = Theme.Ink, Enabled = enabled };
            it.Click += (_, _) => { act(); ApplyFilter(); };
            return it;
        }
        ToolStripLabel Head(string text) => new(text) { ForeColor = Theme.Muted, Font = Theme.Text(8f, FontStyle.Bold) };

        menu.Items.Add(Head(L.T("RESULTADO", "RESULT")));
        menu.Items.Add(Item(L.F("Solo victorias ({0})", "Wins only ({0})", rows.Count(r => r.Placement == 1)), () => _filterWins = true));
        menu.Items.Add(Item(L.F("Solo top 10 ({0})", "Top 10 only ({0})", rows.Count(r => r.Placement is <= 10)), () => _filterTop10 = true));

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Head(L.T("MODO", "MODE")));
        foreach (var mode in rows.GroupBy(r => r.Mode).OrderByDescending(g => g.Count()))
        {
            var name = mode.Key;
            menu.Items.Add(Item($"{name} ({mode.Count()})", () => _filterMode = name));
        }

        var mates = rows.SelectMany(r => r.Detail.Mates ?? (IEnumerable<MateDetail>)Array.Empty<MateDetail>())
            .GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => (Name: g.First().Name, Count: g.Count()))
            .OrderByDescending(m => m.Count).Take(15).ToList();
        if (mates.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            var with = new ToolStripMenuItem(L.T("Con compañero", "With teammate")) { ForeColor = Theme.Ink };
            with.DropDown.BackColor = Theme.Panel;
            ((ToolStripDropDownMenu)with.DropDown).ShowImageMargin = false;
            with.DropDown.Renderer = new DarkMenuRenderer();
            foreach (var (name, count) in mates)
            {
                with.DropDownItems.Add(Item(L.F("{0} ({1})", "{0} ({1})", name, count), () => _filterMate = name));
            }
            menu.Items.Add(with);
        }
        menu.Closed += (_, _) => BeginInvoke(new Action(menu.Dispose));
        menu.Show(_filterButton, new Point(0, _filterButton.Height));
    }

    /// <summary>
    /// Récord de partida o meta del día cumplida: un aviso en la franja de abajo durante 30 s. Solo con partidas
    /// nuevas mientras la ventana está abierta (no al arrancar).
    /// </summary>
    private void CheckCelebrations(MonitorState s)
    {
        var finished = s.Recent.Where(r => !r.Live).ToList();
        var newest = finished.FirstOrDefault();
        var best = finished.Count > 0 ? finished.Max(r => r.Counted) : 0;
        string? flash = null;
        if (_prevNewest != null && newest != null && newest.Start > _prevNewest && _prevBest is { } oldBest && newest.Counted > oldBest)
        {
            flash = L.F("🏆 ¡NUEVO RÉCORD!  {0} cofres en una partida (antes {1})", "🏆 NEW RECORD!  {0} chests in one match (was {1})", L.N(newest.Counted), L.N(oldBest));
        }
        else if (s.DailyGoal > 0 && _prevToday is { } before && before < s.DailyGoal && s.Stats.Today >= s.DailyGoal)
        {
            flash = L.F("🎯 ¡META DE HOY CUMPLIDA!  {0} de {1} cofres", "🎯 TODAY'S GOAL REACHED!  {0} of {1} chests", L.N(s.Stats.Today), L.N(s.DailyGoal));
        }
        if (flash != null)
        {
            _flash = flash;
            _flashUntil = DateTime.UtcNow.AddSeconds(30);
        }
        _prevNewest = newest?.Start;
        _prevBest = best;
        _prevToday = s.Stats.Today;
    }

    /// <summary>
    /// Texto de estado junto a «Actualizar»: buscando, resultado de la búsqueda pedida (unos segundos), partida en
    /// directo o hace cuánto se comprobaron los replays por última vez. Se llama cada segundo.
    /// </summary>
    private void UpdateStatus()
    {
        var s = _last;
        var now = DateTime.UtcNow;
        string text;
        var color = Theme.Muted;
        var waiting = _refreshClickedUtc is { } clicked && (s?.RefreshResult is not { } r0 || r0.AtUtc < clicked)
            && now - clicked < TimeSpan.FromMinutes(5); // por si acaso: nunca se queda bloqueado
        _refresh.Enabled = !waiting;
        if (waiting)
        {
            text = L.T("Buscando partidas nuevas…", "Looking for new matches…");
            color = Theme.AccentSoft;
        }
        else if (s?.RefreshResult is { } r && _refreshClickedUtc != null && now - r.AtUtc < TimeSpan.FromSeconds(6))
        {
            text = r.Added == 0
                ? L.T("Sin partidas nuevas", "No new matches")
                : "+" + L.Count(r.Added, "partida nueva", "partidas nuevas", "new match", "new matches");
            color = r.Added > 0 ? Theme.Mint : Theme.Ink;
        }
        else if (s?.Recent.FirstOrDefault()?.Live == true)
        {
            text = "● " + L.T("Partida en curso", "Match in progress");
            color = Theme.Mint;
        }
        else if (s?.CheckedUtc is { } checkedAt)
        {
            text = L.F("Actualizado {0}", "Updated {0}", Ago(now - checkedAt));
        }
        else
        {
            text = L.T("Leyendo replays…", "Reading replays…");
        }
        if (_status.Text != text) _status.Text = text;
        if (_status.ForeColor != color) _status.ForeColor = color;

        var tip = s?.CheckedUtc is { } c
            ? L.F("Última comprobación de replays: {0:HH:mm:ss}", "Last replay check: {0:HH:mm:ss}", c.ToLocalTime())
                + (s.Recent.FirstOrDefault(m => !m.Live) is { } lastMatch
                    ? "\n" + L.F("Última partida: {0}", "Last match: {0}", L.DateTimeShort(lastMatch.Start))
                    : "")
                + "\n" + L.T("Se comprueba sola cada minuto y al terminar cada partida.", "Checked automatically every minute and when each match ends.")
            : "";
        if (_tips.GetToolTip(_status) != tip) _tips.SetToolTip(_status, tip);
    }

    /// <summary>"ahora mismo", "hace 12 s", "hace 3 min", "hace 2 h".</summary>
    private static string Ago(TimeSpan t)
    {
        if (t < TimeSpan.FromSeconds(5)) return L.T("ahora mismo", "just now");
        if (t < TimeSpan.FromMinutes(1)) return L.F("hace {0} s", "{0} s ago", (int)t.TotalSeconds);
        if (t < TimeSpan.FromHours(1)) return L.F("hace {0} min", "{0} min ago", (int)t.TotalMinutes);
        return L.F("hace {0} h", "{0} h ago", (int)t.TotalHours);
    }

    /// <summary>Hora del tesoro: título dorado con la cuenta atrás, borde dorado en la franja de resumen, la del token y la ventana.</summary>
    private void RenderTreasureHour(TreasureHours.Window? hour)
    {
        var on = hour != null;
        UpdateTreasureClock();
        if (_treasureShown == on) return;
        _treasureShown = on;
        _title.ForeColor = on ? Theme.Amber : Theme.Cyan;
        _summary.EdgeColor = _pirateCard.EdgeColor = on ? Theme.Amber : null;
        _pirateCard.BackColor = _pirateLabel.BackColor = on ? Theme.TreasurePanel : Theme.Panel;
        if (IsHandleCreated) Theme.Border(Handle, on ? Theme.Amber : null);
    }

    /// <summary>
    /// Título y cuenta atrás, cada segundo. Durante la hora el título dorado cuenta el tiempo que le queda. El día que hay
    /// Hora del tesoro y aún no ha empezado, la píldora junto al perfil cuenta hacia ella.
    /// </summary>
    private void UpdateTreasureClock()
    {
        var now = DateTime.UtcNow;
        var active = TreasureHours.Active(now);
        var next = active == null ? TreasureHours.Next(now) : null;
        if (next != null && next.StartLocal.Date != DateTime.Now.Date)
        {
            next = null; // no es hoy
        }
        var title = active != null
            ? L.T("★ HORA DEL TESORO", "★ TREASURE HOUR") + " · " + L.F("quedan {0}", "{0} left", TreasureHours.Countdown(active.EndUtc - now))
            : L.T("CONTADOR DE COFRES", "CHEST COUNTER");
        if (_title.Text != title) _title.Text = title;
        _countCard.Visible = next != null;
        if (next != null)
        {
            var text = L.F("Tesoro en {0}", "Treasure in {0}", TreasureHours.Countdown(next.StartUtc - now));
            if (_countLabel.Text != text) _countLabel.Text = text;
            var tip = L.F("Próxima Hora del tesoro: {0}", "Next Treasure Hour: {0}", TreasureHours.When(next));
            if (_tips.GetToolTip(_countLabel) != tip) _tips.SetToolTip(_countLabel, tip);
        }
    }

    /// <summary>Tarjeta derecha sin «Compañeros»: los récords, en filas de texto (nombre a la izquierda, cifra a la derecha).</summary>
    private void RenderRecords(ChestStats st)
    {
        _sideTitle.Text = L.T("Récords", "Records");
        _sideTitle.ForeColor = Theme.Muted;
        var rows = new List<(string Name, string Value, Color Color)>
        {
            (st.BestDay is { } bd ? L.F("Mejor día ({0:dd/MM})", "Best day ({0:dd/MM})", bd) : L.T("Mejor día", "Best day"),
                st.BestDay != null ? L.N(st.BestDayChests) : "—", Theme.Amber),
            (L.T("Mejor partida", "Best match"), st.Matches > 0 ? L.N(st.BestMatch) : "—", Theme.Amber),
            (L.T("Partidas jugadas", "Matches played"), L.N(st.HistoryMatches), Theme.Ink),
        };
        for (var i = 0; i < SideRows; i++)
        {
            var has = i < rows.Count;
            _sideNames[i].Text = has ? rows[i].Name : "";
            _sideNames[i].ForeColor = Theme.Muted;
            _sideValues[i].Text = has ? rows[i].Value : "";
            _sideValues[i].ForeColor = has ? rows[i].Color : Theme.Ink;
        }
    }

    /// <summary>
    /// Tarjeta derecha con «Compañeros»: tus cofres y los vistos de cada compañero en la partida actual o la última.
    /// Los de los compañeros son un mínimo (el replay solo tiene lo que pasa cerca de ti): se marcan con «+».
    /// </summary>
    private void RenderTeam(TeamSummary team)
    {
        _sideTitle.Text = team.Live ? L.T("Equipo · partida actual", "Team · current match") : L.T("Equipo · última partida", "Team · last match");
        _sideTitle.ForeColor = team.Live ? Theme.Mint : Theme.Muted;
        var message = team.Unknown
            ? L.T("Sin datos de equipo", "No team data")
            : team.Mates.Count == 0 ? L.T("Partida en solitario", "Solo match") : null;
        for (var i = 0; i < SideRows; i++)
        {
            string name = "", value = "";
            if (i == 0)
            {
                name = L.T("Tú", "You");
                value = L.N(team.Mine);
            }
            else if (message != null)
            {
                name = i == 1 ? message : "";
            }
            else if (i - 1 < team.Mates.Count)
            {
                name = team.Mates[i - 1].Name;
                value = L.N(team.Mates[i - 1].Chests) + "+";
            }
            _sideNames[i].Text = name;
            _sideNames[i].ForeColor = i == 0 ? Theme.Ink : Theme.Muted;
            _sideValues[i].Text = value;
            _sideValues[i].ForeColor = i == 0 ? Theme.Amber : Theme.Ink;
        }
        _tips.SetToolTip(_sideCard, L.T("«+»: como mínimo. Tu replay solo tiene lo que pasa cerca de ti.",
            "“+”: at least. Your replay only has what happens near you."));
    }

    /// <summary>Cifra de la franja de resumen (columna <paramref name="col"/>): número arriba, texto debajo.</summary>
    private Label SummaryFigure(int col, int colW, Color color, string caption, out Label captionLabel)
    {
        var x = col * colW + 16;
        var value = Lbl(_summary, "—", 17f, FontStyle.Bold, color, new Point(x, 12), new Size(colW - 24, 32));
        captionLabel = Lbl(_summary, caption, 8.0f, FontStyle.Regular, Theme.Muted, new Point(x + 2, 47), new Size(colW - 24, 16));
        return value;
    }

    private PillButton Action(string text, int x)
    {
        var b = new PillButton { Text = text, Location = new Point(x, BottomY), Size = new Size(ButtonW, 38), Radius = 11 };
        Controls.Add(b);
        return b;
    }

    private static Label Lbl(Control parent, string text, float size, FontStyle style, Color color, Point at, Size sz, ContentAlignment align = ContentAlignment.TopLeft)
    {
        var l = new FitLabel
        {
            Text = text,
            Font = Theme.Text(size, style),
            ForeColor = color,
            BackColor = parent.BackColor,
            Location = at,
            Size = sz,
            TextAlign = align,
        };
        parent.Controls.Add(l);
        return l;
    }

    private static void Open(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })?.Dispose();
        }
        catch
        {
            // el informe aún no existe o no hay navegador: nada que hacer
        }
    }

    private sealed record ProfileItem(string Key, string Text)
    {
        public override string ToString() => Text;
    }
}
