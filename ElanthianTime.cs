using System.Text.RegularExpressions;

namespace Genie.Plugins.TimeTracker;

/// <summary>
/// Canonical Elanthian calendar tables (months, year-cycle, anlaen) and the
/// fixed conversion constants. Authoritative ordering from Elanthipedia, verified
/// against a live <c>time</c> reading: "7th month of Moliko the Balance" → Moliko
/// is index 7 here; "year of the Bronze Wyvern" → second in the cycle; "Anlas of
/// Asketi's Hunt" → index 2 of the twelve anlaen.
/// </summary>
internal static class ElanthianCalendar
{
    public const int DaysPerMonth        = 40;
    public const int MonthsPerYear       = 10;
    public const int DaysPerYear         = DaysPerMonth * MonthsPerYear;   // 400
    public const int AnlaenPerDay        = 12;
    public const int RoisaenPerAnlas     = 30;   // an anlas is 30 roisaen …
    public const int RealSecondsPerRoisaen = 60; // … and a roisaen is one Earth minute
    // 12 anlaen × 30 roisaen × 60 s = 21 600 s = 6 Earth hours = one Elanthian day.

    /// <summary>The ten months, in order, with their epithets.</summary>
    public static readonly string[] Months =
    {
        "Akroeg the Ram", "Ka'len the Sea Drake", "Lirisa the Archer",
        "Shorka the Cobra", "Uthmor the Giant", "Arhat the Fire Lion",
        "Moliko the Balance", "Skullcleaver the Dwarven Axe",
        "Dolefaren the Brigantine", "Nissa the Maiden",
    };

    /// <summary>The seven year-names, in cycle order (without the "Year of the" prefix).</summary>
    public static readonly string[] Years =
    {
        "Silver Unicorn", "Bronze Wyvern", "Golden Panther", "Amber Phoenix",
        "Iron Toad", "Emerald Dolphin", "Crystal Snow Hare",
    };

    /// <summary>The twelve anlaen (named day-periods), in order. A new Elanthian
    /// day begins at Anduwen (index 0).</summary>
    public static readonly string[] Anlaen =
    {
        "Anduwen", "Starwatch", "Asketi's Hunt", "Berengaria's Touch",
        "Hodierna's Blessing", "Peri'el's Watch", "Dergati's Bane",
        "Firulf's Flame", "Tamsine's Toil", "Meraud's Cloak",
        "Phelim's Vigil", "Revelfae",
    };

    /// <summary>Short form of a month epithet: "Moliko the Balance" → "Moliko".</summary>
    public static string ShortMonth(string full)
    {
        var i = full.IndexOf(" the ", StringComparison.Ordinal);
        return i < 0 ? full : full[..i];
    }
}

/// <summary>
/// A parsed snapshot of the <c>time</c> verb output, anchored to the wall-clock
/// moment it was read. From this the plugin live-ticks the Elanthian time-of-day
/// (and rolls the date) using only the system clock — no server round-trips.
/// </summary>
internal sealed class ElanthianTimeReading
{
    public DateTimeOffset CapturedAt;

    public int    YearsSinceVictory;   // "456 years"
    public int    DayOfYear;           // "277 days" (0–399 into the current year)
    public int    MonthOrdinal;        // 1–10, from "the 7th month of …"
    public string MonthName = "";      // "Moliko the Balance"
    public string YearName  = "";      // "Bronze Wyvern"
    public string Season    = "";      // "fall"
    public bool   IsNight;             // "it is night"
    public int    RoisaenToAnlas;      // "8 roisaen before …"
    public string AnlasTarget = "";    // "Asketi's Hunt"
    public string Confidence  = "";    // "positive" / "fairly certain" / …

    public bool Valid => MonthOrdinal > 0;

    // "It has been 456 years, 277 days since the Victory of Lanival the Redeemer."
    private static readonly Regex VictoryRe = new(
        @"It has been (\d+) years?,\s*(\d+) days? since the Victory of Lanival",
        RegexOptions.Compiled);

    // "It is the 7th month of Moliko the Balance in the year of the Bronze Wyvern."
    private static readonly Regex MonthRe = new(
        @"It is the (\d+)(?:st|nd|rd|th) month of (.+?) in the year of the (.+?)\.",
        RegexOptions.Compiled);

    // "It is currently fall and it is night."
    private static readonly Regex SeasonRe = new(
        @"It is currently (\w+) and it is (day|night)\.",
        RegexOptions.Compiled);

