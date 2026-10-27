// ─────────────────────────────────────────────────────────────────────────────
// FlipBrochureHint — a screen-space hint card ("Flip the brochure to explore
// the AR campus map") shown while the video side of the brochure is tracked.
//
// SETUP
//  Easiest: Tools > Create Flip Brochure Hint (FlipBrochureHintBuilder) builds
//  the canvas and card, adds this component to HintCanvas and wires it. Save
//  the scene afterwards.
//
//  Manual:
//  1. Put this component on an always-active object (e.g. the hint canvas),
//     NOT on the card: the card is hidden/shown by this script.
//  2. Assign:
//       - videoObserver: ImageTarget_Video's Observer (target A, front)
//       - mapObserver:   ImageTarget_Map's Observer (target B, back)
//       - videoPlayer:   defaults to videoObserver.videoPlayer
//       - card:          CanvasGroup on the card (anchored bottom-centre)
//       - icon, label, closeButton: the card's children
//     Empty observers are looked up by name (ImageTarget_Video /
//     ImageTarget_Map), then by content (VideoPlayer / MapLocationMarker).
//
// BEHAVIOUR
//  - Shows once target A is tracked and the video has been playing for
//    showDelayAfterVideoStart seconds (re-armed each time A is found), and
//    again when the video ends if showOnVideoEnd.
//  - Hides when target B is found, target A is lost, the close button is
//    tapped, or after autoHideAfter seconds (0 = stays until dismissed).
//  - Once target B has been found, never shows again this session.
//  - Not shown during fullscreen video (videoObserver.SuppressTrackingReactions).
//  - Slides slideOffset px into place (negative = down from above, positive =
//    up from below) + fades in over 0.3 s, fades out over 0.2 s, icon pulses
//    while visible. Unscaled time, since PlayPauseToggle sets timeScale = 0.
//  - Card width = widthFraction of the parent, capped at maxWidth.
// ─────────────────────────────────────────────────────────────────────────────

