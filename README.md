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

Every threat readout lives inside the game's own threat list — the stack of
`Missile [ARH] 5.2km` lines the game already draws — so it inherits the stock
font, layout, colours and flash instead of floating over the HUD.

### Missile defeat hint and time to impact

Each of the game's missile threat lines is extended in place with the
countermeasure that defeats the seeker, and a live time to impact:

```
Missile [IR] 2.1km  FLARE  4.2s
```

| Seeker | Hint |
| --- | --- |
| IR | `FLARE` |
| ARH / SARH | `NOTCH` |
| Optical | `HIDE` |
| ARAD | `RADAR OFF` |

An unrecognised seeker type — a new game version, say — still gets a generic
`DEFEND` rather than silently vanishing. The line keeps the game's own threat
colouring: yellow while the seeker searches, flashing red once it locks.

The closure maths behind the countdown is the same one the game's own AI pilot
uses to decide when to break and dispense, so the number agrees with what the
AI would do in your seat. It hides itself when nothing is actually gaining on
you, rather than showing a fictional number for a missile that has already been
defeated.

### Radar warning tags

The stock RWR draws an undifferentiated arrow per emitter: something is painting
you, but not what, and not how far along it is. This adds one line per emitter
to the same threat list, cloned from the game's own threat entry prefab and
coloured in the game's own threat vocabulary:

| Tag | Meaning | Style |
| --- | --- | --- |
| `Radar [MIG-29] SEARCH` | Sweeping you, no track. | Yellow, like a searching seeker. |
| `Radar [SAM] LOCK` | You are the tracked target. | The red/green flash of a locked seeker. |
| `Radar [MIG-29] LAUNCH` | A missile currently inbound was fired by this emitter. | Red blink, the missile warning light's cadence. |

Shooters sort first, then trackers, then everything merely sweeping — always
below the game's own missile entries, which are more urgent. Contacts age out
four seconds after their last sweep, matching the lifetime the game gives its
own warning icons.

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
| `SHOOT` | All requirements met, inside the no-escape zone. The game's own cue, untouched. |
| `IN RANGE` | All requirements met, between NEZ and Rmax. |

The cue lives in the game's own hint label beside the range ladder, in the
stock style — the label's colour is never written, so every cue renders exactly
as the game draws its own. The rejection reasons (`OUT OF RANGE`, `TOO CLOSE`,
`OUT OF ARC`, `TOO SLOW`) are left exactly as they are.

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

## Contact readouts

### Contact labels

Ground units are told apart at a glance because their icons differ — a tank does
not look like a radar. Every aircraft draws the same triangle, so the only way to
find out what is out there is to select a contact and read the target block, one
contact at a time.

This writes the type code beside each aircraft marker:

```
        ▽ KR-67  12.4km
                     ▽ FS-12  18km
   ▽ EW-25  22km
```

A four-ship now reads as four airframes without touching the target list — and
the AWACS orbiting behind them stops looking like a fighter.

| Key | Default | Effect |
| --- | --- | --- |
| `ContactLabels` | `true` | The labels. |
| `LabelFriendlyAircraft` | `false` | Label your own faction too. |
| `LabelRange` | `true` | Range after the type code. |
| `LabelMaxRange` | `25000` | Metres. Further out keeps the marker, drops the text. `0` for no limit. |
| `LabelTextScale` | `0.6` | Size relative to your HUD text. |

The code is the same `definition.code` the game's own target block prints, and
nothing here reveals a contact the game had already decided not to draw: the
marker was on the glass either way, this only says what it is. The selected
target gets no label, because the game is already annotating that one.

Labels inherit their marker's colour, so they dim with a stale track and flicker
under jamming exactly as the icon does. They are anchored to the markers rather
than to a place on the glass, so the layout table below does not reach them —
`LabelTextScale` is their only size control.

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

One config table controls offset, scale and visibility for the game's HUD
elements:

```ini
Elements = Climbrate:0,40;SpeedGauge:20,0,0.9;CountermeasureIndicator:0,0,1,false
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

Elements are named after the HUD component that drives them (`Climbrate`,
`CountermeasureIndicator`, `WeaponIndicator`, …) rather than by GameObject name,
because the component name is what the game's own code commits to. 28 of the
game's 30 HUD elements can be placed this way; `ArtificialHorizon` and
`GearIndicator` are the two exceptions, being the only elements that do not
route through the shared settings refresh this hooks.

This plugin's own readouts live inside game elements — the threat list, the
hint label, the target block, the fuel gauge — so they move and scale with
their hosts rather than having entries of their own. The contact labels follow
their markers around the glass and have their own `LabelTextScale` instead.

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
| Threats | `MissileDefeatHint` | `true` |
| Threats | `ImpactCountdown` | `true` |
| Threats | `RadarWarningTags` | `true` |
| Threats | `HideStockRadarWarning` | `false` |
| Threats | `CountermeasureColours` | `true` |
| Weapons | `ShootCue` | `true` |
| Weapons | `TargetDataBlock` | `true` |
| Contacts | `ContactLabels` | `true` |
| Contacts | `LabelFriendlyAircraft` | `false` |
| Contacts | `LabelRange` | `true` |
| Contacts | `LabelMaxRange` | `25000` |
| Contacts | `LabelTextScale` | `0.6` |
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

### How the readouts are drawn

There is no overlay canvas and nothing floats over the screen. Every readout
either **extends text the game already wrote** (the threat list entries, the
target block, the shoot hint) or is a **clone of a real HUD label parented next
to its host** (the fuel time under the fuel gauge, the radar tags in the threat
list, the contact labels in the icon layer). Extending in place inherits the
font, the player's HUD colour and text size, the material, the projection and
the game's own flash cadences for free — which is why every readout sits on the
glass like the game drew it.

Readout text is deliberately **ASCII only**. The HUD font is the game's own and
has no reason to carry an interpunct, an arrow or a multiplication sign, and a
missing glyph on a missile warning is the worst possible place to discover that.

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
