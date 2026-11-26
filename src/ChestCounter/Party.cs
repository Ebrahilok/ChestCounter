using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChestCounter;

/// <summary>Un miembro de la party, como lo devuelve el servidor.</summary>
public sealed class PartyMember
{
    public string Id { get; set; } = "";
    public string EpicId { get; set; } = "";
    public string Name { get; set; } = "";
    public DateTime JoinedAt { get; set; }
    public DateTime? LastSync { get; set; }
}

/// <summary>Una partida que un miembro subió desde su propio replay: sus cofres exactos, no un mínimo.</summary>
public sealed class PartyMatch
{
    public string MemberId { get; set; } = "";
    public string SessionId { get; set; } = "";
    public DateTime? StartUtc { get; set; }
    public int Chests { get; set; }
    public Dictionary<string, int> Kinds { get; set; } = new();
    public string? Playlist { get; set; }
    public int? Placement { get; set; }
}

/// <summary>Lo guardado en party.json: la party, la clave de este PC y lo descargado de los demás.</summary>
public sealed class PartyState
{
    public string Server { get; set; } = Party.DefaultServer;
    public string? Code { get; set; }
    public string? MemberId { get; set; }
    public string? Secret { get; set; }
    /// <summary>Cuenta de Epic con la que se entró (sus partidas son las que se suben).</summary>
    public string? MyEpicId { get; set; }
    public string? MyName { get; set; }
    /// <summary>Hora del servidor de la última sincronización: la siguiente solo trae lo cambiado desde entonces.</summary>
    public string? Since { get; set; }
    public DateTime? LastSyncUtc { get; set; }
    public List<PartyMember> Members { get; set; } = new();
    public List<PartyMatch> Matches { get; set; } = new();
    /// <summary>Partidas propias ya subidas (session id → firma): solo se vuelve a subir lo que cambia.</summary>
    public Dictionary<string, string> Uploaded { get; set; } = new();

    [JsonIgnore]
    public bool Joined => Code != null && MemberId != null && Secret != null;
}

/// <summary>Un miembro en la ventana de la party.</summary>
public sealed record PartyMemberView(string Name, bool IsMe, DateTime? LastSync, int Today, int Total, int Matches, int Together);

/// <summary>Estado de la party para la ventana (null = sin party).</summary>
public sealed record PartyView(string Code, IReadOnlyList<PartyMemberView> Members, DateTime? LastSyncUtc, string? Error, bool Syncing)
{
    /// <summary>Partidas en las que jugaron juntos al menos dos miembros de la party, la más reciente primero.</summary>
    public IReadOnlyList<PartyGame> Games { get; init; } = Array.Empty<PartyGame>();
}

/// <summary>
/// Un jugador en una partida de la party. <paramref name="Exact"/>: cofres de su propio replay (miembro que la subió);
/// si no, lo que se le vio abrir desde tu replay (un mínimo: un random, o un miembro que aún no la subió).
/// </summary>
public sealed record PartyGamePlayer(string Name, int Chests, bool IsMe, bool Exact, bool InParty);

/// <summary>Una partida jugada juntos: cuándo, modo, puesto y los cofres de cada uno.</summary>
public sealed record PartyGame(DateTime StartUtc, string Mode, int? Placement, IReadOnlyList<PartyGamePlayer> Players)
{
    public int Total => Players.Sum(p => p.Chests);
    public bool Exact => Players.All(p => p.Exact);
}

/// <summary>
/// Modo party: cada miembro sube a un servidor común los cofres que <b>él</b> abrió según <b>su propio</b> replay
/// (exactos), y descarga los de los demás. En las partidas en las que jugasteis juntos, esos números sustituyen a los
/// «N+» que se ven desde tu replay (que solo tiene lo que pasa cerca de ti). Los compañeros que no están en la party
/// siguen con su mínimo observado.
/// Las llamadas de red van en segundo plano; los datos (TrackerData) solo se tocan desde el hilo de vigilancia en
/// <see cref="Tick"/>.
/// </summary>
public sealed class Party
{
    public const string DefaultServer = "https://chestcounter-party.vercel.app";
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SameMatchWindow = TimeSpan.FromSeconds(90);
    private const int ChunkSize = 500;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private readonly Store _store;
    private readonly object _gate = new();
    private PartyState _state;
    private Task? _inFlight;
    private DateTime _nextSync = DateTime.MinValue;
    private DateTime _nextAnnotate = DateTime.MinValue;
    private volatile bool _annotate = true;
    private volatile string? _error;
    private Dictionary<string, int> _together = new();

