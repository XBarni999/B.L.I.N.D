# B.L.I.N.D.

**Best Luminescence & Infrared Navigation Device** — cockpit thermal imaging and
friendly ground laser designation for **Nuclear Option**.

[Download](https://github.com/XBarni999/B.L.I.N.D/releases) ·
[Report an issue](https://github.com/XBarni999/B.L.I.N.D/issues) ·
[Changes](CHANGELOG.md)

Press **F7** to cycle **STANDARD IR → LONGBOW → IR BLACK**.
The thermal pass runs on the cockpit target camera; the outside view and the
original vehicle materials keep their normal appearance.

## What it does

- Two thermal palettes share an independent heat buffer. Engine hotspots use
  the game's infrared sources; body heat responds to engine activity, movement
  and damage. White Hot has a separate highlight ceiling.
- Burning missile motors have a compact exhaust signature. Native fire and
  explosion particles are rendered from camera-specific geometry snapshots,
  with transparent edges and depth occlusion. Smoke is much cooler than flame.
- Selecting an eligible hostile surface target can request designation from a
  friendly ground vehicle or building. The HUD reports acquisition or why a
  ground observer cannot designate the target. Stock laser seekers use the
  game's laser state, including normal acquisition limits and loss of signal.

## Requirements

- **Windows x64**, Nuclear Option **0.34.1** (other game versions are unverified).
- **BepInEx 5 for Unity Mono** installed and working.
- Both `BLIND.dll` and `blind-thermal.bundle` from the **same release**.

BLIND is standalone. The trainer and torpedo sources also present in this
repository are separate mods and are not included in the BLIND download.

## Install or update

1. Close the game.
2. When updating, back up the old BLIND DLL and bundle **outside** `BepInEx/plugins`,
   then remove those old copies. Keep exactly one installed `BLIND.dll`.
3. Extract `BLIND-v0.4.2.zip` into the game folder. The resulting files should be:

```text
Nuclear Option/
└─ BepInEx/plugins/BLIND/
   ├─ BLIND.dll
   └─ blind-thermal.bundle
```

4. Launch the game, enter an aircraft, select a target so its cockpit screen is
   active, and press **F7**. The configuration is created at
   `BepInEx/config/ua.ncmod.blind.cfg`.

Restart after updating. Replacing files does not reload an already running mod.
To uninstall, close the game and remove the two BLIND files.

## Multiplayer and JTAC

Sensor modes are local visual changes. JTAC requires host authority and is driven
by the **host player's local aircraft**; this release does not provide autonomous
JTAC on a headless dedicated server. Client-only installation provides the sensor
modes but cannot create ground designations.

By default, the ground observer must be within **4 km** of the target with valid
line of sight, and the aircraft within **15 km** receive range. The observer has a
20-second designation window and a 12-second cooldown. A friendly observer is not
a guarantee of acquisition: range, terrain, observer availability and the weapon's
own seeker limits still apply.

## Configuration

Edit the config with the game closed, or use BepInEx Configuration Manager.
Existing config values are preserved when updating.

| Setting | Default | Purpose |
|---|---:|---|
| `Sensors.CycleModeKey` | `F7` | Change sensor mode |
| `Sensors.ThermalSpan` | `1.25` | Heat display range; lower gives stronger contrast |
| `Sensors.ThermalNoise` | `0.008` | Fine detector noise; use `0` for a clean image |
| `Sensors.WhiteHotCeiling` | `0.86` | White Hot highlight brightness cap |
| `JTAC.Enabled` | `true` | Ground designation on the host |
| `JTAC.GroundLaserRange` | `4000` | Observer-to-target limit in metres |
| `JTAC.AircraftReceiveRange` | `15000` | Aircraft receive limit in metres |
| `JTAC.MaxDesignationTime` | `20` | Continuous designation time in seconds |
| `JTAC.DesignatorCooldown` | `12` | Observer cooldown in seconds |
| `JTAC.MaxConcurrentDesignations` | `4` | Maximum simultaneous designations |

## Limits and troubleshooting

This is a **visual approximation**, not a temperature-measuring simulation.
Terrain uses a compressed visible-image estimate; emissivity, infrared reflection
and smoke transmission are not physically simulated. Some custom weapon/effect
shaders may need specific support. Tiny missiles also depend on the target screen's
resolution and viewing angle. Fixed gain avoids whole-frame pumping during flashes.

- **No thermal modes:** verify that the cockpit target camera is active and there
  is only one BLIND installation. Check `BepInEx/LogOutput.log` for `[Thermal]`.
- **Shader unavailable / pink image:** reinstall the matching DLL and bundle.
  An unavailable shader disables the thermal pass rather than changing scene materials.
- **White Hot too bright:** lower `WhiteHotCeiling`; for all palettes, increase
  `ThermalSpan` to reduce contrast. These controls do different jobs.
- **JTAC HOST ONLY:** the local machine does not have authority to designate.
- **Visual issue:** include the game/mod version, aircraft, weapon, sensor mode,
  screenshot or short clip, and relevant BLIND log lines. Avoid sharing an entire
  log if it contains private connection details.

## Build

From the repository root, with a .NET SDK and .NET Framework 4.7.2 targeting support:

```powershell
dotnet restore B.L.I.N.D/BLIND.csproj --configfile B.L.I.N.D/NuGet.Config
dotnet build B.L.I.N.D/BLIND.csproj -c Release --no-restore -p:GameDir="F:\path\to\Nuclear Option"
```

Game and BepInEx assemblies are referenced from your installation and are not
redistributed. The prebuilt shader bundle is included. To rebuild it, open
`B.L.I.N.D/ShaderProject` with the recorded Unity Editor version (6000.2.7f2), or:

```powershell
Unity.exe -batchmode -quit -projectPath "<absolute path>/B.L.I.N.D/ShaderProject" -executeMethod BuildThermal.Build -logFile "<build log>"
```

Run `ThermalRegression.Run` instead of `BuildThermal.Build` for GPU regressions;
do **not** pass `-nographics` for those tests. The game runtime compatibility must
also be checked when changing the shader bundle or editor version.

Create the distribution archive with `B.L.I.N.D/tools/Package.ps1`.
See [validation notes](VALIDATION.md) for the checks and their limits.

Created by **XBarni999** for Nuclear Option by **Shockfront Studios**.
