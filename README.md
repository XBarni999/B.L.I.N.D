# B.L.I.N.D.

**Best Luminescence & Infrared Navigation Device** — cockpit thermal imaging,
allied ground/naval laser designation, and visual explosion overhaul for **Nuclear Option**.

[Download](https://github.com/XBarni999/B.L.I.N.D/releases) ·
[Report an issue](https://github.com/XBarni999/B.L.I.N.D/issues) ·
[Changes](CHANGELOG.md)

Press **F7** to cycle **STANDARD IR → LONGBOW → IR BLACK**.
The thermal pass runs on the cockpit target camera; the outside view and the
original vehicle materials keep their normal appearance.

## What it does

### 1. Cockpit Thermal Imaging (FLIR)
- **Three streamlined sensor modes**:
  - **STANDARD IR**: Enhanced vanilla night-vision / infrared with dynamic daylight exposure balance.
  - **LONGBOW**: Classic Ironbow high-contrast thermal palette mapping engine heat, weapon signatures, and body temperatures.
  - **IR BLACK**: Clean White-Hot thermal profile with an adjustable soft highlight shoulder ceiling.
- Engine hotspots use the game's native infrared signatures; body heat responds dynamically to engine activity, movement, and structural damage.
- Burning missile motors have dedicated radiant exhaust footprints, while lingering smoke trails remain cool.

### 2. Allied Ground & Naval Laser Designation (JTAC)
- **Multi-Target Designation**: Select multiple hostile surface or naval targets in your weapon manager. Allied units will automatically and greedily pair with available targets (1 ally lases 1 target) based on proximity and line of sight.
- **Naval Support**: Allied warships (`Ship`) can act as JTAC designators, and hostile warships can be designated. Ships utilize an extended **15 km optical horizon** and elevated superstructure sighting origins.
- **Seeker Memory & Evasion Persistence**: When launching laser-guided weapons (e.g. AGM-48, laser bombs), the missile seeker memorizes its designated target. You can safely turn away, maneuver to evade air defenses, or deselect the target without breaking missile lock while an ally continues designation.
- **Subtle HUD Telemetry**: Minimalist, non-intrusive corner brackets (`┌ ┐ └ ┘`) track each designated target on the HUD with exact telemetry, accompanied by a clean status banner (`JTAC [2 TGT] • 4.2 KM`).

### 3. Explosion Overhaul & Optical Shockwaves (VFX)
- **Physics-Inspired Yield Scaling**: Particle systems dynamically scale in size and volume based on TNT equivalent yield (Hopkinson–Cranz cube-root scaling $R \propto \sqrt[3]{Y}$).
- **Prolonged Lingering Smoke**: Heavy black smoke and dust clouds billow and persist **2.8× longer** with smooth alpha fade-out curves. Native premature 30-second despawn timers are extended to 120 seconds to prevent abrupt pop-out.
- **Supersonic Optical Shockwave**: Spawns a procedural inverted mesh sphere at the blast epicenter expanding non-linearly over 0.35–0.60s with URP screen-space refractive distortion and heat shimmer (built completely inline without external asset bundles).

## Requirements

- **Windows x64**, Nuclear Option **0.34.x** (compatible with 0.34.1+).
- **BepInEx 5 for Unity Mono** installed and working.
- Both `BLIND.dll` and `blind-thermal.bundle` from the **same release**.

BLIND is standalone. The trainer and torpedo sources also present in this
repository are separate mods and are not included in the BLIND download.

## Install or update

1. Close the game.
2. When updating, back up the old BLIND DLL and bundle **outside** `BepInEx/plugins`,
   then remove those old copies. Keep exactly one installed `BLIND.dll`.
3. Extract `BLIND.zip` into the game folder. The resulting files should be:

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

Sensor modes and explosion overhauls are local client visual improvements. JTAC designation requires host authority and is driven by the **host player's local aircraft**; client-only installations enjoy thermal imaging and VFX overhauls but cannot originate JTAC designations.

## Configuration

Edit `BepInEx/config/ua.ncmod.blind.cfg` with the game closed, or use the BepInEx Configuration Manager.

| Setting | Default | Purpose |
|---|---:|---|
| **Sensors** | | |
| `Sensors.CycleModeKey` | `F7` | Keybind to cycle sensor modes |
| `Sensors.ThermalSpan` | `1.25` | Heat display dynamic range; lower gives stronger contrast |
| `Sensors.ThermalNoise` | `0.008` | Fine detector noise; set to `0` for pristine digital image |
| `Sensors.WhiteHotCeiling` | `0.86` | Maximum highlight brightness in IR BLACK |
| **JTAC** | | |
| `JTAC.Enabled` | `true` | Enable allied ground and naval laser designation on host |
| `JTAC.GroundLaserRange` | `4000` | Maximum observer-to-target designation range for ground vehicles (m) |
| `JTAC.NavalLaserRange` | `15000` | Maximum observer-to-target designation range for naval ships (m) |
| `JTAC.AircraftReceiveRange` | `18000` | Maximum aircraft datalink cue range (m) |
| `JTAC.MaxDesignationTime` | `20` | Continuous designation time in seconds |
| `JTAC.DesignatorCooldown` | `12` | Observer cooldown after designation cycle in seconds |
| `JTAC.MaxConcurrentDesignations` | `4` | Maximum simultaneous allied designations |
| **Explosions** | | |
| `Explosions.Enabled` | `true` | Enable dynamic explosion scaling and prolonged smoke |
| `Explosions.SmokePersistenceMultiplier` | `2.8` | Lifetime multiplier for lingering black smoke and dust |
| `Explosions.ShockwaveDistortion` | `true` | Enable procedural optical refraction shockwave at epicenter |
| `Explosions.ShockwaveIntensity` | `1.0` | Strength of screen-space optical distortion |

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

Create the distribution archive with `B.L.I.N.D/tools/Package.ps1`.
See [validation notes](VALIDATION.md) for the checks and their limits.

Created by **XBarni999** for Nuclear Option by **Shockfront Studios**.
