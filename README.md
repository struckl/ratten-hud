# Ratten HUD

Combat HUD readouts for
[Nuclear Option](https://store.steampowered.com/app/2168680/Nuclear_Option/).

The game already knows when a missile will hit you, which countermeasure defeats
its seeker, whether the radar sweeping you has locked, and whether your shot is
inside the envelope. It just does not always say so. Ratten HUD puts that on the
glass.

It is the silent twin of [Bitching Ratte](../Bitching%20Ratte): where that plugin
speaks, this one draws. The two are designed to run together — nothing here
duplicates a callout, and neither plugin depends on the other.

## Threat readouts

### Missile threat banner

A flashing banner naming the countermeasure for every inbound seeker type at
once, one line per type, most urgent first:

| Seeker | Banner | Colour |
| --- | --- | --- |
| IR | `MISSILE · IR → FLARE` | orange |
| ARH / SARH | `MISSILE · RADAR → NOTCH` | red |
| Optical | `MISSILE · OPTICAL → HIDE` | white |
| ARAD | `MISSILE · ARAD → RADAR OFF` | magenta |

Two missiles of the same type collapse into one line with a count suffix
(`MISSILE · IR → FLARE ×3`). An unrecognised seeker type — a new game version,
say — still shows as a generic `DEFEND` line rather than silently vanishing.

The whole banner strobes off the closest missile's clock, ramping from a slow
2 Hz pulse at 15 seconds out to a 10 Hz strobe inside 2 seconds.

### Time to impact

Under the banner, a live countdown for the closest inbound: `IMPACT 4.2s`. It
turns "am I flaring too early" into a number.

The closure maths is the same one the game's own AI pilot uses to decide when to
break and dispense, so the readout agrees with what the AI would do in your
seat. The countdown hides itself when nothing is actually gaining on you, rather
than showing a fictional number for a missile that has already been defeated.

### Radar warning tags

The stock RWR draws an undifferentiated arrow per emitter: something is painting
you, but not what, and not how far along it is. This adds a strobe list tagged
by state and named by unit type:

| Tag | Meaning |
| --- | --- |
| `SEARCH` | Sweeping you, no track. |
| `LOCK` | You are the tracked target. |
| `LAUNCH` | A missile currently inbound was fired by this emitter. Flashes. |

Contacts age out four seconds after their last sweep, matching the lifetime the
game gives its own warning icons.

`HideStockRadarWarning` suppresses the game's own directional wedges so the
tagged list is the only radar warning display. It gates icon creation only —
the sweep still runs, so the warning tone and the tags are untouched, unlike
disabling the receiver outright.

### Countermeasure state of charge

The stock indicator is green until the moment it runs dry, then grey — no
warning while the load drains. This repaints it as a state of charge: green
above half, amber, red below a fifth, and a flashing red when empty. Flares show
a count, a jammer shows charge percent.

Full load is learned per airframe from the highest count seen, so it is correct
on anything from a light fighter to a bomber.

## Weapon readouts

### In-range / SHOOT cue

The game computes Rmin, Rmax and the no-escape range every second, and decides
whether every firing requirement is met — then only ever shows the `SHOOT` hint
*inside* the no-escape zone:

```csharp
hint.enabled = maxTargetDist < noEscapeRange;   // HUDMissileState.DisplayText
```

Between NEZ and Rmax you have a valid firing solution and no cue at all. Ratten
HUD surfaces the state the game is already tracking:

| Cue | Condition |
| --- | --- |
| `SHOOT` | All requirements met, inside the no-escape zone. Flashes. |
| `IN RANGE` | All requirements met, between NEZ and Rmax. |

The game's own rejection reasons (`OUT OF RANGE`, `TOO CLOSE`, `OUT OF ARC`,
`TOO SLOW`) are left exactly as they are.

The cue lives in the game's own hint label, beside the range ladder.
`ShootCueOverlay` can additionally draw it as a line under the centre of the
HUD, but that is **off by default** — the game already prints MAX, MIN and NEZ
next to the ladder, so it largely duplicates them, and it sits on the overlay
canvas that draws above every other view.

### Target data block

Extends the selected target readout from "who and how far" to what you need for
an intercept:

```
Ratte
MIG-29
  12.4 km
  340 m/s closing
  HOT 22°
  ANGELS 8200 m
```

Aspect is measured from the target's nose to the reciprocal of the line of
sight — `HOT` under 45°, `BEAM` through the middle, `COLD` past 135°.

It is deliberately one compact line at 70% of the surrounding text size. This
readout is anchored to the target marker, so every extra line pushes it further
across the glass — and with several targets selected there are several markers
competing for the same space.

## Flight readouts

### Fuel time

An estimated remaining fuel time in minutes, `(42m)`, under the fuel gauge
reading. The estimate comes from the fuel actually burned between samples, so it
follows your current throttle setting rather than assuming a fixed rate — it
will drop hard in afterburner and recover when you pull back.

Ported from MKMods, which has been retired in favour of the standalone plugins.
The matching voice callouts ("fuel low", "bingo fuel") live in
[Bitching Ratte](../Bitching%20Ratte).

## Declutter

### Objective label

