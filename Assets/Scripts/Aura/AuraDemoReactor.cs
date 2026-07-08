using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Reacts to Aura Core driver-state events and stages the cinematic "Aura takes over"
    /// sequence — the emotional centrepiece of the demo. On a drowsiness <c>safety.alert</c> it:
    ///   • hands the wheel to the autonomous car (<see cref="onnxcontroller.SetEmergencyStop"/>),
    ///   • snaps the camera to the cockpit so you feel it from the driver seat,
    ///   • plays a brief slow-motion beat + camera shake,
    ///   • flashes hazards + a full-screen "AURA ASSIST ENGAGED" guardian overlay,
    /// then restores everything on <c>safety.clear</c>.
    ///
    /// Debug keys inject the events locally so the whole sequence is provable without Aura Core.
    /// (Class name + serialized fields are preserved so existing scene wiring stays valid.)
    /// </summary>
    [RequireComponent(typeof(AuraClient))]
    public class AuraDemoReactor : MonoBehaviour
    {
        [Header("Debug (works without Aura Core)")]
        [Tooltip("Inject a fake drowsiness alert.")]
        public KeyCode debugAlertKey = KeyCode.K;
        [Tooltip("Inject a fake driver-identified event.")]
        public KeyCode debugIdentifyKey = KeyCode.J;
        [Tooltip("Inject a fake resume (clear the pull-over).")]
        public KeyCode debugResumeKey = KeyCode.L;

        [Header("Vehicle")]
        [Tooltip("The self-driving car to pull over. Auto-found if left empty.")]
        public onnxcontroller vehicle;

        [Header("Cinematic takeover")]
        [Tooltip("Camera rig to drive during a takeover. Auto-found if left empty.")]
        [SerializeField] private AuraCameraDirector cameraDirector;
        [Tooltip("Snap to the cockpit view when Aura takes over.")]
        [SerializeField] private bool forceCockpitOnTakeover = true;
        [Tooltip("Brief slow-motion on the takeover beat for drama. Always restored afterwards.")]
        [SerializeField] private bool enableSlowMotion = true;
        [Range(0.2f, 1f)] [SerializeField] private float slowMoScale = 0.55f;
        [SerializeField] private float slowMoHold = 1.0f;
        [Tooltip("Optional chime played on takeover (assign any AudioClip).")]
        [SerializeField] private AudioSource sfxSource;
        [SerializeField] private AudioClip takeoverChime;

        // ── runtime ───────────────────────────────────────────────────
        private AuraClient _client;
        private string _driverName = "Driver";
        private string _banner = string.Empty;
        private float _bannerUntil;

        private bool _takeover;
        private string _takeoverReason = "";

        /// <summary>True while Aura is actively taking the car over (read by the cockpit HUD).</summary>
        public bool TakeoverActive => _takeover;
        /// <summary>The identified driver's name (read by the cockpit HUD).</summary>
        public string DriverName => _driverName;
        private float _takeoverStart;      // unscaled
        private float _slowMoT;            // unscaled elapsed for the slow-mo envelope
        private bool _slowMoRunning;

        private void Awake()
        {
            _client = GetComponent<AuraClient>();
            if (vehicle == null) vehicle = FindFirstObjectByType<onnxcontroller>();
            if (cameraDirector == null) cameraDirector = FindFirstObjectByType<AuraCameraDirector>();
        }

        private void OnEnable() => _client.OnMessage += HandleMessage;
        private void OnDisable()
        {
            _client.OnMessage -= HandleMessage;
            Time.timeScale = 1f; // never leave the editor stuck in slow-mo
        }

        private void Update()
        {
            if (Input.GetKeyDown(debugAlertKey))
                HandleMessage("safety.alert", JObject.FromObject(new
                {
                    level = "critical",
                    reason = "Eyes closed 3.2s (your baseline threshold 2.4s)",
                    action = "pull_over",
                    modality = "audio"
                }));

            if (Input.GetKeyDown(debugIdentifyKey))
                HandleMessage("driver.identified", JObject.FromObject(new { name = "Haresh", playlist = "Focus Drive" }));

            if (Input.GetKeyDown(debugResumeKey))
                HandleMessage("safety.clear", new JObject());

            // Drive the slow-motion envelope on unscaled time (down → hold → back to 1.0).
            if (_slowMoRunning)
            {
                _slowMoT += Time.unscaledDeltaTime;
                const float easeDown = 0.30f, easeUp = 0.65f;
                float ts;
                if (_slowMoT < easeDown)                    ts = Mathf.Lerp(1f, slowMoScale, _slowMoT / easeDown);
                else if (_slowMoT < easeDown + slowMoHold)  ts = slowMoScale;
                else
                {
                    float u = (_slowMoT - easeDown - slowMoHold) / easeUp;
                    ts = Mathf.Lerp(slowMoScale, 1f, u);
                    if (u >= 1f) { _slowMoRunning = false; ts = 1f; }
                }
                Time.timeScale = ts;
            }
        }

        private void HandleMessage(string type, JObject payload)
        {
            switch (type)
            {
                case "driver.identified":
                    _driverName = payload.Value<string>("name") ?? "Driver";
                    string playlist = payload.Value<string>("playlist") ?? "";
                    Show($"Welcome, {_driverName}" + (string.IsNullOrEmpty(playlist) ? "" : $"   ·   {playlist}"), 4f);
                    Debug.Log($"[Aura] driver.identified -> {_driverName}");
                    break;

                case "safety.alert":
                    _takeoverReason = payload.Value<string>("reason") ?? "";
                    string action = payload.Value<string>("action") ?? "";
                    Debug.Log($"[Aura] safety.alert -> {action} | {_takeoverReason}");
                    if (action == "pull_over") BeginTakeover();
                    break;

                case "safety.clear":
                    Debug.Log("[Aura] safety.clear -> resume");
                    EndTakeover();
                    break;
            }
        }

        private void BeginTakeover()
        {
            if (vehicle != null) vehicle.SetEmergencyStop(true);
            _takeover = true;
            _takeoverStart = Time.unscaledTime;

            if (cameraDirector != null)
            {
                if (forceCockpitOnTakeover) cameraDirector.SetMode(AuraCameraDirector.CameraMode.Cockpit);
                cameraDirector.AddShake(0.35f, 1.4f);
                cameraDirector.SetFocusBoost(10f);
            }
            if (enableSlowMotion) { _slowMoRunning = true; _slowMoT = 0f; }
            if (sfxSource != null && takeoverChime != null) sfxSource.PlayOneShot(takeoverChime);
        }

        private void EndTakeover()
        {
            _takeover = false;
            _slowMoRunning = false;
            Time.timeScale = 1f;
            if (vehicle != null) vehicle.SetEmergencyStop(false);
            if (cameraDirector != null) cameraDirector.SetFocusBoost(0f);
            Show($"Driver responded — resuming drive", 3f);
        }

        private void Show(string message, float seconds)
        {
            _banner = message;
            _bannerUntil = Time.unscaledTime + seconds;
        }

        // ─────────────────────────────────────────────────────────────
        // HUD — connection status, welcome banner, and the takeover overlay
        // ─────────────────────────────────────────────────────────────
        private void OnGUI()
        {
            // Status line + controls (top-left).
            bool connected = _client != null && _client.IsConnected;
            GUI.color = connected ? new Color(0.3f, 0.95f, 0.5f) : new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(12, 10, 320, 22), connected ? "● Aura Core connected" : "○ Aura Core offline");
            GUI.color = Color.white;
            GUI.Label(new Rect(12, 30, 620, 22),
                $"[Aura]  {debugAlertKey}=alert  {debugIdentifyKey}=identify  {debugResumeKey}=resume   ·   [V]=view  [1-5]=cameras");

            // Welcome / info banner (mid-top).
            if (!_takeover && Time.unscaledTime < _bannerUntil && !string.IsNullOrEmpty(_banner))
            {
                var style = new GUIStyle(GUI.skin.box) { fontSize = 18, alignment = TextAnchor.MiddleCenter, wordWrap = true };
                style.normal.textColor = Color.white;
                var prev = GUI.color;
                GUI.color = new Color(0.0f, 0.42f, 0.55f, 0.92f);
                GUI.Box(new Rect(Screen.width / 2f - 260f, 60f, 520f, 60f), _banner, style);
                GUI.color = prev;
            }

            if (_takeover) DrawTakeoverOverlay();
        }

        private void DrawTakeoverOverlay()
        {
            float sw = Screen.width, sh = Screen.height;
            float t = Time.unscaledTime;
            float pulse = 0.5f + 0.5f * Mathf.Sin(t * 6f);            // 0..1 fast pulse
            float age = t - _takeoverStart;

            // Red danger vignette — four edge bars that breathe.
            float edge = Mathf.Lerp(46f, 74f, pulse);
            Color vig = new Color(0.85f, 0.08f, 0.06f, 0.55f + 0.25f * pulse);
            Fill(new Rect(0, 0, sw, edge), vig);
            Fill(new Rect(0, sh - edge, sw, edge), vig);
            Fill(new Rect(0, 0, edge, sh), vig);
            Fill(new Rect(sw - edge, 0, edge, sh), vig);

            // Top system strip.
            Fill(new Rect(0, 0, sw, 30f), new Color(0.06f, 0.02f, 0.03f, 0.92f));
            GUI.Label(new Rect(14, 6, sw, 20f), "SYSTEM · AURA GUARDIAN · AUTONOMOUS SAFETY OVERRIDE ACTIVE",
                Lbl(12, new Color(1f, 0.55f, 0.5f), FontStyle.Bold));

            // Centre plate — only pop it in after a short beat so the shake reads first.
            if (age > 0.15f)
            {
                float pw = Mathf.Min(sw - 80f, 720f), ph = 210f;
                float px = (sw - pw) / 2f, py = sh * 0.30f;
                Fill(new Rect(px, py, pw, ph), new Color(0.10f, 0.02f, 0.03f, 0.82f));
                Border(new Rect(px, py, pw, ph), new Color(1f, 0.3f, 0.25f, 0.6f + 0.4f * pulse), 2f);

                GUI.Label(new Rect(px, py + 20f, pw, 30f), "⚠  DROWSINESS DETECTED",
                    Lbl(16, new Color(1f, 0.8f, 0.4f), FontStyle.Bold, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(px, py + 54f, pw, 56f), "AURA ASSIST ENGAGED",
                    Lbl(46, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(px, py + 116f, pw, 24f), "Taking control · pulling the vehicle over safely",
                    Lbl(15, new Color(1f, 0.85f, 0.85f), FontStyle.Normal, TextAnchor.MiddleCenter));
                if (!string.IsNullOrEmpty(_takeoverReason))
                    GUI.Label(new Rect(px, py + 150f, pw, 40f), $"Why: {_takeoverReason}   ·   protecting {_driverName}",
                        Lbl(13, new Color(0.9f, 0.75f, 0.75f), FontStyle.Normal, TextAnchor.MiddleCenter));
            }

            // Hazard flashers (blink) bottom-centre.
            if (pulse > 0.5f)
            {
                var haz = Lbl(30, new Color(1f, 0.6f, 0.05f), FontStyle.Bold, TextAnchor.MiddleCenter);
                GUI.Label(new Rect(sw / 2f - 230f, sh - 92f, 140f, 40f), "◄ ▲ ►", haz);
                GUI.Label(new Rect(sw / 2f + 90f, sh - 92f, 140f, 40f), "◄ ▲ ►", haz);
            }
            GUI.Label(new Rect(0, sh - 48f, sw, 22f), "Say “I'm awake” or press [L] to resume",
                Lbl(13, new Color(1f, 0.9f, 0.9f), FontStyle.Normal, TextAnchor.MiddleCenter));

            GUI.color = Color.white;
        }

        // ── tiny GUI helpers ──────────────────────────────────────────
        private static Texture2D _tex;
        private static void Fill(Rect r, Color c)
        {
            if (_tex == null) _tex = Texture2D.whiteTexture;
            var prev = GUI.color; GUI.color = c; GUI.DrawTexture(r, _tex); GUI.color = prev;
        }
        private static void Border(Rect r, Color c, float t)
        {
            Fill(new Rect(r.x, r.y, r.width, t), c);
            Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
            Fill(new Rect(r.x, r.y, t, r.height), c);
            Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
        }
        private static GUIStyle Lbl(int size, Color col, FontStyle fs = FontStyle.Normal, TextAnchor a = TextAnchor.UpperLeft)
        {
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a, wordWrap = true };
            s.normal.textColor = col;
            return s;
        }
    }
}
