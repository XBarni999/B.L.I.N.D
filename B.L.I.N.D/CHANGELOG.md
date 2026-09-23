# Changelog

## 0.4.2

- Remove an invalid Missile.OnDisable patch that interrupted startup and prevented
  the immediate explosion hook from being installed. Roll back patches on startup failure.
- Render particle snapshots for the target camera, accounting separately for local,
  world and custom simulation origins. Do not replay the game's particle renderer
  with incompatible shader inputs.
- Cull effects against the target camera's frustum and their actual bounds.
- Remove normal-dependent calculations from particles and remove the artificial
  opacity floor around flame sprites. Transparent edges contribute no heat.
- Replace large depth tolerances with a narrow precision allowance and soft
  intersection fade. Effects behind solid geometry remain occluded.
- Preserve the current Ironbow palette. Restore a soft White Hot highlight curve
  and its independent brightness ceiling.
- Keep missile motor signatures separate from cold trails; discover native
  explosion effects immediately at creation.
- Isolate thermal shader uniforms from the game's other shaders.
- Add GPU regressions and an opt-in native-game explosion test build. Test code is
  excluded from the downloadable assembly.

## 0.4.1

Experimental thermal rendering, native explosion registration, missile tracking,
and initial palette tuning. Superseded by 0.4.2.
