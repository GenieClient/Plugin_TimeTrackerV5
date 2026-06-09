# Plugin_TimeTrackerV5

The **Time Tracker** plugin for [Genie 5](https://github.com/GenieClient/Genie5) —
a port of the Genie 4 `TimeTracker` plugin.

It shows **Elanthian time** and the **state of the sky** — the sun, the three
moons (Katamba, Xibar, Yavash), the constellations, the weather, and (for a Moon
Mage) the celestial influences — in its own **Time Tracker** dock window:

```
Elanthian Time
──────────────────────────────────────
 38th of Moliko the Balance
 Year of the Bronze Wyvern  (fall, night)
 456 years, 277 days since the Victory of Lanival
 ~8 roisaen before the Anlas of Asketi's Hunt

Moons
──────────────────────────────────────
 Katamba   up (clear)
 Xibar     up (clear)
 Yavash    below the horizon
 Influence: Yavash is dominant, while Xibar and Katamba's influences are strong.
 Favored:   Perception and Psychic Projection

Sky
──────────────────────────────────────
 Thousands of stars twinkle merrily from clear autumn skies.
 17 heavenly bodies above the horizon
 (sky read 4s ago)
```

## How it works — parse-first, fully passive

The Genie 4 plugin computed moon positions **client-side** from epoch offsets
stored in `Time_Tracker.xml`. Because the moons' periods are non-constant
averages (gravitational interaction between the three bodies), that approach is
inherently approximate.

This port is **parse-first**: it reads DragonRealms' own output for the `time`,
`weather`, `obs sky` and `perceive` verbs **as you naturally type them** and
reflects it. It therefore:

- **sends no commands by default** — it just enriches what you already do;
- **can never get the moon state subtly wrong** — it shows exactly what the game
  reported;
- **live-ticks the time-of-day** from the system clock between readings. An anlas
  is 30 roisaen and a roisaen is one Earth minute, so the *"N roisaen before the
  Anlas of X"* countdown advances on its own and rolls through the twelve anlaen
  (and the date) without another round-trip.

> **Note:** the live tick assumes real wall-clock time, so it's only meaningful in
> a **live session** — in a replay of a recording the clock has no relation to the
> recorded moment (the same caveat that applies to roundtime display in replay).

The plugin has **no UI dependency** — it writes formatted text to a *named window*
via the host API (`SetWindow("Time Tracker", …)`), and the host surfaces that
window as a dock panel. It references only `Genie.Plugins.Abstractions`.

## Requirements

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (to build).
- A Genie 5 install with the plugin system (host version ≥ 5.0).

## Build

```sh
dotnet build -c Release
```

Output: `bin/Release/net8.0/Plugin_TimeTrackerV5.dll`. The project references the
Genie 5 plugin contract (`Genie.Plugins.Abstractions`) from a committed copy in
`lib/`, so no NuGet feed is required to compile.

## Install

Copy `Plugin_TimeTrackerV5.dll` into your Genie 5 plugins folder:

- **Windows:** `%APPDATA%\Genie5\Plugins\`
- **macOS:** `~/Library/Application Support/Genie5/Plugins/`
- **Linux:** `~/.local/share/Genie5/Plugins/` (or `$XDG_DATA_HOME/Genie5/Plugins/`)

Then **reconnect**, or **Plugins → Load → Plugin_TimeTrackerV5.dll**, or
`#plugin load Plugin_TimeTrackerV5`. Show the window via
**Window → Plugin Windows → Time Tracker**.

## Use

Just play. The window updates whenever you type any of:

```
time
weather
obs sky          (note: the verb is "obs sky" — a bare "sky" is not recognised)
perceive         (Moon Mage only — adds the influence / favoured-spell lines)
```

Commands:

| Command | What it does |
|---|---|
| `/tt` | Repaint / open the Time Tracker window from the latest readings. |
| `/tt refresh` | Send `time` + `obs sky` to the game to pull a fresh reading (the only path that sends commands — user-initiated). |
| `/tt help` | Show the command summary. |

`/timetracker` is accepted as the long form of `/tt`.

## Options

Mirrors the Genie 4 `Time_Tracker.xml` `<Options>` block. Defaults are embedded;
drop a copy at `{AppData}/Genie5/Time_Tracker.xml` to override:

| Option | Default | Effect |
|---|---|---|
| `ShowElanthiaTime` | True | Show the Elanthian date/time block. |
| `ShowLongNames` | True | "Moliko the Balance" vs. the short "Moliko". |
| `UseGameTime` | True | Live-tick from the clock vs. showing only the last reading verbatim. |
| `IncludeAnlasName` | True | Show the "N roisaen before the Anlas of X" line. |
| `IncludeTimeOfDay` | True | Show day/night. |
| `LogGameEvents` | False | `Log()` a diagnostic line on each reading and on day rollover. |

The Genie 4 `<Calculations>` epoch offsets are retained in the shipped file for
format familiarity but are **not used** (see *How it works*).

## License

[GPL-3.0](LICENSE) — same as Genie 5 and the Genie 4 ecosystem.

## Credits

Behaviour ported from the Genie 4 `TimeTracker` plugin
([GenieClient/Plugins](https://github.com/GenieClient/Plugins)). Elanthian
calendar data from [Elanthipedia](https://elanthipedia.play.net/Elanthian_time).
