using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Cinematic environment layer for the Aura night-city demo — turns the flat arcade scene
    /// into a moody, rain-slicked neon street. Built-in Render Pipeline safe (no URP/HDRP): it
    /// only touches RenderSettings (fog/ambient), spawns a rain particle system on the camera,
    /// and adds headlights to the self-driving car. Everything is additive and reversible, and
    /// each layer is an independent toggle so nothing here can break the existing sim.
    ///
    /// It also listens to the takeover (<see cref="AuraDemoReactor.TakeoverActive"/>) and swings
    /// the whole scene into a pulsing red "emergency" mood for the pull-over beat, then restores.
    ///
    /// Drop this on any GameObject (e.g. an "Aura Atmosphere" object) and press Play. Tune the
    /// toggles/values live in the Inspector.
    /// </summary>
    [DefaultExecutionOrder(50)]
    public class AuraAtmosphere : MonoBehaviour
    {
        [Header("Master")]
        [Tooltip("Restore the original RenderSettings when this component is disabled/stopped.")]
        [SerializeField] private bool restoreOnDisable = true;

        [Header("Fog / Ambient (neon night haze)")]
        [SerializeField] private bool enableFog = true;
        [SerializeField] private Color fogColor = new Color(0.03f, 0.04f, 0.09f);
        [Tooltip("Exponential fog density. 0.006–0.02 reads as haze without washing the scene.")]
        [Range(0f, 0.05f)] [SerializeField] private float fogDensity = 0.010f;
        [SerializeField] private bool overrideAmbient = true;
        [SerializeField] private Color ambientColor = new Color(0.06f, 0.07f, 0.14f);

        [Header("Rain (follows the main camera)")]
        [SerializeField] private bool enableRain = true;
        [Range(0, 4000)] [SerializeField] private int rainRate = 900;
        [SerializeField] private Color rainColor = new Color(0.7f, 0.8f, 1f, 0.35f);
        [SerializeField] private float rainFallSpeed = 28f;
        [SerializeField] private Vector3 rainAreaSize = new Vector3(34f, 1f, 34f);
        [SerializeField] private float rainHeight = 16f;

        [Header("Headlights (attached to the self-driving car)")]
        [SerializeField] private bool enableHeadlights = true;
        [SerializeField] private Color headlightColor = new Color(1f, 0.95f, 0.85f);
        [SerializeField] private float headlightIntensity = 4.5f;
        [SerializeField] private float headlightRange = 45f;
        [SerializeField] private float headlightAngle = 55f;
        [Tooltip("Local offsets on the car for the two headlights (metres).")]
        [SerializeField] private Vector3 headlightLeft = new Vector3(-0.7f, 0.7f, 1.9f);
        [SerializeField] private Vector3 headlightRight = new Vector3(0.7f, 0.7f, 1.9f);

        [Header("Takeover mood (syncs with AuraDemoReactor)")]
        [SerializeField] private bool enableTakeoverMood = true;
        [SerializeField] private Color takeoverFog = new Color(0.20f, 0.02f, 0.02f);
        [SerializeField] private float takeoverFogDensity = 0.020f;
        [SerializeField] private Color emergencyLight = new Color(1f, 0.12f, 0.1f);
        [SerializeField] private float emergencyIntensity = 6f;
        [SerializeField] private float emergencyPulseHz = 3f;

        // ── runtime ───────────────────────────────────────────────────
        private AuraDemoReactor _reactor;
        private Transform _car;
        private ParticleSystem _rain;
        private Light _emergency;
        private float _moodT;            // 0 = normal, 1 = full takeover mood

        // saved originals for restore
        private bool _sFog; private Color _sFogColor; private FogMode _sFogMode; private float _sFogDensity;
        private UnityEngine.Rendering.AmbientMode _sAmbMode; private Color _sAmbient;
        private bool _saved;

        private void OnEnable()
        {
            SaveOriginal();
            _reactor = FindFirstObjectByType<AuraDemoReactor>();
            var onnx = FindFirstObjectByType<onnxcontroller>();
            _car = onnx != null ? onnx.transform : null;

            ApplyBaseAtmosphere();
            if (enableRain) BuildRain();
            if (enableHeadlights && _car != null) BuildHeadlights();
            if (enableTakeoverMood) BuildEmergencyLight();
        }

        private void OnDisable()
        {
            if (restoreOnDisable && _saved) RestoreOriginal();
            if (_rain != null) Destroy(_rain.gameObject);
            if (_emergency != null) Destroy(_emergency.gameObject);
        }

        private void Update()
        {
            // Blend the takeover mood in/out on unscaled time (survives the slow-mo beat).
            bool takingOver = enableTakeoverMood && _reactor != null && _reactor.TakeoverActive;
            _moodT = Mathf.MoveTowards(_moodT, takingOver ? 1f : 0f, Time.unscaledDeltaTime / 0.5f);

            if (enableFog)
            {
                RenderSettings.fogColor   = Color.Lerp(fogColor, takeoverFog, _moodT);
                RenderSettings.fogDensity = Mathf.Lerp(fogDensity, takeoverFogDensity, _moodT);
            }

            if (_emergency != null)
            {
                if (_car != null) _emergency.transform.position = _car.position + Vector3.up * 2.2f;
                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * emergencyPulseHz * Mathf.PI * 2f);
                _emergency.enabled = _moodT > 0.02f;
                _emergency.intensity = emergencyIntensity * _moodT * pulse;
            }
        }

        // ── base atmosphere ───────────────────────────────────────────
        private void ApplyBaseAtmosphere()
        {
            if (enableFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.ExponentialSquared;
                RenderSettings.fogColor = fogColor;
                RenderSettings.fogDensity = fogDensity;
            }
            if (overrideAmbient)
            {
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = ambientColor;
            }
        }

        // ── rain ──────────────────────────────────────────────────────
        private void BuildRain()
        {
            Camera cam = Camera.main ?? FindFirstObjectByType<Camera>();
            if (cam == null) return;

            var go = new GameObject("Aura Rain");
            go.transform.SetParent(cam.transform, false);
            go.transform.localPosition = new Vector3(0f, rainHeight, 6f); // ahead + above the camera
            go.transform.rotation = Quaternion.Euler(90f, 0f, 0f);         // emit downward in world space

            _rain = go.AddComponent<ParticleSystem>();
            var main = _rain.main;
            main.startLifetime = rainHeight / Mathf.Max(rainFallSpeed, 1f) + 0.4f;
            main.startSpeed = rainFallSpeed;
            main.startSize = 0.06f;
            main.startColor = rainColor;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 6000;
            main.gravityModifier = 0.6f;

            var emission = _rain.emission;
            emission.rateOverTime = rainRate;

            var shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = rainAreaSize;

            // Stretched streaks so drops read as rain, not snow.
            var rend = go.GetComponent<ParticleSystemRenderer>();
            rend.renderMode = ParticleSystemRenderMode.Stretch;
            rend.velocityScale = 0.08f;
            rend.lengthScale = 4.5f;
            var shader = Shader.Find("Sprites/Default"); // always included, no magenta
            if (shader != null) rend.material = new Material(shader);
            rend.material.color = rainColor;
        }

        // ── headlights ────────────────────────────────────────────────
        private void BuildHeadlights()
        {
            AddSpot("Aura Headlight L", headlightLeft);
            AddSpot("Aura Headlight R", headlightRight);
        }

        private void AddSpot(string lightName, Vector3 localPos)
        {
            var go = new GameObject(lightName);
            go.transform.SetParent(_car, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(6f, 0f, 0f); // slight downward tilt onto the road
            var l = go.AddComponent<Light>();
            l.type = LightType.Spot;
            l.color = headlightColor;
            l.intensity = headlightIntensity;
            l.range = headlightRange;
            l.spotAngle = headlightAngle;
            l.renderMode = LightRenderMode.ForcePixel; // crisp cone on the road
            l.shadows = LightShadows.Soft;
        }

        // ── emergency light ───────────────────────────────────────────
        private void BuildEmergencyLight()
        {
            var go = new GameObject("Aura Emergency Light");
            _emergency = go.AddComponent<Light>();
            _emergency.type = LightType.Point;
            _emergency.color = emergencyLight;
            _emergency.range = 40f;
            _emergency.intensity = 0f;
            _emergency.enabled = false;
        }

        // ── save / restore ────────────────────────────────────────────
        private void SaveOriginal()
        {
            if (_saved) return;
            _sFog = RenderSettings.fog; _sFogColor = RenderSettings.fogColor;
            _sFogMode = RenderSettings.fogMode; _sFogDensity = RenderSettings.fogDensity;
            _sAmbMode = RenderSettings.ambientMode; _sAmbient = RenderSettings.ambientLight;
            _saved = true;
        }

        private void RestoreOriginal()
        {
            RenderSettings.fog = _sFog; RenderSettings.fogColor = _sFogColor;
            RenderSettings.fogMode = _sFogMode; RenderSettings.fogDensity = _sFogDensity;
            RenderSettings.ambientMode = _sAmbMode; RenderSettings.ambientLight = _sAmbient;
        }
    }
}
