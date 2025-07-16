using UnityEngine;
using Vuforia; // Make sure to include Vuforia namespace

public class TouchRotate : MonoBehaviour
{
    public float rotationSpeed = 0.5f; // Adjust this value to control rotation sensitivity
    private bool isRotating = false;
    private Vector2 previousTouchPosition;

    void Update()
    {
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
}
