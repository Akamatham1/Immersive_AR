using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class SafeAreaFitter : MonoBehaviour
{
    RectTransform rt;
    Rect lastSafeArea = Rect.zero;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        Apply();
    }

    void Update()
    {
        if (Screen.safeArea != lastSafeArea)
            Apply();
    }

    void Apply()
    {
        lastSafeArea = Screen.safeArea;
        var s = new Vector2(Screen.width, Screen.height);
        rt.anchorMin = lastSafeArea.position / s;
        rt.anchorMax = (lastSafeArea.position + lastSafeArea.size) / s;
    }
}
