using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
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

    [Header("Image targets")]
    [Tooltip("Observers to watch. Empty = every Observer in the scene. The viewfinder hides " +
             "when any target is found and returns only when all are lost.")]
    public Observer[] observers;

    [Header("Buttons to hide while scanning (optional)")]
    public GameObject buttonsContainer;

    Coroutine dotCoroutine;
    Coroutine cornerCoroutine;
    bool found = false;

    readonly HashSet<Observer> foundTargets = new HashSet<Observer>();
    readonly List<(Observer observer, UnityAction onFound, UnityAction onLost)> subscriptions =
        new List<(Observer, UnityAction, UnityAction)>();

    // Subscribe once for the object's lifetime so events still fire when this is inactive.
    void Start()
    {
        if (observers == null || observers.Length == 0)
            observers = FindObjectsByType<Observer>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        foreach (var observer in observers)
        {
            if (observer == null) continue;
            var o = observer;
            UnityAction onFound = () => OnTargetFound(o);
            UnityAction onLost  = () => OnTargetLost(o);
            o.OnTargetFound.AddListener(onFound);
            o.OnTargetLost.AddListener(onLost);
            subscriptions.Add((o, onFound, onLost));
        }
    }

    void OnDestroy()
    {
        foreach (var (observer, onFound, onLost) in subscriptions)
        {
            if (observer == null) continue;
            observer.OnTargetFound.RemoveListener(onFound);
            observer.OnTargetLost.RemoveListener(onLost);
        }
        subscriptions.Clear();
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

    void OnTargetFound(Observer observer)
    {
        bool wasNoneFound = foundTargets.Count == 0;
        foundTargets.Add(observer);
        found = true;
        // Already hidden (another target is tracked): nothing to do, and an
        // inactive object can't start coroutines.
        if (!wasNoneFound || !isActiveAndEnabled) return;

        if (statusLabel) statusLabel.text = foundText;
        if (dotCoroutine != null) { StopCoroutine(dotCoroutine); dotCoroutine = null; }
        if (buttonsContainer != null) buttonsContainer.SetActive(true);
        StartCoroutine(HideAfterDelay(0.01f));
    }

    void OnTargetLost(Observer observer)
    {
        foundTargets.Remove(observer);
        if (foundTargets.Count > 0) return; // another target is still tracked

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
