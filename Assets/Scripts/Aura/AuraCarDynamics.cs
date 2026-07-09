using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Visual weight-transfer for the car: leans the body into turns and pitches it forward under
    /// braking / back under acceleration, so the car feels like it has real mass. Purely cosmetic —
    /// it tilts only the visual body mesh (a child transform), never the physics Rigidbody or the
    /// wheels, so it can never affect the actual driving. Attach to the car (with the onnxcontroller).
    /// </summary>
    [DefaultExecutionOrder(320)]
    public class AuraCarDynamics : MonoBehaviour
    {
        [Header("Amount (degrees)")]
        [SerializeField] private float maxRoll  = 5.5f;   // lean into a turn
        [SerializeField] private float maxPitch = 3.5f;   // dip under braking / squat under accel
        [SerializeField] private float responsiveness = 6f;

        [Header("Sensitivity")]
        [SerializeField] private float rollFromTurn   = 0.11f; // per (yawRate * speed)
        [SerializeField] private float pitchFromAccel = 0.5f;  // per m/s^2

        private Rigidbody _rb;
        private Transform _body;
        private Quaternion _bodyBase;
        private float _lastSpeed, _roll, _pitch;

        private void Start()
        {
            _rb = GetComponent<Rigidbody>() ?? GetComponentInParent<Rigidbody>();
            _body = FindBodyMesh();
            if (_body != null) _bodyBase = _body.localRotation;
            else Debug.Log("[Aura] AuraCarDynamics: no distinct body mesh found — dynamics disabled (safe).", this);
        }

        // Largest visual mesh that isn't a wheel and isn't the physics root itself.
        private Transform FindBodyMesh()
        {
            Transform best = null; float bestVol = 0f;
            foreach (var mr in GetComponentsInChildren<MeshRenderer>())
            {
                if (mr.transform == transform) continue;   // never tilt the Rigidbody root
                string n = mr.gameObject.name.ToLower();
                if (n.Contains("wheel") || n.Contains("tire") || n.Contains("tyre")) continue;
                if (n.Contains("cockpit") || n.Contains("dashboard") || n.Contains("steering")) continue;
                float v = mr.bounds.size.sqrMagnitude;
                if (v > bestVol) { bestVol = v; best = mr.transform; }
            }
            return best;
        }

        private void LateUpdate()
        {
            if (_body == null || _rb == null) return;
            float dt = Time.deltaTime;
            if (dt < 1e-5f) return;

            float speed = _rb.linearVelocity.magnitude;
            float yaw   = _rb.angularVelocity.y;                       // turn rate (rad/s)
            float targetRoll  = Mathf.Clamp(yaw * speed * rollFromTurn, -maxRoll, maxRoll);
            float accel = (speed - _lastSpeed) / dt; _lastSpeed = speed;
            float targetPitch = Mathf.Clamp(-accel * pitchFromAccel, -maxPitch, maxPitch);

            float k = 1f - Mathf.Exp(-responsiveness * dt);
            _roll  = Mathf.Lerp(_roll,  targetRoll,  k);
            _pitch = Mathf.Lerp(_pitch, targetPitch, k);

            _body.localRotation = _bodyBase * Quaternion.Euler(_pitch, 0f, _roll);
        }
    }
}
