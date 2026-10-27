// ─────────────────────────────────────────────────────────────────────────────
// DirectionsUIVisibility — shows the directions UI (dropdown, Start button,
// distance label, CompassArrow) for the map side of the brochure and hides it
// for the video side, while keeping it up during navigation.
//
// SETUP
//  1. Add this component to an always-active object (e.g. the one holding
//     DirectionsController, or CanvasButtons > SafeArea). NOT on DirectionsUI
//     itself.
//  2. Assign:
//       - mapObserver:   ImageTarget_Map's Observer (target B)
//       - videoObserver: ImageTarget_Video's Observer (target A)
//       - directionsUI:  the "DirectionsUI" GameObject
//       - directions:    DirectionsController (found in the scene when empty)
//     Empty observers are looked up like FlipBrochureHint does (by name, then
//     by content).
//  3. A CanvasGroup is added to DirectionsUI if missing. DirectionsUI starts
//     hidden (alpha 0, not interactable) and is activated if it was off.
//  4. With a Start button: untick DirectionsController.startOnSelect and wire
//     the button's OnClick to DirectionsController.StartNavigation.
//
// RULES
//  - Target B found → show.
//  - Target B lost → hide, unless navigating (DirectionsController.IsNavigating).
//  - Target A found → always hide (navigation is kept, not cancelled).
//  - Target A lost → show again if navigating, otherwise stay hidden.
//  Evaluated every frame as: show = !A tracked && (B tracked || IsNavigating),
//  so it also hides once navigation ends (arrival / End / None) off the map.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

public class DirectionsUIVisibility : MonoBehaviour
{
    public Observer             mapObserver;
    public Observer             videoObserver;
    public GameObject           directionsUI;
    public DirectionsController directions;
    public float                fadeDuration = 0.25f;

    CanvasGroup _group;
    bool _mapTracked, _videoTracked;

    void Start()
    {
        if (mapObserver == null || videoObserver == null)
        {
            FlipBrochureHint.FindObservers(out var video, out var map);
            if (mapObserver   == null) mapObserver   = map;
            if (videoObserver == null) videoObserver = video;
        }
        if (directions == null)
            directions = FindFirstObjectByType<DirectionsController>(FindObjectsInactive.Include);

        if (mapObserver != null)
        {
            mapObserver.OnTargetFound.AddListener(OnMapFound);
            mapObserver.OnTargetLost.AddListener(OnMapLost);
        }
        else Debug.LogWarning("[DirectionsUIVisibility] No map (target B) Observer found.", this);

        if (videoObserver != null)
        {
            videoObserver.OnTargetFound.AddListener(OnVideoFound);
            videoObserver.OnTargetLost.AddListener(OnVideoLost);
        }
        else Debug.LogWarning("[DirectionsUIVisibility] No video (target A) Observer found.", this);

        if (directionsUI == null)
        {
            Debug.LogWarning("[DirectionsUIVisibility] No directionsUI assigned.", this);
            enabled = false;
            return;
        }
        if (directionsUI == gameObject || transform.IsChildOf(directionsUI.transform))
            Debug.LogWarning("[DirectionsUIVisibility] Put this on an object outside DirectionsUI.", this);

        _group = directionsUI.GetComponent<CanvasGroup>();
        if (_group == null) _group = directionsUI.AddComponent<CanvasGroup>();
        Apply(0f);
        if (!directionsUI.activeSelf) directionsUI.SetActive(true);
    }

    void OnDestroy()
    {
        if (mapObserver != null)
        {
            mapObserver.OnTargetFound.RemoveListener(OnMapFound);
            mapObserver.OnTargetLost.RemoveListener(OnMapLost);
        }
        if (videoObserver != null)
        {
            videoObserver.OnTargetFound.RemoveListener(OnVideoFound);
            videoObserver.OnTargetLost.RemoveListener(OnVideoLost);
        }
    }

    void OnMapFound()   => _mapTracked   = true;
    void OnMapLost()    => _mapTracked   = false;
    void OnVideoFound() => _videoTracked = true;
    void OnVideoLost()  => _videoTracked = false;

    bool IsNavigating => directions != null && directions.IsNavigating;

    void Update()
    {
        bool show = !_videoTracked && (_mapTracked || IsNavigating);
        float target = show ? 1f : 0f;
        if (Mathf.Approximately(_group.alpha, target)) return;

        float step = fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f;
        Apply(Mathf.MoveTowards(_group.alpha, target, step));
    }

    void Apply(float alpha)
    {
        _group.alpha = alpha;
        _group.interactable = _group.blocksRaycasts = alpha > 0.5f;
    }
}
