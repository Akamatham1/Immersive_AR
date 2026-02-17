using UnityEngine;

// Attach to your WeatherPanel GameObject.
// No coroutines, no external dependencies, no exceptions.
// Retries finding the camera every frame until found.
public class BillboardFaceCamera : MonoBehaviour
{
    [Header("Settings")]
    public float rotationSpeed = 10f;
    public bool lockXAxis = true;

    private Camera _arCamera;
    private float _retryTimer;
    private const float RETRY_INTERVAL = 0.5f;

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

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            Quaternion.LookRotation(-dir),
            Time.deltaTime * rotationSpeed
        );
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