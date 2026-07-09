using System.Collections.Generic;
using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Draws the car's planned/predicted trajectory as a glowing ribbon on the road ahead — the
    /// "explainable" view of where the autonomous planner intends to go next. Reads the live path
    /// from the onnxcontroller and renders the next N metres, tapering and emissive so the scene's
    /// Bloom makes it glow. Hidden in cockpit view (it's a chase/overhead storytelling overlay).
    /// Attach to the car (with the onnxcontroller).
    /// </summary>
    [DefaultExecutionOrder(330)]
    public class AuraTrajectoryViz : MonoBehaviour
    {
        [SerializeField] private float aheadDistance = 45f;
        [SerializeField] private int   samples = 40;
        [SerializeField] private float heightOffset = 0.14f;
        [SerializeField] private float startWidth = 0.95f;
        [SerializeField] private float endWidth   = 0.12f;
        [SerializeField] private Color trajColor = new Color(0.15f, 0.85f, 1f);
        [SerializeField] private bool  hideInCockpit = true;

        private onnxcontroller _car;
        private AuraCameraDirector _director;
        private LineRenderer _lr;

        private void Start()
        {
            _car = GetComponent<onnxcontroller>() ?? GetComponentInParent<onnxcontroller>() ?? FindFirstObjectByType<onnxcontroller>();
            _director = FindFirstObjectByType<AuraCameraDirector>();

            var go = new GameObject("Aura Predicted Trajectory");
            _lr = go.AddComponent<LineRenderer>();
            _lr.useWorldSpace = true;
            _lr.numCapVertices = 4;
            _lr.numCornerVertices = 4;
            _lr.textureMode = LineTextureMode.Stretch;
            _lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _lr.receiveShadows = false;
            _lr.widthCurve = AnimationCurve.Linear(0f, startWidth, 1f, endWidth);

            var mat = new Material(Shader.Find("Sprites/Default"));
            mat.color = trajColor;
            _lr.material = mat;
            _lr.startColor = new Color(trajColor.r, trajColor.g, trajColor.b, 0.95f);
            _lr.endColor   = new Color(trajColor.r, trajColor.g, trajColor.b, 0f);
        }

        private void LateUpdate()
        {
            if (_car == null || _lr == null) return;

            if (hideInCockpit && _director != null && _director.Mode == AuraCameraDirector.CameraMode.Cockpit)
            {
                _lr.positionCount = 0;
                return;
            }

            // No "predicted trajectory" while Aura is taking over / pulling the car over.
            if (_car.emergencyStop)
            {
                _lr.positionCount = 0;
                return;
            }

            var path = _car.PathPoints;
            if (path == null || path.Count < 2) { _lr.positionCount = 0; return; }

            int idx = Mathf.Clamp(_car.WpIndex, 0, path.Count - 1);
            var pts = new List<Vector3> { path[idx] + Vector3.up * heightOffset };
            float step = Mathf.Max(aheadDistance / Mathf.Max(samples, 1), 0.5f);
            float acc = 0f, sinceSample = 0f;

            for (int guard = 0; guard < path.Count && acc < aheadDistance; guard++)
            {
                int nxt = idx + 1;
                if (nxt >= path.Count) break;   // don't wrap the loop — avoids wild geometry
                Vector3 a = path[idx], b = path[nxt];
                float seg = Vector3.Distance(a, b);
                if (seg > 1e-3f)
                {
                    acc += seg; sinceSample += seg;
                    if (sinceSample >= step) { pts.Add(b + Vector3.up * heightOffset); sinceSample = 0f; }
                }
                idx = nxt;
            }

            _lr.positionCount = pts.Count;
            _lr.SetPositions(pts.ToArray());
        }
    }
}
