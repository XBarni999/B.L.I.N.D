using UnityEngine;

namespace BLIND
{
    internal sealed class BlindRuntime : MonoBehaviour
    {
        internal static BlindRuntime Instance;

        private BlindPlugin _plugin;
        private JtacDesignationSystem _jtac;
        private GUIStyle _modeStyle;
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

        private readonly System.Collections.Generic.List<JtacDesignationSystem.Designation> _cuesBuffer =
            new System.Collections.Generic.List<JtacDesignationSystem.Designation>();

        private void OnGUI()
        {
            if (_localAircraft == null || Sensors == null || _jtac == null) return;
            EnsureStyles();

            // 1. Стильний авіаційний OSD індикатор режимів сенсора (у верхній зоні HUD)
            DrawSensorModeBanner();

            // 2. Індикація лазерного підсвічування JTAC (підтримка кількох підсвічених цілей)
            _jtac.GetActiveCues(_localAircraft, _cuesBuffer);
            if (_cuesBuffer.Count > 0)
            {
                float closestRange = float.MaxValue;
                for (int i = 0; i < _cuesBuffer.Count; i++)
                {
                    Unit target = _cuesBuffer[i].Target;
                    if (target == null) continue;
                    float range = FastMath.Distance(_localAircraft.GlobalPosition(), target.GlobalPosition());
                    if (range < closestRange) closestRange = range;
                    DrawTargetBox(target, range);
                }

                DrawJtacCueBanner(closestRange, _cuesBuffer.Count);
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

            float width = 200f;
            float height = 22f;
            float x = (Screen.width - width) * 0.5f;
            float y = 20f;

            Color accentColor;
            switch (Sensors.Mode)
            {
                case SensorMode.VanillaIR:
                    accentColor = new Color(0.85f, 0.90f, 0.95f);
                    break;
                case SensorMode.Ironbow:
                    accentColor = new Color(1f, 0.65f, 0.28f);
                    break;
                default:
                    accentColor = Color.white;
                    break;
            }

            // М'яка напівпрозора підкладка без рамок (не ріже око і легко читається)
            DrawRect(new Rect(x, y, width, height), new Color(0.02f, 0.04f, 0.03f, 0.45f * alpha));

            Color mainColor = new Color(accentColor.r, accentColor.g, accentColor.b, alpha);
            Color shadowColor = new Color(0f, 0f, 0f, 0.70f * alpha);

            DrawOutlinedText(new Rect(x, y + 1f, width, 20f), Sensors.ModeLabel, _modeStyle, shadowColor, mainColor);
        }

        private void DrawJtacCueBanner(float range, int targetCount)
        {
            float width = targetCount > 1 ? 250f : 220f;
            float height = 20f;
            float x = (Screen.width - width) * 0.5f;
            float y = Sensors.ModeMessageAlpha > 0.005f ? 46f : 20f;

            Color hudGreen = new Color(0.35f, 0.95f, 0.65f);
            DrawRect(new Rect(x, y, width, height), new Color(0.02f, 0.05f, 0.03f, 0.45f));

            string text = targetCount > 1
                ? "JTAC [" + targetCount + " TGT]  •  " + (range * 0.001f).ToString("0.0") + " KM"
                : "JTAC  •  " + (range * 0.001f).ToString("0.0") + " KM";
            DrawOutlinedText(new Rect(x, y + 1f, width, 18f), text, _cueStyle, Color.black, hudGreen);
        }

        private void DrawDiagnosticBanner(string status)
        {
            float width = 280f;
            float height = 20f;
            float x = (Screen.width - width) * 0.5f;
            float y = Sensors.ModeMessageAlpha > 0.005f ? 46f : 20f;

            Color warnColor = new Color(1f, 0.72f, 0.28f);
            DrawRect(new Rect(x, y, width, height), new Color(0.05f, 0.04f, 0.02f, 0.45f));

            DrawOutlinedText(new Rect(x, y + 1f, width, 18f), status, _cueStyle, Color.black, warnColor);
        }

        private void DrawLaserWarning()
        {
            float width = 260f;
            float height = 22f;
            float x = (Screen.width - width) * 0.5f;
            float y = 70f;

            float pulse = Mathf.PingPong(Time.unscaledTime * 4f, 1f);
            Color alertColor = new Color(1f, 0.20f, 0.18f, 0.75f + pulse * 0.25f);

            DrawRect(new Rect(x, y, width, height), new Color(0.12f, 0.02f, 0.02f, 0.55f));
            DrawOutlinedText(new Rect(x, y + 1f, width, 20f), "▲ LASER WARNING ▲", _warningStyle, Color.black, alertColor);
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
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            _cueStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold
            };
            _warningStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 12,
                fontStyle = FontStyle.Bold
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
