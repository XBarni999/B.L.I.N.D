using UnityEngine;

namespace BLIND
{
    internal sealed class BlindRuntime : MonoBehaviour
    {
        internal static BlindRuntime Instance;

        private BlindPlugin _plugin;
        private JtacDesignationSystem _jtac;
        private GUIStyle _modeStyle;
        private GUIStyle _subStyle;
        private GUIStyle _cueStyle;
        private GUIStyle _warningStyle;
        private GUIStyle _targetLabelStyle;
        private Texture2D _boxTexture;
        private Aircraft _localAircraft;

        internal SensorSuite Sensors { get; private set; }

        internal bool IsJtacDesignated(Unit unit)
        {
            return _jtac != null && _jtac.IsTargetDesignated(unit);
        }

        internal bool TryGetJtacTargetInView(Transform seeker, float maxAngle, float maxRange, out Unit target)
        {
            target = null;
            return _jtac != null && _jtac.TryGetDesignatedTargetInView(seeker, maxAngle, maxRange, out target);
        }

        internal void Initialize(BlindPlugin plugin)
        {
            Instance = this;
            _plugin = plugin;
            Sensors = new SensorSuite(plugin);
            _jtac = new JtacDesignationSystem(plugin);
        }

        internal void Shutdown()
        {
            if (_jtac != null) _jtac.Shutdown();
            if (Sensors != null) Sensors.Shutdown();
            if (_boxTexture != null) Destroy(_boxTexture);
            Instance = null;
        }

        private void Update()
        {
            if (!GameManager.GetLocalAircraft(out _localAircraft))
            {
                _localAircraft = null;
            }
            Sensors.Update(_localAircraft);
            _jtac.Update(_localAircraft);
        }

        private void LateUpdate()
        {
            if (Sensors != null)
            {
                Sensors.LateUpdate();
            }
        }

        private void OnGUI()
        {
            if (_localAircraft == null || Sensors == null || _jtac == null) return;
            EnsureStyles();

            // 1. Стильний авіаційний OSD індикатор режимів сенсора (у верхній зоні HUD)
            DrawSensorModeBanner();

            // 2. Індикація лазерного підсвічування JTAC
            Unit target;
            float range;
            if (_jtac.TryGetBestCue(_localAircraft, out target, out range))
            {
                DrawJtacCueBanner(range);
                DrawTargetBox(target, range);
            }
            else if (_jtac.ShowDiagnostic)
            {
                DrawDiagnosticBanner(_jtac.DiagnosticStatus);
            }

            // 3. Попередження про лазерне опромінення літака
            if (_jtac.IsTargetDesignated(_localAircraft))
            {
                DrawLaserWarning();
            }
        }

