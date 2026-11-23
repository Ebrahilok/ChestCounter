namespace ChestCounter;

/// <summary>
/// Probabilidad de sacar el token de la Tienda pirata (acceso a la tienda) con los cofres abiertos.
/// Epic publica una tasa base de 1 entre 137.500 por cofre y un multiplicador de ×1,5 durante la Hora del tesoro.
/// Cada cofre es un intento independiente: P(al menos uno) = 1 − Π(1 − pᵢ).
/// </summary>
public static class TokenOdds
{
    /// <summary>Uno de cada cuántos cofres da el token, fuera de la Hora del tesoro.</summary>
    public const double OneInChests = 137_500;

    /// <summary>Multiplicador del acceso a la Tienda pirata en la Hora del tesoro.</summary>
    public const double TreasureHourBoost = 1.5;

    public static double PerChest(bool treasureHour) => (treasureHour ? TreasureHourBoost : 1.0) / OneInChests;

    /// <summary>
    /// Probabilidad de al menos un token en una partida, cofre a cofre (sus cofres en la Hora del tesoro, ×1,5).
    /// Con <paramref name="includeTeammates"/>, también los cofres que se vio abrir a los compañeros (los tesoros se
    /// comparten con el equipo). De esos no se sabe el segundo: cuentan ×1,5 si la mitad de la partida cae en la Hora del tesoro.
    /// </summary>
    /// <param name="owners">Compañeros que ya tienen el token (ver <see cref="Config.TokenOwners"/>): sus cofres no cuentan.</param>
    public static (double Chance, int HourChests, int MateChests) ForMatch(MatchRecord m, bool includeTeammates = false, IReadOnlyCollection<string>? owners = null)
    {
        var start = DateTime.SpecifyKind(m.MatchStartUtc ?? m.AnalyzedAtUtc, DateTimeKind.Utc);
        var miss = 1.0;
        var hour = 0;
        foreach (var c in m.Chests)
        {
            var inHour = TreasureHours.Active(start.AddSeconds(c.TimeSeconds)) != null;
            if (inHour) hour++;
            miss *= 1 - PerChest(inHour);
        }
        var mates = includeTeammates ? TeammateChests(m, owners) : 0;
        if (mates > 0)
        {
            var mateHour = TreasureHours.Active(start.AddSeconds((m.DurationSeconds ?? 0) / 2.0)) != null;
            if (mateHour) hour += mates;
            miss *= Math.Pow(1 - PerChest(mateHour), mates);
        }
        return (1 - miss, hour, mates);
    }

    /// <summary>Cofres (sin cajas ni cañones) que se vio abrir a los compañeros en la partida.</summary>
    public static int TeammateChests(MatchRecord m, IReadOnlyCollection<string>? owners = null) =>
        m.Teammates?.Where(t => owners == null || !HasToken(t, owners)).Sum(t => t.ChestsSeen) ?? 0;

    /// <summary>Clave guardada de un compañero en la lista «ya tiene el token»: su Epic id, o su nombre si no lo hay.</summary>
    public static string OwnerKey(TeammateRecord t) => !string.IsNullOrEmpty(t.EpicId) ? t.EpicId : t.Name;

    /// <summary>True si el compañero está en la lista (por Epic id o por nombre, sin mayúsculas).</summary>
    public static bool HasToken(TeammateRecord t, IReadOnlyCollection<string> owners) => owners.Any(o =>
        (!string.IsNullOrEmpty(t.EpicId) && string.Equals(o, t.EpicId, StringComparison.OrdinalIgnoreCase))
        || string.Equals(o, t.Name, StringComparison.OrdinalIgnoreCase));

    public static double ForUntimedChests(int count) => 1 - Math.Pow(1 - PerChest(false), count);

    public static string MatchPercent(double chance) => (chance * 100).ToString("F4", L.Culture) + " %";

    /// <summary>Al menos un token en varias partidas.</summary>
    public static double Combine(IEnumerable<double> chances) => 1 - chances.Aggregate(1.0, (miss, p) => miss * (1 - p));

    /// <summary>"0,024 %", "1,4 %", "0,0007 %": siempre con dos cifras significativas.</summary>
    public static string Percent(double p)
    {
        var pct = p * 100;
        if (pct <= 0) return "0 %";
        var decimals = Math.Clamp(1 - (int)Math.Floor(Math.Log10(pct)), 0, 6);
        return pct.ToString("F" + decimals, L.Culture) + " %";
    }

    /// <summary>"1 de cada 4.170".</summary>
    public static string OneIn(double p) => p <= 0 ? "—" : L.F("1 de cada {0}", "1 in {0}", ((int)Math.Round(1 / p)).ToString("#,0", L.Culture));
}
