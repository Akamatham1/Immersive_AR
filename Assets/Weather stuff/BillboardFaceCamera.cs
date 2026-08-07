using UnityEngine;

// Attach to your WeatherPanel GameObject.
// No coroutines, no external dependencies, no exceptions.
// Retries finding the camera every frame until found.
public class BillboardFaceCamera : MonoBehaviour
{
    [Header("Settings")]
    public float rotationSpeed = 2f;
    public bool lockXAxis = true;

    [Tooltip("Degrees the panel may be off from its damped target before it re-orients. Higher = calmer panel.")]
    public float deadZoneAngle = 10f;

    [Tooltip("Fraction (0–1) of the full camera-facing turn the panel performs. 1 = classic billboard, 0 = never turns.")]
    [Range(0f, 1f)] public float followAmount = 0.5f;

    [Tooltip("Maximum degrees the panel may swing away from its rest orientation on the target.")]
    public float maxSwingAngle = 30f;

    private Camera _arCamera;
    private float _retryTimer;
    private bool _reorienting;
    private Quaternion _restLocalRotation;
    private const float RETRY_INTERVAL = 0.5f;
    private const float SETTLE_ANGLE = 0.5f;

    void Awake()
    {
        _restLocalRotation = transform.localRotation;
    }

    void LateUpdate()
    {
        // Retry finding camera on a timer — no exceptions possible
        if (_arCamera == null)
        {
            _retryTimer -= Time.deltaTime;
            if (_retryTimer > 0f) return;
            _retryTimer = RETRY_INTERVAL;

            _arCamera = GetARCamera();
            return; // wait until next frame to use it
        }

        // Bill board logic
        Vector3 dir = _arCamera.transform.position - transform.position;
        if (lockXAxis) dir.y = 0f;
        if (dir.sqrMagnitude < 0.0001f) return;

        // Damped target: only perform a fraction of the full camera-facing
        // turn, and never swing more than maxSwingAngle away from the rest
        // orientation the panel has on the image target.
        Quaternion rest   = transform.parent != null
            ? transform.parent.rotation * _restLocalRotation
            : _restLocalRotation;
        Quaternion facing = Quaternion.LookRotation(-dir);
        Quaternion target = Quaternion.Slerp(rest, facing, followAmount);
        target = Quaternion.RotateTowards(rest, target, maxSwingAngle);

        // Dead zone with hysteresis: hold still for small camera movements,
        // and once a re-orient starts, ease all the way in before stopping.
        float angle = Quaternion.Angle(transform.rotation, target);

        if (!_reorienting && angle > deadZoneAngle)
            _reorienting = true;

        if (_reorienting)
        {
            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                target,
                Time.deltaTime * rotationSpeed
            );
            if (angle < SETTLE_ANGLE)
                _reorienting = false;
        }
    }

    static Camera GetARCamera()
    {
        // 1. Standard tag
        if (Camera.main != null) return Camera.main;

        // 2. Vuforia's default GameObject name
        GameObject go = GameObject.Find("ARCamera");
        if (go != null)
        {
            Camera c = go.GetComponent<Camera>();
            if (c != null) return c;
        }

        // 3. Any active camera
        Camera[] all = Camera.allCameras;
        if (all != null && all.Length > 0) return all[0];

        return null;
    }
}