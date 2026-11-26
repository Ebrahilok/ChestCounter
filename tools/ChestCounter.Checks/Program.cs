using System.Drawing;
using System.Reflection;
using System.Text.Json;
using ChestCounter;
using System.Windows.Forms;

internal static class Program
{
    private static int _checks;
    private static void Check(bool value, string message) { if (!value) throw new Exception(message); _checks++; }
    [STAThread]
    private static void Main(string[] args)
    {
        var output = Path.GetFullPath(args[0]);
        Directory.CreateDirectory(output);
        foreach (var p in new[] { "Playlist_DefaultSolo", "Playlist_DefaultSquad", "Playlist_NoBuildBR_Trio", "Playlist_Trios", "Playlist_HabaneroBR_Solo" })
            Check(Profiles.IsBattleRoyalePlaylist(p), "BR rejected: " + p);
        foreach (var p in new string?[] { null, "", "Playlist_Creative", "Playlist_BlastBerry_Squad", "Playlist_Reload_Solo", "Playlist_DefaultSquad_LTM", "Playlist_Delulu", "Playlist_Festival" })
            Check(!Profiles.IsBattleRoyalePlaylist(p), "Other mode accepted: " + p);
        var own = new TreasureFind { Item = "Own reward", RawItem = "own" };
        var team = new TreasureFind { Item = "Team reward", Scope = "team", Finder = "Mate", RawItem = "team" };
        var nearby = new TreasureFind { Item = "Pirate Shop Access", Scope = "nearby", Finder = "Opponent", RawItem = "PirateShopAccess", Evidence = "recipient-handler" };
        var match = new MatchRecord { Playlist = "Playlist_NoBuildBR_Squad", GameVersion = "++Fortnite+Release-43.10", PlayerName = "Test", EpicId = "test", ChestsOpened = 3,
            TreasureFinds = new() { own }, NearbyTreasureFinds = new() { team, nearby }, Teammates = new() { new() { Name = "Mate", ChestsSeen = 4 } } };
        var config = new Config();
        Check(Profiles.Adjust(match, config)!.TreasureFinds.Count == 1, "Team treasures default off");
        config.CountTeammateTreasures = true;
        var adjusted = Profiles.Adjust(match, config)!;
        Check(adjusted.TreasureFinds.Count == 2, "Shared treasures missing");
        Check(adjusted.ChestsOpened == 3, "Independent chest setting changed");
        Check(adjusted.AllTreasureFinds.Count() == 3, "Report duplicates shared treasures");
        Check(adjusted.NearbyTreasureFinds.Count == 1, "Nearby enemy included in own total");
        Check(match.TreasureFinds.Count == 1 && match.NearbyTreasureFinds.Count == 2, "Stored data changed");
        config.CountTeammateChests = true;
        Check(Profiles.Adjust(match, config)!.ChestsOpened == 7, "Squad chest regression");
        config.CountTeammateTreasures = false;
        Check(Profiles.Adjust(match, config)!.TreasureFinds.Count == 1, "Toggle not reversible");
        Check(!new TreasureFind { RawItem = "WID_PirateShopAccess", Evidence = "handler-export" }.IsPirateToken, "Class export mistaken for an obtained token");
        Check(new TreasureFind { RawItem = "WID_PirateShopAccess", Evidence = "recipient-handler" }.IsPirateToken, "Confirmed token receipt ignored");
        Check(match.PirateTokenSeen, "Enemy token not noticed");
        Check(!TreasureNames.IsPirateToken("DelilahPrize:dpid_item16"), "Guessed numeric prize");
        Check(!TreasureNames.IsPirateToken("WID_SmugglerExtractionKey"), "Extraction key mistaken for Pirate token");
        var json = JsonSerializer.Serialize(match);
        var loaded = JsonSerializer.Deserialize<MatchRecord>(json)!;
        Check(loaded.PirateTokenSeen && loaded.AllTreasureFinds.Count() == 3, "Persistence lost rewards");
        var other = new MatchRecord { Playlist = "Playlist_Creative", GameVersion = match.GameVersion, ChestsOpened = 999, EpicId = "test" };
        var data = new TrackerData { Matches = new() { match, other } };
        Check(Profiles.View(data, config).Matches.Count == 1, "Legacy other mode leaks into stats");
        Check(!ReportWriter.Csv(data).Contains("999"), "Other mode leaks into CSV");
        Check(ChestKinds.Classify("B_SpritePosters_Booty_Chest_C") == ChestKinds.PosterChest, "Poster chest not recognized");
        Check(!ChestKinds.IsChest(ChestKinds.Classify("B_SpritePosters_Poster_C")), "Map collection counted as a chest");
        Check(ChestKinds.Classify("Tiered_Ammo_Athena_C") == ChestKinds.AmmoBox, "Ammo classification changed");
        Check(ChestKinds.Classify("FoodBox_Large_C") == ChestKinds.FoodBox, "Food classification changed");
        config.CountTeammateTreasures = true;
        var html = ReportWriter.Report(config, Profiles.View(data, config), Profiles.List(data, config));
        Check(html.Contains("Opponent") && html.Contains("Mate") && html.Contains("Pirate Shop Access"), "Treasure report lost scopes");
        Check(html.Contains("TOKEN", StringComparison.OrdinalIgnoreCase), "Token alert missing in report");
        File.WriteAllText(Path.Combine(output, "report-test.html"), html);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException); // un fallo al pintar rompe la prueba (en la app sale «Parameter is not valid»)
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var store = new Store(Path.Combine(output, "isolated-data"));
        var tracker = new Tracker(store, config, data);
        var monitor = new ChestCounter.Monitor(store, config, data, tracker, store.Folder);
        using (var settings = new SettingsForm(monitor, store))
        {
            settings.StartPosition = FormStartPosition.Manual;
            settings.Location = new Point(-30000, -30000);
            settings.Show();
            Check(settings.FormBorderStyle == FormBorderStyle.Sizable && settings.MaximizeBox, "Settings cannot resize");
            var baseSize = settings.ClientSize;
            settings.ClientSize = new Size(baseSize.Width * 3 / 2, baseSize.Height * 3 / 2);
            Check(settings.Controls.Cast<Control>().Where(c => c.Visible).All(c => c.Right <= settings.ClientSize.Width + 2 && c.Bottom <= settings.ClientSize.Height + 2), "Resized settings overflow");
            // Estirar solo a lo ancho deja la letra igual: antes se liberaba la fuente que el control seguía usando.
            settings.ClientSize = new Size(baseSize.Width * 2, baseSize.Height * 3 / 2);
            settings.Refresh();
            Application.DoEvents();
            settings.ClientSize = new Size(baseSize.Width * 3 / 2, baseSize.Height * 3 / 2);
            settings.Refresh();
            Application.DoEvents();
            using var bitmap = new Bitmap(settings.Width, settings.Height);
            settings.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, "settings-resized.png"));
            settings.ClientSize = baseSize;
            using var normal = new Bitmap(settings.Width, settings.Height);
            settings.DrawToBitmap(normal, new Rectangle(Point.Empty, normal.Size));
            normal.Save(Path.Combine(output, "settings-normal.png"));
        }
        using (var main = new MainForm(monitor, store))
        {
            main.ShowInTaskbar = false;
            main.StartPosition = FormStartPosition.Manual;
            main.Location = new Point(-30000, -30000);
            main.Show();
            var stats = ChestStats.Empty with { LastMatch = new(3, 0, 1, 1, 80, 2, true) };
            var state = new MonitorState(OverlayModel.Build(Profiles.View(data, config), null, false), Profiles.List(data, config), "test", null, null, false, stats);
            typeof(MainForm).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, new object[] { state });
            var buttons = main.Controls.Cast<Control>().Where(c => c is PillButton && (c.Text == "Ajustes" || c.Text == "Settings" || c.Text == "Informe" || c.Text == "Report")).ToList();
            Check(buttons.Count == 2 && buttons.All(c => c.Visible), "Token banner hides action buttons");
            using var bitmap = new Bitmap(main.Width, main.Height);
            main.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, "main-token.png"));
            var teamState = state with { Team = new TeamSummary(false, 3, new[] { new TeamMember("Mate", 4) }, false) };
            typeof(MainForm).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, new object[] { teamState });
            Check(main.ClientSize.Height > 430 && buttons.All(c => c.Visible), "Team and token panels overlap actions");
            using var combined = new Bitmap(main.Width, main.Height);
            main.DrawToBitmap(combined, new Rectangle(Point.Empty, combined.Size));
            combined.Save(Path.Combine(output, "main-token-team.png"));
            var noToken = state with { Stats = ChestStats.Empty };
            typeof(MainForm).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, new object[] { noToken });
            var oddsLabel = (Label)typeof(MainForm).GetField("_pirateLabel", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            Check(oddsLabel.Visible && !string.IsNullOrWhiteSpace(oddsLabel.Text), "Persistent token status is missing");
            var liveOdds = noToken with { TokenLive = 0, TokenToday = 0.0012, TokenAllTime = 0.125 };
            typeof(MainForm).GetMethod("Render", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(main, new object[] { liveOdds });
            var totalsLabel = (Label)typeof(MainForm).GetField("_pirateTotals", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            Check(oddsLabel.Text.Contains(TokenOdds.MatchPercent(0))
                && oddsLabel.Text.Contains(TokenOdds.Percent(liveOdds.TokenToday))
                && totalsLabel.Text.Contains(TokenOdds.Percent(liveOdds.TokenAllTime))
                && oddsLabel.Bottom <= totalsLabel.Top, "Match or cumulative token odds are missing");
            Check(Math.Abs(TokenOdds.ForUntimedChests(1) - TokenOdds.PerChest(false)) < 1e-12, "Initial chests should use the base rate");
        var squad = new MatchRecord { MatchStartUtc = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc), DurationSeconds = 1200,
            Chests = Enumerable.Range(0, 27).Select(i => new ChestRecord { TimeSeconds = i * 10, Kind = ChestKinds.Normal }).ToList(),
            Teammates = new() { new() { Name = "A", ChestsSeen = 29 }, new() { Name = "B", ChestsSeen = 23 }, new() { Name = "C", ChestsSeen = 13 } } };
        Check(Math.Abs(TokenOdds.ForMatch(squad).Chance - TokenOdds.ForUntimedChests(27)) < 1e-12, "Token odds changed without teammates");
        var teamOdds = TokenOdds.ForMatch(squad, includeTeammates: true);
        Check(teamOdds.MateChests == 65 && Math.Abs(teamOdds.Chance - TokenOdds.ForUntimedChests(92)) < 1e-12, "Teammate chests not added to token odds");
        var withoutA = TokenOdds.ForMatch(squad, includeTeammates: true, owners: new[] { "a" });
        Check(withoutA.MateChests == 36 && Math.Abs(withoutA.Chance - TokenOdds.ForUntimedChests(63)) < 1e-12, "Teammate with the token still counted");
        var backupStore = new Store(Path.Combine(output, "backup-test"));
        backupStore.SaveData(new TrackerData());
        backupStore.BackupDaily();
        Check(Directory.GetFiles(backupStore.BackupFolder, "data-*.json").Length == 1, "Daily backup missing");
            using var emptyToken = new Bitmap(main.Width, main.Height);
            main.DrawToBitmap(emptyToken, new Rectangle(Point.Empty, emptyToken.Size));
            emptyToken.Save(Path.Combine(output, "main-token-empty.png"));
            var scaleLayout = typeof(MainForm).GetMethod("ApplyLayoutScale", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var tokenCard = oddsLabel.Parent!;
            var originalCard = tokenCard.Bounds;
            var originalSize = main.ClientSize;
            var matchList = (Control)typeof(MainForm).GetField("_matches", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(main)!;
            var baseList = matchList.Size;
            var baseFont = matchList.Parent!.Controls[0].Font.SizeInPoints;
            foreach (var size in new[] { new Size(900, 460), new Size(480, 650), new Size(480, 345), new Size(1400, 900), new Size(1500, 950), new Size(1500, 1000), new Size(800, 1000), new Size(820, 1000), originalSize })
            {
                main.ClientSize = size;
                scaleLayout.Invoke(main, null);
                Check(main.ClientRectangle.Contains(tokenCard.Bounds)
                    && tokenCard.ClientRectangle.Contains(oddsLabel.Bounds)
                    && tokenCard.ClientRectangle.Contains(totalsLabel.Bounds)
                    && oddsLabel.Bottom <= totalsLabel.Top, "Token strip clips or overlaps when resized");
                // Nada se sale de la ventana ni de su tarjeta, y las tarjetas no se pisan.
                var cards = main.Controls.Cast<Control>().Where(c => c.Visible && c is Card or MatchList or PillButton).ToList();
                Check(cards.All(c => main.ClientRectangle.Contains(c.Bounds)), $"Control outside the window at {size}");
                Check(main.Controls.Cast<Control>().OfType<Card>().Where(c => c.Visible).SelectMany(c => c.Controls.Cast<Control>().Where(x => x.Visible).Select(x => (c, x)))
                    .All(p => p.c.ClientRectangle.Contains(p.x.Bounds)), $"Control outside its card at {size}");
                var visibleCards = main.Controls.Cast<Control>().OfType<Card>().Where(c => c.Visible).ToList();
                Check(!visibleCards.Any(c => visibleCards.Any(o => o != c && o.Bounds.IntersectsWith(c.Bounds))), $"Cards overlap at {size}");
                // Fluida: con más sitio, la lista crece; la letra solo se ajusta entre el 80 y el 120 %.
                var font = matchList.Parent!.Controls[0].Font.SizeInPoints;
                Check((size.Width < 640 || size.Height < 460 || font >= baseFont * 0.79f) && font <= baseFont * 1.21f, $"Font scaled too much at {size}: {font}");
                if (size.Width >= originalSize.Width && size.Height >= originalSize.Height && size != originalSize)
                {
                    Check(matchList.Width > baseList.Width || matchList.Height > baseList.Height, $"Match list did not grow at {size}");
                }
                main.Refresh(); // pinta de verdad cada control: un fallo al pintar lanza aquí
                Application.DoEvents();
                using var shot = new Bitmap(main.Width, main.Height);
                main.DrawToBitmap(shot, new Rectangle(Point.Empty, shot.Size));
                shot.Save(Path.Combine(output, $"main-{size.Width}x{size.Height}.png"));
            }
            Check(tokenCard.Bounds == originalCard, "Restoring the original window size moved the token strip");
        }
        // Formas redondeadas de 0 o 1 px (ventana muy estrecha): antes GDI+ lanzaba «Parameter is not valid».
        foreach (var r in new[] { new RectangleF(0, 0, 0, 10), new RectangleF(0, 0, 0.5f, 0.5f), new RectangleF(0.5f, 0.5f, 0, 0), new RectangleF(0, 0, -3, 5), new RectangleF(0, 0, 40, 20) })
        {
            using var path = Shape.Rounded(r, 6);
            using var bmp = new Bitmap(50, 30);
            using var g = Graphics.FromImage(bmp);
            g.FillPath(Brushes.White, path);
            _checks++;
        }
        // ---- Party: el número exacto que sube un compañero sustituye al mínimo observado -----------------------------
        var exactMate = new TeammateRecord { Name = "Friend", EpicId = "friend", ChestsSeen = 4, Kinds = new() { [ChestKinds.Normal] = 4, [ChestKinds.AmmoBox] = 1 } };
        var seenMate = new TeammateRecord { Name = "Random", ChestsSeen = 2, Kinds = new() { [ChestKinds.Normal] = 2 } };
        var partyMatch = new MatchRecord { Playlist = "Playlist_DefaultSquad", GameVersion = "++Fortnite+Release-43.10", EpicId = "test", ChestsOpened = 3,
            Teammates = new() { exactMate, seenMate } };
        Check(!partyMatch.TeammatesExact && MatchRecord.Seen(exactMate, Array.Empty<string>()) == 4, "Unverified mate is not the observed minimum");
        exactMate.VerifiedChests = 9;
        exactMate.VerifiedKinds = new() { [ChestKinds.Normal] = 9, [ChestKinds.AmmoBox] = 6 };
        Check(MatchRecord.Seen(exactMate, Array.Empty<string>()) == 9, "Party number not used");
        Check(MatchRecord.Seen(exactMate, new[] { ChestKinds.AmmoBox }) == 15, "Party kinds not used for extra kinds");
        Check(MatchRecord.Seen(seenMate, Array.Empty<string>()) == 2, "Random teammate lost its observed number");
        Check(!partyMatch.TeammatesExact, "Team marked exact with a random teammate");
        Check(TokenOdds.TeammateChests(partyMatch) == 11, "Token odds ignore the party number");
        Check(Profiles.Adjust(partyMatch, new Config { CountTeammateChests = true })!.ChestsOpened == 14, "Team total ignores the party number");
        var reloaded = JsonSerializer.Deserialize<MatchRecord>(JsonSerializer.Serialize(partyMatch))!;
        Check(reloaded.Teammates![0].Verified && reloaded.Teammates[0].Chests == 9 && !reloaded.Teammates[1].Verified, "Party number not saved");
        seenMate.VerifiedChests = 2;
        seenMate.VerifiedKinds = new() { [ChestKinds.Normal] = 2 };
        Check(partyMatch.TeammatesExact, "All-party team not exact");
        var ownKinds = Party.OwnKinds(new MatchRecord { Chests = { new() { Kind = ChestKinds.Normal }, new() { Kind = ChestKinds.Normal } }, OtherInteractions = new() { [ChestKinds.AmmoBox] = 3 } });
        Check(ownKinds[ChestKinds.Normal] == 2 && ownKinds[ChestKinds.AmmoBox] == 3, "Uploaded kinds differ from teammate kinds");

        // Partidas juntos: misma partida por id de sesión o por hora (±90 s); los randoms salen con su mínimo.
        var partyStore = new Store(Path.Combine(output, "party-data"));
        var t0 = new DateTime(2026, 11, 20, 20, 0, 0, DateTimeKind.Utc);
        const string br = "Playlist_DefaultSquad";
        partyStore.SaveParty(new PartyState
        {
            Server = "http://127.0.0.1:9", Code = "TEST01", MemberId = "me", Secret = "x", MyEpicId = "test",
            Members = { new() { Id = "me", EpicId = "test", Name = "Yo" }, new() { Id = "fr", EpicId = "friend", Name = "Amigo" }, new() { Id = "o3", EpicId = "o3", Name = "Otro" } },
            Matches =
            {
                new() { MemberId = "me", SessionId = "s1", StartUtc = t0, Chests = 30, Playlist = br, Placement = 2 },
                new() { MemberId = "fr", SessionId = "s1", StartUtc = t0, Chests = 19, Playlist = br, Placement = 2 },
                new() { MemberId = "fr", SessionId = "s2", StartUtc = t0.AddHours(1), Chests = 12, Playlist = br },
                new() { MemberId = "me", SessionId = "s3", StartUtc = t0.AddHours(2), Chests = 25, Playlist = br },
                new() { MemberId = "o3", SessionId = "s3-other", StartUtc = t0.AddHours(2).AddSeconds(40), Chests = 8, Playlist = br },
            },
        });
        var party = new Party(partyStore);
        var partyData = new TrackerData
        {
            Matches =
            {
                new MatchRecord { SessionId = "s1", EpicId = "test", MatchStartUtc = t0, Playlist = br,
                    Teammates = new() { new() { Name = "Amigo", EpicId = "friend", ChestsSeen = 11 }, new() { Name = "Random", EpicId = "rnd", ChestsSeen = 4 } } },
            },
        };
        typeof(Party).GetMethod("Annotate", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(party, new object[] { partyData, partyStore.LoadParty()! });
        var partyGames = party.View()!.Games;
        Check(partyGames.Count == 2, $"Matches together: expected 2, got {partyGames.Count}");
        var g1 = partyGames.Single(g => g.StartUtc == t0);
        Check(g1.Players.Count == 3 && g1.Players.Single(p => p.Name == "Amigo").Chests == 19 && g1.Players.Single(p => p.Name == "Amigo").Exact
            && g1.Players.Single(p => p.Name == "Random") is { Exact: false, InParty: false, Chests: 4 } && g1.Total == 53 && !g1.Exact, "Match together players wrong");
        Check(partyGames.Single(g => g.StartUtc != t0).Players.Select(p => p.Name).OrderBy(n => n).SequenceEqual(new[] { "Otro", "Yo" }), "Same match by start time not grouped");
        Check(partyData.Matches[0].Teammates![0].VerifiedChests == 19 && partyData.Matches[0].Teammates![1].VerifiedChests == null, "Annotate wrong");

        var partyView = new PartyView("YP5A7X", new[]
        {
            new PartyMemberView("Ebrahilok", true, DateTime.UtcNow, 112, 4821, 179, 0),
            new PartyMemberView("Amigo1", false, DateTime.UtcNow.AddMinutes(-3), 87, 2210, 95, 41),
            new PartyMemberView("Amigo2", false, null, 0, 0, 0, 0),
        }, DateTime.UtcNow, null, false)
        {
            Games = new[]
            {
                new PartyGame(DateTime.UtcNow.AddMinutes(-20), "Squads", 1, new[]
                {
                    new PartyGamePlayer("Ebrahilok", 31, true, true, true), new PartyGamePlayer("Amigo1", 19, false, true, true),
                    new PartyGamePlayer("Jugador_random", 4, false, false, false),
                }),
                new PartyGame(DateTime.UtcNow.AddDays(-1), "Zero Build · Duos", 7, new[]
                {
                    new PartyGamePlayer("Ebrahilok", 22, true, true, true), new PartyGamePlayer("Amigo2", 6, false, false, true),
                }),
            },
        };
        var gamesTab = typeof(PartyDialog).GetField("_tab", BindingFlags.NonPublic | BindingFlags.Instance)!;
        foreach (var (name, view, tab) in new[] { ("party-joined.png", (PartyView?)partyView, 0), ("party-games.png", partyView, 1), ("party-join.png", null, 0) })
        {
            using var dialog = (Form)Activator.CreateInstance(typeof(PartyDialog), BindingFlags.NonPublic | BindingFlags.Instance, null, new object?[] { monitor, view }, null)!;
            dialog.StartPosition = FormStartPosition.Manual;
            dialog.Location = new Point(-30000, -30000);
            if (tab != 0)
            {
                gamesTab.SetValue(dialog, tab);
                typeof(PartyDialog).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null);
            }
            dialog.Show();
            Check(dialog.Controls[0].Controls.Cast<Control>().All(c => c.Right <= dialog.ClientSize.Width && c.Bottom <= dialog.ClientSize.Height), "Party dialog overflows: " + name);
            // Una sincronización rehace la ventana ya abierta: debe quedar igual (antes se perdía la escala del monitor).
            var before = dialog.ClientSize;
            typeof(PartyDialog).GetMethod("Rebuild", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(dialog, null);
            Check(dialog.ClientSize == before, $"Party dialog changes size when rebuilt ({before} -> {dialog.ClientSize}, {dialog.DeviceDpi} dpi)");
            Check(dialog.Controls[0].Width == dialog.ClientSize.Width
                && dialog.Controls[0].Controls.Cast<Control>().All(c => c.Right <= dialog.ClientSize.Width && c.Bottom <= dialog.ClientSize.Height), "Rebuilt party dialog overflows: " + name);
            Console.WriteLine($"party dialog {name}: {dialog.DeviceDpi} dpi, {dialog.ClientSize}");
            using var bitmap = new Bitmap(dialog.Width, dialog.Height);
            dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, name));
        }
        var row = new MatchRow(DateTime.Now, "Squads", 3, 4, 100, 2, 0, false, false, 14)
        {
            Detail = new MatchDetail(1200, new[] { new NamedCount("Chest", 3) }, 0, Array.Empty<OtherCount>(),
                new[] { new MateDetail("Friend", 9, new[] { new NamedCount("Chest", 9) }, true), new MateDetail("Random", 2, new[] { new NamedCount("Chest", 2) }) },
                Array.Empty<TreasureDetail>()),
        };
        using (var detail = (Form)Activator.CreateInstance(typeof(MatchDialog), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { row, "report.html" }, null)!)
        {
            detail.StartPosition = FormStartPosition.Manual;
            detail.Location = new Point(-30000, -30000);
            detail.Show();
            using var bitmap = new Bitmap(detail.Width, detail.Height);
            detail.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, "match-party.png"));
        }
        var allExact = row with { TeamExact = true, Detail = row.Detail with { Mates = row.Detail.Mates!.Select(m => m with { Verified = true }).ToList() } };
        using (var detail = (Form)Activator.CreateInstance(typeof(MatchDialog), BindingFlags.NonPublic | BindingFlags.Instance, null, new object[] { allExact, "report.html" }, null)!)
        {
            detail.StartPosition = FormStartPosition.Manual;
            detail.Location = new Point(-30000, -30000);
            detail.Show();
            using var bitmap = new Bitmap(detail.Width, detail.Height);
            detail.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            bitmap.Save(Path.Combine(output, "match-party-exact.png"));
        }
        Console.WriteLine($"PASS: {_checks} regression checks; UI images and report saved in {output}");
    }
}