    /// <summary>
    /// Compañeros de tus partidas sin número de la party (randoms, o miembros que aún no la subieron), por id de
    /// sesión: así la pestaña «Partidas juntos» también los muestra, con su mínimo observado.
    /// </summary>
    private Dictionary<string, List<(string? MemberId, string Name, int Seen)>> _unresolved = new();
    private int _version;

    /// <summary>Cambia cada vez que termina una sincronización o se entra / sale: la ventana se vuelve a pintar.</summary>
    public int Version => Volatile.Read(ref _version);

    /// <summary>Terminó una sincronización (desde un hilo del pool): el Monitor despierta para aplicarla.</summary>
    public event Action? Changed;

    public Party(Store store)
    {
        _store = store;
        _state = store.LoadParty() ?? new PartyState();
    }

    public bool Joined { get { lock (_gate) return _state.Joined; } }

    /// <summary>Pide sincronizar en el próximo ciclo (partida nueva, botón «Actualizar»).</summary>
    public void Kick()
    {
        _nextSync = DateTime.MinValue;
        _annotate = true;
    }

    // ---- Crear / unirse / salir (desde la ventana) -----------------------------------------------------------------

    public Task<string?> CreateAsync(string epicId, string name) =>
        EnterAsync("api/create", new { epicId, name }, epicId, name);

    public Task<string?> JoinAsync(string code, string epicId, string name) =>
        EnterAsync("api/join", new { code = code.Trim().ToUpperInvariant(), epicId, name }, epicId, name);

    private async Task<string?> EnterAsync(string path, object body, string epicId, string name)
    {
        string server;
        lock (_gate) server = _state.Server;
        var (result, error, _) = await PostAsync<EnterResult>(server, path, body);
        if (result == null)
        {
            return error;
        }
        lock (_gate)
        {
            _state = new PartyState
            {
                Server = server, Code = result.Party.Code, MemberId = result.MemberId, Secret = result.Secret,
                MyEpicId = epicId, MyName = name, Members = result.Party.Members,
            };
            SaveState();
        }
        _error = null;
        Kick();
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
        return null;
    }

    /// <summary>Sale de la party (sus partidas subidas se borran del servidor). Sin conexión, se sale igual en este PC.</summary>
    public async Task<string?> LeaveAsync()
    {
        PartyState s;
        lock (_gate) s = _state;
        if (!s.Joined) return null;
        var (_, error, status) = await PostAsync<JsonElement>(s.Server, "api/leave", new { memberId = s.MemberId, secret = s.Secret });
        if (error != null && status is not 401)
        {
            return error;
        }
        lock (_gate)
        {
            _state = new PartyState { Server = s.Server };
            SaveState();
        }
        _error = null;
        _together = new();
        Interlocked.Increment(ref _version);
        Changed?.Invoke();
        return null;
    }

    // ---- Hilo de vigilancia --------------------------------------------------------------------------------------

    /// <summary>
    /// Llamado en cada vuelta del hilo de vigilancia (el único que toca <paramref name="data"/>): lanza la sincronización
    /// cuando toca y aplica lo descargado a los compañeros de tus partidas. Devuelve true si cambió algún número.
    /// </summary>
    public bool Tick(TrackerData data)
    {
        PartyState s;
        lock (_gate) s = _state;
        if (!s.Joined)
        {
            return false;
        }
        var changed = false;
        // También cada 30 s: un replay reanalizado trae sus compañeros sin el número de la party.
        if (_annotate || DateTime.UtcNow >= _nextAnnotate)
        {
            _annotate = false;
            _nextAnnotate = DateTime.UtcNow.AddSeconds(30);
            changed = Annotate(data, s);
        }
        if ((_inFlight == null || _inFlight.IsCompleted) && DateTime.UtcNow >= _nextSync)
        {
            _nextSync = DateTime.UtcNow + Interval;
            var uploads = OwnMatches(data, s);
            _inFlight = Task.Run(() => SyncAsync(s, uploads));
        }
        return changed;
    }

