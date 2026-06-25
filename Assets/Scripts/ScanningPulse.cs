using UnityEngine;

// Attach to Image_Icon on InitialScreen for a pulsing "scanning" effect.
public class ScanningPulse : MonoBehaviour
{
    public float minScale = 0.92f;
    public float maxScale = 1.08f;
    public float speed = 1.8f;

    private RectTransform rt;

    void Start() => rt = GetComponent<RectTransform>();

    void Update()
    {
        float t = (Mathf.Sin(Time.unscaledTime * speed) + 1f) * 0.5f;
        float scale = Mathf.Lerp(minScale, maxScale, t);
        rt.localScale = Vector3.one * scale;
    }
}
