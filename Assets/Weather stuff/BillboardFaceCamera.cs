using UnityEngine;

// Attach this script to your WeatherPanel GameObject.
// It will always rotate to face the AR camera smoothly.
public class BillboardFaceCamera : MonoBehaviour
{
    [Header("Settings")]
    [Tooltip("How smoothly the panel rotates to face the camera (higher = snappier)")]
    public float rotationSpeed = 10f;

    [Tooltip("Lock X axis so panel doesn't tilt up/down — recommended for AR")]
    public bool lockXAxis = true;

    private Camera _arCamera;

    void Start()
    {
        // Find the main camera (works for both AR Foundation and Vuforia)
        _arCamera = Camera.main;

        if (_arCamera == null)
            Debug.LogWarning("BillboardFaceCamera: No main camera found!");
    }

    void LateUpdate()
    {
        if (_arCamera == null) return;

        // Get direction from panel to camera
        Vector3 direction = _arCamera.transform.position - transform.position;

        // Lock vertical tilt if enabled
        if (lockXAxis)
            direction.y = 0;

        // Don't rotate if camera is at the same position
        if (direction == Vector3.zero) return;

        // Smoothly rotate to face the camera
        Quaternion targetRotation = Quaternion.LookRotation(-direction);
        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            Time.deltaTime * rotationSpeed
        );
    }
}
