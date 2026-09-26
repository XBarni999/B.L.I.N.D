# Validation — 0.6.0

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
