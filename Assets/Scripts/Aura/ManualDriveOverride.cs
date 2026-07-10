using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Manual WASD override — the driver can grab the wheel and drive the self-driving car.
    /// While a key is held it directly drives the Rigidbody (so it actually overrides the AI, not
    /// just nudges it); with no key pressed it does nothing and the autopilot stays in control.
    /// It NEVER fights an emergency pull-over (safety wins). Runs after the AI and the speed
    /// controller so manual input takes priority.
    ///   W / S = accelerate forward / reverse
    ///   A / D = steer left / right
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(280)]
    public class ManualDriveOverride : MonoBehaviour
    {
        [SerializeField] private float driveKmh = 42f;     // forward target while W held
        [SerializeField] private float reverseKmh = 12f;   // reverse target while S held
        [SerializeField] private float accelRate = 16f;    // m/s² toward the target
        [SerializeField] private float turnRateDeg = 75f;  // yaw deg/sec while steering

        private Rigidbody _rb;
        private onnxcontroller _car;

        /// <summary>True while the driver is actively holding a WASD key (for HUD, etc.).</summary>
        public bool ManualActive { get; private set; }

        private void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _car = GetComponent<onnxcontroller>();
        }

        private void FixedUpdate()
        {
            if (_rb == null) return;

            // Safety first: never override an emergency pull-over.
            if (_car != null && _car.emergencyStop) { ManualActive = false; return; }

            float v = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float h = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);

            // No input → hand control back to the autopilot / speed controller.
            if (Mathf.Abs(v) < 0.01f && Mathf.Abs(h) < 0.01f) { ManualActive = false; return; }
            ManualActive = true;

            // Steer: rotate the car body immediately (visible, responsive).
            if (Mathf.Abs(h) > 0f)
                transform.Rotate(0f, h * turnRateDeg * Time.fixedDeltaTime, 0f, Space.World);

            // Drive: push the forward speed toward the manual target along the current heading.
            float fwd = Vector3.Dot(_rb.linearVelocity, transform.forward);
            float target = v > 0f ? driveKmh / 3.6f : (v < 0f ? -reverseKmh / 3.6f : fwd);
            float next = Mathf.MoveTowards(fwd, target, accelRate * Time.fixedDeltaTime);
            _rb.linearVelocity = transform.forward * next + Vector3.up * _rb.linearVelocity.y;
            _rb.angularVelocity = Vector3.ClampMagnitude(_rb.angularVelocity, 2f); // anti spin-out
        }
    }
}
