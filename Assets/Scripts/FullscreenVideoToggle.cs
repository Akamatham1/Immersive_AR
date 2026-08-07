using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.Video;
using TMPro;

/// <summary>
/// Shows a fullscreen button while the image target is tracked; pressing it
/// rotates the device to landscape and expands a RawImage video display to a
/// letterboxed full-screen view with playback controls (play/pause, seek bar,
/// time readout) and an Exit button that restores the AR screen.
///
/// While fullscreen is active, tracking loss no longer pauses the video or
/// exits the view (the phone is expected to point away from the target); the
/// scene's tracked/lost UI state is re-applied when the user exits.
///
/// Automatically subscribes to <see cref="Observer.OnTargetFound"/> /
/// <see cref="Observer.OnTargetLost"/> so the overlay is only visible while
/// the image target is being tracked.
/// </summary>
public class FullscreenVideoToggle : MonoBehaviour
{
    [Header("References")]
    public RawImage videoDisplay;
    public RectTransform videoRect;
    public Button fullscreenButton;
    public GameObject[] uiPanelsToHide;

    [Header("Overlay root (VideoDisplay parent)")]
    public GameObject videoOverlay;

    [Tooltip("Icon for the exit-fullscreen button shown top-right in fullscreen mode.")]
    public Sprite minimizeSprite;

    [Header("Normal-Mode Layout")]
    public Vector2 normalSize    = new Vector2(400f, 225f);
    public Vector2 normalPadding = new Vector2(20f, 20f);

    const float  ANIM_DURATION    = 0.3f;
    const float  ROTATION_TIMEOUT = 1f;

    bool            _isFullscreen;
    bool            _targetVisible;
    RectTransform   _canvasRect;
    Coroutine       _tween;
    Coroutine       _transition;
    bool[]          _panelStates;

    Observer    _observer;
    VideoPlayer _videoPlayer;

    // Runtime-built fullscreen controls
    GameObject      _controlsRoot;
    TextMeshProUGUI _playPauseLabel;
    TextMeshProUGUI _timeLabel;
    Slider          _progress;
    bool            _scrubbing;
    int             _lastShownSecond = -1;

    void Start()
    {
        if (videoRect == null && videoDisplay != null)
            videoRect = videoDisplay.GetComponent<RectTransform>();

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null)
            _canvasRect = canvas.GetComponent<RectTransform>();

        if (videoRect != null)
        {
            videoRect.anchorMin        = new Vector2(0.5f, 0.5f);
            videoRect.anchorMax        = new Vector2(0.5f, 0.5f);
            videoRect.pivot            = new Vector2(0.5f, 0.5f);
            videoRect.sizeDelta        = normalSize;
            videoRect.anchoredPosition = ComputeNormalPos();
        }

        if (fullscreenButton != null)
            fullscreenButton.onClick.AddListener(Toggle);

        // Subscribe to Vuforia tracking events
        _observer = FindFirstObjectByType<Observer>();
        if (_observer != null)
        {
            _observer.OnTargetFound.AddListener(OnTrackingFound);
            _observer.OnTargetLost.AddListener(OnTrackingLost);
            _videoPlayer = _observer.videoPlayer;
        }
        if (_videoPlayer == null)
            _videoPlayer = FindFirstObjectByType<VideoPlayer>();

        BuildControls();