        private void DrawSensorModeBanner()
        {
            float alpha = Sensors.ModeMessageAlpha;
            if (alpha <= 0.005f) return;

            float width = 330f;
            float height = 44f;
            float x = (Screen.width - width) * 0.5f;
            float y = 28f; // Верхній HUD (не перекриває прилади MFD кабіни)

            Color accentColor;
            string subtitle;

            switch (Sensors.Mode)
            {
                case SensorMode.FlirWhiteHot:
                    accentColor = new Color(0.92f, 0.96f, 1f); // Arctic FLIR White
                    subtitle = "FLIR THERMAL POD  •  WHITE-HOT";
                    break;
                case SensorMode.FlirBlackHot:
                    accentColor = new Color(1f, 0.76f, 0.38f); // Tactical Amber
                    subtitle = "FLIR THERMAL POD  •  BLACK-HOT";
                    break;
                case SensorMode.FlirIronbow:
                    accentColor = new Color(1f, 0.52f, 0.16f); // Fire Orange
                    subtitle = "FLIR THERMAL POD  •  IRONBOW";
                    break;
                case SensorMode.NightVision:
                    accentColor = new Color(0.35f, 1f, 0.55f); // Phosphor Green
                    subtitle = "IMAGE INTENSIFIER  •  NVG GEN-III";
                    break;
                default:
                    accentColor = new Color(0.45f, 0.90f, 1f); // DTV Cyan
                    subtitle = "DAYLIGHT OPTICS  •  DTV";
                    break;
            }

            // Темна тактична підкладка
            DrawRect(new Rect(x, y, width, height), new Color(0.04f, 0.07f, 0.06f, 0.88f * alpha));

            // Сяючі горизонтальні мікро-рамки
            DrawRect(new Rect(x, y, width, 1.5f), new Color(accentColor.r, accentColor.g, accentColor.b, 0.65f * alpha));
            DrawRect(new Rect(x, y + height - 1.5f, width, 1.5f), new Color(accentColor.r, accentColor.g, accentColor.b, 0.65f * alpha));

            // Військові кутові дужки (corner accents)
            Color cornerCol = new Color(accentColor.r, accentColor.g, accentColor.b, 0.95f * alpha);
            DrawRect(new Rect(x, y, 2.5f, 9f), cornerCol);
            DrawRect(new Rect(x, y + height - 9f, 2.5f, 9f), cornerCol);
            DrawRect(new Rect(x + width - 2.5f, y, 2.5f, 9f), cornerCol);
            DrawRect(new Rect(x + width - 2.5f, y + height - 9f, 2.5f, 9f), cornerCol);

            // Текстовий блок
            Color subColor = new Color(accentColor.r, accentColor.g, accentColor.b, 0.72f * alpha);
            Color mainColor = new Color(accentColor.r, accentColor.g, accentColor.b, alpha);
            Color shadowColor = new Color(0f, 0f, 0f, 0.85f * alpha);

            DrawOutlinedText(new Rect(x, y + 4f, width, 14f), subtitle, _subStyle, shadowColor, subColor);
            DrawOutlinedText(new Rect(x, y + 19f, width, 22f), "⟪ " + Sensors.ModeLabel + " ⟫", _modeStyle, shadowColor, mainColor);
        }

        private void DrawJtacCueBanner(float range)
        {
            float width = 340f;
            float height = 30f;
            float x = (Screen.width - width) * 0.5f;
            float y = 78f;

            Color hudGreen = new Color(0.35f, 1f, 0.72f);
            DrawRect(new Rect(x, y, width, height), new Color(0.04f, 0.08f, 0.06f, 0.82f));
            DrawRect(new Rect(x, y, width, 1.5f), hudGreen * 0.75f);
            DrawRect(new Rect(x, y + height - 1.5f, width, 1.5f), hudGreen * 0.75f);

            string text = "JTAC DESIGNATION  •  " + (range * 0.001f).ToString("0.0") + " KM";
            DrawOutlinedText(new Rect(x, y + 4f, width, 22f), text, _cueStyle, Color.black, hudGreen);
        }

        private void DrawDiagnosticBanner(string status)
        {
            float width = 360f;
            float height = 30f;
            float x = (Screen.width - width) * 0.5f;
            float y = 78f;

            Color warnColor = new Color(1f, 0.65f, 0.2f);
            DrawRect(new Rect(x, y, width, height), new Color(0.08f, 0.06f, 0.02f, 0.82f));
            DrawRect(new Rect(x, y, width, 1.5f), warnColor * 0.8f);
            DrawRect(new Rect(x, y + height - 1.5f, width, 1.5f), warnColor * 0.8f);

            DrawOutlinedText(new Rect(x, y + 4f, width, 22f), status, _cueStyle, Color.black, warnColor);
        }

        private void DrawLaserWarning()
        {
            float width = 360f;
            float height = 36f;
            float x = (Screen.width - width) * 0.5f;
            float y = 114f;

            float pulse = Mathf.PingPong(Time.unscaledTime * 4.5f, 1f);
            Color alertColor = new Color(1f, 0.15f, 0.12f, 0.75f + pulse * 0.25f);

            DrawRect(new Rect(x, y, width, height), new Color(0.12f, 0.02f, 0.02f, 0.88f));
            DrawRect(new Rect(x, y, width, 2f), alertColor);
            DrawRect(new Rect(x, y + height - 2f, width, 2f), alertColor);
            DrawRect(new Rect(x, y, 2f, height), alertColor);
            DrawRect(new Rect(x + width - 2f, y, 2f, height), alertColor);

            DrawOutlinedText(new Rect(x, y + 6f, width, 24f), "▲ LASER ILLUMINATION WARNING ▲", _warningStyle, Color.black, alertColor);
        }