The objective overlay's name-and-range text is hidden by default, leaving just
the circle, the dot and the off-screen pointer.

| `ObjectiveLabel` | Result |
| --- | --- |
| `Hidden` *(default)* | Circle only, no text. |
| `DistanceOnly` | Range, no objective name. |
| `Full` | Stock label. |

### Hidden markers

`HiddenMarkerUnits` is a comma-separated, case-insensitive list of units that
get no HUD marker. They stay on the map — this filters
`CombatHUD.CreateMarker`, and the map draws from `DynamicMap` instead.

Defaults to `pilot`, to keep downed pilots off the glass.

> The game has no single class for a downed pilot — `Pilot` is not a `Unit` —
> so this matches on the unit's display name, type code and object name rather
> than on a type. If it catches too much or too little, check the unit's name on
> the map and adjust the list.

## Layout

### Element offsets and declutter

One config table controls offset, scale and visibility for every HUD element,
the game's own and this plugin's alike:

```ini
Elements = Climbrate:0,40;RadarWarnings:20,0,0.9;CountermeasureIndicator:0,0,1,false
```

Each entry is `Name:xOffset,yOffset[,scale][,visible]`. Positive Y is up, offsets
are in 1080p reference pixels, and a trailing `false` hides the element outright.
Deleting an entry restores the element to exactly what it was before this plugin
touched it, rather than freezing the last value.

Elements with **no rule are never written to at all**. This hook runs against
every HUD element on every settings refresh, so normalising unruled elements
would flatten prefab scaling on gauges that ship scaled, re-show elements the
game deliberately hid, and undo other plugins' positioning. `scale` multiplies
the element's existing scale rather than replacing it, for the same reason.

Game elements are named after the HUD component that drives them (`Climbrate`,
`CountermeasureIndicator`, `WeaponIndicator`, …) rather than by GameObject name,
because the component name is what the game's own code commits to. 28 of the
game's 30 HUD elements can be placed this way; `ArtificialHorizon` and
`GearIndicator` are the two exceptions, being the only elements that do not
route through the shared settings refresh this hooks.

This plugin's readouts register under `MissileBanner`, `ImpactCountdown`,
`RadarWarnings`, `ShootCue` and `TargetData`.

#### The climb rate default

The table defaults to `Climbrate:0,40`. That is the old MKMods
`ClimbRateVerticalOffset`, which now lives here — the stock climb rate readout
sits low enough to collide with its neighbours.

If you still run MKMods alongside this, set its `ClimbRateVerticalOffset` to `0`
first, or the readout moves twice.

## Configuration

Every feature has its own switch in
`BepInEx/config/dev.sewerlabs.rattenhud.cfg`, written on first run.

| Section | Key | Default |
| --- | --- | --- |
| Threats | `MissileBanner` | `true` |
| Threats | `ImpactCountdown` | `true` |
| Threats | `RadarWarningTags` | `true` |
| Threats | `HideStockRadarWarning` | `false` |
| Threats | `CountermeasureColours` | `true` |
| Weapons | `ShootCue` | `true` |
| Weapons | `ShootCueOverlay` | `false` |
| Weapons | `TargetDataBlock` | `true` |
| Flight | `FuelTimeReadout` | `true` |
| Flight | `FuelTimeUpdateRate` | `10` |
| Layout | `Enabled` | `true` |
| Layout | `Elements` | `Climbrate:0,40` |
| Declutter | `ObjectiveLabel` | `Hidden` |
| Declutter | `HideMarkers` | `true` |
| Declutter | `HiddenMarkerUnits` | `pilot` |

The layout table re-applies live when the config file changes, so you can nudge
elements without restarting.

### Nothing showing up?

**The shoot cue and the countermeasure colours need the game's own Weapons HUD
turned on** (Settings → HUD → Weapons). Both ride on elements the game does not
draw at all when it is off, so with `HUDWeapons = 0` you get no `IN RANGE`, no
countermeasure indicator and no flare count — from the game or from this plugin.
`PlayerSettings.hudWeapons` is checked deliberately rather than forced, so the
setting keeps meaning what it says.

The plugin logs one diagnostic line the first time you are in a cockpit, naming
every gate a missing readout can be stuck behind:

```
[Info   :Ratten HUD] Overlay: canvas=True, font=LegacyRuntime, elements=4, inCockpit=True, ...
```

`font=<null>` means the overlay text is drawing with no font, which renders
nothing. `inCockpit=False` means the canvas readouts are clearing themselves
every frame. Either way the patch-based readouts carry on working, which is what
makes the failure look selective.

## Building

Requires the .NET SDK and a local Nuclear Option install. The build copies the
plugin straight into `BepInEx/plugins/RattenHUD`:

```bash
dotnet build -c Release
```

Override the game directory if yours is elsewhere:

```bash
dotnet build -c Release -p:GameDirectory="D:\SteamLibrary\steamapps\common\Nuclear Option"
```

## Installation

Requires [BepInEx 5](https://github.com/BepInEx/BepInEx). Drop the `RattenHUD`
folder into `BepInEx/plugins`.

---

HOSTED IN THE SEWERS · POWERED BY CHEESE & CLAUDE
