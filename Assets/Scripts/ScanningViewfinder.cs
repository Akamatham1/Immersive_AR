using System.Collections;
using UnityEngine;
using TMPro;

public class ScanningViewfinder : MonoBehaviour
{
    [Header("Corner brackets (4 RectTransforms)")]
    public RectTransform cornerTL;
    public RectTransform cornerTR;
    public RectTransform cornerBL;
    public RectTransform cornerBR;
    public float cornerPulseScale = 2.5f;
    public float cornerPulseDuration = 2f;

    [Header("Status label")]
    public TextMeshProUGUI statusLabel;
    public string scanningText = "Scan the front of the pamphlet";
    public string foundText    = "Target found!";

    [Header("Buttons to hide while scanning")]
    public GameObject buttonsContainer;

    Coroutine dotCoroutine;
    Coroutine cornerCoroutine;
    bool found = false;

    // Subscribe once for the object's lifetime so events still fire when this is inactive.
    void Start()
    {
        var observer = FindFirstObjectByType<Observer>();
        if (observer != null)
        {
            observer.OnTargetFound.AddListener(OnTargetFound);
            observer.OnTargetLost.AddListener(OnTargetLost);
        }
    }

    void OnDestroy()
    {
        var observer = FindFirstObjectByType<Observer>();
        if (observer != null)
        {
            observer.OnTargetFound.RemoveListener(OnTargetFound);
            observer.OnTargetLost.RemoveListener(OnTargetLost);
        }
    }

    void OnEnable()
    {
        found = false;
        if (buttonsContainer != null) buttonsContainer.SetActive(false);
        if (statusLabel) statusLabel.text = scanningText + "...";
        if (dotCoroutine    != null) StopCoroutine(dotCoroutine);
        if (cornerCoroutine != null) StopCoroutine(cornerCoroutine);
        dotCoroutine    = StartCoroutine(PulseDots());
        cornerCoroutine = StartCoroutine(PulseCorners());
    }

    void OnDisable()
    {
        if (dotCoroutine    != null) { StopCoroutine(dotCoroutine);    dotCoroutine    = null; }
        if (cornerCoroutine != null) { StopCoroutine(cornerCoroutine); cornerCoroutine = null; }
    }

    void OnTargetFound()
    {
        found = true;
        if (statusLabel) statusLabel.text = foundText;
        if (dotCoroutine != null) { StopCoroutine(dotCoroutine); dotCoroutine = null; }
        if (buttonsContainer != null) buttonsContainer.SetActive(true);
        StartCoroutine(HideAfterDelay(0.01f));
    }

    void OnTargetLost()
    {
        found = false;
        gameObject.SetActive(true); // triggers OnEnable: hides buttons, resets text, restarts coroutines
    }

    IEnumerator HideAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (found) gameObject.SetActive(false);
    }

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

    IEnumerator PulseCorners()
    {
        RectTransform[] corners = { cornerTL, cornerTR, cornerBL, cornerBR };
        while (true)
        {
            for (float t = 0f; t < 1f; t += Time.deltaTime / (cornerPulseDuration * 0.5f))
            {
                float s = Mathf.Lerp(1f, cornerPulseScale, EaseInOutSine(t));
                foreach (var c in corners)
                    if (c) c.localScale = Vector3.one * s;
                yield return null;
            }
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
