using System.Text;
using Genie.Plugins;

namespace Genie.Plugins.TimeTracker;

/// <summary>
/// The V5 port of the Genie 4 TimeTracker plugin. Surfaces Elanthian time and the
/// state of the sky (sun, the three moons, constellations, weather, and — for a
/// Moon Mage — celestial influences) in the host's "Time Tracker" dock panel.
///
/// <para><b>Parse-first, fully passive.</b> The Genie 4 original computed moon
/// positions client-side from epoch offsets. This port instead reads DR's own
/// output for the <c>time</c>, <c>weather</c>, <c>obs sky</c> and <c>perceive</c>
/// verbs as the player naturally types them (<see cref="OnGameText"/>) and
/// reflects it — so it sends no commands by default and can never get the moon
/// state subtly wrong. Between readings it live-ticks the time-of-day from the
/// system clock: an anlas is thirty roisaen and a roisaen is one Earth minute, so
/// the "N roisaen before the Anlas of X" countdown advances on its own and rolls
/// through the twelve anlaen (and the date) without another round-trip.</para>
///
/// <para><b>Output</b> is via the named-window seam, so the plugin has no
/// UI/Avalonia dependency — the host surfaces "Time Tracker" as a dock panel. It
/// references only <c>Genie.Plugins.Abstractions</c> and loads as a DLL from the
/// Plugins folder. <c>/tt refresh</c> is the one path that sends commands, and only
/// when the user asks.</para>
/// </summary>
public sealed class TimeTrackerPlugin : IGeniePlugin
{
    public string Id             => "genie.timetracker";
    public string Name           => "Time Tracker";
    public string Version        => "1.0";
    public string Author         => "Original Genie 4 TimeTracker (ported to Genie 5)";
    public string Description     => "Shows Elanthian time and the state of the sky (moons, sun, weather) in a dock panel.";
    public string MinHostVersion => "5.0.0";

    private const string WindowName = "Time Tracker";