        // Hidden until the image target is found
        SetOverlayVisible(false);
    }

    void Update()
    {
        if (_isFullscreen)
            UpdateControls();
    }

    // ── Tracking callbacks ────────────────────────────────────────────────────

    void OnTrackingFound()
    {
        _targetVisible = true;
        if (!_isFullscreen)
            SetOverlayVisible(true);
    }

    void OnTrackingLost()
    {
        _targetVisible = false;
        // Fullscreen playback survives tracking loss — the phone is expected
        // to point away from the target while watching in landscape.
        if (!_isFullscreen)
            SetOverlayVisible(false);
    }

    [ContextMenu("Simulate Tracking Found")]
    public void SimulateTrackingFound() => OnTrackingFound();

    [ContextMenu("Simulate Tracking Lost")]
    public void SimulateTrackingLost() => OnTrackingLost();

    void SetOverlayVisible(bool visible)
    {
        if (videoOverlay != null)
            videoOverlay.SetActive(visible);

        // The button lives in the shared bottom button bar, not under the
        // overlay root, so it is toggled separately.
        if (fullscreenButton != null)
            fullscreenButton.gameObject.SetActive(visible);

        // The video window itself only shows while fullscreen; in normal mode
        // the video is visible on the AR quad instead.
        if (videoDisplay != null)
            videoDisplay.gameObject.SetActive(visible && _isFullscreen);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Whether the video is currently displayed in fullscreen mode.</summary>
    public bool isFullscreen => _isFullscreen;

    /// <summary>Toggles between normal and fullscreen modes.</summary>
    public void Toggle()
    {
        if (_isFullscreen) ExitFullscreen();
        else               EnterFullscreen();
    }

    /// <summary>
    /// Rotates to landscape, expands the video display to a letterboxed
    /// full-screen view and shows the playback controls. Panels listed in
    /// <see cref="uiPanelsToHide"/> are hidden for the duration.
    /// </summary>
    public void EnterFullscreen()
    {
        if (_isFullscreen || videoRect == null) return;
        _isFullscreen = true;

        // Remember each panel's state so exiting doesn't re-show panels that
        // were already hidden by other systems (e.g. Observer's hideUI).
        _panelStates = new bool[uiPanelsToHide.Length];
        for (int i = 0; i < uiPanelsToHide.Length; i++)
        {
            if (uiPanelsToHide[i] == null) continue;
            _panelStates[i] = uiPanelsToHide[i].activeSelf;
            uiPanelsToHide[i].SetActive(false);
        }

        if (fullscreenButton != null)
            fullscreenButton.gameObject.SetActive(false);

        if (_observer != null)
            _observer.SuppressTrackingReactions = true;

        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(EnterRoutine());
    }

    /// <summary>
    /// Shrinks the video display, restores portrait/auto rotation, the hidden
    /// UI panels and the scene's current tracked/lost state.
    /// </summary>
    public void ExitFullscreen()
    {
        if (!_isFullscreen) return;
        _isFullscreen = false;

        if (_controlsRoot != null)
            _controlsRoot.SetActive(false);

        if (_observer != null)
            _observer.SuppressTrackingReactions = false;

        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(ExitRoutine());
    }

    // ── Enter/exit transitions ────────────────────────────────────────────────

    IEnumerator EnterRoutine()
    {
        SetLandscape(true);
        yield return WaitForOrientation(landscape: true);

        if (videoDisplay != null)
            videoDisplay.gameObject.SetActive(true);

        // Grow from a small centered rect up to the letterboxed fullscreen size.
        videoRect.sizeDelta        = normalSize;
        videoRect.anchoredPosition = Vector2.zero;

        if (_controlsRoot != null)
            _controlsRoot.SetActive(true);

        Animate(FullscreenSize(), Vector2.zero);
        _transition = null;
    }

    IEnumerator ExitRoutine()
    {
        SetLandscape(false);
        yield return WaitForOrientation(landscape: false);

        RestorePanels();

        if (fullscreenButton != null)
            fullscreenButton.gameObject.SetActive(true);

        if (!_targetVisible && _observer != null)
        {
            // Target was lost while fullscreen: hand the scene back in its
            // "scanning" state — pause until the target is found again and
            // re-hide the tracked-state UI.
            _observer.PauseVideoUntilTargetFound();
            if (_observer.hideUI != null)
                _observer.hideUI.SetActive(false);
        }

        if (videoRect != null)
        {
            Animate(normalSize, ComputeNormalPos(), () =>
            {
                if (_isFullscreen) return;
                if (videoDisplay != null) videoDisplay.gameObject.SetActive(false);
                SetOverlayVisible(_targetVisible);
            });
        }
        else
        {
            SetOverlayVisible(_targetVisible);
        }
        _transition = null;
    }

    void SetLandscape(bool landscape)
    {
        // Force the target orientation directly — relying on autorotation
        // alone does nothing when the device's rotation lock is enabled.
        Screen.orientation = landscape
            ? ScreenOrientation.LandscapeLeft
            : ScreenOrientation.Portrait;
    }

    IEnumerator WaitForOrientation(bool landscape)
    {
        // Device rotation is asynchronous; wait for the canvas to take on the
        // new proportions (with a timeout — in the Editor nothing rotates).
        float deadline = Time.unscaledTime + ROTATION_TIMEOUT;
        while (Time.unscaledTime < deadline)
        {
            Vector2 size = CanvasSize();
            if (landscape == (size.x > size.y)) break;
            yield return null;
        }
        yield return null; // let the canvas rect settle

        // Hand control back to autorotation, constrained to the orientations
        // that fit the current mode (the forced switch above still holds if
        // the OS rotation lock keeps autorotation from acting).
        Screen.autorotateToPortrait           = !landscape;
        Screen.autorotateToPortraitUpsideDown = !landscape;
        Screen.autorotateToLandscapeLeft      = landscape;
        Screen.autorotateToLandscapeRight     = landscape;
        Screen.orientation = ScreenOrientation.AutoRotation;
    }

    // ── Fullscreen controls ───────────────────────────────────────────────────

    void BuildControls()
    {
        Transform parent = videoOverlay != null ? videoOverlay.transform : transform;
        RectTransform root = MakeRect("FullscreenControls", parent);
        Stretch(root);
        _controlsRoot = root.gameObject;

        // Exit-fullscreen button, top-right: minimize icon (text fallback
        // when no sprite is assigned)
        Button quit;
        if (minimizeSprite != null)
        {
            RectTransform iconRect = MakeRect("MinimizeButton", root);
            iconRect.sizeDelta = new Vector2(100f, 100f);
            Image icon = iconRect.gameObject.AddComponent<Image>();
            icon.sprite         = minimizeSprite;
            icon.color          = Color.white;
            icon.preserveAspect = true;
            quit = iconRect.gameObject.AddComponent<Button>();
            quit.targetGraphic = icon;
        }
        else
        {
            quit = MakeButton(root, "MinimizeButton", "✕ Exit", new Vector2(220f, 90f), 40f);
        }
        var quitRect = (RectTransform)quit.transform;
        quitRect.anchorMin = quitRect.anchorMax = quitRect.pivot = Vector2.one;
        quitRect.anchoredPosition = new Vector2(-30f, -30f);
        quit.onClick.AddListener(ExitFullscreen);

        // Bottom control bar
        RectTransform bar = MakeRect("ControlBar", root);
        bar.anchorMin        = new Vector2(0f, 0f);
        bar.anchorMax        = new Vector2(1f, 0f);
        bar.pivot            = new Vector2(0.5f, 0f);
        bar.sizeDelta        = new Vector2(-60f, 100f);
        bar.anchoredPosition = new Vector2(0f, 20f);
        Image barBg = bar.gameObject.AddComponent<Image>();
        barBg.color = new Color(0f, 0f, 0f, 0.55f);

        // Play/pause button, bar left
        Button playPause = MakeButton(bar, "PlayPauseButton", "Pause", new Vector2(150f, 70f), 36f);
        var playRect = (RectTransform)playPause.transform;
        playRect.anchorMin = playRect.anchorMax = new Vector2(0f, 0.5f);
        playRect.pivot     = new Vector2(0f, 0.5f);
        playRect.anchoredPosition = new Vector2(20f, 0f);
        _playPauseLabel = playPause.GetComponentInChildren<TextMeshProUGUI>();
        playPause.onClick.AddListener(TogglePlayPause);

        // Time readout, bar right
        _timeLabel = MakeLabel(bar, "TimeLabel", "0:00 / 0:00", 36f);
        RectTransform timeRect = _timeLabel.rectTransform;
        timeRect.anchorMin = timeRect.anchorMax = new Vector2(1f, 0.5f);
        timeRect.pivot     = new Vector2(1f, 0.5f);
        timeRect.sizeDelta = new Vector2(340f, 70f);
        timeRect.anchoredPosition = new Vector2(-20f, 0f);
        _timeLabel.alignment = TextAlignmentOptions.MidlineRight;

        // Seek bar between play button and time readout
        _progress = MakeSlider(bar);
        var sliderRect = (RectTransform)_progress.transform;
        sliderRect.anchorMin = new Vector2(0f, 0.5f);
        sliderRect.anchorMax = new Vector2(1f, 0.5f);
        sliderRect.pivot     = new Vector2(0.5f, 0.5f);
        sliderRect.offsetMin = new Vector2(190f, -17f);
        sliderRect.offsetMax = new Vector2(-380f, 17f);
        HookScrubEvents(_progress.gameObject);

        _controlsRoot.SetActive(false);
    }

    void UpdateControls()
    {
        if (_videoPlayer == null) return;

        if (_playPauseLabel != null)
            _playPauseLabel.text = _videoPlayer.isPlaying ? "Pause" : "Play";

        double length   = _videoPlayer.length;
        bool   seekable = _videoPlayer.canSetTime && length > 0.01;

        if (_progress != null)
        {
            if (_progress.gameObject.activeSelf != seekable)
                _progress.gameObject.SetActive(seekable);
            if (seekable && !_scrubbing)
                _progress.SetValueWithoutNotify((float)(_videoPlayer.time / length));
        }

        if (_timeLabel != null)
        {
            int second = (int)_videoPlayer.time;
            if (second != _lastShownSecond)
            {
                _lastShownSecond = second;
                _timeLabel.text = length > 0.01
                    ? $"{FormatTime(_videoPlayer.time)} / {FormatTime(length)}"
                    : "LIVE";
            }
        }
    }

    void TogglePlayPause()
    {
        if (_videoPlayer == null) return;
        if (_videoPlayer.isPlaying) _videoPlayer.Pause();
        else                        _videoPlayer.Play();
    }

    void HookScrubEvents(GameObject go)
    {
        var trigger = go.AddComponent<EventTrigger>();

        var down = new EventTrigger.Entry { eventID = EventTriggerType.PointerDown };
        down.callback.AddListener(_ => _scrubbing = true);
        trigger.triggers.Add(down);

        var up = new EventTrigger.Entry { eventID = EventTriggerType.PointerUp };
        up.callback.AddListener(_ => EndScrub());
        trigger.triggers.Add(up);
    }

    void EndScrub()
    {
        if (_videoPlayer != null && _videoPlayer.canSetTime && _videoPlayer.length > 0.01)
            _videoPlayer.time = _progress.value * _videoPlayer.length;
        _scrubbing = false;
    }

    static string FormatTime(double seconds) =>
        $"{(int)(seconds / 60)}:{(int)(seconds % 60):00}";

    // ── Runtime UI builders ───────────────────────────────────────────────────

    static RectTransform MakeRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Stretch(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static TextMeshProUGUI MakeLabel(RectTransform parent, string name, string text, float fontSize)
    {
        RectTransform rt = MakeRect(name, parent);
        var label = rt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text          = text;
        label.fontSize      = fontSize;
        label.color         = Color.white;
        label.alignment     = TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return label;
    }

    static Button MakeButton(RectTransform parent, string name, string text, Vector2 size, float fontSize)
    {
        RectTransform rt = MakeRect(name, parent);
        rt.sizeDelta = size;

        Image bg = rt.gameObject.AddComponent<Image>();
        bg.color = new Color(1f, 1f, 1f, 0.16f);

        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;

        // Soft tint response instead of the default darkening
        ColorBlock colors = button.colors;
        colors.normalColor      = Color.white;
        colors.highlightedColor = new Color(1f, 1f, 1f, 1.4f);
        colors.pressedColor     = new Color(1f, 1f, 1f, 2f);
        colors.selectedColor    = Color.white;
        colors.fadeDuration     = 0.15f;
        button.colors = colors;

        TextMeshProUGUI label = MakeLabel(rt, "Label", text, fontSize);
        Stretch(label.rectTransform);
        return button;
    }

    static Slider MakeSlider(RectTransform parent)
    {
        RectTransform rt = MakeRect("ProgressSlider", parent);
        var slider = rt.gameObject.AddComponent<Slider>();
        slider.transition = Selectable.Transition.None;
        slider.minValue   = 0f;
        slider.maxValue   = 1f;

        RectTransform bg = MakeRect("Background", rt);
        Stretch(bg);
        bg.gameObject.AddComponent<Image>().color = new Color(1f, 1f, 1f, 0.25f);

        RectTransform fillArea = MakeRect("FillArea", rt);
        Stretch(fillArea);
        RectTransform fill = MakeRect("Fill", fillArea);
        Stretch(fill);
        fill.gameObject.AddComponent<Image>().color = new Color(0.25f, 0.6f, 1f, 0.9f);

        RectTransform handleArea = MakeRect("HandleArea", rt);
        Stretch(handleArea);
        RectTransform handle = MakeRect("Handle", handleArea);
        handle.sizeDelta = new Vector2(34f, 34f);
        Image handleImg = handle.gameObject.AddComponent<Image>();
        handleImg.color = Color.white;

        slider.fillRect      = fill;
        slider.handleRect    = handle;
        slider.targetGraphic = handleImg;
        return slider;
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    void RestorePanels()
    {
        for (int i = 0; i < uiPanelsToHide.Length; i++)
        {
            if (uiPanelsToHide[i] == null) continue;
            bool wasActive = _panelStates == null || i >= _panelStates.Length || _panelStates[i];
            uiPanelsToHide[i].SetActive(wasActive);
        }
        _panelStates = null;
    }

    void Animate(Vector2 targetSize, Vector2 targetPos, System.Action onComplete = null)
    {
        if (_tween != null) StopCoroutine(_tween);
        _tween = StartCoroutine(TweenRect(targetSize, targetPos, onComplete));
    }

    IEnumerator TweenRect(Vector2 targetSize, Vector2 targetPos, System.Action onComplete)
    {
        Vector2 fromSize = videoRect.sizeDelta;
        Vector2 fromPos  = videoRect.anchoredPosition;

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / ANIM_DURATION)
        {
            float e = EaseInOutQuad(t);
            videoRect.sizeDelta        = Vector2.LerpUnclamped(fromSize, targetSize, e);
            videoRect.anchoredPosition = Vector2.LerpUnclamped(fromPos,  targetPos,  e);
            yield return null;
        }

        videoRect.sizeDelta        = targetSize;
        videoRect.anchoredPosition = targetPos;
        onComplete?.Invoke();
    }

    Vector2 CanvasSize() =>
        _canvasRect != null ? _canvasRect.rect.size : new Vector2(Screen.width, Screen.height);

    /// <summary>
    /// Largest size that fits inside the canvas while keeping the video's
    /// aspect ratio (letterboxed rather than stretched).
    /// </summary>
    Vector2 FullscreenSize()
    {
        Vector2 canvas = CanvasSize();
        float aspect = VideoAspect();

        float width  = canvas.x;
        float height = width / aspect;
        if (height > canvas.y)
        {
            height = canvas.y;
            width  = height * aspect;
        }
        return new Vector2(width, height);
    }

    float VideoAspect()
    {
        Texture tex = videoDisplay != null ? videoDisplay.texture : null;
        if (tex != null && tex.height > 0)
            return (float)tex.width / tex.height;
        return normalSize.x / Mathf.Max(normalSize.y, 1f);
    }

    Vector2 ComputeNormalPos()
    {
        Vector2 half = CanvasSize() * 0.5f;
        return new Vector2(
            -half.x + normalSize.x * 0.5f + normalPadding.x,
            -half.y + normalSize.y * 0.5f + normalPadding.y);
    }

    static float EaseInOutQuad(float t) =>
        t < 0.5f ? 2f * t * t : 1f - Mathf.Pow(-2f * t + 2f, 2f) / 2f;
}
