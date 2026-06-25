using UnityEngine;
using Vuforia;

public class VuforiaActivationController : MonoBehaviour
{
    public VuforiaBehaviour vuforiaBehaviour;
    public GameObject contentToHide;
    public GameObject hide_targetAR;
    public GameObject enable_Init_screen;
    public GameObject scanningViewfinder;

    void Start()
    {
        if (vuforiaBehaviour == null)
            vuforiaBehaviour = FindFirstObjectByType<VuforiaBehaviour>();

        if (vuforiaBehaviour != null)
            vuforiaBehaviour.enabled = false;

        if (contentToHide != null)
            contentToHide.SetActive(false);
    }

    public void ActivateVuforia()
    {
        if (vuforiaBehaviour != null && !vuforiaBehaviour.enabled)
        {
            vuforiaBehaviour.enabled = true;
            hide_targetAR.SetActive(true);
            if (scanningViewfinder != null) scanningViewfinder.SetActive(true);
            Debug.Log("Vuforia AR Activated!");
        }
        else
        {
            Debug.LogWarning("Vuforia Behaviour is already enabled or null.");
        }
    }

    public void DeactivateVuforia()
    {
        if (vuforiaBehaviour != null && vuforiaBehaviour.enabled)
        {
            vuforiaBehaviour.enabled = false;
            if (contentToHide != null)
            {
                contentToHide.SetActive(false);
                hide_targetAR.SetActive(false);
            }
            Debug.Log("Vuforia AR Deactivated!");

            if (enable_Init_screen != null && !enable_Init_screen.activeSelf)
                enable_Init_screen.SetActive(true);
            if (scanningViewfinder != null) scanningViewfinder.SetActive(false);
        }
    }
}
