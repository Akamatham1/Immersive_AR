using System.Collections;
using UnityEngine;

// Attach to WeatherPanel (NOT CanvasWeather) — animates UI localScale (1,1,1), not world scale.
public class WeatherPanelEntrance : MonoBehaviour
{
    public float enterDuration = 0.45f;
    public float exitDuration  = 0.2f;

    Coroutine current;

    void Start()
    {
        var observer = GetComponentInParent<Observer>();
        if (observer != null)
        {
            observer.OnTargetFound.AddListener(PlayEntrance);
            observer.OnTargetLost.AddListener(PlayExit);
            transform.localScale = Vector3.zero;
        }
        // no observer = editor preview, stays visible at scale 1
    }

    public void PlayEntrance()
    {
        if (current != null) StopCoroutine(current);
        current = StartCoroutine(ScaleTo(Vector3.one, enterDuration, overshoot: true));
    }

    public void PlayExit()
    {
        if (current != null) StopCoroutine(current);
        current = StartCoroutine(ScaleTo(Vector3.zero, exitDuration, overshoot: false));
    }

    IEnumerator ScaleTo(Vector3 target, float duration, bool overshoot)
    {
        Vector3 start = transform.localScale;
        for (float t = 0f; t < 1f; t += Time.deltaTime / duration)
        {
            float ease = overshoot ? EaseOutBack(t) : EaseInQuad(t);
            transform.localScale = Vector3.LerpUnclamped(start, target, ease);
            yield return null;
        }
        transform.localScale = target;
    }

    static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
    }

    static float EaseInQuad(float t) => t * t;
}
