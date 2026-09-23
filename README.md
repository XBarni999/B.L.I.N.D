# B.L.I.N.D. (Best Luminescence & Infrared Navigation Device)

[![Release](https://img.shields.io/github/v/release/XBarni999/B.L.I.N.D?style=flat-square)](https://github.com/XBarni999/B.L.I.N.D/releases)
[![Game Version](https://img.shields.io/badge/Nuclear%20Option-v0.34.x-blue?style=flat-square)](https://store.steampowered.com/app/2168680/Nuclear_Option/)

**B.L.I.N.D.** is a high-fidelity FLIR (Forward Looking Infrared) targeting pod and automated JTAC (Joint Terminal Attack Controller) enhancement mod for **Nuclear Option**.

It overhauls the cockpit target camera (`TargetCam`) with physically-inspired thermal imaging, night-vision optics, and network datalink ground-designation for laser-guided ordnance.

---

##  Features

### 1. Advanced Targeting Pod Sensor Optics
* **Isolated Cockpit Display**: Custom URP post-processing render pipeline running strictly on the aircraft target camera. Does not modify cockpit cockpit dials, canopy glass, or external world lighting.
* **FLIR IRONBOW**: Full false-color spectrum palette (deep purple/black cold background gradating up through vibrant red, orange, and blinding white-hot thermal cores).
* **FLIR WHITE HOT**: Clean monochrome thermal imaging where hot engine cowlings, vehicle exhausts, and friction glow white with soft highlight roll-off to avoid eye fatigue.
* **FLIR BLACK HOT**: Direct inverse monochrome gradient where heat sources appear as distinct, crisp dark silhouettes.
* **NVG (Night Vision)**: Green phosphor night-vision enhancement with authentic film grain, light blooming, and daytime overexposure penalty.

### 2. Dynamic Vehicle & Particle Thermal Physics
* **Engine & Airframe Heat**: Vehicles, aircraft, and armor heat up dynamically based on engine RPM, throttle, speed, and sustained combat damage with natural cooling dissipation.
* **Missile Plumes & Rockets**: Rocket motors, booster burns, and missile trails emit intense thermal signatures in all FLIR modes.
* **Explosions & Fire**: Fuel burns and detonations display smooth, organic thermal expansion and decay without visual block artifacts.

### 3. JTAC Ground Laser Designation
* **Allied Coordination**: Friendly ground armor (IFVs, APCs, MBTs) and defensive outpost structures scan and designate hostile vehicles within 4 km line-of-sight.
* **Datalink Cueing**: When flying within 15 km of a JTAC engagement, pilot HUD displays `LGB TGT ACQ`, exact target range, and a cue box.
* **Autonomous Laser Guidance**: Stock laser-guided weapons (LGBs, guided missiles) lock onto the ground observer's laser emitter, allowing standoff drops without the player needing to self-lase.

---

##  Controls

| Key | Action | Notes |
|---|---|---|
| **`F7`** | **Cycle Sensor Mode** | `COLOR` ➔ `FLIR IRONBOW` ➔ `FLIR WHITE HOT` ➔ `FLIR BLACK HOT` ➔ `NVG` |

*(Keybinding can be rebound in `BepInEx/config/ua.ncmod.blind.cfg` or via BepInEx Configuration Manager).*

---

## 📥 Installation

1. Make sure you have **[BepInEx 5](https://github.com/BepInEx/BepInEx/releases)** installed for Nuclear Option.
2. Download the latest release from the [Releases](https://github.com/XBarni999/B.L.I.N.D/releases) page.
3. Place both:
   * `BLIND.dll`
   * `blind-thermal.bundle`
   directly into your `Nuclear Option/BepInEx/plugins/` directory.
4. Launch the game! Configuration file is automatically generated at `BepInEx/config/ua.ncmod.blind.cfg`.

---

##  Configuration (`ua.ncmod.blind.cfg`)

| Parameter | Default | Description |
|---|---|---|
| `Sensors.CycleModeKey` | `F7` | Key shortcut to switch sensor modes |
| `Sensors.ThermalSpan` | `1.25` | Display dynamic range span (lower = higher contrast) |
| `Sensors.ThermalNoise` | `0.008` | Thermal detector sensor noise level |
| `Sensors.WhiteHotCeiling`| `0.86` | Highlight cap to prevent extreme overexposure in White-Hot |
| `JTAC.Enabled` | `true` | Toggle friendly JTAC ground laser designation |
| `JTAC.GroundLaserRange` | `4000` | Observer-to-target designation range (meters) |
| `JTAC.AircraftReceiveRange` | `15000` | Max datalink broadcast range to player aircraft (meters) |
| `JTAC.MaxDesignationTime` | `20` | Max continuous laser designation duration (seconds) |
| `JTAC.DesignatorCooldown` | `12` | Designator cooling interval between lasing cycles (seconds) |

---

##  Building from Source

```powershell
# Build mod assembly
dotnet restore BLIND.csproj --configfile NuGet.Config
dotnet build BLIND.csproj -c Release --no-restore
```

```powershell
# Build thermal shader bundle via Unity Editor (Unity 6000.2.7f2 / 2022.3 compatible)
Unity.exe -batchmode -quit -projectPath "ShaderProject" -executeMethod BuildThermal.Build -logFile "build.log"
```

---

## 📜 License & Credits

Created by **XBarni999**.  
Designed for **Nuclear Option** by Shockfront Studios.
