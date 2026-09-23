# B.L.I.N.D. (Best Luminescence & Infrared Navigation Device)

**B.L.I.N.D.** is a high-fidelity FLIR (Forward Looking Infrared) and JTAC (Joint Terminal Attack Controller) targeting pod enhancement mod for **Nuclear Option** (v0.34.x).

It upgrades the cockpit target camera (`TargetCam`) with realistic physical thermal imaging, NVG night vision optics, and automated JTAC ground-designation mechanics for laser-guided munitions.

---

## 🇺🇦 Інструкція для гравців (Ukrainian)

### Основні можливості:
1. **Реалістичний тепловізор (FLIR) у прицільному контейнері літака:**
   - Працює безпосередньо в екрані `TargetCam` у кабіні, не ламаючи графіку світу та освітлення.
   - **FLIR IRONBOW** — класична кольорова спектральна теплова палітра (від холодного фіолетового/чорного до яскраво-жовтого/білого на гарячих точках).
   - **FLIR WHITE HOT** — монохромний білий режим (гарячі об'єкти та техніка світяться білим, з м'яким балансом щоб не сліпити очі).
   - **FLIR BLACK HOT** — зворотний монохромний чорний режим (гарячі цілі та вихлопи виділяються контрастним темним силуетом).
   - **NVG (ПНБ)** — режим нічного бачення з характерним зеленим фосфорним світінням, зернистістю та реакцією на денне світло.
2. **Динамічне тепловиділення:**
   - Двигуни техніки та літаків гріються від обертів (RPM), швидкості та пошкоджень.
   - Ракети чітко видно в тепловізорі під час роботи ракетного двигуна та розгону.
   - Вибухи, вогнища та полум'я мають плавне згасання тепла без прямокутних артефактів.
3. **Система наведення JTAC (Союзне лазерне цілевказання):**
   - Союзні наземні війська (БМП, танки, спостережні пункти) можуть підсвічувати лазером наземні цілі противника на відстані до 4 км.
   - Ви отримуєте сповіщення на ІЛС (`LGB TGT ACQ`), дальність та зелений маркер цілі, якщо ви перебуваєте в радіусі до 15 км.
   - Керовані бомби та ракети з лазерною ГСН самостійно захоплюють підсвітку союзного JTAC, дозволяючи скидати боєприпаси без необхідності самостійно супроводжувати ціль носом літака.

### Керування:
- **`F7`** — перемикання режимів прицільного контейнера:
  `COLOR` ➔ `FLIR IRONBOW` ➔ `FLIR WHITE HOT` ➔ `FLIR BLACK HOT` ➔ `NVG`
  *(Клавішу можна перепризначити в налаштуваннях BepInEx)*.

### Встановлення:
1. Переконайтеся, що у вас встановлено **BepInEx 5** для Nuclear Option.
2. Скопіюйте обидва файли:
   - `BLIND.dll`
   - `blind-thermal.bundle`
   до вашої папки `Nuclear Option/BepInEx/plugins/`.
3. Запустіть гру. При першому запуску створиться конфігураційний файл `BepInEx/config/ua.ncmod.blind.cfg`.

---

## 🇬🇧 Player Guide (English)

### Key Features:
1. **Targeting Pod FLIR & Optic Modes:**
   - Isolated cockpit target camera rendering with zero degradation to main world lighting or cockpit gauges.
   - **FLIR IRONBOW**: False-color gradient (cold purple/black to blazing red/orange/yellow/white).
   - **FLIR WHITE HOT**: Optimized monochrome imaging where hot engine bays, active exhausts, and fires glow white with soft highlight roll-off.
   - **FLIR BLACK HOT**: Inverted monochrome contrast where heat sources appear as distinct dark silhouettes.
   - **NVG (Night Vision)**: Green phosphor night-vision with film grain, bloom, and realistic day overexposure penalty.
2. **Dynamic Thermal Signatures:**
   - Vehicle and aircraft bodies heat up with throttle/RPM, movement, and combat damage.
   - Rocket plumes, missile motors, and detonations produce authentic infrared blooms with smooth alpha dissipation.
3. **JTAC Target Designation System:**
   - Friendly ground armor and outpost units detect and designate enemy vehicles/structures within line-of-sight (up to 4 km).
   - Local aircraft receive datalink cues (`LGB TGT ACQ` on HUD) within a 15 km network bubble.
   - Stock laser-guided weapons lock onto JTAC-designated lasers seamlessly.

### Controls:
- **`F7`**: Cycle sensor modes (`COLOR` ➔ `FLIR IRONBOW` ➔ `FLIR WHITE HOT` ➔ `FLIR BLACK HOT` ➔ `NVG`).
  *(Customizable via BepInEx Configuration Manager or `BepInEx/config/ua.ncmod.blind.cfg`)*.

### Installation:
1. Requires **BepInEx 5** installed in your Nuclear Option directory.
2. Extract or copy:
   - `BLIND.dll`
   - `blind-thermal.bundle`
   directly into `Nuclear Option/BepInEx/plugins/`.
3. Launch Nuclear Option. Enjoy!

---

## ⚙️ Configuration (`ua.ncmod.blind.cfg`)

| Setting | Default | Description |
|---|---|---|
| `Sensors.CycleModeKey` | `F7` | Key shortcut to switch camera sensor modes |
| `Sensors.ThermalSpan` | `1.25` | Contrast range (lower = higher contrast) |
| `Sensors.ThermalNoise` | `0.008` | Thermal detector sensor noise amplitude |
| `Sensors.WhiteHotCeiling`| `0.86` | Highlight ceiling to prevent White-Hot eye strain |
| `JTAC.Enabled` | `true` | Toggle friendly JTAC laser designation |
| `JTAC.GroundLaserRange` | `4000` | Max observer laser designation distance (meters) |
| `JTAC.AircraftReceiveRange` | `15000` | Max datalink broadcast range to aircraft (meters) |
| `JTAC.MaxDesignationTime` | `20` | Max continuous laser painting duration (seconds) |
| `JTAC.DesignatorCooldown` | `12` | Cooldown time between laser designations (seconds) |

---

## 🛠️ Building From Source

```powershell
# Restore dependencies and build
dotnet restore BLIND.csproj --configfile NuGet.Config
dotnet build BLIND.csproj -c Release --no-restore
```
To build the shader bundle from Unity:
```powershell
Unity.exe -batchmode -quit -projectPath "ShaderProject" -executeMethod BuildThermal.Build -logFile "build.log"
```

---
*Created by XBarni999.*
