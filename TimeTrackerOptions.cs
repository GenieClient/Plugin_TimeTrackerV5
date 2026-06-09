using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Genie.Plugins.TimeTracker;

/// <summary>
/// Display options, mirroring the Genie 4 <c>Time_Tracker.xml</c> &lt;Options&gt;
/// block. Defaults match the shipped file; a user copy at
/// <c>{AppData}/Genie5/Time_Tracker.xml</c> overrides them at load.
///
/// <para>The Genie 4 file also carried a &lt;Calculations&gt; block of epoch
/// offsets used to compute moon positions client-side. This port reads moon state
/// from DR's own <c>obs sky</c>/<c>perceive</c> output instead, so those offsets
/// are intentionally ignored.</para>
/// </summary>
internal sealed class TimeTrackerOptions
{
    public bool ShowElanthiaTime  = true;   // show the Elanthian date/time block
    public bool ShowLongNames     = true;   // "Moliko the Balance" vs "Moliko"
    public bool UseGameTime       = true;   // live-tick from the clock vs. show only the last reading
    public bool IncludeAnlasName  = true;   // show the "N roisaen before the Anlas of X" line
    public bool IncludeTimeOfDay  = true;   // show day/night
    public bool LogGameEvents     = false;  // host.Log() on new readings / day rollover

    /// <summary>Load options: the embedded defaults first, then a user override
    /// file if one exists. Never throws — a malformed file just keeps defaults.</summary>
    public static TimeTrackerOptions Load()
    {
        var o = new TimeTrackerOptions();
        TryApply(o, EmbeddedXml());
        var path = Path.Combine(DataDir(), "Time_Tracker.xml");
        if (File.Exists(path))
        {
            try { TryApply(o, File.ReadAllText(path)); } catch { /* keep what we have */ }
        }
        return o;
    }

    private static void TryApply(TimeTrackerOptions o, string? xml)
    {
        if (string.IsNullOrWhiteSpace(xml)) return;
        XElement? opts;
        try { opts = XDocument.Parse(xml).Root?.Element("Options"); }
        catch { return; }
        if (opts is null) return;

        o.ShowElanthiaTime = Bool(opts, "ShowElanthiaTime", o.ShowElanthiaTime);
        o.ShowLongNames    = Bool(opts, "ShowLongNames",    o.ShowLongNames);
        o.UseGameTime      = Bool(opts, "UseGameTime",      o.UseGameTime);
        o.IncludeAnlasName = Bool(opts, "IncludeAnlasName", o.IncludeAnlasName);
        o.IncludeTimeOfDay = Bool(opts, "IncludeTimeOfDay", o.IncludeTimeOfDay);
        o.LogGameEvents    = Bool(opts, "LogGameEvents",    o.LogGameEvents);
    }

    private static bool Bool(XElement parent, string name, bool fallback) =>
        bool.TryParse(parent.Element(name)?.Value, out var b) ? b : fallback;

    private static string? EmbeddedXml()
    {
        var asm  = Assembly.GetExecutingAssembly();
        var name = asm.GetManifestResourceNames()
                      .FirstOrDefault(n => n.EndsWith("Time_Tracker.xml", StringComparison.Ordinal));
        if (name is null) return null;
        using var s = asm.GetManifestResourceStream(name);
        if (s is null) return null;
        using var r = new StreamReader(s);
        return r.ReadToEnd();
    }

    /// <summary>Genie 5 per-platform data directory (mirrors the host's AppPaths).</summary>
    private static string DataDir()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Genie5");
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            return Path.Combine(home, "Library", "Application Support", "Genie5");
        var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        return Path.Combine(string.IsNullOrEmpty(xdg) ? Path.Combine(home, ".local", "share") : xdg, "Genie5");
    }
}
