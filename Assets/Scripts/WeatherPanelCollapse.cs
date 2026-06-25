using System.Collections;
using UnityEngine;
using TMPro;

public class WeatherPanelCollapse : MonoBehaviour
{
    [Header("Collapsible rows (hidden when collapsed)")]
    public GameObject[] collapseTargets;

    [Header("Panel sizing")]
    public RectTransform panelRect;
    public float expandedHeight = 600f;
    public float collapsedHeight = 60f;
    public float animDuration = 0.25f;

    [Header("Chevron (its Z rotation flips 180 on collapse)")]
    public RectTransform chevronTransform;

    bool isCollapsed = false;
    Coroutine current;

    public void Toggle()
    {
        isCollapsed = !isCollapsed;
        if (current != null) StopCoroutine(current);
        current = StartCoroutine(Animate(isCollapsed));
    }

    IEnumerator Animate(bool collapse)
    {
        float fromH = panelRect.sizeDelta.y;
        float toH   = collapse ? collapsedHeight : expandedHeight;
        float fromZ = chevronTransform ? chevronTransform.localEulerAngles.z : 0f;
        float toZ   = collapse ? 180f : 0f;

        if (!collapse)
            foreach (var go in collapseTargets)
                if (go) go.SetActive(true);

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / animDuration)
        {
            float e = EaseInOutCubic(t);
            panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, Mathf.Lerp(fromH, toH, e));
            if (chevronTransform)
                chevronTransform.localEulerAngles = new Vector3(0f, 0f, Mathf.LerpAngle(fromZ, toZ, e));
            yield return null;
        }

        panelRect.sizeDelta = new Vector2(panelRect.sizeDelta.x, toH);
        if (chevronTransform)
            chevronTransform.localEulerAngles = new Vector3(0f, 0f, toZ);

        if (collapse)
            foreach (var go in collapseTargets)
                if (go) go.SetActive(false);
    }

    static float EaseInOutCubic(float t) =>
        t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
}