using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class FlipBrochureHint : MonoBehaviour
{
    [Header("Targets")]
    [Tooltip("Target A (front of the brochure, video). Found by name/content when empty.")]
    public Observer    videoObserver;
    [Tooltip("Target B (back of the brochure, campus map). Found by name/content when empty.")]
    public Observer    mapObserver;
    [Tooltip("Defaults to the video observer's videoPlayer.")]
    public VideoPlayer videoPlayer;

    [Header("UI")]
    public CanvasGroup   card;
    public RectTransform icon;
    public TMP_Text      label;
    public Button        closeButton;

    [Header("Settings")]
    public string message = "Flip the brochure to explore the AR campus map";
    public float  showDelayAfterVideoStart = 4f;
    public bool   showOnVideoEnd = true;
    [Tooltip("Seconds before the hint hides itself. 0 = stays until dismissed.")]
    public float  autoHideAfter = 0f;

    [Header("Layout")]
    [Tooltip("Card width as a fraction of its parent's width.")]
    public float widthFraction = 0.9f;
    public float maxWidth      = 900f;

    [Header("Animation")]
    [Tooltip("Entrance slide distance in px. Negative slides down from above, positive slides up from below.")]
    public float slideOffset = -40f;

    const float FADE_IN      = 0.3f;
    const float FADE_OUT     = 0.2f;
    const float PULSE_PERIOD = 1.2f;
    const float PULSE_SCALE  = 1.12f;

    RectTransform _cardRt;
    float _restY;
    float _t;            // 0 = hidden, 1 = shown
    bool  _visible;
    bool  _videoTracked;
    bool  _mapSeen;      // target B found: never show again
    bool  _delayFired;   // the "playing for N seconds" trigger fired this tracking session
    float _playingFor;
    float _shownFor;
    float _pulse;

    bool Suppressed => videoObserver != null && videoObserver.SuppressTrackingReactions;

    void Start()
    {
        if (videoObserver == null || mapObserver == null)
        {
            FindObservers(out var v, out var m);
            if (videoObserver == null) videoObserver = v;
            if (mapObserver   == null) mapObserver   = m;
        }
        if (videoPlayer == null && videoObserver != null) videoPlayer = videoObserver.videoPlayer;

        if (videoObserver != null)
        {
            videoObserver.OnTargetFound.AddListener(OnVideoFound);
            videoObserver.OnTargetLost.AddListener(OnVideoLost);
        }
        else Debug.LogWarning("[FlipBrochureHint] No video (target A) Observer found.", this);

        if (mapObserver != null) mapObserver.OnTargetFound.AddListener(OnMapFound);
        else Debug.LogWarning("[FlipBrochureHint] No map (target B) Observer found.", this);

        if (videoPlayer != null) videoPlayer.loopPointReached += OnVideoEnded;
        if (closeButton != null) closeButton.onClick.AddListener(Hide);
        if (label != null) label.text = message;

        if (card == null)
        {
            Debug.LogWarning("[FlipBrochureHint] No card assigned.", this);
            enabled = false;
            return;
        }
        _cardRt = (RectTransform)card.transform;
        _restY  = _cardRt.anchoredPosition.y;
        Apply(0f);
    }

    void OnDestroy()
    {
        if (videoObserver != null)
        {
            videoObserver.OnTargetFound.RemoveListener(OnVideoFound);
            videoObserver.OnTargetLost.RemoveListener(OnVideoLost);
        }
        if (mapObserver != null) mapObserver.OnTargetFound.RemoveListener(OnMapFound);
        if (videoPlayer != null) videoPlayer.loopPointReached -= OnVideoEnded;
        if (closeButton != null) closeButton.onClick.RemoveListener(Hide);
    }

    // ── Events ────────────────────────────────────────────────────────────────

    void OnVideoFound()
    {
        _videoTracked = true;
        _delayFired   = false;
        _playingFor   = 0f;
    }

    void OnVideoLost()
    {
        // Fullscreen video keeps playing with the phone pointed away; stay quiet then.
        if (Suppressed) return;
        _videoTracked = false;
        Hide();
    }

    void OnMapFound()
    {
        _mapSeen = true;
        Hide();
    }

    void OnVideoEnded(VideoPlayer vp)
    {
        if (showOnVideoEnd && !vp.isLooping) Show();
    }

    /// <summary>Shows the hint unless target B was already found this session.</summary>
    public void Show()
    {
        if (_mapSeen || Suppressed || card == null) return;
        if (!card.gameObject.activeSelf) card.gameObject.SetActive(true);
        if (label != null && label.text != message) label.text = message;
        _visible  = true;
        _shownFor = 0f;
    }

    public void Hide() => _visible = false;

    // ── Per frame ─────────────────────────────────────────────────────────────

    void Update()
    {
        float dt = Time.unscaledDeltaTime;

        if (_videoTracked && !_delayFired && videoPlayer != null && videoPlayer.isPlaying)
        {
            _playingFor += dt;
            if (_playingFor >= showDelayAfterVideoStart)
            {
                _delayFired = true;
                Show();
            }
        }

        if (_visible && Suppressed) _visible = false; // went fullscreen

        if (_visible && autoHideAfter > 0f)
        {
            _shownFor += dt;
            if (_shownFor >= autoHideAfter) _visible = false;
        }

        float target = _visible ? 1f : 0f;
        if (_t != target)
            Apply(Mathf.MoveTowards(_t, target, dt / (_visible ? FADE_IN : FADE_OUT)));

        if (icon != null)
        {
            if (_t > 0f)
            {
                _pulse = (_pulse + dt / PULSE_PERIOD) % 1f;
                float s = 1f + (PULSE_SCALE - 1f) * (0.5f - 0.5f * Mathf.Cos(_pulse * 2f * Mathf.PI));
                icon.localScale = Vector3.one * s;
            }
            else
            {
                _pulse = 0f;
                icon.localScale = Vector3.one;
            }
        }
    }

    void LateUpdate()
    {
        if (_cardRt == null || !(_cardRt.parent is RectTransform parent)) return;
        float w = Mathf.Min(parent.rect.width * widthFraction, maxWidth);
        if (!Mathf.Approximately(_cardRt.sizeDelta.x, w))
            _cardRt.sizeDelta = new Vector2(w, _cardRt.sizeDelta.y);
    }

    void Apply(float t)
    {
        _t = t;
        // Fade-in eases out with a slide; fade-out is a plain fade from the rest position.
        float e = _visible ? 1f - Mathf.Pow(1f - t, 3f) : 1f;
        card.alpha = t;
        card.interactable = card.blocksRaycasts = _visible && t > 0.5f;
        _cardRt.anchoredPosition = new Vector2(_cardRt.anchoredPosition.x, _restY - slideOffset * (1f - e));
    }

    // ── Lookup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds target A (video) and B (map) Observers: by GameObject name
    /// (ImageTarget_Video / ImageTarget_Map), then by content (a MapLocationMarker
    /// under it = map, a videoPlayer = video).
    /// </summary>
    public static void FindObservers(out Observer video, out Observer map)
    {
        video = map = null;
        var all = FindObjectsByType<Observer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var o in all)
        {
            if (o.name == "ImageTarget_Video") video = o;
            else if (o.name == "ImageTarget_Map") map = o;
        }
        if (map == null)
            foreach (var o in all)
                if (o != video && o.GetComponentInChildren<MapLocationMarker>(true) != null) { map = o; break; }
        if (video == null)
            foreach (var o in all)
                if (o != map && o.videoPlayer != null) { video = o; break; }
    }
}
