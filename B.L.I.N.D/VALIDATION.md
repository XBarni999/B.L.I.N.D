# Validation — 0.5.3

The 0.5.3 renderer filtering compiles. It excludes ship wake and water meshes
from the ship drawing pass and rejects child meshes much larger than the ship.
This addresses the hot triangles seen around an intact battleship in the
cockpit screenshot, but the moving-ship FLIR image still requires an in-mission
acceptance check. The unchanged shader bundle was built in Unity 6000.2.7f2
and passed the 0.5.2 GPU regression (palettes, particle spaces, effect pixels
and depth occlusion). BLIND no longer patches vanilla explosion VFX.

A short Nuclear Option 0.34.1 Direct3D 11 startup loaded BLIND 0.5.3 and
reported the four-pass thermal shader and native IRSource integration. It did
not enter a mission or verify the battleship view.

The detailed native render results below are from 0.4.2.

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
