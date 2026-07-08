using UnityEngine;

namespace Aura
{
    /// <summary>
    /// A compact, premium status ribbon that ties the first-person experience together:
    /// a top-centre AURA badge with the identified driver, the autonomy state
    /// (AUTOPILOT / AURA CONTROL), and the live camera view; plus a subtle cockpit
    /// vignette while in the first-person seat. Complements the existing speedometer
    /// (bottom-right) and the Explainable Monitor (centre, toggle M) — it does not
    /// duplicate them. Attach anywhere in the scene; everything auto-wires.
    /// </summary>
    [DefaultExecutionOrder(250)]
    public class AuraCockpitHud : MonoBehaviour
    {
        [SerializeField] private AuraCameraDirector cameraDirector;
        [SerializeField] private AuraDemoReactor reactor;
        [SerializeField] private onnxcontroller car;
        [Tooltip("Dim the screen edges while in the cockpit for an in-cabin feel.")]
        [SerializeField] private bool cockpitVignette = true;

        private Rigidbody _rb;

        private void Start()
        {
            if (cameraDirector == null) cameraDirector = FindFirstObjectByType<AuraCameraDirector>();
            if (reactor == null) reactor = FindFirstObjectByType<AuraDemoReactor>();
            if (car == null) car = FindFirstObjectByType<onnxcontroller>();
            if (car != null) _rb = car.GetComponent<Rigidbody>();
        }

        private void OnGUI()
        {
            float sw = Screen.width;
            bool takeover = reactor != null && reactor.TakeoverActive;
            bool cockpit = cameraDirector != null && cameraDirector.Mode == AuraCameraDirector.CameraMode.Cockpit;

            // Subtle cockpit vignette (skip during takeover — that overlay owns the screen).
            if (cockpitVignette && cockpit && !takeover)
            {
                float sh = Screen.height;
                Fill(new Rect(0, 0, sw, 34f), new Color(0.02f, 0.03f, 0.05f, 0.55f));
                Fill(new Rect(0, sh - 44f, sw, 44f), new Color(0.02f, 0.03f, 0.05f, 0.60f));
            }

            // ── Top-centre status ribbon ──────────────────────────────
            string driver = reactor != null ? reactor.DriverName : "Driver";
            string view = cameraDirector != null ? cameraDirector.ModeName : "—";
            float kmh = _rb != null ? _rb.linearVelocity.magnitude * 3.6f : (car != null ? car.hudSpeed : 0f);

            float rw = 460f, rh = 34f;
            float rx = (sw - rw) / 2f, ry = 12f;
            Fill(new Rect(rx, ry, rw, rh), new Color(0.04f, 0.06f, 0.11f, 0.86f));
            Border(new Rect(rx, ry, rw, rh), new Color(0f, 0.55f, 0.72f, 0.75f), 1f);

            // AURA mark
            GUI.Label(new Rect(rx + 12f, ry + 7f, 90f, 20f), "◆ AURA",
                Lbl(14, new Color(0.4f, 0.85f, 1f), FontStyle.Bold));

            // Driver
            GUI.Label(new Rect(rx + 92f, ry + 8f, 160f, 20f), driver,
                Lbl(13, Color.white, FontStyle.Bold, TextAnchor.MiddleLeft));

            // Autonomy state pill
            bool engaged = takeover;
            Color stCol = engaged ? new Color(1f, 0.35f, 0.3f) : new Color(0.25f, 0.85f, 0.5f);
            string stTxt = engaged ? "⚠ AURA CONTROL" : "● AUTOPILOT";
            GUI.Label(new Rect(rx + rw - 250f, ry + 8f, 150f, 20f), stTxt,
                Lbl(12, stCol, FontStyle.Bold, TextAnchor.MiddleLeft));

            // Camera view + speed
            GUI.Label(new Rect(rx + rw - 100f, ry + 8f, 90f, 20f), $"◉ {view}",
                Lbl(11, new Color(0.7f, 0.82f, 0.92f), FontStyle.Bold, TextAnchor.MiddleRight));

            // Compact speed under the ribbon, centre (secondary to the corner speedometer).
            GUI.Label(new Rect(rx, ry + rh + 2f, rw, 18f), $"{Mathf.RoundToInt(kmh)} km/h · self-driving",
                Lbl(11, new Color(0.6f, 0.72f, 0.85f), FontStyle.Normal, TextAnchor.MiddleCenter));

            GUI.color = Color.white;
        }

        // ── helpers ───────────────────────────────────────────────────
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
            var s = new GUIStyle(GUI.skin.label) { fontSize = size, fontStyle = fs, alignment = a };
            s.normal.textColor = col;
            return s;
        }
    }
}
