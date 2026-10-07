# Validation — 0.7.0

The sensor has two modes. STANDARD IR uses the game's target camera settings.
IRONBOW uses the same camera settings plus a Unity URP `ColorLookup` generated
at runtime. There is no custom heat or geometry rendering path.

## Build checks

- Build `BLIND.csproj` in Release with the installed Nuclear Option 0.34.1 assemblies.
- Confirm the distributable contains only `BLIND.dll`; the old
  `blind-thermal.bundle` is not loaded or shipped.
- Confirm `ColorLookup.ValidateLUT()` requirements from the installed URP
  assembly: a linear 2D strip with width `lutSize * lutSize` and height
  `lutSize`. The runtime reads `lutSize` from the active URP asset.

## Gameplay acceptance still required

In an aircraft, compare the same target at the same zoom before and after F7.
The silhouette, clouds, terrain, reticle and effects should have the same
shapes and motion in both modes; only the scene colors should differ. Check a
near aircraft, a distant radar station, a bright sky/cloud view, and a missile
launch. Toggle back to STANDARD IR and confirm the original appearance returns.

JTAC designation, seeker behavior, and multiplayer authority require their
own gameplay checks; this palette change does not modify those systems.

## IR ground attack acceptance (0.7.0)

Release build passes against the installed 0.34.1 assemblies. On 2026-10-07 the player confirmed the feature works in gameplay. The full boundary and multiplayer matrix below remains outstanding:

1. With each available IR air-to-air weapon, select a ground vehicle and a building.
   Confirm acquisition blocks firing for 1.25 seconds, then permits launch within 85% of nominal range.
2. Cross the 85% boundary, break visibility, change target/weapon and deselect/reselect.
   Confirm acquisition resets, no ammunition is consumed by a rejected launch, and the cue explains why.
3. Compare air-to-air shots, radar/laser weapons, SAM launchers, and JTAC to vanilla behavior.
4. Check hit location, native proximity-fuse damage, countermeasures and terrain occlusion.
5. With matching host/client builds and configuration, test remote launches, rejected requests,
   observer ammunition/launch replay, latency, multiple selected targets and mission changes.

No mission or multiplayer results are claimed by the build/package checks.
The pre-circle 0.7.0 build also passed Unity/BepInEx initialization with all new
launch/seeker/HUD patches: LogOutput.log reported BLIND 0.7.0 loaded without a
BLIND patch error. The final circle build compiles and the player subsequently confirmed it works.
Specific missile coverage, boundary behavior and multiplayer are not independently verified.

Verify the small circle is amber/pulsing during acquisition, green/tighter after
lock, grey when blocked, hidden for non-IR weapons, and removed after deselection.
## Modded missile compatibility check — 2026-10-07

- Eligibility checks component type and anti-air role; there is no vanilla name whitelist.
- Installed MeridianWorksMod.dll patches native IRSeeker.Seek for its IR rounds,
  supporting use of the same native seeker path. BLIND patches Initialize, so it
  does not replace Meridian's flight guidance modification.
- Automatic late-loaded weapon detection follows the same runtime predicate.
- Custom replacement seekers and individual modded-missile firing tests remain unverified.