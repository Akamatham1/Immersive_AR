using UnityEngine;
using UnityEngine.InputSystem.Android;
using UnityEngine.UI;
using Vuforia; // Required for VuforiaBehaviour

public class VuforiaActivationController : MonoBehaviour
{
    public VuforiaBehaviour vuforiaBehaviour; // Assign the VuforiaBehaviour component here
  //  public Button activateARButton;           // Assign your button here
    public GameObject contentToHide; // Optional: Any AR content you want to hide initially
    public GameObject enable_Init_screen;
    public GameObject hide_UI;

    void Start()
    {
        // Ensure components are assigned
        if (vuforiaBehaviour == null)
        {
            // Try to find the VuforiaBehaviour if not assigned
            vuforiaBehaviour = FindObjectOfType<VuforiaBehaviour>();
        }

        // Initially disable Vuforia if it wasn't already disabled in the Inspector
        if (vuforiaBehaviour != null)
        {
            vuforiaBehaviour.enabled = false;
        }
        if (contentToHide != null)
        {
            contentToHide.SetActive(false);
        }

        // Add listener to the button
        // if (activateARButton != null)
        // {
        //     activateARButton.onClick.AddListener(ActivateVuforia);
        // }
    }

    public void ActivateVuforia()
    {
        if (vuforiaBehaviour != null && !vuforiaBehaviour.enabled)
        {
            vuforiaBehaviour.enabled = true;
            if (contentToHide != null)
            {
                contentToHide.SetActive(true);
            }
            Debug.Log("Vuforia AR Activated!");

           // Optionally hide the button after activation
            // if (activateARButton != null)
            // {
            //     activateARButton.gameObject.SetActive(false);
            // }
        }
        else
        {
            Debug.LogWarning("Vuforia Behaviour is already enabled or null.");
        }
    }

    // Optional: A method to disable Vuforia again (e.g., for a "turn off AR" button)
    public void DeactivateVuforia()
    {
        if (vuforiaBehaviour != null && vuforiaBehaviour.enabled)
        {
            vuforiaBehaviour.enabled = false;
            if (contentToHide != null)
            {
                contentToHide.SetActive(false);
            }
            Debug.Log("Vuforia AR Deactivated!");

            // Show the activation button again
            // if (activateARButton != null)
            // {
            //     activateARButton.gameObject.SetActive(true);
            // }

            if (!enable_Init_screen.activeSelf)
            {
                enable_Init_screen.SetActive(true);
                hide_UI.SetActive(false);
            }
        }
    }
}
