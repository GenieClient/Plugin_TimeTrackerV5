using System.Text.RegularExpressions;

namespace Genie.Plugins.TimeTracker;

/// <summary>Visibility of a single heavenly body, as reported by <c>obs sky</c>.</summary>
internal enum Visibility { Unknown, Clear, Cloudy, BelowHorizon }

/// <summary>
/// Last-known state of the sky, parsed from DR's own output. Sources:
/// <list type="bullet">
/// <item><c>obs sky</c> — the planet Dawgolesh, sixteen constellations and the
///   three moons, each "unobscured by clouds" / "obscured by clouds" /
///   "below the horizon";</item>
/// <item><c>weather</c> — the prevailing conditions line;</item>
/// <item><c>perceive</c> — (Moon Mage) which moon is dominant, the others'
///   influence strength, and the favoured spell types.</item>
/// </list>
/// All parsing is line-based; <see cref="Feed"/> is fed one game-text line at a
/// time and keeps just enough state to assemble the multi-line <c>obs sky</c> block.
/// </summary>
internal sealed class SkyState
{
    public static readonly string[] Moons = { "Katamba", "Xibar", "Yavash" };

    public DateTimeOffset? SkyCapturedAt;
    public string Conditions = "";                                   // "clear autumn skies"
    public readonly Dictionary<string, Visibility> Bodies = new(StringComparer.Ordinal);

    public DateTimeOffset? PerceiveAt;
    public string InfluenceLine = "";                                // raw perceive line 1
    public string FavoredLine   = "";                                // "Perception and Psychic Projection"

    private bool _inScan;            // inside an `obs sky` body list
    private bool _expectCondLine;    // next line is the conditions description

    // "The planet Dawgolesh is unobscured by clouds." / "Katamba is below the horizon."
    private static readonly Regex BodyRe = new(
        @"^(?:The planet |The )?(.+?) is (unobscured by clouds|obscured by clouds|below the horizon)\.$",
        RegexOptions.Compiled);

    // "Perception and Psychic Projection spells are favored."
    private static readonly Regex FavoredRe = new(
        @"^(.+?) spells are favou?red\.$", RegexOptions.Compiled);

    // "Yavash is dominant, while Xibar and Katamba's influences are strong."
    private static readonly Regex DominantRe = new(
        @"\bis dominant\b", RegexOptions.Compiled);

    /// <summary>Feed one game-text line. Returns true if it was a sky/weather/
    /// perceive line the tracker consumed.</summary>
    public bool Feed(string line, DateTimeOffset now)
    {
        var t = line.Trim();

        // ── obs sky: begin / body lines / end ──────────────────────────────────
        if (t == "The following heavenly bodies are visible:")
        {
            Bodies.Clear();
            SkyCapturedAt = now;
            _inScan = true;
            return true;
        }
        if (_inScan)
        {
            // The scan ends at the roundtime, a prompt, or any non-body line.
            if (t.StartsWith("Roundtime:", StringComparison.Ordinal) || t.Length == 0)
            {
                _inScan = false;
                return t.StartsWith("Roundtime:", StringComparison.Ordinal);
            }
            var b = BodyRe.Match(t);
            if (b.Success)
            {
                Bodies[b.Groups[1].Value.Trim()] = Parse(b.Groups[2].Value);
                return true;
            }
            _inScan = false;   // unexpected line — fall through to other matchers
        }

        // ── conditions line (follows the "glance up" / "scan the sky" intro) ────
        if (t == "You glance up at the sky." || t == "You scan the sky from horizon to horizon.")
        {
            _expectCondLine = true;
            return true;
        }
        if (_expectCondLine && t.Length > 0)
        {
            Conditions    = t;
            SkyCapturedAt = now;
            _expectCondLine = false;
            return true;
        }

        // ── perceive (Moon Mage) ───────────────────────────────────────────────
        if (DominantRe.IsMatch(t) && Moons.Any(mn => t.Contains(mn, StringComparison.Ordinal)))
        {
            InfluenceLine = t;
            PerceiveAt    = now;
            return true;
        }
        var fav = FavoredRe.Match(t);
        if (fav.Success)
        {
            FavoredLine = fav.Groups[1].Value.Trim();
            PerceiveAt  = now;
            return true;
        }

        return false;
    }

    public Visibility MoonVisibility(string moon) =>
        Bodies.TryGetValue(moon, out var v) ? v : Visibility.Unknown;

    /// <summary>Count of constellations/planet (i.e. non-moon bodies) currently
    /// above the horizon, for the summary line.</summary>
    public int BodiesUp() =>
        Bodies.Count(kv => !Moons.Contains(kv.Key, StringComparer.Ordinal)
                           && kv.Value is Visibility.Clear or Visibility.Cloudy);

    public static string Describe(Visibility v) => v switch
    {
        Visibility.Clear        => "up (clear)",
        Visibility.Cloudy        => "up (cloudy)",
        Visibility.BelowHorizon => "below the horizon",
        _                        => "unknown",
    };

    private static Visibility Parse(string phrase) => phrase switch
    {
        "unobscured by clouds" => Visibility.Clear,
        "obscured by clouds"   => Visibility.Cloudy,
        "below the horizon"    => Visibility.BelowHorizon,
        _                      => Visibility.Unknown,
    };
}
