namespace ChestCounter;

/// <summary>
/// Probabilidad de sacar el token de la Tienda pirata (acceso a la tienda) con los cofres abiertos.
/// Epic no publica la cifra: se usa 1 entre 137.500 por cofre (dato indicado por el usuario del fork, de la
/// comunidad; cámbialo aquí si se conoce uno mejor). Durante la Hora del tesoro el acceso a la Tienda pirata es ×1,5.
/// Cada cofre es un intento independiente: P(al menos uno) = 1 − Π(1 − pᵢ).
/// </summary>
public static class TokenOdds
{
    /// <summary>Uno de cada cuántos cofres da el token, fuera de la Hora del tesoro.</summary>
    public const double OneInChests = 137_500;

    /// <summary>Multiplicador del acceso a la Tienda pirata en la Hora del tesoro.</summary>
    public const double TreasureHourBoost = 1.5;

    public static double PerChest(bool treasureHour) => (treasureHour ? TreasureHourBoost : 1.0) / OneInChests;

    /// <summary>Probabilidad de al menos un token en una partida, cofre a cofre (sus cofres en la Hora del tesoro, ×1,5).</summary>
    public static (double Chance, int HourChests) ForMatch(MatchRecord m)
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
        return (1 - miss, hour);
    }

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
