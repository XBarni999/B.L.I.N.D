# B.L.I.N.D.

**Best Luminescence & Infrared Navigation Device** — cockpit IR color grading,
and allied ground/naval laser designation for **Nuclear Option**.

[Download](https://github.com/XBarni999/B.L.I.N.D/releases) ·
[Report an issue](https://github.com/XBarni999/B.L.I.N.D/issues) ·
[Changes](CHANGELOG.md)

Press **F7** to toggle **STANDARD IR ↔ IRONBOW**. Both use the game's original
IR camera. Ironbow only changes its color lookup; target geometry, heat,
clouds, explosions, and depth remain the game's own rendering.

## What it does

### 1. Cockpit IR
- **STANDARD IR**: The game's original IR view with the existing daylight exposure adjustment.
- **IRONBOW**: The same IR view with a color lookup applied to its brightness.
- BLIND does not redraw targets or effects, and it does not calculate separate heat values.

### 2. Allied Ground & Naval Laser Designation (JTAC)
- **Multi-Target Designation**: Select multiple hostile surface or naval targets in your weapon manager. Allied units will automatically and greedily pair with available targets (1 ally lases 1 target) based on proximity and line of sight.
- **Naval Support**: Allied warships (`Ship`) can act as JTAC designators, and hostile warships can be designated. Ships utilize an extended **15 km optical horizon** and elevated superstructure sighting origins.
- **Seeker Memory & Evasion Persistence**: When launching laser-guided weapons (e.g. AGM-48, laser bombs), the missile seeker memorizes its designated target. You can safely turn away, maneuver to evade air defenses, or deselect the target without breaking missile lock while an ally continues designation.
- **Subtle HUD Telemetry**: Minimalist, non-intrusive corner brackets (`┌ ┐ └ ┘`) track each designated target on the HUD with exact telemetry, accompanied by a clean status banner (`JTAC [2 TGT] • 4.2 KM`).

## IR air-to-ground attacks (0.7.0)

Aircraft-carried IR missiles whose primary role is anti-air can also engage ground
vehicles and buildings. Select a ground target and keep it inside the weapon's
native firing arc. The `IR A/G` cue shows acquisition progress and launch blockers.
A small target-following circle pulses amber during acquisition, turns green and
shrinks after lock, and stays dim grey while launch conditions are unmet.

- Ground slant range is **85% of the weapon's native maximum range**.
- Ground acquisition takes **1.25 seconds** continuously. Changing the target or
  weapon, losing visibility, or leaving the native arc/range resets acquisition.
- Air-to-air behavior, warhead damage, missile physics and native IR guidance are unchanged.
- For units without an IR source, a temporary source is passed to the native seeker
  during initialization; it does not alter the sensor renderer or persist on the unit.
- Install the same build/configuration on the host and participating clients.

`IRGroundAttack.Enabled`, `RangeFraction` (0.85) and `LockTime` (1.25) are configurable.
Range uses the weapon's configured maximum, rather than a claimed guaranteed hit distance.

### Modded missile compatibility

Detection is automatic and uses components/weapon roles, rather than vanilla names
or a fixed whitelist. Modded aircraft-carried weapons are included when they have
`WeaponInfo.missile = true`, `antiAir > antiSurface`, and an `IRSeeker` component
on their projectile prefab. Late-loaded Blueprinter weapons use the same checks.

Installed Meridian Works code was checked and uses the native `IRSeeker` path.
This is a code compatibility check, not a firing test of every modded weapon.
Custom seekers that replace native IR initialization or launch handling may need
separate integration. Radar-guided missiles and surface-launched SAMs are excluded.

## Requirements

- **Windows x64**, Nuclear Option **0.34.x** (compatible with 0.34.1+).
- **BepInEx 5 for Unity Mono** installed and working.
- `BLIND.dll` version 0.7.0 or newer. The old shader bundle is no longer needed.

BLIND is standalone. Its repository and release contain only BLIND files.

## Install or update

1. Close the game.
2. When updating, back up the old BLIND files **outside** `BepInEx/plugins`.
   Remove the obsolete `blind-thermal.bundle` and keep one installed `BLIND.dll`.
3. Extract `BLIND.zip` into the game folder. The resulting files should be:

```text
Nuclear Option/
└─ BepInEx/plugins/BLIND/
   └─ BLIND.dll
```

4. Launch the game, enter an aircraft, select a target so its cockpit screen is
   active, and press **F7**. The configuration is created at
   `BepInEx/config/ua.ncmod.blind.cfg`.

Restart after updating. Replacing files does not reload an already running mod.
To uninstall, close the game and remove `BLIND.dll`.

## Multiplayer and JTAC

Sensor modes are local client visual changes. JTAC designation requires host authority and is driven by the **host player's local aircraft**; client-only installations retain the IR modes but cannot originate JTAC designations. Vanilla explosion effects are unchanged.

## Configuration

Edit `BepInEx/config/ua.ncmod.blind.cfg` with the game closed, or use the BepInEx Configuration Manager.

| Setting | Default | Purpose |
|---|---:|---|
| **Sensors** | | |
| `Sensors.CycleModeKey` | `F7` | Toggle STANDARD IR and IRONBOW |
| **JTAC** | | |
| `JTAC.Enabled` | `true` | Enable allied ground and naval laser designation on host |
| `JTAC.GroundLaserRange` | `4000` | Maximum observer-to-target designation range for ground vehicles (m) |
| `JTAC.NavalLaserRange` | `15000` | Maximum observer-to-target designation range for naval ships (m) |
| `JTAC.AircraftReceiveRange` | `18000` | Maximum aircraft datalink cue range (m) |
| `JTAC.MaxDesignationTime` | `20` | Continuous designation time in seconds |
| `JTAC.DesignatorCooldown` | `12` | Observer cooldown after designation cycle in seconds |
| `JTAC.MaxConcurrentDesignations` | `4` | Maximum simultaneous allied designations |

## Build

From the repository root, with a .NET SDK and .NET Framework 4.7.2 targeting support:

```powershell
dotnet restore B.L.I.N.D/BLIND.csproj --configfile B.L.I.N.D/NuGet.Config
dotnet build B.L.I.N.D/BLIND.csproj -c Release --no-restore -p:GameDir="F:\path\to\Nuclear Option"
```

Game and BepInEx assemblies are referenced from your installation and are not
redistributed. No separate Unity shader project or asset bundle is required.

The compiled DLL is written to `B.L.I.N.D/bin/Release/BLIND.dll`.
See [validation notes](VALIDATION.md) for the checks and their limits.

Created by **XBarni999** for Nuclear Option by **Shockfront Studios**.
