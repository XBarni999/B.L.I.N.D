**B.L.I.N.D. 0.4.2 — cockpit FLIR + ground laser designation**

Test build: GPU regressions and 18 native explosion renders passed; broader
in-mission and multiplayer testing is still needed.

Adds **Ironbow, White Hot, Black Hot and NVG** to the cockpit target screen in
Nuclear Option. Cycle modes with **F7** (rebindable).

Thermal imaging uses its own heat buffer, with engine hotspots, missile motor
signatures and native fire/explosion effects. This update reworks particle
rendering and depth handling to address flickering and malformed explosions,
and softens White Hot highlights.

The JTAC feature lets eligible friendly ground units designate a selected hostile
surface target for stock laser-guided weapons. JTAC requires the host player's
aircraft; visual sensor modes also work on clients.

**Requirements:** Windows x64, Nuclear Option 0.34.1, BepInEx 5 (Mono).
**Install:** close the game, extract the ZIP into the game folder, and keep only
one copy of `BLIND.dll`. The DLL and `blind-thermal.bundle` must stay together.

Download: https://github.com/XBarni999/B.L.I.N.D/releases/tag/v0.4.2
Source / feedback: https://github.com/XBarni999/B.L.I.N.D

Thermal signatures are approximations, not measured temperatures. If reporting
an issue, include the sensor mode, aircraft/weapon, game version and a screenshot.