        private void DrawTargetBox(Unit target, float range)
        {
            Camera camera = SceneSingleton<CameraStateManager>.i == null
                ? Camera.main
                : SceneSingleton<CameraStateManager>.i.mainCamera;
            if (camera == null || target == null) return;
            Vector3 point = camera.WorldToScreenPoint(target.transform.position);
            if (point.z <= 0f) return;

            float size = Mathf.Clamp(1600f / Mathf.Max(point.z, 1f), 20f, 48f);
            Rect rect = new Rect(point.x - size * 0.5f, Screen.height - point.y - size * 0.5f, size, size);

            Color cueColor = new Color(0.35f, 1f, 0.72f, 0.95f);
            float arm = Mathf.Clamp(size * 0.28f, 5f, 10f);

            // Авіаційні кутові візири ┌ ┐ └ ┘
            // Верх-ліво
            DrawRect(new Rect(rect.x, rect.y, arm, 2f), cueColor);
            DrawRect(new Rect(rect.x, rect.y, 2f, arm), cueColor);
            // Верх-право
            DrawRect(new Rect(rect.xMax - arm, rect.y, arm, 2f), cueColor);
            DrawRect(new Rect(rect.xMax - 2f, rect.y, 2f, arm), cueColor);
            // Низ-ліво
            DrawRect(new Rect(rect.x, rect.yMax - 2f, arm, 2f), cueColor);
            DrawRect(new Rect(rect.x, rect.yMax - arm, 2f, arm), cueColor);
            // Низ-право
            DrawRect(new Rect(rect.xMax - arm, rect.yMax - 2f, arm, 2f), cueColor);
            DrawRect(new Rect(rect.xMax - 2f, rect.yMax - arm, 2f, arm), cueColor);

            // Центральна перехресна мітка (+)
            float midX = rect.x + size * 0.5f;
            float midY = rect.y + size * 0.5f;
            DrawRect(new Rect(midX - 3f, midY - 0.5f, 6f, 1f), cueColor);
            DrawRect(new Rect(midX - 0.5f, midY - 3f, 1f, 6f), cueColor);

            // Телеметрія цілі під рамкою
            string tgtText = "JTAC [" + (range * 0.001f).ToString("0.0") + "km]";
            DrawOutlinedText(new Rect(rect.x - 30f, rect.yMax + 3f, rect.width + 60f, 16f), tgtText, _targetLabelStyle, Color.black, cueColor);
        }

        private void DrawRect(Rect rect, Color color)
        {
            Color prev = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, _boxTexture);
            GUI.color = prev;
        }

        private static void DrawOutlinedText(Rect rect, string text, GUIStyle style, Color outlineColor, Color textColor)
        {
            Color prev = GUI.color;
            GUI.color = outlineColor;
            GUI.Label(new Rect(rect.x + 1f, rect.y, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x - 1f, rect.y, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x, rect.y + 1f, rect.width, rect.height), text, style);
            GUI.Label(new Rect(rect.x, rect.y - 1f, rect.width, rect.height), text, style);
            GUI.color = textColor;
            GUI.Label(rect, text, style);
            GUI.color = prev;
        }

        private void EnsureStyles()
        {
            if (_boxTexture == null)
            {
                _boxTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                _boxTexture.SetPixel(0, 0, Color.white);
                _boxTexture.Apply();
                _boxTexture.hideFlags = HideFlags.HideAndDontSave;
            }
            if (_modeStyle != null) return;

            _modeStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 15,
                fontStyle = FontStyle.Bold
            };
            _subStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10,
                fontStyle = FontStyle.Bold
            };
            _cueStyle = new GUIStyle(_modeStyle)
            {
                fontSize = 13
            };
            _warningStyle = new GUIStyle(_modeStyle)
            {
                fontSize = 16
            };
            _targetLabelStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11,
                fontStyle = FontStyle.Bold
            };
        }
    }
}