    private bool _enabled = true;
    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            if (!value) _host?.SetWindow(WindowName, "(Time Tracker plugin disabled)");
            else        _dirty = true;
        }
    }

    private IPluginHost _host = null!;
    private TimeTrackerOptions _opts = new();
    private readonly ElanthianTimeReading _time = new();
    private readonly SkyState _sky = new();
    private bool _dirty;
    private int _lastDayShown = -1;   // for the LogGameEvents day-rollover notice
    private string _lastRender = "";  // skip redundant SetWindow calls on quiet prompts

    public void Initialize(IPluginHost host)
    {
        _host = host;
        _opts = TimeTrackerOptions.Load();
    }

    public void Shutdown() { }

    public string? OnGameText(string text, string stream)
    {
        if (stream != "main") return text;     // time/sky/perceive all land on main
        var now = DateTimeOffset.UtcNow;
        var hit = _time.Feed(text, now);
        hit |= _sky.Feed(text, now);
        if (hit)
        {
            _dirty = true;
            if (_opts.LogGameEvents) _host.Log($"[TimeTracker] {text.Trim()}");
        }
        return text;                            // observe-only — never rewrite game text
    }

    public void OnPrompt()
    {
        // Repaint whenever a reading changed, and also while a valid reading is
        // live-ticking (so the countdown advances during normal play). Paint()
        // skips the host call when the rendered text is unchanged.
        if (!_dirty && !(_opts.UseGameTime && _time.Valid)) return;
        _dirty = false;
        Paint();
    }

    /// <summary>Render and push to the window only if the text actually changed —
    /// the live tick fires OnPrompt every prompt, but the roisaen countdown only
    /// moves once a minute.</summary>
    private void Paint()
    {
        var text = Render(DateTimeOffset.UtcNow);
        if (text == _lastRender) return;
        _lastRender = text;
        _host.SetWindow(WindowName, text);
    }

    public string? OnInput(string input)
    {
        var t = input.Trim();
        if (!t.StartsWith("/tt", StringComparison.OrdinalIgnoreCase) &&
            !t.StartsWith("/timetracker", StringComparison.OrdinalIgnoreCase))
            return input;

        var arg = t.Contains(' ') ? t[(t.IndexOf(' ') + 1)..].Trim().ToLowerInvariant() : "";
        switch (arg)
        {
            case "refresh":
                // The one path that talks to the game — user-initiated only. Two
                // commands stays under DR's type-ahead throttle.
                _host.SendCommand("time");
                _host.SendCommand("obs sky");
                _host.Echo("[Time Tracker] requested time + obs sky.");
                break;
            case "help":
                _host.Echo("Time Tracker — /tt (repaint) · /tt refresh (poll time + obs sky) · /tt help");
                _host.Echo("Tip: it updates automatically whenever you type time / weather / obs sky / perceive.");
                break;
            default:
                _lastRender = "";   // force a repaint even if nothing changed
                Paint();
                _host.Echo("[Time Tracker] window updated (Window → Time Tracker to show it).");
                break;
        }
        return null;   // swallow — it's a plugin command, not a game command
    }

    public void OnXml(string xml)                        { }
    public void OnCommandSent(string command)            { }
    public void OnVariableChanged(string name, string v) { }

    // ── rendering ───────────────────────────────────────────────────────────────

    private string Render(DateTimeOffset now)
    {
        var sb = new StringBuilder();

        if (_opts.ShowElanthiaTime && _time.Valid)
            RenderTime(sb, now);

        RenderMoons(sb, now);
        RenderSky(sb, now);

        if (sb.Length == 0)
            sb.Append("(no readings yet — type 'time' or 'obs sky')");

        return sb.ToString().TrimEnd();
    }

    private void RenderTime(StringBuilder sb, DateTimeOffset now)
    {
        var (roisaen, anlas, days) = _opts.UseGameTime
            ? _time.NowTimeOfDay(now)
            : (_time.RoisaenToAnlas, _time.AnlasTarget, 0);
        var (years, doy, _, monthName, yearName) =
            _opts.UseGameTime ? _time.NowDate(days)
                              : (_time.YearsSinceVictory, _time.DayOfYear, _time.MonthOrdinal, _time.MonthName, _time.YearName);

        var month = _opts.ShowLongNames ? monthName : ElanthianCalendar.ShortMonth(monthName);
        var dayInMonth = doy % ElanthianCalendar.DaysPerMonth + 1;

        sb.Append("Elanthian Time\n");
        sb.Append("──────────────────────────────────────\n");
        sb.Append($" {Ordinal(dayInMonth)} of {month}\n");
        sb.Append($" Year of the {yearName}");
        if (_opts.IncludeTimeOfDay)
            sb.Append($"  ({_time.Season}, {(_time.IsNight ? "night" : "day")})");
        sb.Append('\n');
        sb.Append($" {years} years, {doy} days since the Victory of Lanival\n");
        if (_opts.IncludeAnlasName)
            sb.Append($" ~{roisaen} roisaen before the Anlas of {anlas}\n");

        if (_opts.LogGameEvents && days != _lastDayShown)
        {
            if (_lastDayShown >= 0 && days > _lastDayShown)
                _host.Log($"[TimeTracker] a new Elanthian day has dawned ({Ordinal(dayInMonth)} of {month}).");
            _lastDayShown = days;
        }
        sb.Append('\n');
    }

    private void RenderMoons(StringBuilder sb, DateTimeOffset now)
    {
        if (_sky.SkyCapturedAt is null && _sky.PerceiveAt is null) return;

        sb.Append("Moons\n");
        sb.Append("──────────────────────────────────────\n");
        foreach (var moon in SkyState.Moons)
            sb.Append($" {moon,-9} {SkyState.Describe(_sky.MoonVisibility(moon))}\n");

        if (_sky.InfluenceLine.Length > 0)
            sb.Append($" Influence: {_sky.InfluenceLine}\n");
        if (_sky.FavoredLine.Length > 0)
            sb.Append($" Favored:   {_sky.FavoredLine}\n");
        sb.Append('\n');
    }

    private void RenderSky(StringBuilder sb, DateTimeOffset now)
    {
        if (_sky.SkyCapturedAt is null) return;

        sb.Append("Sky\n");
        sb.Append("──────────────────────────────────────\n");
        if (_sky.Conditions.Length > 0)
            sb.Append($" {_sky.Conditions}\n");
        var up = _sky.BodiesUp();
        if (up > 0)
            sb.Append($" {up} heavenly bodies above the horizon\n");
        sb.Append($" (sky read {Ago(now, _sky.SkyCapturedAt.Value)})\n");
    }

    private static string Ordinal(int n)
    {
        var suffix = (n % 100) is >= 11 and <= 13 ? "th"
                   : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return $"{n}{suffix}";
    }

    private static string Ago(DateTimeOffset now, DateTimeOffset then)
    {
        var s = (int)(now - then).TotalSeconds;
        if (s < 0) s = 0;
        return s < 90      ? $"{s}s ago"
             : s < 3600    ? $"{s / 60}m ago"
             :               $"{s / 3600}h ago";
    }
}
