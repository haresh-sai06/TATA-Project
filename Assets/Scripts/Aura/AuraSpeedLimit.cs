using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Speed-limit awareness + alerts for the self-driving car. Shows a road-style speed-limit
    /// sign on the HUD, and when the car exceeds the limit it flashes a "SLOW DOWN" warning, beeps
    /// (procedurally — no audio asset needed), and streams a <c>speed.limit</c> event to Aura Core
    /// so the dashboard can react. The limit can change along the route (zones by waypoint) so the
    /// sign updates as you drive. Attach anywhere; it auto-finds the car and the AuraClient.
    /// Demo keys:  [ = lower limit 5,  ] = raise limit 5.
    /// </summary>
    [DefaultExecutionOrder(260)]
    public class AuraSpeedLimit : MonoBehaviour
    {
        [Header("Limit")]
        [Tooltip("Base speed limit (km/h) when zones are off.")]
        [SerializeField] private float speedLimitKmh = 50f;
        [Tooltip("How far over the limit (km/h) before it counts as speeding.")]
        [SerializeField] private float toleranceKmh = 3f;
        [Tooltip("Must stay over the limit this long before the alert fires (debounce).")]
        [SerializeField] private float sustainSeconds = 0.4f;

        [Header("Route zones (optional)")]
        [Tooltip("Vary the limit along the route. Off = a fixed limit (also set by the dashboard Drive Control).")]
        [SerializeField] private bool useZones = false;
        [SerializeField] private float[] zoneLimits = { 60f, 40f, 80f, 50f };
        [Tooltip("How many waypoints each zone spans.")]
        [SerializeField] private int zoneEveryWaypoints = 6;

        [Header("Alert")]
        [SerializeField] private bool playBeep = true;
        [SerializeField] private float beepInterval = 1.1f;

        [SerializeField] private onnxcontroller car;

        private Rigidbody _rb;
        private AuraClient _client;
        private AudioSource _audio;
        private AudioClip _beep;

        private float _limitOffset;      // demo key adjustment
        private float _overSince = -1f;  // when the car first went over (for debounce)
        private bool _speeding;          // confirmed speeding (post-debounce)
        private float _lastBeep;
        private float _lastSend;
        private float _flash;            // banner flash phase
        private float _cmdSpeed = -1f;   // target speed commanded by the dashboard; -1 = none (car drives itself)
        private float _cmdLimit = -1f;   // limit commanded by the dashboard; -1 = use serialized/zone limit

        public bool IsSpeeding => _speeding;
        public float CurrentLimit => ActiveLimit();

        private void Awake()
        {
            if (car == null) car = FindFirstObjectByType<onnxcontroller>();
            if (car != null) _rb = car.GetComponent<Rigidbody>();
            _client = FindFirstObjectByType<AuraClient>();
            if (_client != null) _client.OnMessage += OnAura;

            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;
            _beep = MakeBeep();
        }

        private void OnDestroy()
        {
            if (_client != null) _client.OnMessage -= OnAura;
        }

        // The System-A dashboard Drive Control sets the car's speed + limit; drive the HUD/alert from it.
        private void OnAura(string type, JObject payload)
        {
            if (type != "control.speed" || payload == null) return;
            float? lim = payload.Value<float?>("limitKmh");
            float? sp = payload.Value<float?>("speedKmh");
            if (lim.HasValue) _cmdLimit = lim.Value;                 // fixed 50 sign
            if (sp.HasValue)
            {
                _cmdSpeed = sp.Value;
                if (car != null) car.SetMaxSpeed(sp.Value);          // raise the cap so the wheels don't brake against us
            }
        }

        // Smoothly hold the car at the dashboard-commanded speed (precise) — steering stays autonomous.
        private void FixedUpdate()
        {
            if (_rb == null && car != null) _rb = car.GetComponent<Rigidbody>();
            if (_rb == null) return;

            // ── EMERGENCY TAKEOVER: a guaranteed-safe minimal-risk stop. We take deterministic
            // control of the car's motion — smooth straight-line braking + strong rotation damping —
            // so the pull-over can NEVER spin out, flip, or crash, whatever the speed or surroundings.
            if (car != null && car.emergencyStop)
            {
                _cmdSpeed = -1f;                                   // drop any dashboard speed command
                Vector3 v = _rb.linearVelocity;
                Vector3 flat = new Vector3(v.x, 0f, v.z);
                flat = Vector3.MoveTowards(flat, Vector3.zero, 9f * Time.fixedDeltaTime);  // ~9 m/s² brake
                _rb.linearVelocity = new Vector3(flat.x, Mathf.Clamp(v.y, -20f, 1f), flat.z); // keep gravity, no launch
                _rb.angularVelocity = Vector3.MoveTowards(_rb.angularVelocity, Vector3.zero, 10f * Time.fixedDeltaTime);
                return;
            }

            if (_cmdSpeed < 0f) return;

            // ── Normal: hold the dashboard-commanded speed. Keep the car's heading (no forced
            // steering) and clamp spin so speed control can't ever destabilize the car.
            Vector3 vel = _rb.linearVelocity;
            Vector3 f = new Vector3(vel.x, 0f, vel.z);
            float target = _cmdSpeed / 3.6f;                          // km/h -> m/s
            float next = Mathf.MoveTowards(f.magnitude, target, 12f * Time.fixedDeltaTime);
            Vector3 dir = f.sqrMagnitude > 0.25f ? f.normalized : car.transform.forward;
            _rb.linearVelocity = dir * next + Vector3.up * vel.y;
            _rb.angularVelocity = Vector3.ClampMagnitude(_rb.angularVelocity, 1.5f);  // anti spin-out safety
        }

        private float ActiveLimit()
        {
            if (_cmdLimit > 0f) return _cmdLimit;   // a dashboard-commanded fixed limit wins
            float baseLimit = speedLimitKmh;
            if (useZones && zoneLimits != null && zoneLimits.Length > 0 && car != null && zoneEveryWaypoints > 0)
            {
                int idx = Mathf.Max(0, car.hudWpIdx) / zoneEveryWaypoints;
                baseLimit = zoneLimits[idx % zoneLimits.Length];
            }
            return Mathf.Max(5f, baseLimit + _limitOffset);
        }

        private float SpeedKmh()
        {
            if (_rb == null && car != null) _rb = car.GetComponent<Rigidbody>();
            return _rb != null ? _rb.linearVelocity.magnitude * 3.6f : (car != null ? car.hudSpeed : 0f);
        }

        private void Update()
        {
            // Demo controls.
            if (Input.GetKeyDown(KeyCode.LeftBracket)) _limitOffset -= 5f;
            if (Input.GetKeyDown(KeyCode.RightBracket)) _limitOffset += 5f;

            float speed = SpeedKmh();
            float limit = ActiveLimit();
            bool over = speed > limit + toleranceKmh;

            // Debounce: require the car to stay over for sustainSeconds before we call it speeding.
            if (over)
            {
                if (_overSince < 0f) _overSince = Time.unscaledTime;
                if (!_speeding && Time.unscaledTime - _overSince >= sustainSeconds)
                {
                    _speeding = true;
                    SendEvent(speed, limit, true);   // entered speeding
                }
            }
            else
            {
                _overSince = -1f;
                if (_speeding)
                {
                    _speeding = false;
                    SendEvent(speed, limit, false);  // back under the limit
                }
            }

            // Audible warning while speeding.
            if (_speeding && playBeep && _beep != null && Time.unscaledTime - _lastBeep >= beepInterval)
            {
                _lastBeep = Time.unscaledTime;
                _audio.PlayOneShot(_beep);
            }

            // Periodic telemetry of the current limit (throttled), so the dashboard always knows it.
            if (Time.unscaledTime - _lastSend >= 0.5f)
                SendEvent(speed, limit, _speeding);

            _flash += Time.unscaledDeltaTime * 6f;
        }

        private void SendEvent(float speed, float limit, bool over)
        {
            _lastSend = Time.unscaledTime;
            if (_client == null || !_client.IsConnected) return;
            _client.Send("speed.limit", new
            {
                limitKmh = Mathf.RoundToInt(limit),
                speedKmh = Mathf.Round(speed * 10f) / 10f,
                over = over,
                overByKmh = over ? Mathf.RoundToInt(speed - limit) : 0,
            });
        }

        // Procedurally generate a short warning beep so no audio asset is required.
        private static AudioClip MakeBeep()
        {
            const int sr = 44100;
            const float dur = 0.14f;
            int n = (int)(sr * dur);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / sr;
                float env = Mathf.Clamp01(1f - t / dur);       // quick decay
                data[i] = Mathf.Sin(2f * Mathf.PI * 880f * t) * 0.4f * env;
            }
            var clip = AudioClip.Create("AuraSpeedBeep", n, 1, sr, false);
            clip.SetData(data, 0);
            return clip;
        }

        private void OnGUI()
        {
            float speed = SpeedKmh();
            float limit = ActiveLimit();

            // ── Speed-limit sign (top-left): white plate, red ring, black number ──
            float x = 18f, y = 76f, s = 68f;
            Fill(new Rect(x - 3f, y - 3f, s + 6f, s + 6f), new Color(0.85f, 0.1f, 0.12f, 0.95f)); // red ring
            Fill(new Rect(x + 4f, y + 4f, s - 8f, s - 8f), new Color(0.96f, 0.96f, 0.96f, 0.98f)); // white plate
            GUI.Label(new Rect(x, y + 6f, s, 20f), "LIMIT",
                Lbl(10, new Color(0.2f, 0.2f, 0.2f), FontStyle.Bold, TextAnchor.MiddleCenter));
            GUI.Label(new Rect(x, y + 20f, s, 34f), Mathf.RoundToInt(limit).ToString(),
                Lbl(30, Color.black, FontStyle.Bold, TextAnchor.MiddleCenter));

            // Current speed chip under the sign (green under limit, red over).
            bool over = _speeding;
            Color spCol = over ? new Color(1f, 0.35f, 0.3f) : new Color(0.35f, 0.85f, 0.5f);
            GUI.Label(new Rect(x - 6f, y + s + 4f, s + 12f, 18f), $"{Mathf.RoundToInt(speed)} km/h",
                Lbl(13, spCol, FontStyle.Bold, TextAnchor.MiddleCenter));

            // ── "SLOW DOWN" banner while speeding (flashing) ──
            if (over)
            {
                float a = 0.55f + 0.35f * Mathf.Abs(Mathf.Sin(_flash));
                float bw = 360f, bh = 46f;
                float bx = (Screen.width - bw) / 2f, by = Screen.height * 0.18f;
                Fill(new Rect(bx, by, bw, bh), new Color(0.8f, 0.12f, 0.12f, a));
                Border(new Rect(bx, by, bw, bh), new Color(1f, 0.9f, 0.4f, 0.9f), 2f);
                GUI.Label(new Rect(bx, by + 6f, bw, 22f), "⚠  SLOW DOWN — SPEED LIMIT",
                    Lbl(18, Color.white, FontStyle.Bold, TextAnchor.MiddleCenter));
                GUI.Label(new Rect(bx, by + 26f, bw, 18f), $"{Mathf.RoundToInt(speed)} km/h  ·  limit {Mathf.RoundToInt(limit)}  ·  +{Mathf.RoundToInt(speed - limit)} over",
                    Lbl(13, new Color(1f, 0.92f, 0.85f), FontStyle.Bold, TextAnchor.MiddleCenter));
            }

            GUI.color = Color.white;
        }

        // ── OnGUI helpers (match AuraCockpitHud) ──────────────────────
        private static void Fill(Rect r, Color c)
        {
            var prev = GUI.color; GUI.color = c; GUI.DrawTexture(r, Texture2D.whiteTexture); GUI.color = prev;
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
            var st = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a };
            st.normal.textColor = col;
            return st;
        }
    }
}
