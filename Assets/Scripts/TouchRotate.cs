using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Video;

public class TouchRotate : MonoBehaviour
{
    public float rotationSpeed = 0.5f; // Adjust this value to control rotation sensitivity

    [Tooltip("Video player checked to lock rotation during playback. Auto-found on this GameObject when left empty.")]
    public VideoPlayer videoPlayer;

    [Tooltip("Degrees per second used to return the plane to its initial rotation while the video plays.")]
    public float returnSpeed = 360f;

    private bool isRotating = false;
    private Vector2 previousTouchPosition;
    private Quaternion initialLocalRotation;

    void Awake()
    {
        initialLocalRotation = transform.localRotation;

        if (videoPlayer == null)
            videoPlayer = GetComponent<VideoPlayer>();
    }

    void Update()
    {
        // While the video is playing, rotation is locked: ignore input and
        // ease the plane back to the rotation it had at startup.
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            isRotating = false;
            transform.localRotation = Quaternion.RotateTowards(
                transform.localRotation, initialLocalRotation, returnSpeed * Time.deltaTime);
            return;
        }

        // Check for touch input (mobile) or mouse input (editor)
        if (Input.touchCount > 0)
        {
            HandleTouchInput();
        }
        else if (Input.GetMouseButton(0)) // For testing in the Unity Editor
        {
            HandleMouseInput();
        }
    }

    void HandleTouchInput()
    {
        Touch touch = Input.GetTouch(0); // Get the first touch

        switch (touch.phase)
        {
            case TouchPhase.Began:
                // Don't start rotating from taps on UI (buttons, seek bar, ...)
                if (IsPointerOverUI(touch.fingerId))
                    break;
                isRotating = true;
                previousTouchPosition = touch.position;
                break;

            case TouchPhase.Moved:
                if (isRotating)
                {
                    // Calculate the difference in touch position
                    Vector2 deltaPosition = touch.position - previousTouchPosition;

                    // Only rotate around the Y-axis based on horizontal finger movement
                    float rotationY = -deltaPosition.x * rotationSpeed; // Horizontal movement for Y-axis rotation

                    // Apply rotation to the object's transform around its local Y-axis (vertical axis)
                    transform.Rotate(Vector3.up, rotationY, Space.World);
                    // Note: Vector3.up is the world's up direction, ensuring rotation on the horizontal plane regardless of the model's current orientation.

                    previousTouchPosition = touch.position;
                }
                break;

            case TouchPhase.Ended:
            case TouchPhase.Canceled:
                isRotating = false;
                break;
        }
    }

    void HandleMouseInput()
    {
        // For testing in the Unity Editor with mouse drag
        if (Input.GetMouseButtonDown(0))
        {
            if (IsPointerOverUI())
                return;
            isRotating = true;
            previousTouchPosition = Input.mousePosition;
        }
        else if (Input.GetMouseButton(0) && isRotating)
        {
            Vector2 currentMousePosition = Input.mousePosition;
            Vector2 deltaPosition = currentMousePosition - previousTouchPosition;

            float rotationY = -deltaPosition.x * rotationSpeed;

            transform.Rotate(Vector3.up, rotationY, Space.World);

            previousTouchPosition = currentMousePosition;
        }
        else if (Input.GetMouseButtonUp(0))
        {
            isRotating = false;
        }
    }

    static bool IsPointerOverUI(int fingerId = -1)
    {
        if (EventSystem.current == null)
            return false;
        return fingerId >= 0
            ? EventSystem.current.IsPointerOverGameObject(fingerId)
            : EventSystem.current.IsPointerOverGameObject();
    }
}
