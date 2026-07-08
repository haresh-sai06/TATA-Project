using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Cinematic multi-camera rig for the Aura demo. Rides the self-driving car
    /// (<see cref="onnxcontroller"/>) and offers several framings you can cycle live:
    ///
    ///   • COCKPIT   — first-person driver seat, looking out over the hood. The star view:
    ///                 you sit inside the autonomous car and feel it drive + take over.
    ///   • CHASE     — classic third-person spring-follow.
    ///   • ORBIT     — slow hero orbit around the car.
    ///   • CINEMATIC — low, sweeping dolly that re-frames itself for a trailer feel.
    ///   • OVERHEAD  — top-down chase, good for showing the AI's path + traffic.
    ///
    /// Attach to your MAIN display camera (the one tagged MainCamera). It drives that
    /// camera's transform + FOV only — it never touches the car's ONNX "vision" camera.
    /// Everything auto-wires on Start, so dropping it on the camera is enough.
    ///
    /// Controls (defaults): [V] cycle view · [1..5] jump to a view · [B] toggle FOV speed-kick.
    /// </summary>
    [DefaultExecutionOrder(300)] // run after the car has moved this frame
    public class AuraCameraDirector : MonoBehaviour
    {
        public enum CameraMode { Cockpit = 0, Chase = 1, Orbit = 2, Cinematic = 3, Overhead = 4 }

        [Header("Target (auto-found if empty)")]
        [Tooltip("The self-driving car to follow. Leave empty to auto-find the onnxcontroller car.")]
        [SerializeField] private Transform target;
        [Tooltip("Optional: the onnxcontroller, used to read speed/steer/takeover for camera feel. Auto-found.")]
        [SerializeField] private onnxcontroller car;

        [Header("Start / Controls")]
        [SerializeField] private CameraMode startMode = CameraMode.Cockpit;
        [SerializeField] private KeyCode cycleKey = KeyCode.V;
        [SerializeField] private bool numberKeysSelect = true;
        [SerializeField] private KeyCode fovKickToggleKey = KeyCode.B;

        [Header("Cockpit (first-person)")]
        [Tooltip("Driver 'head' position in the car's local space. Nudge until the hood sits low in frame.")]
        [SerializeField] private Vector3 cockpitOffset = new Vector3(0f, 1.15f, 0.35f);
        [SerializeField] private float cockpitFov = 68f;
        [Tooltip("How much the view leans into the steering (deg of yaw per deg of wheel).")]
        [SerializeField] private float cockpitSteerLean = 0.18f;
        [SerializeField] private float cockpitPitch = 2.5f;      // slight look-down so the hood shows
        [SerializeField] private float cockpitSharpness = 22f;   // ~rigid (attached to the seat)

        [Header("Chase (third-person)")]
        [SerializeField] private Vector3 chaseOffset = new Vector3(0f, 3.1f, -7.2f);
        [SerializeField] private float chaseLookHeight = 1.4f;
        [SerializeField] private float chaseFov = 60f;
        [SerializeField] private float chaseSharpness = 6f;

        [Header("Orbit")]
        [SerializeField] private float orbitRadius = 8.5f;
        [SerializeField] private float orbitHeight = 3.4f;
        [SerializeField] private float orbitSpeed = 20f;         // deg/s
        [SerializeField] private float orbitFov = 55f;
        [SerializeField] private float orbitSharpness = 5f;

        [Header("Cinematic")]
        [SerializeField] private float cinematicRadius = 6.2f;
        [SerializeField] private float cinematicHeight = 1.3f;
        [SerializeField] private float cinematicFov = 42f;
        [SerializeField] private float cinematicSharpness = 3.2f;

        [Header("Overhead")]
        [SerializeField] private float overheadHeight = 22f;
        [SerializeField] private float overheadBackBias = 4f;
        [SerializeField] private float overheadFov = 55f;
        [SerializeField] private float overheadSharpness = 5f;

        [Header("Feel")]
        [Tooltip("Extra FOV added as the car approaches its top speed — a sense of pace.")]
        [SerializeField] private bool fovSpeedKick = true;
        [SerializeField] private float fovKickAmount = 12f;
        [SerializeField] private float speedForFullKick = 45f;   // km/h at which the kick is maxed
        [SerializeField] private float blendTime = 0.9f;         // smooth blend when switching views
        [SerializeField] private float highSpeedShake = 0.05f;   // subtle handheld shake at speed

        // ── runtime ───────────────────────────────────────────────────
        private Camera _cam;
        private CameraMode _mode;
        private float _orbitAngle;
        private float _cineTime;
        private float _blendTimer;

        private Vector3 _pos;
        private Quaternion _rot;
        private float _fov;

        private Rigidbody _rb;
        private float _shakeAmp;
        private float _shakeDecay = 1f;
        private float _focusBoost;      // set by the takeover to tighten FOV dramatically

        /// <summary>Current view name, e.g. for a HUD label.</summary>
        public string ModeName => _mode.ToString().ToUpperInvariant();
        public CameraMode Mode => _mode;

        // ── lifecycle ─────────────────────────────────────────────────
        private void Awake()
        {
            _cam = GetComponent<Camera>() ?? Camera.main;
            if (_cam == null)
                Debug.LogWarning("[Aura] AuraCameraDirector: no Camera found. Attach it to your Main Camera.", this);
        }

        private void OnEnable()
        {
            // A chase-follow script on the same camera would fight us — stand it down.
            var follow = GetComponent<CameraFollow>();
            if (follow != null) follow.enabled = false;
        }

        private void Start()
        {
            ResolveTarget();
            _mode = startMode;
            if (_cam != null) _fov = _cam.fieldOfView;
            _pos = transform.position;
            _rot = transform.rotation;
            // Seed a sensible first pose so we don't blend in from wherever the camera sat.
            if (target != null) { ComputeTarget(_mode, out _pos, out _rot, out _fov); ApplyPose(); }
        }

        private void ResolveTarget()
        {
            if (car == null) car = FindFirstObjectByType<onnxcontroller>();
            if (target == null && car != null) target = car.transform;
            if (target == null)
            {
                var tagged = GameObject.FindGameObjectWithTag("Player");
                if (tagged != null) target = tagged.transform;
            }
            if (target != null && _rb == null) _rb = target.GetComponentInParent<Rigidbody>();
            if (target == null)
                Debug.LogWarning("[Aura] AuraCameraDirector: no target car found (expected an onnxcontroller in the scene).", this);
        }

        private void Update()
        {
            if (Input.GetKeyDown(cycleKey)) CycleMode();
            if (Input.GetKeyDown(fovKickToggleKey)) fovSpeedKick = !fovSpeedKick;

            if (numberKeysSelect)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1)) SetMode(CameraMode.Cockpit);
                if (Input.GetKeyDown(KeyCode.Alpha2)) SetMode(CameraMode.Chase);
                if (Input.GetKeyDown(KeyCode.Alpha3)) SetMode(CameraMode.Orbit);
                if (Input.GetKeyDown(KeyCode.Alpha4)) SetMode(CameraMode.Cinematic);
                if (Input.GetKeyDown(KeyCode.Alpha5)) SetMode(CameraMode.Overhead);
            }
        }

        private void LateUpdate()
        {
            if (_cam == null) return;
            if (target == null) { ResolveTarget(); if (target == null) return; }

            float dt = Time.unscaledDeltaTime; // camera stays smooth even during takeover slow-mo
            if (_blendTimer > 0f) _blendTimer -= dt;

            ComputeTarget(_mode, out Vector3 tPos, out Quaternion tRot, out float tFov);

            // Exponential (frame-rate-independent) smoothing toward the target pose.
            // During a view switch we temporarily soften the follow for a clean blend.
            float sharp = ModeSharpness(_mode);
            if (_blendTimer > 0f) sharp = Mathf.Min(sharp, 4.5f);
            float k = 1f - Mathf.Exp(-sharp * dt);

            _pos = Vector3.Lerp(_pos, tPos, k);
            _rot = Quaternion.Slerp(_rot, tRot, k);
            _fov = Mathf.Lerp(_fov, tFov, k);

            // Speed shake (subtle) + any takeover shake pulse.
            _shakeAmp = Mathf.MoveTowards(_shakeAmp, 0f, _shakeDecay * dt);
            float speed01 = Mathf.Clamp01(SpeedKmh() / Mathf.Max(speedForFullKick, 1f));
            float shake = _shakeAmp + (highSpeedShake * speed01 * speed01);
            Vector3 shakeOff = shake > 0.0001f
                ? new Vector3(
                    (Mathf.PerlinNoise(Time.unscaledTime * 13f, 0.1f) - 0.5f),
                    (Mathf.PerlinNoise(0.3f, Time.unscaledTime * 15f) - 0.5f),
                    0f) * shake
                : Vector3.zero;

            ApplyPose(shakeOff);
        }

        private void ApplyPose(Vector3 shakeOffset = default)
        {
            transform.SetPositionAndRotation(_pos + _rot * shakeOffset, _rot);
            if (_cam != null) _cam.fieldOfView = Mathf.Clamp(_fov - _focusBoost, 20f, 100f);
        }

        // ── target pose per mode ──────────────────────────────────────
        private void ComputeTarget(CameraMode mode, out Vector3 pos, out Quaternion rot, out float fov)
        {
            Vector3 c = target.position;
            Quaternion carRot = target.rotation;
            Vector3 fwd = target.forward;
            float steer = car != null ? car.hudSteer : 0f;
            float kick = (fovSpeedKick ? fovKickAmount * Mathf.Clamp01(SpeedKmh() / Mathf.Max(speedForFullKick, 1f)) : 0f);

            switch (mode)
            {
                default:
                case CameraMode.Cockpit:
                {
                    pos = target.TransformPoint(cockpitOffset);
                    // Look forward along the car, lean gently into the steering, small look-down for the hood.
                    Quaternion lean = Quaternion.Euler(cockpitPitch, steer * cockpitSteerLean, -steer * cockpitSteerLean * 0.5f);
                    rot = carRot * lean;
                    fov = cockpitFov + kick;
                    break;
                }
                case CameraMode.Chase:
                {
                    pos = target.TransformPoint(chaseOffset);
                    Vector3 look = c + Vector3.up * chaseLookHeight;
                    rot = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
                    fov = chaseFov + kick;
                    break;
                }
                case CameraMode.Orbit:
                {
                    _orbitAngle += orbitSpeed * Time.unscaledDeltaTime;
                    Quaternion yaw = Quaternion.Euler(0f, _orbitAngle, 0f);
                    Vector3 off = yaw * new Vector3(0f, orbitHeight, -orbitRadius);
                    pos = c + off;
                    rot = Quaternion.LookRotation((c + Vector3.up * 1.1f - pos).normalized, Vector3.up);
                    fov = orbitFov + kick * 0.5f;
                    break;
                }
                case CameraMode.Cinematic:
                {
                    _cineTime += Time.unscaledDeltaTime;
                    // Slow counter-orbit at a low, closer height with a gentle radius breathe.
                    float ang = -_cineTime * 12f;
                    float r = cinematicRadius + Mathf.Sin(_cineTime * 0.35f) * 1.6f;
                    Quaternion yaw = Quaternion.Euler(0f, ang, 0f);
                    Vector3 off = yaw * new Vector3(1.5f, cinematicHeight + Mathf.Sin(_cineTime * 0.5f) * 0.4f, -r);
                    pos = c + off;
                    // Aim slightly ahead of the car for a leading-line trailer look.
                    Vector3 aim = c + fwd * 3f + Vector3.up * 0.8f;
                    rot = Quaternion.LookRotation((aim - pos).normalized, Vector3.up);
                    fov = cinematicFov + kick * 0.3f;
                    break;
                }
                case CameraMode.Overhead:
                {
                    pos = c + Vector3.up * overheadHeight - fwd * overheadBackBias;
                    rot = Quaternion.LookRotation((c - pos).normalized, fwd);
                    fov = overheadFov;
                    break;
                }
            }
        }

        private float ModeSharpness(CameraMode m) => m switch
        {
            CameraMode.Cockpit => cockpitSharpness,
            CameraMode.Chase => chaseSharpness,
            CameraMode.Orbit => orbitSharpness,
            CameraMode.Cinematic => cinematicSharpness,
            CameraMode.Overhead => overheadSharpness,
            _ => 6f
        };

        private float SpeedKmh()
        {
            if (_rb != null) return _rb.linearVelocity.magnitude * 3.6f;
            if (car != null) return car.hudSpeed;
            return 0f;
        }

        // ── public API (used by the takeover director) ────────────────
        public void SetMode(CameraMode mode)
        {
            if (mode == _mode) return;
            _mode = mode;
            _blendTimer = blendTime;
        }

        public void CycleMode()
        {
            SetMode((CameraMode)(((int)_mode + 1) % 5));
        }

        /// <summary>Kick a short handheld shake (used on the takeover beat).</summary>
        public void AddShake(float amplitude, float decay = 1.2f)
        {
            _shakeAmp = Mathf.Max(_shakeAmp, amplitude);
            _shakeDecay = decay;
        }

        /// <summary>Tighten the FOV for a dramatic "locking on" feel (0 = off). Set/cleared by takeover.</summary>
        public void SetFocusBoost(float amount) => _focusBoost = amount;
    }
}
