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

Two threat readouts **on the glass**, drawn exactly like the fuel time and
climb rate readouts: clones of a real HUD label, in the HUD font, sitting
inside the projected HUD with the flight readouts rather than over the map. The
game's own threat list above the map is left exactly as it is — this is the
same information said where you are actually looking.

Missile warnings are a red block centred just above the weapon hint (where
`SHOOT` and `OUT OF RANGE` appear), because that is where you are looking when
the answer matters. The block grows upwards: the soonest impact is always the
fixed bottom line.

```
[ARH] 5.2km  NOTCH  9.4s
[IR] 2.0km  FLARE  2.3s
```

Radar warnings list in the right-hand flight column — the layout table pulls
the altitude block up under the climb rate (`Altitude:0,28`), and the radar
list takes the space that frees below it:

```
[MIG-29] LOCK
[RDR] x3
```

There are no `Missile`/`Radar` prefixes: the bracketed code and the tag already
say it, and short lines read faster in combat. Both blocks move via the layout
table, as `Missiles` and `Threats` respectively (e.g. `Elements = Threats:100,-50`).

### Missile defeat hint and time to impact

One line per inbound missile, with the countermeasure that defeats the seeker
and a live countdown:

| Seeker | Hint |
| --- | --- |
| IR | `FLARE` |
| ARH / SARH | `NOTCH` |
| Optical | `HIDE` |
| ARAD | `RADAR OFF` |

An unrecognised seeker type — a new game version, say — still gets a generic
`DEFEND` rather than silently vanishing.

The closure maths behind the countdown is the same one the game's own AI pilot
uses to decide when to break and dispense, so the number agrees with what the
AI would do in your seat. It hides itself when nothing is actually gaining on
you, rather than showing a fictional number for a missile that has already been
defeated.

### Radar warning tags

The stock RWR draws an undifferentiated arrow per emitter: something is painting
you, but not what, and not how far along it is. This lists each emitter in the
right-hand flight column, tagged by state and named by unit type:

| Tag | Meaning |
| --- | --- |
| `[RDR] x3` | Sweeping you, no track — pinged three times recently. |
| `[SAM] LOCK` | You are the tracked target. |
| `[MIG-29] LAUNCH` | A missile currently inbound was fired by this emitter. |

A radar that pings you once and never again is noise, so a single ping is not
shown at all — the list starts at `x2`. A lock or a launch shows immediately,
however fresh the contact.

Shooters sort first, then trackers, then everything merely sweeping. Contacts
age out four seconds after their last sweep, matching the lifetime the game
gives its own warning icons — which also resets the ping count, so a slow
scanner that only finds you every five seconds stays off the glass.

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

### Contact symbols

Ground units are told apart at a glance because their icons differ — a tank does
not look like a radar. Every aircraft draws the same triangle, so a fighter, a
bomber and the AWACS behind them are one shape repeated, and the only way to
tell them apart is to select each in turn and read the target block.

This puts the number out of the type code inside the marker:

```
   /12\        /81\        /25\        /46\
   FS-12       SFB-81      EW-25       SAH-46
```

Numbers rather than letters, because the roster collides on its letters and not
on its numbers: FS-12 and FS-20 are both `F`, SAH-46 and SFB-81 are both `S`,
while 12, 20, 22, 25, 30, 46, 49, 67 and 81 are all distinct.

| Key | Default | Effect |
| --- | --- | --- |
| `Aircraft type symbols` | `true` | The symbols. |
| `Symbols on friendly aircraft` | `true` | Mark your own faction too. |
| `Symbol size` | `0.2` | Symbol size as a fraction of the marker. |

Inside the icon it costs no space at all — the triangle was already there. This
started as a label beside each marker, which is exactly the mistake it looks
like once a dozen contacts are each trailing text.

`Symbol size` follows your HUD icon size and the marker's own distance shrink, so
a far contact gets a smaller symbol and below five pixels gets none rather than
a smudge. It is read every frame, so editing it while the game runs resizes the
symbols as you save — no restart to find the size you want.

