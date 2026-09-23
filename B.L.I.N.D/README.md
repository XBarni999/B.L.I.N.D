# B.L.I.N.D.

**Best Luminescence & Infrared Navigation Device** is a standalone BepInEx mod for
Nuclear Option 0.34.x. It does not depend on `NCMod.NuclearOptionTrainer` or the
torpedo mod in the parent directory.

## Implemented

- `F7` cycles the cockpit target-camera modes:
  - `COLOR`
  - `FLIR IRONBOW` (yellow/orange/red/purple thermal palette)
  - `FLIR WHITE HOT`
  - `FLIR BLACK HOT`
  - `NVG`
- FLIR 0.4.0 uses an isolated URP render pass on the native cockpit `TargetCam`.
  It renders a floating-point heat buffer, then maps that single signal to Ironbow,
  White Hot or Black Hot. It does not modify game materials, renderer property
  blocks or shader keywords. The normal cockpit/world camera is unaffected.
- Body heat responds to engine RPM, movement and damage, with gradual warm-up and
  cooling. Native `Unit.IRSources` supply engine/nozzle locations and intensity;
  vehicles without native emitters use an approximate rear engine compartment.
- Depth testing preserves terrain/building occlusion. Original surface textures
  contribute only weak microstructure; visible-light illumination does not drive
  the heat of vehicle surfaces. Selection/JTAC does not change apparent temperature.
- Fire/explosion particle renderers have a separate heat pass, while smoke, dust,
  vapour and contrails are not treated as fire. Pooled prefab effects and missile
  detonations trigger discovery; other effects are discovered once per second.
- A fixed logarithmic display span preserves vehicle contrast during bright flashes.
  White Hot and Black Hot use exactly inverse display intensities. Adjustable subtle
  detector noise and distance/cloud attenuation are applied before display.
- NVG adds green phosphor tint, film grain and bloom. Daylight drives a strong
  overexposure penalty.
- Selecting a hostile surface target in the aircraft requests a JTAC designation.
  Allied `GroundVehicle` units and stationary `Building` posts can designate hostile
  `GroundVehicle` and `Building` targets only when:
  - the target is within the configured ground-laser range (4 km by default);
  - the target has direct terrain/static line of sight;
  - the local aircraft is within the configured receive range (15 km by default).
- A designated target uses Nuclear Option's native `FactionHQ.UpdateLasedState` path.
  Stock `LaserSeeker` weapons therefore acquire it through their normal seeker cone,
  retain normal guidance physics, and revert to their stock last-known/ballistic
  behavior when designation is lost.
- The HUD displays `LGB TGT ACQ`, range and a cue box for the nearest available JTAC
  target.
- Designators have a configurable continuous-use limit and cooldown. Destruction,
  loss of line of sight, range loss or faction change releases the laser immediately.
- Once acquired, the designation stays on that target for its full valid window even
  if the pilot selects another target. Selecting another visible surface target may
  task a second free designator.
- A launched stock `LaserSeeker` remembers its JTAC target independently of the
  aircraft target list. Deselecting or switching the cockpit target therefore does not
  clear the missile's lock while the ground designation remains valid.
- If a selected target cannot be designated, the HUD states the reason: host-only,
  datalink range, no allied designator, designator cooldown, or no ground line of sight.

## Multiplayer authority

Ground designation changes the native HQ laser state and must run on the host/server.
Sensor modes remain client-side. For multiplayer JTAC guidance, install the mod on the
host; clients that also want the B.L.I.N.D. visual modes should install it locally.

## Quick JTAC test mission

The fastest test is a single-player or hosted Mission Editor scenario on open terrain:

1. Add `Primeva` and `Boscali` factions and join `Primeva`.
2. Add a Primeva airbase or player spawn with an aircraft that can carry a stock
   laser-guided bomb.
3. Put one Primeva `Linebreaker_IFV`, `Linebreaker_APC` or `MBT1` on level ground.
   This is the JTAC observer; no special component is required.
