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
`TOO SLOW`) are left exactly as they are. An overlay line adds the numbers
behind the cue: current range against Rmax and NEZ.

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

## Layout

### Element offsets and declutter

One config table controls offset, scale and visibility for every HUD element,
the game's own and this plugin's alike:

```ini
Elements = Climbrate:0,40;RadarWarnings:20,0,0.9;CountermeasureIndicator:0,0,1,false
```

Each entry is `Name:xOffset,yOffset[,scale][,visible]`. Positive Y is up, offsets
are in 1080p reference pixels, and a trailing `false` hides the element outright.
Deleting an entry restores the element to its stock position rather than freezing
the last value.

Game elements are named after the HUD component that drives them (`Climbrate`,
`CountermeasureIndicator`, `WeaponIndicator`, …) rather than by GameObject name,
because the component name is what the game's own code commits to.

This plugin's readouts register under `MissileBanner`, `ImpactCountdown`,
`RadarWarnings`, `ShootCue` and `TargetData`.

The default table is `Climbrate:0,40`, which reproduces the old MKMods
`ClimbRateVerticalOffset` tweak — that setting is now just one row in this table.

## Configuration

Every feature has its own switch in
`BepInEx/config/dev.sewerlabs.rattenhud.cfg`, written on first run.

| Section | Key | Default |
| --- | --- | --- |
| Threats | `MissileBanner` | `true` |
| Threats | `ImpactCountdown` | `true` |
| Threats | `RadarWarningTags` | `true` |
| Threats | `CountermeasureColours` | `true` |
| Weapons | `ShootCue` | `true` |
| Weapons | `TargetDataBlock` | `true` |
| Layout | `Enabled` | `true` |
| Layout | `Elements` | `Climbrate:0,40` |

The layout table re-applies live when the config file changes, so you can nudge
elements without restarting.

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
