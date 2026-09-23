# Validation — 0.4.2

Tested on Windows with Nuclear Option 0.34.1, BepInEx 5 Mono and Direct3D 11.

## Automated GPU checks

- Palette monotonicity, retained highlights, dark background and palette isolation passed.
- Native particle mesh baking passed for local, world and custom simulation spaces,
  including rotated/scaled origins.
- Particle pixels were finite, alpha edges remained transparent, and the effect
  occupied a bounded area (988 pixels in the test). Geometry in front completely
  occluded it (zero visible pixels).
- The shader asset bundle built successfully.

## Native game render check

An opt-in diagnostic build rendered the game's `explosion_100kg_dusty` prefab
from AGM_heavy through BLIND in the actual game renderer. It captured 18 images:
Ironbow, White Hot and Black Hot at 0.05, 0.15, 0.35, 0.7, 1.5 and 3 seconds.
Repeated renders of each unchanged scene had zero changed pixels with detector
noise disabled; no shader-error magenta was detected. Early fireball detail was
visually inspected in the saved images. The native fire particles fade rapidly;
excluded cold dust is intentionally not turned into a persistent hot cloud.

Startup installed all patches successfully and reported immediate registration
at six native explosion spawn sites. The release assembly excludes the probe.

These are isolated rendering checks, not a complete in-mission acceptance test.
Moving cockpit cameras, every weapon/effect, missile launches, multiplayer JTAC,
long-session performance and other game versions still need gameplay coverage.
The GitHub build is marked as a prerelease for that reason.

## Reproduce

Run the Unity shader project with `-batchmode -force-d3d11 -quit` and
`-executeMethod ThermalRegression.Run`. Do not use `-nographics` for GPU tests.

For the native probe, build the C# project with
`-p:DefineConstants=BLIND_DIAGNOSTICS -p:OutputPath=bin/Diagnostics/`, install that
DLL and its shader bundle temporarily, and launch the game with
`--blind-render-test`. The probe writes `BLIND-test-output` beside the plugin and
quits the game. Restore the ordinary Release DLL after testing; never distribute
the diagnostic assembly.
