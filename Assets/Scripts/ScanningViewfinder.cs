using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class ScanningViewfinder : MonoBehaviour
{
    [Header("Corner brackets (4 RectTransforms)")]
    public RectTransform cornerTL;
    public RectTransform cornerTR;
    public RectTransform cornerBL;
    public RectTransform cornerBR;
    public float cornerPulseScale = 1.08f;
    public float cornerPulseDuration = 0.9f;

    [Header("Status label")]
    public TextMeshProUGUI statusLabel;
    public string scanningText = "Scanning for target";
    public string foundText    = "Target found!";

    Coroutine dotCoroutine;
    Coroutine cornerCoroutine;
    bool found = false;

    void OnEnable()
    {
        found = false;
        if (statusLabel) statusLabel.text = scanningText + "...";
        if (dotCoroutine  != null) StopCoroutine(dotCoroutine);
        if (cornerCoroutine != null) StopCoroutine(cornerCoroutine);
        dotCoroutine    = StartCoroutine(PulseDots());
        cornerCoroutine = StartCoroutine(PulseCorners());

        var observer = FindFirstObjectByType<Observer>();
        if (observer != null)
        {
            observer.OnTargetFound.AddListener(OnTargetFound);
            observer.OnTargetLost.AddListener(OnTargetLost);
        }
    }

    void OnDisable()
    {
        var observer = FindFirstObjectByType<Observer>();
        if (observer != null)
        {
            observer.OnTargetFound.RemoveListener(OnTargetFound);
            observer.OnTargetLost.RemoveListener(OnTargetLost);
        }
    }

    void OnTargetFound()
    {
        found = true;
        if (statusLabel) statusLabel.text = foundText;
        if (dotCoroutine != null) { StopCoroutine(dotCoroutine); dotCoroutine = null; }
        StartCoroutine(HideAfterDelay(1f));
    }

    void OnTargetLost()
    {
        found = false;
        gameObject.SetActive(true);
        if (statusLabel) statusLabel.text = scanningText + "...";
        if (dotCoroutine == null) dotCoroutine = StartCoroutine(PulseDots());
        if (cornerCoroutine == null) cornerCoroutine = StartCoroutine(PulseCorners());
    }

    IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (found) gameObject.SetActive(false);
    }

    // Animates "Scanning..." -> "Scanning." -> "Scanning.." -> "Scanning..."
    IEnumerator PulseDots()
    {
        int dots = 0;
        while (true)
        {
            if (statusLabel && !found)
                statusLabel.text = scanningText + new string('.', dots + 1);
            dots = (dots + 1) % 3;
            yield return new WaitForSeconds(0.5f);
        }
    }

    // Gentle scale pulse on all four corners in sync
    IEnumerator PulseCorners()
    {
        RectTransform[] corners = { cornerTL, cornerTR, cornerBL, cornerBR };
        while (true)
        {
            // Scale up
            for (float t = 0f; t < 1f; t += Time.deltaTime / (cornerPulseDuration * 0.5f))
            {
                float s = Mathf.Lerp(1f, cornerPulseScale, EaseInOutSine(t));
                foreach (var c in corners)
                    if (c) c.localScale = Vector3.one * s;
                yield return null;
            }
            // Scale back
            for (float t = 0f; t < 1f; t += Time.deltaTime / (cornerPulseDuration * 0.5f))
            {
                float s = Mathf.Lerp(cornerPulseScale, 1f, EaseInOutSine(t));
                foreach (var c in corners)
                    if (c) c.localScale = Vector3.one * s;
                yield return null;
            }
        }
    }

    static float EaseInOutSine(float t) =>
        -(Mathf.Cos(Mathf.PI * t) - 1f) / 2f;
}
