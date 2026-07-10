using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Manual WASD nudge — lets the driver briefly override the self-driving car to steer away
    /// from a hazard. While a key is held it adds force/torque to the car's Rigidbody; with no key
    /// pressed it does nothing, so the autopilot stays in control.
    ///   W / S = accelerate forward / reverse
    ///   A / D = steer left / right
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ManualDriveOverride : MonoBehaviour
    {
        [SerializeField] private float accelForce = 3200f;
        [SerializeField] private float steerTorque = 1100f;

        private Rigidbody _rb;

        private void Awake() => _rb = GetComponent<Rigidbody>();

        private void FixedUpdate()
        {
            if (_rb == null) return;
            float v = (Input.GetKey(KeyCode.W) ? 1f : 0f) - (Input.GetKey(KeyCode.S) ? 1f : 0f);
            float h = (Input.GetKey(KeyCode.D) ? 1f : 0f) - (Input.GetKey(KeyCode.A) ? 1f : 0f);
            if (Mathf.Abs(v) > 0f)
                _rb.AddForce(transform.forward * v * accelForce, ForceMode.Force);
            if (Mathf.Abs(h) > 0f)
                _rb.AddTorque(Vector3.up * h * steerTorque, ForceMode.Force);
        }
    }
}
