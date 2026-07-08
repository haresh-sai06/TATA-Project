using UnityEngine;

namespace Aura
{
    /// <summary>
    /// Streams the self-driving car's live state to Aura Core as <c>vehicle.telemetry</c>, so the
    /// React dashboard mirrors the REAL Unity car (speed, steering, pull-over state) instead of a
    /// faked number. The hub rebroadcasts it to every client. Attach anywhere; it auto-finds the
    /// AuraClient socket and the onnxcontroller car.
    /// </summary>
    [RequireComponent(typeof(AuraClient))]
    public class AuraTelemetryStreamer : MonoBehaviour
    {
        [Tooltip("The self-driving car to report. Auto-found if empty.")]
        [SerializeField] private onnxcontroller car;
        [Tooltip("How many telemetry frames to send per second.")]
        [Range(2f, 30f)] [SerializeField] private float sendRate = 10f;
        [Tooltip("Scenario label shown on the dashboard.")]
        [SerializeField] private string scenario = "City Night";

        private AuraClient _client;
        private Rigidbody _rb;
        private float _next;

        private void Awake()
        {
            _client = GetComponent<AuraClient>();
            if (car == null) car = FindFirstObjectByType<onnxcontroller>();
            if (car != null) _rb = car.GetComponent<Rigidbody>();
        }

        private void Update()
        {
            if (_client == null || !_client.IsConnected || car == null) return;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f / Mathf.Max(sendRate, 1f);

            if (_rb == null) _rb = car.GetComponent<Rigidbody>();
            float kmh = _rb != null ? _rb.linearVelocity.magnitude * 3.6f : car.hudSpeed;

            _client.Send("vehicle.telemetry", new
            {
                speedKmh = Mathf.Round(kmh * 10f) / 10f,
                throttle = Mathf.Round(car.hudThrottle * 100f) / 100f,
                steer = Mathf.Round(car.hudSteer * 10f) / 10f,
                autonomous = true,
                pullingOver = car.emergencyStop,
                scenario = scenario,
                wpIndex = car.hudWpIdx,
                wpTotal = car.hudWpTotal,
            });
        }
    }
}