    // "You're positive it's 8 roisaen before the Anlas of Asketi's Hunt."
    // The confidence wording ("positive", "fairly certain", "would guess") varies
    // with whether you can see the sky / carry a timepiece — captured loosely.
    private static readonly Regex AnlasRe = new(
        @"(?:You(?:'re| are| would| think)?\s*(?<conf>[\w' ]+?)\s+)?it's (?:roughly |about |approximately )?(?<rois>\d+) roisae?n before the Anlas of (?<anlas>.+?)\.",
        RegexOptions.Compiled);

    /// <summary>Feed one game-text line; updates whichever field the line carries.
    /// Returns true if the line was a recognised <c>time</c> line.</summary>
    public bool Feed(string line, DateTimeOffset now)
    {
        var m = VictoryRe.Match(line);
        if (m.Success)
        {
            YearsSinceVictory = int.Parse(m.Groups[1].Value);
            DayOfYear         = int.Parse(m.Groups[2].Value);
            CapturedAt        = now;
            return true;
        }

        m = MonthRe.Match(line);
        if (m.Success)
        {
            MonthOrdinal = int.Parse(m.Groups[1].Value);
            MonthName    = m.Groups[2].Value.Trim();
            YearName     = m.Groups[3].Value.Trim();
            CapturedAt   = now;
            return true;
        }

        m = SeasonRe.Match(line);
        if (m.Success)
        {
            Season  = m.Groups[1].Value.Trim();
            IsNight = m.Groups[2].Value == "night";
            return true;
        }

        m = AnlasRe.Match(line);
        if (m.Success)
        {
            Confidence      = m.Groups["conf"].Value.Trim();
            RoisaenToAnlas  = int.Parse(m.Groups["rois"].Value);
            AnlasTarget     = m.Groups["anlas"].Value.Trim();
            CapturedAt      = now;
            return true;
        }

        return false;
    }

    /// <summary>The live time-of-day, advanced from the system clock since capture.
    /// Returns the current "N roisaen before the Anlas of X" plus how many whole
    /// Elanthian days have rolled over since the reading.</summary>
    public (int roisaen, string anlas, int daysElapsed) NowTimeOfDay(DateTimeOffset now)
    {
        var elapsed = (int)Math.Floor((now - CapturedAt).TotalSeconds
                                      / ElanthianCalendar.RealSecondsPerRoisaen);
        if (elapsed < 0) elapsed = 0;

        var idx = Array.IndexOf(ElanthianCalendar.Anlaen, AnlasTarget);
        var remaining = RoisaenToAnlas - elapsed;
        if (idx < 0) return (Math.Max(remaining, 0), AnlasTarget, 0);   // unknown name: don't advance

        var days = 0;
        while (remaining <= 0)
        {
            remaining += ElanthianCalendar.RoisaenPerAnlas;
            idx = (idx + 1) % ElanthianCalendar.AnlaenPerDay;
            if (idx == 0) days++;            // wrapped to Anduwen → a new Elanthian day
        }
        return (remaining, ElanthianCalendar.Anlaen[idx], days);
    }

    /// <summary>The current date, rolled forward by <paramref name="daysElapsed"/>
    /// whole Elanthian days. Year-name and month are recomputed from the canonical
    /// tables (the month ordinal is day-of-year / 40, which matches the live
    /// reading); the season is held from the last reading because its phase offset
    /// within the year isn't documented.</summary>
    public (int years, int dayOfYear, int month, string monthName, string yearName)
        NowDate(int daysElapsed)
    {
        var doy   = DayOfYear + daysElapsed;
        var years = YearsSinceVictory + doy / ElanthianCalendar.DaysPerYear;
        doy %= ElanthianCalendar.DaysPerYear;

        var month     = doy / ElanthianCalendar.DaysPerMonth + 1;             // 1–10
        var monthName = ElanthianCalendar.Months[month - 1];

        // Step the seven-year cycle by the number of years rolled, anchored on the
        // year-name we were told.
        var yearName = YearName;
        var baseIdx  = Array.IndexOf(ElanthianCalendar.Years, YearName);
        if (baseIdx >= 0)
        {
            var rolled = daysElapsed > 0
                ? (DayOfYear + daysElapsed) / ElanthianCalendar.DaysPerYear
                : 0;
            yearName = ElanthianCalendar.Years[(baseIdx + rolled) % ElanthianCalendar.Years.Length];
        }
        return (years, doy, month, monthName, yearName);
    }
}