A fifth of the marker is already a readable symbol, because the marker's rect is
a good deal larger than the triangle drawn inside it. How much larger is a
property of a sprite this plugin cannot measure, so the first symbol of each
session logs what it worked from:

```
[Info   :Ratten HUD] ContactGlyphs: marker rect=1.0 x scale=37.5 = 37.5, glyph=8 at GlyphScale=0.20, HUD text=40
```

Symbols inherit their marker's colour and dim with a stale track and flicker
under jamming exactly as the icon does. The selected target gets none, because
the game already prints its full code on that marker. Nothing here reveals a
contact the game had decided not to draw: the marker was on the glass either
way, and the number is the same `definition.code` the target block shows.

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

| `Objective label` | Result |
| --- | --- |
| `Hidden` *(default)* | Circle only, no text. |
| `DistanceOnly` | Range, no objective name. |
| `Full` | Stock label. |

### Hidden markers

`Hidden unit types` is a comma-separated, case-insensitive list of units that
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
Element layout = Climbrate:0,40;SpeedGauge:20,0,0.9;CountermeasureIndicator:0,0,1,false
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

This plugin's threat readout registers as `Threats` and can be moved and
scaled like any game element. The other readouts live inside game elements —
the hint label, the target block, the fuel gauge — so they move and scale with
their hosts rather than having entries of their own.

#### The climb rate default

The table defaults to `Climbrate:0,40`. That is the old MKMods
`ClimbRateVerticalOffset`, which now lives here — the stock climb rate readout
sits low enough to collide with its neighbours.

If you still run MKMods alongside this, set its `ClimbRateVerticalOffset` to `0`
first, or the readout moves twice.

## Configuration

Every feature has its own switch in
`BepInEx/config/dev.sewerlabs.rattenhud.cfg`, written on first run. `Enable
mod` in `1. General` is the master switch for everything at once.

| Section | Key | Default |
| --- | --- | --- |
| 1. General | `Enable mod` | `true` |
| 2. Threat warnings | `Missile defeat hint` | `true` |
| 2. Threat warnings | `Impact countdown` | `true` |
| 2. Threat warnings | `Radar warning tags` | `true` |
| 2. Threat warnings | `Hide stock radar arrows` | `false` |
| 2. Threat warnings | `Countermeasure colours` | `true` |
| 3. Weapons | `Extended shoot cue` | `true` |
| 3. Weapons | `Target data block` | `true` |
| 4. Contacts | `Aircraft type symbols` | `true` |
| 4. Contacts | `Symbols on friendly aircraft` | `true` |
| 4. Contacts | `Symbol size` | `0.2` |
| 5. Flight info | `Fuel time readout` | `true` |
| 5. Flight info | `Fuel check interval (seconds)` | `10` |
| 6. HUD layout | `Enable custom layout` | `true` |
| 6. HUD layout | `Element layout` | `Climbrate:0,40;Altitude:0,28` |
| 7. Declutter | `Objective label` | `Hidden` |
| 7. Declutter | `Hide chosen unit markers` | `true` |
| 7. Declutter | `Hidden unit types` | `pilot` |

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

Nothing floats over the screen on a canvas of this plugin's own. Every readout
either **extends text the game already wrote** (the target block, the shoot
hint) or is a **clone of a real HUD label parented into the HUD canvas** (the
threat block, the fuel time under the fuel gauge). Cloning a live label
inherits the HUD font, the player's HUD colour and text size, the material and
the projection for free — which is why every readout sits on the glass like
the game drew it.

The clone source is the climb rate label, taken from `HUDApp.RefreshSettings`:
a plain single line of body text present on every airframe, so the copy is not
styled as something outsized or pre-coloured for a warning. If a threat block
ever goes missing, the plugin logs one diagnostic line the first time you are
in a cockpit, naming every gate it can be stuck behind.

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
