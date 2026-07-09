using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Builds a first-person cockpit interior so the Cockpit camera reads like a real driver's
    /// seat (dashboard + steering wheel + windshield frame) instead of a bumper-cam over the
    /// wheels. Everything is procedural — no art assets — so it works in any scene. The steering
    /// wheel physically turns with the car's steering input, and the whole interior only shows in
    /// Cockpit view (hidden in chase/orbit/etc.).
    ///
    /// Attach to the self-driving car (the object with <see cref="onnxcontroller"/>). All the
    /// placement values are Inspector-tunable in the car's local space (+Z = forward, +Y = up),
    /// so you can nudge the dash/wheel live without recompiling.
    /// </summary>
    [DefaultExecutionOrder(310)] // after AuraCameraDirector (300)
    public class AuraCockpitRig : MonoBehaviour
    {
        [Header("Dashboard (car-local space)")]
        public Vector3 dashLocalPos = new Vector3(0f, 0.70f, 1.0f);
        public Vector3 dashSize     = new Vector3(2.1f, 0.34f, 0.8f);
        public Vector3 dashEuler    = new Vector3(-16f, 0f, 0f);

        [Header("Steering wheel")]
        public Vector3 wheelLocalPos = new Vector3(-0.32f, 1.0f, 0.58f);
        public Vector3 wheelEuler    = new Vector3(-67f, 0f, 0f); // laid back on the column
        public float   wheelRimRadius = 0.20f;
        public float   wheelTubeRadius = 0.022f;
        [Tooltip("Degrees of wheel rotation per degree of car steer.")]
        public float   steerMultiplier = 11f;
        public float   steerSmoothing  = 9f;

        [Header("Framing")]
        public bool  buildPillars = true;
        public Color interiorColor = new Color(0.045f, 0.045f, 0.055f);
        public Color wheelColor    = new Color(0.07f, 0.07f, 0.08f);

        [Header("Dash infotainment screen")]
        public bool  buildDashScreen = true;
        public Color screenColor = new Color(0.10f, 0.55f, 0.95f);

        // ── runtime ───────────────────────────────────────────────────
        private onnxcontroller _car;
        private AuraCameraDirector _director;
        private Transform _interior;
        private Transform _wheel;
        private float _steer;

        private void Start()
        {
            _car = GetComponent<onnxcontroller>() ?? GetComponentInParent<onnxcontroller>() ?? FindFirstObjectByType<onnxcontroller>();
            _director = FindFirstObjectByType<AuraCameraDirector>();
            Transform carT = _car != null ? _car.transform : transform;

            Material matBody   = MakeMat(interiorColor, 0.2f, 0.05f);
            Material matWheel  = MakeMat(wheelColor, 0.35f, 0.2f);
            Material matScreen = MakeEmissive(screenColor);

            var root = new GameObject("Aura Cockpit Interior");
            _interior = root.transform;
            _interior.SetParent(carT, false);

            // ── Dashboard ──────────────────────────────────────────────
            MakeBox("Dashboard", _interior, dashLocalPos, dashEuler, dashSize, matBody);

            // Infotainment screen — parented to the interior (not the non-uniformly-scaled dash,
            // which would distort it); sits centre-dash, tilted up toward the driver.
            if (buildDashScreen)
            {
                var scr = GameObject.CreatePrimitive(PrimitiveType.Quad);
                DestroyImmediate(scr.GetComponent<Collider>());
                scr.name = "Dash Screen";
                scr.transform.SetParent(_interior, false);
                scr.transform.localPosition = new Vector3(0.12f, 0.9f, 0.92f);
                scr.transform.localRotation = Quaternion.Euler(18f, 180f, 0f);
                scr.transform.localScale = new Vector3(0.36f, 0.21f, 1f);
                scr.GetComponent<MeshRenderer>().sharedMaterial = matScreen;
            }

            // ── Steering wheel (rim + hub + 3 spokes) ──────────────────
            var wheelRoot = new GameObject("Steering Wheel");
            _wheel = wheelRoot.transform;
            _wheel.SetParent(_interior, false);
            _wheel.localPosition = wheelLocalPos;
            _wheel.localEulerAngles = wheelEuler;

            var rim = new GameObject("Rim");
            rim.transform.SetParent(_wheel, false);
            rim.AddComponent<MeshFilter>().sharedMesh = BuildTorus(wheelRimRadius, wheelTubeRadius, 44, 14);
            rim.AddComponent<MeshRenderer>().sharedMaterial = matWheel;

            var hub = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            DestroyImmediate(hub.GetComponent<Collider>());
            hub.name = "Hub";
            hub.transform.SetParent(_wheel, false);
            hub.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            hub.transform.localScale = new Vector3(0.07f, 0.014f, 0.07f);
            hub.GetComponent<MeshRenderer>().sharedMaterial = matWheel;

            for (int i = 0; i < 3; i++)
            {
                float a = -90f + i * 120f;
                var spoke = MakeBox("Spoke", _wheel, Vector3.zero, new Vector3(0f, 0f, a),
                    new Vector3(0.026f, wheelRimRadius, 0.026f), matWheel);
                spoke.transform.localPosition = Quaternion.Euler(0f, 0f, a) * new Vector3(0f, wheelRimRadius * 0.5f, 0f);
            }

            // ── Windshield frame (A-pillars + roof + mirror) ───────────
            if (buildPillars) BuildFrame(_interior, matBody);
        }

        private void LateUpdate()
        {
            // Only visible from the cockpit seat.
            if (_director != null && _interior != null)
            {
                bool cockpit = _director.Mode == AuraCameraDirector.CameraMode.Cockpit;
                if (_interior.gameObject.activeSelf != cockpit) _interior.gameObject.SetActive(cockpit);
                if (!cockpit) return;
            }

            float target = (_car != null ? _car.hudSteer : 0f) * steerMultiplier;
            _steer = Mathf.Lerp(_steer, target, 1f - Mathf.Exp(-steerSmoothing * Time.unscaledDeltaTime));
            if (_wheel != null)
                _wheel.localRotation = Quaternion.Euler(wheelEuler.x, wheelEuler.y, wheelEuler.z - _steer);
        }

        // ── builders ──────────────────────────────────────────────────
        private void BuildFrame(Transform parent, Material mat)
        {
            // Two A-pillars angling up from the dash corners.
            MakeBox("A-Pillar L", parent, new Vector3(-0.95f, 1.35f, 0.85f), new Vector3(18f, 0f, 12f), new Vector3(0.09f, 1.5f, 0.09f), mat);
            MakeBox("A-Pillar R", parent, new Vector3(0.95f, 1.35f, 0.85f),  new Vector3(18f, 0f, -12f), new Vector3(0.09f, 1.5f, 0.09f), mat);
            // Roof line across the top of the windscreen.
            MakeBox("Roof", parent, new Vector3(0f, 2.02f, 0.55f), new Vector3(10f, 0f, 0f), new Vector3(2.1f, 0.14f, 0.5f), mat);
            // Rear-view mirror hint.
            MakeBox("Mirror", parent, new Vector3(0.18f, 1.78f, 0.7f), new Vector3(4f, 0f, 0f), new Vector3(0.34f, 0.09f, 0.05f), mat);
        }

        private static GameObject MakeBox(string boxName, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(go.GetComponent<Collider>());
            go.name = boxName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        private static Material MakeMat(Color c, float smoothness, float metallic)
        {
            var m = new Material(Shader.Find("Standard"));
            m.color = c;
            m.SetFloat("_Glossiness", smoothness);
            m.SetFloat("_Metallic", metallic);
            return m;
        }

        private static Material MakeEmissive(Color c)
        {
            var m = new Material(Shader.Find("Standard"));
            m.color = c * 0.3f;
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            m.SetColor("_EmissionColor", c * 1.6f);
            return m;
        }

        // Procedural torus in the local XY plane (face normal = +Z), so a Z-rotation spins it
        // like a steering wheel. R = rim radius, r = tube radius.
        private static Mesh BuildTorus(float R, float r, int seg, int side)
        {
            var verts = new Vector3[(seg + 1) * (side + 1)];
            var norms = new Vector3[verts.Length];
            var uvs   = new Vector2[verts.Length];
            var tris  = new int[seg * side * 6];
            int vi = 0;
            for (int i = 0; i <= seg; i++)
            {
                float u = (float)i / seg * Mathf.PI * 2f;
                Vector3 cu = new Vector3(Mathf.Cos(u), Mathf.Sin(u), 0f);
                for (int j = 0; j <= side; j++)
                {
                    float v = (float)j / side * Mathf.PI * 2f;
                    Vector3 n = cu * Mathf.Cos(v) + Vector3.forward * Mathf.Sin(v);
                    verts[vi] = cu * R + n * r;
                    norms[vi] = n;
                    uvs[vi] = new Vector2((float)i / seg, (float)j / side);
                    vi++;
                }
            }
            int ti = 0;
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a = i * (side + 1) + j;
                    int b = a + side + 1;
                    tris[ti++] = a; tris[ti++] = b; tris[ti++] = a + 1;
                    tris[ti++] = b; tris[ti++] = b + 1; tris[ti++] = a + 1;
                }
            var mesh = new Mesh { name = "AuraSteeringRim" };
            mesh.vertices = verts; mesh.normals = norms; mesh.uv = uvs; mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