    /// <summary>Tus partidas que aún no están en el servidor (o que cambiaron, por ejemplo al reanalizarlas).</summary>
    private static List<(string Sig, UploadMatch Body)> OwnMatches(TrackerData data, PartyState s)
    {
        var list = new List<(string, UploadMatch)>();
        foreach (var m in data.Matches)
        {
            if (!string.Equals(Profiles.Key(m), s.MyEpicId, StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(m.SessionId))
            {
                continue;
            }
            var kinds = OwnKinds(m);
            var start = m.MatchStartUtc is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : (DateTime?)null;
            var sig = $"{m.ChestsOpened}|{start:O}|{m.Placement}|{m.Playlist}|{string.Join(",", kinds.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + "=" + k.Value))}";
            if (s.Uploaded.TryGetValue(m.SessionId, out var done) && done == sig)
            {
                continue;
            }
            list.Add((sig, new UploadMatch(m.SessionId, start, m.ChestsOpened, kinds, m.Playlist, m.Placement)));
        }
        return list;
    }

    /// <summary>Tus cofres por tipo y los demás contenedores, con las mismas claves que se guardan de los compañeros.</summary>
    public static Dictionary<string, int> OwnKinds(MatchRecord m)
    {
        var kinds = m.Chests.GroupBy(c => c.Kind).ToDictionary(g => g.Key, g => g.Count());
        if (m.OtherInteractions.Count > 0)
        {
            foreach (var kv in m.OtherInteractions) kinds[kv.Key] = kinds.GetValueOrDefault(kv.Key) + kv.Value;
        }
        else if (m.AmmoBoxesOpened > 0)
        {
            kinds[ChestKinds.AmmoBox] = m.AmmoBoxesOpened;
        }
        return kinds;
    }

    private async Task SyncAsync(PartyState s, List<(string Sig, UploadMatch Body)> uploads)
    {
        try
        {
            var since = s.Since;
            var chunks = uploads.Chunk(ChunkSize).ToList();
            if (chunks.Count == 0) chunks.Add(Array.Empty<(string, UploadMatch)>());
            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                var (result, error, status) = await PostAsync<SyncResult>(s.Server, "api/sync",
                    new { memberId = s.MemberId, secret = s.Secret, since, matches = chunk.Select(u => u.Body) });
                if (result == null)
                {
                    if (status == 401)
                    {
                        lock (_gate)
                        {
                            if (_state.MemberId == s.MemberId)
                            {
                                _state = new PartyState { Server = s.Server };
                                SaveState();
                            }
                        }
                        _error = L.T("Ya no estás en la party (te sacaron o se borró).", "You are no longer in the party.");
                        return;
                    }
                    _error = error;
                    return;
                }
                lock (_gate)
                {
                    if (_state.MemberId != s.MemberId) return; // salió o cambió de party mientras tanto
                    var cur = _state;
                    foreach (var u in chunk)
                    {
                        cur.Uploaded[u.Body.SessionId] = u.Sig;
                    }
                    var byKey = cur.Matches.ToDictionary(x => x.MemberId + "|" + x.SessionId);
                    foreach (var x in result.Matches)
                    {
                        byKey[x.MemberId + "|" + x.SessionId] = x;
                    }
                    var ids = result.Party.Members.Select(x => x.Id).ToHashSet();
                    cur.Matches = byKey.Values.Where(x => ids.Contains(x.MemberId)).ToList();
                    cur.Members = result.Party.Members;
                    cur.LastSyncUtc = DateTime.UtcNow;
                    // Solo con el último trozo: los anteriores ya trajeron todo lo cambiado hasta su hora.
                    if (i == chunks.Count - 1) cur.Since = result.ServerTime;
                    since = cur.Since ?? since;
                    SaveState();
                }
                _error = null;
            }
            _annotate = true;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            ErrorLog.Write("Party: sincronizar", ex);
        }
        finally
        {
            Interlocked.Increment(ref _version);
            Changed?.Invoke();
        }
    }