4. Put one Boscali `MBT`, `HLT-M` or `AFV8_IFV` 1.5-2.5 km in front of it. Do not put
   a ridge, building or dense static scenery between the two vehicles.
5. Keep both vehicles on hold-position so that the line of sight remains stable.
6. Start the mission as single-player/host and remain within 15 km of the hostile
   vehicle.

Expected confirmation sequence:

- `LGB TGT ACQ • <range> • JTAC` appears at the top of the HUD.
- A green cue box marks the designated hostile vehicle.
- After selecting that vehicle and a stock laser-guided weapon, the stock weapon HUD
  changes from `NOT LASED` to `SHOOT` when normal range/arc requirements are met.
- Release with the target inside approximately 10 degrees of the weapon nose. The
  stock `LaserSeeker` then acquires the ground designation.
- To test signal loss, place a hill between the vehicles or destroy the friendly JTAC
  after release. The cue disappears and the bomb continues on the game's normal
  last-known/ballistic path.

On the locally installed `Tank Busters 2` mission there is already a usable pair near
Agrapol Airbase: a Primeva `MBT1` around `(-864, 137, -26749)` and several Boscali
vehicles roughly 2.0-2.5 km west. This can be used for a quick test without building a
new mission, provided the terrain gives them direct line of sight.

## Build

```powershell
dotnet restore BLIND.csproj --configfile NuGet.Config
dotnet build BLIND.csproj -c Release --no-restore
```

Copy **both** `bin/Release/BLIND.dll` and `bin/Release/blind-thermal.bundle` into the same BepInEx plugin folder. Keep only one active copy of BLIND.dll. Restart the game after updating.
The first launch creates `BepInEx/config/ua.ncmod.blind.cfg`.

## Configuration

- `Sensors.CycleModeKey`
- `Sensors.ThermalSpan` (default 1.25; lower = stronger contrast)
- `Sensors.ThermalNoise` (default 0.008)
- `JTAC.Enabled`
- `JTAC.GroundLaserRange`
- `JTAC.AircraftReceiveRange`
- `JTAC.MaxDesignationTime`
- `JTAC.DesignatorCooldown`
- `JTAC.MaxConcurrentDesignations`

## Current scope

The first release targets the stock cockpit target camera and stock laser-guided
seekers. It does not add a new targeting-pod 3D model, network protocol, or smoke/LWR
AI behavior. Terrain and static objects break designation through the game's native
line-of-sight test; cloud attenuation is implemented for FLIR.

## Thermal rendering limits and shader build

This is a visual thermal approximation, not calibrated temperature measurement.
Terrain/scenery still uses compressed visible-image luminance as a background
estimate; only registered unit surfaces and recognized hot effects have an independent
heat signal. Atmospheric transmission is approximated from range and camera-local
cloud coverage. Smoke spectral transmission, emissivity maps and reflected infrared
radiation are not physically simulated. Ground engine position is a fallback and
may need aircraft/vehicle-specific masks. Particle flipbook/distortion shaders and
modded effects may require adapters; their display names currently aid classification.
Display gain is fixed/configurable, not automatic histogram AGC.

Shader source and a reproducible batch builder are in `ShaderProject/Assets`.
Run Unity with `-batchmode -quit -projectPath <ShaderProject> -executeMethod
BuildThermal.Build -logFile <log-path>`, then build the C# project. The installed
editor used for the current bundle is 6000.2.7f2; the game's Unity 2022.3 runtime
successfully loaded the shader and its four passes during startup testing. This
runtime check does not establish compatibility with other Unity versions/platforms.

The .NET build no longer installs itself implicitly. `-p:DeployToGame=true` opts
into DLL deployment; deploy the bundle beside it as well.

### Visual acceptance

Check the same target in daylight/night and all three palettes; verify local engine
hotspots, cool body detail, terrain/building occlusion, and unchanged cockpit colour.
Test a missile during burn and after burnout, plus explosion/fire/smoke. Cycle back
to COLOR and NVG and change aircraft/mission. Look for `[Thermal] First sensor frame`
in `BepInEx/LogOutput.log`; shader load alone does not verify the rendered image.
