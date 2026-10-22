// ─────────────────────────────────────────────────────────────────────────────
// MapFullscreenToggle — tap the AR campus map to open it fullscreen.
//
// SETUP
//  1. Add this component to MapPanel (the UI Image under ImageTarget >
//     CanvasWeather). Leave "Source Image" empty to use MapPanel's own Image.
//  2. Make sure MapPanel's Image has "Raycast Target" ticked, and that
//     CanvasWeather (World Space) has a GraphicRaycaster and its Event Camera
//     set to the Vuforia ARCamera. A GraphicRaycaster is added automatically
//     if it's missing. The scene needs an EventSystem (it already has one for
//     the other buttons).
//  3. Optional: assign "Close Sprite" for the close button. Without one, a
//     plain drawn X is used.
//  4. Nothing else needs wiring. The fullscreen overlay (a Screen Space -
//     Overlay canvas) is built at runtime the first time it's opened, as a
//     scene-root object. It is NOT under the image target, so Vuforia hiding
//     the target's renderers/canvases on tracking loss doesn't affect it.
//
// BEHAVIOUR
//  - Tap the map: fades/scales in a fullscreen view of the same sprite, fitted
//    inside the safe area (SafeAreaFitter is added when "Use Safe Area" is on).
//  - Pinch to zoom (clamped Min..Max Zoom, default 1x–4x), one-finger drag to
//    pan (clamped so the image edges never come inside the view), double-tap
//    to reset zoom. Close with the X button or the Android back button.
//  - Editor: mouse wheel zooms around the cursor, left-drag pans, double-click
//    resets.
//  - While open, the parent Observer's SuppressTrackingReactions is set to
//    true so losing the target doesn't pause the video or hide its UI. On
//    close it goes back to its previous value. If the target was lost while
//    the map was open, the lost state (video paused, hideUI hidden) is applied
//    then.
//  - Any MapLocationMarker under MapPanel is copied onto the fullscreen map.
//    It follows pinch/pan and keeps the dot a constant on-screen size.
//  - All animation uses unscaled time, so it still works while
//    PlayPauseToggle has Time.timeScale at 0.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class MapFullscreenToggle : MonoBehaviour, IPointerClickHandler
{
    [Header("Source")]
    [Tooltip("Image whose sprite is shown fullscreen. Defaults to the Image on this GameObject.")]
    public Image sourceImage;

    [Header("Overlay")]
    [Tooltip("Optional icon for the close button. A drawn X is used when empty.")]
    public Sprite closeSprite;
    public Color backdropColor = new Color(0f, 0f, 0f, 0.92f);
    [Tooltip("Sorting order of the overlay canvas; keep above other screen-space canvases.")]
    public int sortingOrder = 100;
    [Tooltip("Fit the map inside Screen.safeArea using SafeAreaFitter.")]
    public bool useSafeArea = true;
    [Tooltip("Padding (canvas units) between the map and the safe-area edges at 1x zoom.")]
    public float padding = 24f;

    [Header("Animation")]
    public float openDuration  = 0.22f;
    public float closeDuration = 0.16f;
    [Tooltip("Scale the view animates from on open (and back to on close).")]
    public float startScale = 0.88f;

    [Header("Zoom & Pan")]
    public float minZoom = 1f;
    public float maxZoom = 4f;
    [Tooltip("Max seconds between two taps to count as a double-tap.")]
    public float doubleTapTime = 0.3f;
    [Tooltip("Editor only: zoom change per mouse-wheel notch.")]
    public float mouseZoomSpeed = 0.15f;
    public float resetDuration = 0.2f;

    const float TAP_MAX_DURATION = 0.25f;

    Observer _observer;
    bool     _targetTracked = true;
    bool     _prevSuppress;

    // Runtime-built overlay
    GameObject    _overlayRoot;
    CanvasGroup   _group;
    RectTransform _viewport;   // safe-area-fitted, masked zoom area
    RectTransform _frame;      // animated (scale) on open/close
    RectTransform _content;    // the map image; zoomed and panned
    Image         _contentImage;
    RectTransform _closeRect;
    readonly System.Collections.Generic.List<MapLocationMarker> _markerCopies =
        new System.Collections.Generic.List<MapLocationMarker>();

    bool      _isOpen;
    Coroutine _transition;
    Coroutine _resetTween;
    Vector2   _lastViewportSize;

    // Gesture state
    float   _zoom = 1f;
    bool    _tapCandidate;
    bool    _touchOnClose;
    float   _tapStartTime;
    Vector2 _tapStartPos;
    float   _lastTapTime = -10f;
    Vector2 _lastTapPos;
    bool    _mouseDragging;
    Vector2 _lastMousePos;

    /// <summary>Whether the fullscreen map is currently shown.</summary>
    public bool IsOpen => _isOpen;

    void Start()
    {
        if (sourceImage == null)
            sourceImage = GetComponent<Image>();

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas != null && canvas.GetComponent<GraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<GraphicRaycaster>();

        _observer = GetComponentInParent<Observer>();
        if (_observer != null)
        {
            _observer.OnTargetFound.AddListener(OnTargetFound);
            _observer.OnTargetLost.AddListener(OnTargetLost);
        }
    }

    void OnDestroy()
    {
        if (_observer != null)
        {
            _observer.OnTargetFound.RemoveListener(OnTargetFound);
            _observer.OnTargetLost.RemoveListener(OnTargetLost);
            if (_isOpen)
                _observer.SuppressTrackingReactions = _prevSuppress;
        }
        if (_overlayRoot != null)
            Destroy(_overlayRoot);
    }

    void OnTargetFound() => _targetTracked = true;
    void OnTargetLost()  => _targetTracked = false;

    // ── Open / close ──────────────────────────────────────────────────────────

    public void OnPointerClick(PointerEventData eventData)
    {
        // Ignore releases that ended a drag (e.g. a swipe across the panel).
        float threshold = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10f;
        if ((eventData.position - eventData.pressPosition).sqrMagnitude > threshold * threshold)
            return;
        Open();
    }

    public void Open()
    {
        if (_isOpen || sourceImage == null || sourceImage.sprite == null) return;
        _isOpen = true;

        if (_overlayRoot == null)
            BuildOverlay();

        _contentImage.sprite = sourceImage.sprite;
        foreach (var m in _markerCopies)
            if (m != null) m.SetMapImage(_contentImage, keepConstantSize: true);
        if (!_overlayRoot.activeSelf)
        {
            _group.alpha      = 0f;
            _frame.localScale = Vector3.one * startScale;
            _overlayRoot.SetActive(true);
        }
        Canvas.ForceUpdateCanvases(); // let SafeAreaFitter anchors resolve before fitting
        FitContent();
        ResetZoomImmediate();
        ResetGestures();

        if (_observer != null)
        {
            _prevSuppress = _observer.SuppressTrackingReactions;
            _observer.SuppressTrackingReactions = true;
        }

        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(Fade(1f, 1f, openDuration, EaseOutCubic, null));
    }

    public void Close()
    {
        if (!_isOpen) return;
        _isOpen = false;

        if (_resetTween != null) { StopCoroutine(_resetTween); _resetTween = null; }

        if (_observer != null)
        {
            _observer.SuppressTrackingReactions = _prevSuppress;

            // Target was lost while the map was open: apply the lost state now,
            // same as FullscreenVideoToggle does on exit.
            if (!_targetTracked && !_prevSuppress)
            {
                _observer.PauseVideoUntilTargetFound();
                if (_observer.hideUI != null)
                    _observer.hideUI.SetActive(false);
            }
        }

        if (_transition != null) StopCoroutine(_transition);
        _transition = StartCoroutine(Fade(0f, startScale, closeDuration, EaseInQuad, () =>
        {
            if (!_isOpen) _overlayRoot.SetActive(false);
        }));
    }

    IEnumerator Fade(float toAlpha, float toScale, float duration, System.Func<float, float> ease, System.Action onDone)
    {
        // Only block touches behind / accept clicks while opening or open.
        _group.blocksRaycasts = toAlpha > 0f;
        _group.interactable   = toAlpha > 0f;

        // Starts from the current values, so reopening mid-close reverses smoothly.
        float   fromAlpha = _group.alpha;
        Vector3 fromScale = _frame.localScale;
        Vector3 target    = Vector3.one * toScale;

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(duration, 0.0001f))
        {
            float e = ease(t);
            _group.alpha      = Mathf.Lerp(fromAlpha, toAlpha, e);
            _frame.localScale = Vector3.LerpUnclamped(fromScale, target, e);
            yield return null;
        }

        _group.alpha      = toAlpha;
        _frame.localScale = target;
        _transition = null;
        onDone?.Invoke();
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    void Update()
    {
        if (!_isOpen || _overlayRoot == null) return;

        if (Input.GetKeyDown(KeyCode.Escape)) // Android back button
        {
            Close();
            return;
        }

        // Re-fit on rotation / safe-area change, keeping the current zoom.
        if (_viewport.rect.size != _lastViewportSize)
        {
            FitContent();
            ClampPan();
        }

        if (Input.touchCount > 0)
            HandleTouches();
        else if (Application.isEditor || !Input.touchSupported)
            HandleMouse();
    }

    void HandleTouches()
    {
        if (Input.touchCount >= 2)
        {
            _tapCandidate = false;
            Touch a = Input.GetTouch(0);
            Touch b = Input.GetTouch(1);

            Vector2 curA = a.position, curB = b.position;
            Vector2 prevA = curA - a.deltaPosition, prevB = curB - b.deltaPosition;

            float prevDist = Vector2.Distance(prevA, prevB);
            float curDist  = Vector2.Distance(curA, curB);
            Vector2 prevMid = (prevA + prevB) * 0.5f;
            Vector2 curMid  = (curA + curB) * 0.5f;

            if (prevDist > 1f)
            {
                CancelReset();
                Pan(ScreenDeltaToLocal(prevMid, curMid));
                ZoomAround(curMid, curDist / prevDist);
            }
            return;
        }

        Touch t = Input.GetTouch(0);
        switch (t.phase)
        {
            case TouchPhase.Began:
                _touchOnClose = IsOverCloseButton(t.position);
                if (_touchOnClose) { _tapCandidate = false; break; }
                _tapCandidate = true;
                _tapStartTime = Time.unscaledTime;
                _tapStartPos  = t.position;
                break;

            case TouchPhase.Moved:
                if (_tapCandidate && (t.position - _tapStartPos).sqrMagnitude > DragThresholdSqr())
                    _tapCandidate = false;
                if (!_tapCandidate && !_touchOnClose)
                {
                    CancelReset();
                    Pan(ScreenDeltaToLocal(t.position - t.deltaPosition, t.position));
                }
                break;

            case TouchPhase.Ended:
                if (_tapCandidate && Time.unscaledTime - _tapStartTime <= TAP_MAX_DURATION)
                    RegisterTap(t.position);
                _tapCandidate = false;
                break;

            case TouchPhase.Canceled:
                _tapCandidate = false;
                break;
        }
    }

    void HandleMouse()
    {
        Vector2 mouse = Input.mousePosition;

        float scroll = Input.mouseScrollDelta.y;
        if (Mathf.Abs(scroll) > 0.01f)
        {
            CancelReset();
            ZoomAround(mouse, Mathf.Exp(scroll * mouseZoomSpeed));
        }

        if (Input.GetMouseButtonDown(0))
        {
            if (IsOverCloseButton(mouse)) return;
            _mouseDragging = true;
            _tapCandidate  = true;
            _tapStartTime  = Time.unscaledTime;
            _tapStartPos   = mouse;
            _lastMousePos  = mouse;
        }
        else if (Input.GetMouseButton(0) && _mouseDragging)
        {
            if (_tapCandidate && (mouse - _tapStartPos).sqrMagnitude > DragThresholdSqr())
                _tapCandidate = false;
            if (!_tapCandidate)
            {
                CancelReset();
                Pan(ScreenDeltaToLocal(_lastMousePos, mouse));
            }
            _lastMousePos = mouse;
        }
        else if (Input.GetMouseButtonUp(0) && _mouseDragging)
        {
            if (_tapCandidate && Time.unscaledTime - _tapStartTime <= TAP_MAX_DURATION)
                RegisterTap(mouse);
            _tapCandidate  = false;
            _mouseDragging = false;
        }
    }

    void RegisterTap(Vector2 pos)
    {
        float maxDist = DragThresholdSqr() * 9f; // second tap within ~3x drag threshold
        if (Time.unscaledTime - _lastTapTime <= doubleTapTime && (pos - _lastTapPos).sqrMagnitude <= maxDist)
        {
            _lastTapTime = -10f;
            ResetZoomAnimated();
            return;
        }
        _lastTapTime = Time.unscaledTime;
        _lastTapPos  = pos;
    }

    void ResetGestures()
    {
        _tapCandidate  = false;
        _touchOnClose  = true; // ignore any touch already down when opening
        _mouseDragging = false;
        _lastTapTime   = -10f;
    }

    static float DragThresholdSqr()
    {
        float px = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 10f;
        return px * px;
    }

    bool IsOverCloseButton(Vector2 screenPos) =>
        _closeRect != null && RectTransformUtility.RectangleContainsScreenPoint(_closeRect, screenPos, null);

    // ── Zoom / pan math ───────────────────────────────────────────────────────

    Vector2 ScreenToViewportLocal(Vector2 screenPos)
    {
        RectTransformUtility.ScreenPointToLocalPointInRectangle(_viewport, screenPos, null, out Vector2 local);
        return local;
    }

    Vector2 ScreenDeltaToLocal(Vector2 fromScreen, Vector2 toScreen) =>
        ScreenToViewportLocal(toScreen) - ScreenToViewportLocal(fromScreen);

    void Pan(Vector2 localDelta)
    {
        _content.anchoredPosition += localDelta;
        ClampPan();
    }

    /// <summary>Scales by <paramref name="factor"/> keeping the point under <paramref name="screenFocus"/> fixed.</summary>
    void ZoomAround(Vector2 screenFocus, float factor)
    {
        float newZoom = Mathf.Clamp(_zoom * factor, minZoom, maxZoom);
        if (Mathf.Approximately(newZoom, _zoom)) return;

        Vector2 focus = ScreenToViewportLocal(screenFocus);
        Vector2 pos   = _content.anchoredPosition;
        Vector2 pointInContent = (focus - pos) / _zoom;

        _zoom = newZoom;
        _content.localScale       = new Vector3(_zoom, _zoom, 1f);
        _content.anchoredPosition = focus - pointInContent * _zoom;
        ClampPan();
    }

    /// <summary>Keeps the image covering the view: no empty space past an edge once it's larger than the view.</summary>
    void ClampPan()
    {
        Vector2 view   = _viewport.rect.size;
        Vector2 scaled = _content.rect.size * _zoom;
        Vector2 max = new Vector2(
            Mathf.Max(0f, (scaled.x - view.x) * 0.5f),
            Mathf.Max(0f, (scaled.y - view.y) * 0.5f));

        Vector2 p = _content.anchoredPosition;
        _content.anchoredPosition = new Vector2(
            Mathf.Clamp(p.x, -max.x, max.x),
            Mathf.Clamp(p.y, -max.y, max.y));
    }

    /// <summary>Sizes the image to fit inside the viewport (minus padding) at 1x, keeping the sprite's aspect.</summary>
    void FitContent()
    {
        _lastViewportSize = _viewport.rect.size;
        Sprite s = _contentImage.sprite;
        if (s == null) return;

        Vector2 avail  = Vector2.Max(_lastViewportSize - Vector2.one * (padding * 2f), Vector2.one);
        float   aspect = s.rect.width / Mathf.Max(s.rect.height, 1f);

        float w = avail.x, h = w / aspect;
        if (h > avail.y) { h = avail.y; w = h * aspect; }
        _content.sizeDelta = new Vector2(w, h);
    }

    void ResetZoomImmediate()
    {
        CancelReset();
        _zoom = 1f;
        _content.localScale       = Vector3.one;
        _content.anchoredPosition = Vector2.zero;
    }

    void ResetZoomAnimated()
    {
        CancelReset();
        _resetTween = StartCoroutine(ResetRoutine());
    }

    void CancelReset()
    {
        if (_resetTween == null) return;
        StopCoroutine(_resetTween);
        _resetTween = null;
    }

    IEnumerator ResetRoutine()
    {
        float   fromZoom = _zoom;
        Vector2 fromPos  = _content.anchoredPosition;

        for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime / Mathf.Max(resetDuration, 0.0001f))
        {
            float e = EaseOutCubic(t);
            _zoom = Mathf.Lerp(fromZoom, 1f, e);
            _content.localScale       = new Vector3(_zoom, _zoom, 1f);
            _content.anchoredPosition = Vector2.Lerp(fromPos, Vector2.zero, e);
            yield return null;
        }

        _resetTween = null;
        ResetZoomImmediate();
    }

    // ── Runtime UI ────────────────────────────────────────────────────────────

    void BuildOverlay()
    {
        // Scene root (not under the image target) so Vuforia's tracking-lost
        // renderer/canvas disabling never touches it.
        _overlayRoot = new GameObject("MapFullscreenOverlay", typeof(RectTransform));
        _overlayRoot.layer = LayerMask.NameToLayer("UI") >= 0 ? LayerMask.NameToLayer("UI") : 0;

        var canvas = _overlayRoot.AddComponent<Canvas>();
        canvas.renderMode     = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = _overlayRoot.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight  = 0.5f;

        _overlayRoot.AddComponent<GraphicRaycaster>();
        _group = _overlayRoot.AddComponent<CanvasGroup>();
        _group.alpha = 0f;

        RectTransform root = (RectTransform)_overlayRoot.transform;

        // Full-screen backdrop; also swallows touches so nothing behind reacts.
        RectTransform backdrop = MakeRect("Backdrop", root);
        Stretch(backdrop);
        backdrop.gameObject.AddComponent<Image>().color = backdropColor;

        // Safe-area container
        RectTransform safe = MakeRect("SafeArea", root);
        Stretch(safe);
        if (useSafeArea)
            safe.gameObject.AddComponent<SafeAreaFitter>();

        // Frame: animated on open/close; holds viewport and close button.
        _frame = MakeRect("Frame", safe);
        Stretch(_frame);

        _viewport = MakeRect("Viewport", _frame);
        Stretch(_viewport);
        _viewport.gameObject.AddComponent<RectMask2D>();

        _content = MakeRect("Map", _viewport);
        _content.anchorMin = _content.anchorMax = _content.pivot = new Vector2(0.5f, 0.5f);
        _contentImage = _content.gameObject.AddComponent<Image>();
        _contentImage.preserveAspect = true;
        _contentImage.raycastTarget  = false; // gestures are read from Input directly

        CopyLocationMarkers();
        BuildCloseButton(_frame);

        _overlayRoot.SetActive(false);
    }

    /// <summary>
    /// Copies any MapLocationMarker on the source map onto the fullscreen map,
    /// so "you are here" also shows (and follows zoom/pan) when it's open.
    /// </summary>
    void CopyLocationMarkers()
    {
        if (sourceImage == null) return;
        foreach (var original in sourceImage.GetComponentsInChildren<MapLocationMarker>(true))
        {
            GameObject copy = Instantiate(original.gameObject, _content, false);
            copy.name = original.name;
            foreach (var t in copy.GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = _content.gameObject.layer;

            var marker = copy.GetComponent<MapLocationMarker>();
            marker.SetMapImage(_contentImage, keepConstantSize: true);
            _markerCopies.Add(marker);
        }
    }

    void BuildCloseButton(RectTransform parent)
    {
        _closeRect = MakeRect("CloseButton", parent);
        _closeRect.anchorMin = _closeRect.anchorMax = _closeRect.pivot = Vector2.one;
        _closeRect.sizeDelta        = new Vector2(110f, 110f);
        _closeRect.anchoredPosition = new Vector2(-30f, -30f);

        Image bg = _closeRect.gameObject.AddComponent<Image>();
        if (closeSprite != null)
        {
            bg.sprite         = closeSprite;
            bg.preserveAspect = true;
            bg.color          = Color.white;
        }
        else
        {
            // Font-independent X: two crossed bars on a translucent square.
            bg.color = new Color(0f, 0f, 0f, 0.5f);
            for (int i = 0; i < 2; i++)
            {
                RectTransform bar = MakeRect(i == 0 ? "Bar1" : "Bar2", _closeRect);
                bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
                bar.sizeDelta     = new Vector2(60f, 8f);
                bar.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
                Image img = bar.gameObject.AddComponent<Image>();
                img.color         = Color.white;
                img.raycastTarget = false;
            }
        }

        Button button = _closeRect.gameObject.AddComponent<Button>();
        button.targetGraphic = bg;
        button.onClick.AddListener(Close);
    }

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

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    static float EaseInQuad(float t)   => t * t;
}