    /// <summary>
    /// Pone los cofres exactos de los miembros de la party en los compañeros de tus partidas. La partida se reconoce por
    /// su id de sesión (el mismo para todos los jugadores); si no coincide, por la hora de inicio (±90 s).
    /// </summary>
    private bool Annotate(TrackerData data, PartyState s)
    {
        List<PartyMember> others;
        Dictionary<string, PartyMatch> bySession;
        ILookup<string, PartyMatch> byMember;
        lock (_gate)
        {
            others = s.Members.Where(m => m.Id != s.MemberId).ToList();
            bySession = new Dictionary<string, PartyMatch>();
            foreach (var x in s.Matches) bySession[x.MemberId + "|" + x.SessionId] = x;
            byMember = s.Matches.ToLookup(x => x.MemberId);
        }
        var together = new Dictionary<string, int>();
        var unresolved = new Dictionary<string, List<(string?, string, int)>>();
        var changed = false;
        foreach (var m in data.Matches)
        {
            if (m.Teammates is not { Count: > 0 } mates) continue;
            foreach (var t in mates)
            {
                var member = others.FirstOrDefault(o => !string.IsNullOrEmpty(t.EpicId)
                    ? string.Equals(o.EpicId, t.EpicId, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(o.Name, t.Name, StringComparison.OrdinalIgnoreCase));
                var entry = member == null ? null
                    : bySession.GetValueOrDefault(member.Id + "|" + m.SessionId) ?? Nearest(byMember[member.Id], m);
                if (entry == null)
                {
                    if (!unresolved.TryGetValue(m.SessionId, out var list)) unresolved[m.SessionId] = list = new();
                    list.Add((member?.Id, t.Name, t.ChestsSeen));
                    continue;
                }
                together[member.Id] = together.GetValueOrDefault(member.Id) + 1;
                if (t.VerifiedChests != entry.Chests || !SameKinds(t.VerifiedKinds, entry.Kinds))
                {
                    t.VerifiedChests = entry.Chests;
                    t.VerifiedKinds = new Dictionary<string, int>(entry.Kinds);
                    changed = true;
                }
            }
        }
        _together = together;
        _unresolved = unresolved;
        return changed;
    }

    private static PartyMatch? Nearest(IEnumerable<PartyMatch> entries, MatchRecord m)
    {
        if (m.MatchStartUtc is not { } start) return null;
        var utc = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        return entries
            .Where(x => x.StartUtc is { } t && (x.Playlist == null || m.Playlist == null || x.Playlist == m.Playlist))
            .Select(x => (x, Gap: (x.StartUtc!.Value.ToUniversalTime() - utc).Duration()))
            .Where(x => x.Gap <= SameMatchWindow)
            .OrderBy(x => x.Gap)
            .Select(x => x.x)
            .FirstOrDefault();
    }

    private static bool SameKinds(Dictionary<string, int>? a, Dictionary<string, int> b) =>
        a != null && a.Count == b.Count && a.All(kv => b.TryGetValue(kv.Key, out var v) && v == kv.Value);

    // ---- Vista para la ventana ---------------------------------------------------------------------------------------

    public PartyView? View()
    {
        lock (_gate)
        {
            var s = _state;
            if (!s.Joined) return null;
            var today = DateTime.Today;
            var rows = s.Members.Select(member =>
            {
                var games = s.Matches.Where(x => x.MemberId == member.Id && Profiles.IsBattleRoyalePlaylist(x.Playlist)).ToList();
                return new PartyMemberView(member.Name, member.Id == s.MemberId, member.LastSync,
                    games.Where(x => x.StartUtc?.ToLocalTime().Date == today).Sum(x => x.Chests),
                    games.Sum(x => x.Chests), games.Count, _together.GetValueOrDefault(member.Id));
            })
                .OrderByDescending(r => r.Today).ThenByDescending(r => r.Total)
                .ToList();
            return new PartyView(s.Code!, rows, s.LastSyncUtc, _error, _inFlight is { IsCompleted: false }) { Games = Games(s) };
        }
    }

    /// <summary>
    /// Agrupa las partidas subidas por todos: misma partida = mismo id de sesión o, si no, misma hora de inicio (±90 s)
    /// y mismo modo. Quedan las que tienen al menos dos miembros de la party (aunque hubiera randoms).
    /// </summary>
    private List<PartyGame> Games(PartyState s)
    {
        var names = s.Members.ToDictionary(m => m.Id, m => m.Name);
        var clusters = new List<List<PartyMatch>>();
        var bySession = new Dictionary<string, List<PartyMatch>>();
        foreach (var e in s.Matches
                     .Where(x => names.ContainsKey(x.MemberId) && Profiles.IsBattleRoyalePlaylist(x.Playlist))
                     .OrderBy(x => x.StartUtc ?? DateTime.MaxValue))
        {
            var cluster = bySession.GetValueOrDefault(e.SessionId);
            if (cluster == null && e.StartUtc is { } start)
            {
                cluster = clusters.Skip(Math.Max(0, clusters.Count - 8)).FirstOrDefault(c =>
                    c.All(x => x.MemberId != e.MemberId) && c.Any(x => x.Playlist == e.Playlist
                        && x.StartUtc is { } t && (t.ToUniversalTime() - start.ToUniversalTime()).Duration() <= SameMatchWindow));
            }
            if (cluster == null)
            {
                clusters.Add(cluster = new List<PartyMatch>());
            }
            if (cluster.Any(x => x.MemberId == e.MemberId)) continue; // la misma partida dos veces
            cluster.Add(e);
            bySession[e.SessionId] = cluster;
        }

        var games = new List<PartyGame>();
        foreach (var c in clusters)
        {
            var players = c.Select(x => new PartyGamePlayer(names[x.MemberId], x.Chests, x.MemberId == s.MemberId, true, true)).ToList();
            var mine = c.FirstOrDefault(x => x.MemberId == s.MemberId);
            if (mine != null && _unresolved.TryGetValue(mine.SessionId, out var extra))
            {
                // Miembros que aún no la subieron (+) y randoms (+), vistos desde tu replay.
                players.AddRange(extra.Where(x => x.MemberId == null || c.All(y => y.MemberId != x.MemberId))
                    .Select(x => new PartyGamePlayer(x.Name, x.Seen, false, false, x.MemberId != null)));
            }
            if (players.Count(p => p.InParty) < 2) continue;
            var first = mine ?? c[0];
            games.Add(new PartyGame(first.StartUtc ?? DateTime.MinValue, Pretty.Playlist(first.Playlist),
                c.Select(x => x.Placement).FirstOrDefault(p => p != null),
                players.OrderByDescending(p => p.InParty).ThenByDescending(p => p.Chests).ToList()));
        }
        return games.OrderByDescending(g => g.StartUtc).ToList();
    }

    // ---- Red -----------------------------------------------------------------------------------------------------

    private static async Task<(T? Result, string? Error, int? Status)> PostAsync<T>(string server, string path, object body)
    {
        try
        {
            using var response = await Http.PostAsJsonAsync(new Uri(new Uri(server.TrimEnd('/') + "/"), path), body, Json);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                string? message = null;
                try { message = JsonDocument.Parse(text).RootElement.GetProperty("error").GetString(); } catch { /* sin JSON */ }
                return (default, message ?? L.F("El servidor respondió {0}", "The server answered {0}", (int)response.StatusCode), (int)response.StatusCode);
            }
            return (JsonSerializer.Deserialize<T>(text, Json), null, (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return (default, L.T("Sin conexión con el servidor de la party", "Can't reach the party server"), null);
        }
    }

    private void SaveState()
    {
        try { _store.SaveParty(_state); }
        catch (Exception ex) { ErrorLog.Write("Party: guardar", ex); }
    }

    private sealed record UploadMatch(string SessionId, DateTime? StartUtc, int Chests, Dictionary<string, int> Kinds, string? Playlist, int? Placement);

    private sealed class PartyInfo
    {
        public string Code { get; set; } = "";
        public List<PartyMember> Members { get; set; } = new();
    }

    private sealed class EnterResult
    {
        public string MemberId { get; set; } = "";
        public string Secret { get; set; } = "";
        public PartyInfo Party { get; set; } = new();
    }

    private sealed class SyncResult
    {
        public string? ServerTime { get; set; }
        public PartyInfo Party { get; set; } = new();
        public List<PartyMatch> Matches { get; set; } = new();
    }
}
