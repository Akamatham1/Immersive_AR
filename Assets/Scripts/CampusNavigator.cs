// ─────────────────────────────────────────────────────────────────────────────
// CampusNavigator — self-building navigation UI for straight-line campus
// directions: a "Directions" button, a bottom sheet listing buildings
// (nearest first), a navigation card, a compass arrow, and the direction
// line + pin on the campus map.
//
// SETUP
//  1. Add this component to any always-active object in ARScene (e.g.
//     CanvasButtons > SafeArea). Nothing else to create or wire.
//  2. "Destinations" fills itself with the project's CampusDestinations asset
//     when the component is added. "Calibration" can stay empty: it's taken
//     from the scene's MapLocationMarker.
//  3. Save the scene.
//  4. Editor testing: tick "Use Fake In Editor" on GPSLocationProvider, enter
//     a campus lat/lon and heading, press Play and start AR.
//
// BEHAVIOUR
//  - The UI is a separate Screen Space - Overlay canvas built at runtime (like
//    MapFullscreenToggle's), so it's independent of CanvasButtons' scaling
//    and of Vuforia hiding the image target's content.
//  - Hidden until AR has started (VuforiaBehaviour enabled) and while
//    fullscreen video is showing. With "Only When Map Tracked" (default) the
//    Directions button appears only while the map's image target is
//    recognised or the fullscreen map is open; once a destination is chosen
//    the card and compass stay up even when the target is lost.
//  - Idle: a "Directions" pill button. Tap it for the bottom sheet: one row
//    per building with its code, name and live walking estimate, sorted
//    nearest first. Tap a row to start; tap the dim area, ✕ or Android back
//    to dismiss.
//  - Navigating: a card at the bottom shows the code, name and "~120 m ·
//    ~2 min" (DirectionsController's estimate rules). Tap the card to change
//    destination, ✕ to stop. It turns green on arrival.
//  - A compass arrow at the top points to the destination (CompassArrow),
//    unless the scene already has its own CompassArrow.
//  - A DirectionLine + pin is added to each map with a MapLocationMarker
//    (unless one is already there). MapFullscreenToggle adds its own to the
//    fullscreen map, where buildings can also be tapped to select them.
//  - Uses an existing DirectionsController if the scene has one; otherwise
//    adds one to this GameObject.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vuforia;
using Image = UnityEngine.UI.Image;

[DisallowMultipleComponent]
public class CampusNavigator : MonoBehaviour
{
    [Header("Data")]
    public CampusDestinations destinations;
    [Tooltip("Defaults to the calibration on the scene's MapLocationMarker.")]
    public MapCalibration calibration;

    [Header("Look")]
    public Color accent      = new Color(1f, 0.55f, 0.1f, 1f);
    public Color arrived     = new Color(0.2f, 0.72f, 0.38f, 1f);
    public Color panelColor  = new Color(0.04f, 0.08f, 0.16f, 0.94f);
    public Color textColor   = Color.white;
    public Color subtleText  = new Color(1f, 1f, 1f, 0.65f);
    [Tooltip("Optional compass arrow sprite pointing UP. A drawn arrow is used when empty.")]
    public Sprite arrowSprite;
    [Tooltip("Keep above other screen-space canvases; MapFullscreenToggle uses 100.")]
    public int  sortingOrder  = 110;
    public bool buttonOnRight = true;
    public bool showCompass   = true;
    [Tooltip("Add a direction line + pin to every map with a MapLocationMarker.")]
    public bool addMapLines   = true;

    [Tooltip("Gap (canvas units) between the screen bottom and the button/card.")]
    public float bottomMargin = 36f;
    [Tooltip("Lift the button/card above these while any of their buttons are showing. " +
             "Defaults to the Observer's hideUI (ButtonUI).")]
    public RectTransform[] keepAbove;

    [Header("Visibility")]
    [Tooltip("Show the Directions button only while the campus map image target is recognised " +
             "(or the fullscreen map is open). The card and compass stay up once navigating.")]
    public bool onlyWhenMapTracked = true;
    [Tooltip("Observer of the map's image target. Defaults to the one above the MapLocationMarker.")]
    public Observer mapObserver;
    [Tooltip("Seconds the button stays after the target is lost (avoids flicker).")]
    public float lostGrace = 0.75f;

    [Header("Behaviour")]
    [Tooltip("Animation speed (1 / seconds).")]
    public float animSpeed = 6f;
    [Tooltip("Seconds between distance refreshes in the list and card.")]
    public float refreshInterval = 0.5f;

    // ── Static hooks used by MapFullscreenToggle ─────────────────────────────

    static CampusNavigator s_instance;
    static int s_backFrame = -1;

    /// <summary>True when the navigator used (or will use) this frame's Android back press.</summary>
    public static bool HandlesBack =>
        s_backFrame == Time.frameCount || (s_instance != null && s_instance._sheetOpen);

    /// <summary>True when <paramref name="screenPos"/> is over visible navigator UI.</summary>
    public static bool IsScreenPointOverUI(Vector2 screenPos) =>
        s_instance != null && s_instance.BlocksPoint(screenPos);

    /// <summary>Accent colour of the active navigator (for map pins/lines built elsewhere).</summary>
    public static Color AccentColor => s_instance != null ? s_instance.accent : new Color(1f, 0.55f, 0.1f, 1f);

    // ── State ─────────────────────────────────────────────────────────────────

    class Row
    {
        public CampusDestinations.Building building;
        public RectTransform rt;
        public Image bg, badge;
        public TextMeshProUGUI code, title, sub;
        public float sortKey;
    }

    DirectionsController _controller;
    VuforiaBehaviour     _vuforia;
    FullscreenVideoToggle[] _videoToggles;
    MapFullscreenToggle[]   _mapToggles;
    bool  _mapTracked;
    float _lostAt = -999f;

    GameObject    _root;
    CanvasGroup   _rootGroup;
    RectTransform _safe;
    RectTransform _button, _card, _sheet, _dim;
    CanvasGroup   _buttonGroup, _cardGroup, _sheetGroup, _dimGroup;
    Image         _cardBadge;
    TextMeshProUGUI _cardCode, _cardTitle, _cardSub, _sheetHint;
    readonly List<Row> _rows = new List<Row>();

    bool  _sheetOpen;
    float _buttonT, _cardT, _sheetT;
    float _bottom = -1f; // current resting offset of button/card above the safe-area bottom
    float _nextRefresh;
    readonly Vector3[] _corners = new Vector3[4];

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    void Awake()
    {
        if (s_instance != null && s_instance != this)
        {
            Debug.LogWarning("[CampusNavigator] More than one in the scene — disabling this one.", this);
            enabled = false;
            return;
        }
        s_instance = this;

        // Configure the controller before its Start runs.
        _controller = FindFirstObjectByType<DirectionsController>(FindObjectsInactive.Include);
        if (_controller == null) _controller = gameObject.AddComponent<DirectionsController>();

        if (calibration == null)
        {
            var marker = FindFirstObjectByType<MapLocationMarker>(FindObjectsInactive.Include);
            if (marker != null) calibration = marker.calibration;
        }
        if (_controller.destinations == null) _controller.destinations = destinations;
        if (_controller.calibration  == null) _controller.calibration  = calibration;

        if (_controller.destinations == null)
            Debug.LogWarning("[CampusNavigator] No CampusDestinations assigned — the list will be empty.", this);
        if (_controller.calibration == null)
            Debug.LogWarning("[CampusNavigator] No MapCalibration found — distances and lines are unavailable.", this);
    }

    void Start()
    {
        if (s_instance != this) return;

        _vuforia      = FindFirstObjectByType<VuforiaBehaviour>(FindObjectsInactive.Include);
        _videoToggles = FindObjectsByType<FullscreenVideoToggle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        _mapToggles   = FindObjectsByType<MapFullscreenToggle>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        HookMapObserver();
        if (keepAbove == null || keepAbove.Length == 0)
        {
            var observer = FindFirstObjectByType<Observer>(FindObjectsInactive.Include);
            if (observer != null && observer.hideUI != null)
                keepAbove = new[] { observer.hideUI.transform as RectTransform };
        }

        BuildUI();
        if (addMapLines) AddMapLines();
        _controller.DestinationChanged += _ => Refresh();
    }

    void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
        if (_root != null) Destroy(_root);
        if (mapObserver != null)
        {
            mapObserver.OnTargetFound.RemoveListener(OnMapFound);
            mapObserver.OnTargetLost.RemoveListener(OnMapLost);
        }
    }

    void OnDisable()
    {
        if (_root != null) _root.SetActive(false);
    }

    void OnEnable()
    {
        if (_root != null) _root.SetActive(true);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public void OpenSheet()
    {
        if (_sheetOpen) return;
        _sheetOpen = true;
        _sheet.gameObject.SetActive(true);
        _dim.gameObject.SetActive(true);
        Refresh(force: true);
        ScrollToTop();
    }

    public void CloseSheet() => _sheetOpen = false;

    public void SelectDestination(CampusDestinations.Building building)
    {
        _controller.Select(building);
        CloseSheet();
    }

    public void StopNavigation() => _controller.ClearDestination();

    // ── Per frame ─────────────────────────────────────────────────────────────

    void Update()
    {
        if (_root == null) return;
        float dt = Time.unscaledDeltaTime;

        bool visible = ShouldShow();
        if (!visible && _sheetOpen) CloseSheet();
        _rootGroup.alpha = Mathf.MoveTowards(_rootGroup.alpha, visible ? 1f : 0f, animSpeed * dt);
        _rootGroup.blocksRaycasts = _rootGroup.interactable = visible;

        if (_sheetOpen && Input.GetKeyDown(KeyCode.Escape)) // Android back
        {
            s_backFrame = Time.frameCount;
            CloseSheet();
        }

        bool hasDest = _controller.HasDestination;
        _buttonT = Mathf.MoveTowards(_buttonT, !hasDest && !_sheetOpen ? 1f : 0f, animSpeed * dt);
        _cardT   = Mathf.MoveTowards(_cardT,   hasDest && !_sheetOpen ? 1f : 0f, animSpeed * dt);
        _sheetT  = Mathf.MoveTowards(_sheetT,  _sheetOpen ? 1f : 0f,             animSpeed * dt);

        float targetBottom = Mathf.Max(bottomMargin, KeepAboveHeight() + 24f);
        _bottom = _bottom < 0f ? targetBottom
                               : Mathf.Lerp(_bottom, targetBottom, 1f - Mathf.Exp(-10f * dt));

        ApplyFade(_buttonGroup, _button, _buttonT, 40f);
        ApplyFade(_cardGroup,   _card,   _cardT,   60f);

        _dimGroup.alpha = _sheetT;
        _dimGroup.blocksRaycasts = _sheetOpen;
        _sheetGroup.blocksRaycasts = _sheetGroup.interactable = _sheetOpen;
        _sheetGroup.alpha = Mathf.Clamp01(_sheetT * 3f);

        float h = Mathf.Max(_safe.rect.height * 0.68f, 600f);
        _sheet.sizeDelta = new Vector2(_sheet.sizeDelta.x, h + 40f); // +40 hides the bottom corners
        float slide = 1f - EaseOutCubic(_sheetT);
        _sheet.anchoredPosition = new Vector2(0f, -40f - (h + 40f) * slide);

        if (!_sheetOpen && _sheetT <= 0f)
        {
            if (_sheet.gameObject.activeSelf) _sheet.gameObject.SetActive(false);
            if (_dim.gameObject.activeSelf)   _dim.gameObject.SetActive(false);
        }

        if (Time.unscaledTime >= _nextRefresh) Refresh();
    }

    bool ShouldShow()
    {
        if (_vuforia != null && !_vuforia.enabled) return false; // AR not started yet
        foreach (var v in _videoToggles)
            if (v != null && v.isFullscreen) return false;
        // Once navigating (or choosing), stay up so the card/compass work while walking.
        return MapVisible() || _controller.HasDestination || _sheetOpen;
    }

    /// <summary>True while the map target is recognised (with a short grace) or the fullscreen map is open.</summary>
    bool MapVisible()
    {
        if (!onlyWhenMapTracked || mapObserver == null) return true;
        if (_mapTracked || Time.unscaledTime - _lostAt < lostGrace) return true;
        foreach (var m in _mapToggles)
            if (m != null && m.IsOpen) return true;
        return false;
    }

    void HookMapObserver()
    {
        if (mapObserver == null)
        {
            var marker = FindFirstObjectByType<MapLocationMarker>(FindObjectsInactive.Include);
            if (marker != null) mapObserver = marker.GetComponentInParent<Observer>(true);
            if (mapObserver == null) mapObserver = FindFirstObjectByType<Observer>(FindObjectsInactive.Include);
        }
        if (mapObserver == null)
        {
            if (onlyWhenMapTracked)
                Debug.LogWarning("[CampusNavigator] No Observer found — showing the button whenever AR runs.", this);
            return;
        }
        mapObserver.OnTargetFound.AddListener(OnMapFound);
        mapObserver.OnTargetLost.AddListener(OnMapLost);
    }

    void OnMapFound() => _mapTracked = true;
    void OnMapLost()  { _mapTracked = false; _lostAt = Time.unscaledTime; }

    void ApplyFade(CanvasGroup g, RectTransform rt, float t, float slide)
    {
        g.alpha = t;
        g.blocksRaycasts = g.interactable = t > 0.5f;
        float e = EaseOutCubic(t);
        rt.localScale = Vector3.one * (0.94f + 0.06f * e);
        rt.anchoredPosition = new Vector2(rt.anchoredPosition.x, _bottom - slide * (1f - e));
    }

    /// <summary>
    /// Height (in this canvas' safe-area units) of the highest visible button
    /// under <see cref="keepAbove"/>, measured from the safe-area bottom. 0 when none show.
    /// </summary>
    float KeepAboveHeight()
    {
        if (keepAbove == null) return 0f;
        float topPx = float.NegativeInfinity;
        foreach (var area in keepAbove)
        {
            if (area == null || !area.gameObject.activeInHierarchy) continue;
            var group = area.GetComponent<CanvasGroup>(); // e.g. faded out by VideoControlsVisibility
            if (group != null && group.alpha < 0.05f) continue;
            foreach (var s in area.GetComponentsInChildren<Selectable>())
            {
                if (!s.isActiveAndEnabled) continue;
                Canvas c = s.GetComponentInParent<Canvas>();
                if (c == null || !c.enabled) continue;
                c = c.rootCanvas;
                Camera cam = c.renderMode == RenderMode.ScreenSpaceOverlay ? null : c.worldCamera;
                ((RectTransform)s.transform).GetWorldCorners(_corners);
                foreach (var w in _corners)
                    topPx = Mathf.Max(topPx, RectTransformUtility.WorldToScreenPoint(cam, w).y);
            }
        }
        if (float.IsNegativeInfinity(topPx)) return 0f;

        RectTransformUtility.ScreenPointToLocalPointInRectangle(_safe, new Vector2(0f, topPx), null, out Vector2 local);
        return Mathf.Max(0f, local.y - _safe.rect.yMin);
    }

    bool BlocksPoint(Vector2 p)
    {
        if (_rootGroup == null || !_rootGroup.blocksRaycasts || _rootGroup.alpha <= 0.01f) return false;
        if (_sheetT > 0.01f) return true; // dim + sheet cover the screen
        if (_cardT   > 0.5f && RectTransformUtility.RectangleContainsScreenPoint(_card,   p, null)) return true;
        if (_buttonT > 0.5f && RectTransformUtility.RectangleContainsScreenPoint(_button, p, null)) return true;
        return false;
    }

    // ── Content refresh ───────────────────────────────────────────────────────

    void Refresh(bool force = false)
    {
        _nextRefresh = Time.unscaledTime + refreshInterval;
        if (_root == null) return;
        RefreshCard();
        if (_sheetOpen || force) RefreshRows();
    }

    void RefreshCard()
    {
        var b = _controller.Selected;
        if (b == null) return;

        _cardCode.text = CodeFor(b);
        if (_controller.HasArrived)
        {
            _cardBadge.color = arrived;
            _cardTitle.text  = $"You've arrived at {b.name}";
            _cardSub.text    = "Tap × to finish";
            return;
        }

        _cardBadge.color = accent;
        _cardTitle.text  = b.name;
        _cardSub.text    = _controller.EstimateText(b) ?? GpsWaitText();
    }

    void RefreshRows()
    {
        bool anyDistance = false;
        foreach (var r in _rows)
        {
            bool selected = r.building == _controller.Selected;
            r.bg.color    = selected ? WithAlpha(accent, 0.22f) : new Color(1f, 1f, 1f, 0.06f);
            r.badge.color = selected ? accent : new Color(1f, 1f, 1f, 0.14f);

            if (_controller.TryEstimate(r.building, out float straight, out int m, out int min))
            {
                anyDistance = true;
                r.sortKey  = straight;
                r.sub.text = straight <= _controller.ArrivalRadius ? "You're here" : $"~{m} m · ~{min} min walk";
            }
            else
            {
                r.sortKey  = 1e6f + r.building.number; // keep list order
                r.sub.text = "Distance unavailable";
            }
        }

        _rows.Sort((a, c) => a.sortKey.CompareTo(c.sortKey));
        for (int i = 0; i < _rows.Count; i++) _rows[i].rt.SetSiblingIndex(i);

        _sheetHint.text = anyDistance ? "Nearest first · straight-line estimates" : GpsWaitText();
    }

    string GpsWaitText()
    {
        var gps = _controller.gps;
        if (gps == null || !gps.IsReady) return gps != null ? gps.Status : "Waiting for GPS…";
        return "Map not calibrated";
    }

    static string CodeFor(CampusDestinations.Building b) =>
        string.IsNullOrEmpty(b.shortCode) ? b.number.ToString() : b.shortCode;

    // ── Map lines ─────────────────────────────────────────────────────────────

    void AddMapLines()
    {
        var markers = FindObjectsByType<MapLocationMarker>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var marker in markers)
        {
            Image map = marker.transform.parent != null ? marker.transform.parent.GetComponent<Image>() : null;
            if (map == null || HasOwnLine(map)) continue;

            float size = Mathf.Max(((RectTransform)marker.transform).sizeDelta.x, 20f);
            DirectionLine.Create(map, marker, _controller, WithAlpha(accent, 0.95f),
                                 thickness: size * 0.22f, pinSize: size * 0.95f, constantSize: false);
        }
    }

    static bool HasOwnLine(Image map)
    {
        foreach (var l in map.GetComponentsInChildren<DirectionLine>(true))
            if (l.transform != map.transform) return true;
        return false;
    }

    // ── UI construction ───────────────────────────────────────────────────────

    void BuildUI()
    {
        _root = new GameObject("CampusNavigatorUI", typeof(RectTransform));
        int uiLayer = LayerMask.NameToLayer("UI");
        _root.layer = uiLayer >= 0 ? uiLayer : 0;

        var canvas = _root.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        var scaler = _root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080f, 1920f);
        scaler.matchWidthOrHeight  = 0.5f;

        _root.AddComponent<GraphicRaycaster>();
        _rootGroup = _root.AddComponent<CanvasGroup>();
        _rootGroup.alpha = 0f;

        var root = (RectTransform)_root.transform;

        _safe = MakeRect("SafeArea", root);
        Stretch(_safe);
        _safe.gameObject.AddComponent<SafeAreaFitter>();

        if (showCompass && FindFirstObjectByType<CompassArrow>(FindObjectsInactive.Include) == null)
            BuildCompass(_safe);
        BuildButton(_safe);
        BuildCard(_safe);

        // Dim covers the whole screen (not just the safe area).
        _dim = MakeRect("Dim", root);
        Stretch(_dim);
        var dimImg = _dim.gameObject.AddComponent<Image>();
        dimImg.color = new Color(0f, 0f, 0f, 0.5f);
        _dim.gameObject.AddComponent<Button>().onClick.AddListener(CloseSheet);
        _dimGroup = _dim.gameObject.AddComponent<CanvasGroup>();
        _dimGroup.alpha = 0f;
        _dim.gameObject.SetActive(false);

        BuildSheet(root);
        Refresh(force: true);
    }

    void BuildCompass(RectTransform parent)
    {
        RectTransform c = MakeRect("CompassArrow", parent);
        c.sizeDelta = new Vector2(150f, 150f);

        RectTransform disc = MakeRect("Disc", c);
        Stretch(disc);
        var discImg = disc.gameObject.AddComponent<Image>();
        discImg.sprite = RuntimeUISprites.Circle;
        discImg.color  = panelColor;
        discImg.raycastTarget = false;

        RectTransform arrow = MakeRect("Arrow", c);
        arrow.anchorMin = arrow.anchorMax = arrow.pivot = new Vector2(0.5f, 0.5f);
        arrow.sizeDelta = new Vector2(92f, 92f);
        var arrowImg = arrow.gameObject.AddComponent<Image>();
        arrowImg.sprite = arrowSprite != null ? arrowSprite : RuntimeUISprites.Arrow;
        arrowImg.preserveAspect = true;
        arrowImg.color = accent;
        arrowImg.raycastTarget = false;

        var compass = c.gameObject.AddComponent<CompassArrow>();
        compass.controller       = _controller;
        compass.placeAtTopCenter = true;
        compass.topOffset        = 28f;
    }

    void BuildButton(RectTransform parent)
    {
        _button = MakeRect("DirectionsButton", parent);
        float side = buttonOnRight ? 1f : 0f;
        _button.anchorMin = _button.anchorMax = _button.pivot = new Vector2(side, 0f);
        _button.sizeDelta = new Vector2(360f, 128f);
        _button.anchoredPosition = new Vector2(buttonOnRight ? -36f : 36f, bottomMargin);

        var bg = Rounded(_button, accent, 64f);
        var btn = _button.gameObject.AddComponent<Button>();
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OpenSheet);

        RectTransform icon = MakeRect("Icon", _button);
        icon.anchorMin = icon.anchorMax = new Vector2(0f, 0.5f);
        icon.pivot = new Vector2(0.5f, 0.5f);
        icon.sizeDelta = new Vector2(64f, 64f);
        icon.anchoredPosition = new Vector2(78f, 0f);
        icon.localRotation = Quaternion.Euler(0f, 0f, -45f); // "send" style
        var iconImg = icon.gameObject.AddComponent<Image>();
        iconImg.sprite = RuntimeUISprites.Arrow;
        iconImg.color  = Color.white;
        iconImg.raycastTarget = false;

        var label = MakeText("Label", _button, 44f, FontStyles.Bold, Color.white, TextAlignmentOptions.MidlineLeft);
        var lrt = label.rectTransform;
        Stretch(lrt);
        lrt.offsetMin = new Vector2(128f, 0f);
        lrt.offsetMax = new Vector2(-24f, 0f);
        label.text = "Directions";

        _buttonGroup = _button.gameObject.AddComponent<CanvasGroup>();
        _buttonGroup.alpha = 0f;
    }

    void BuildCard(RectTransform parent)
    {
        _card = MakeRect("NavigationCard", parent);
        _card.anchorMin = new Vector2(0f, 0f);
        _card.anchorMax = new Vector2(1f, 0f);
        _card.pivot     = new Vector2(0.5f, 0f);
        _card.offsetMin = new Vector2(28f, 0f);
        _card.offsetMax = new Vector2(-28f, 0f);
        _card.sizeDelta = new Vector2(_card.sizeDelta.x, 184f);
        _card.anchoredPosition = new Vector2(0f, bottomMargin);

        var bg = Rounded(_card, panelColor, 40f);
        var btn = _card.gameObject.AddComponent<Button>(); // tap card = change destination
        btn.targetGraphic = bg;
        btn.onClick.AddListener(OpenSheet);

        RectTransform badge = MakeRect("Badge", _card);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
        badge.pivot = new Vector2(0f, 0.5f);
        badge.sizeDelta = new Vector2(128f, 128f);
        badge.anchoredPosition = new Vector2(28f, 0f);
        _cardBadge = Rounded(badge, accent, 32f);
        _cardBadge.raycastTarget = false;
        _cardCode = MakeText("Code", badge, 44f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        Stretch(_cardCode.rectTransform);
        _cardCode.enableAutoSizing = true;
        _cardCode.fontSizeMin = 22f;
        _cardCode.fontSizeMax = 44f;
        _cardCode.rectTransform.offsetMin = new Vector2(8f, 0f);
        _cardCode.rectTransform.offsetMax = new Vector2(-8f, 0f);

        _cardTitle = MakeText("Title", _card, 40f, FontStyles.Bold, textColor, TextAlignmentOptions.BottomLeft);
        Place(_cardTitle.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(184f, 4f), new Vector2(-150f, -26f));
        _cardSub = MakeText("Estimate", _card, 34f, FontStyles.Normal, subtleText, TextAlignmentOptions.TopLeft);
        Place(_cardSub.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(184f, 26f), new Vector2(-150f, -4f));

        RectTransform close = MakeRect("StopButton", _card);
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 0.5f);
        close.sizeDelta = new Vector2(104f, 104f);
        close.anchoredPosition = new Vector2(-28f, 0f);
        BuildCloseGlyph(close, new Color(1f, 1f, 1f, 0.12f), 46f);
        close.gameObject.AddComponent<Button>().onClick.AddListener(StopNavigation);

        _cardGroup = _card.gameObject.AddComponent<CanvasGroup>();
        _cardGroup.alpha = 0f;
    }

    void BuildSheet(RectTransform parent)
    {
        _sheet = MakeRect("DestinationSheet", parent);
        _sheet.anchorMin = new Vector2(0f, 0f);
        _sheet.anchorMax = new Vector2(1f, 0f);
        _sheet.pivot     = new Vector2(0.5f, 0f);
        _sheet.offsetMin = new Vector2(0f, 0f);
        _sheet.offsetMax = new Vector2(0f, 0f);
        _sheet.sizeDelta = new Vector2(0f, 1200f);
        Rounded(_sheet, panelColor, 48f);
        _sheetGroup = _sheet.gameObject.AddComponent<CanvasGroup>();

        // Grab handle
        RectTransform handle = MakeRect("Handle", _sheet);
        handle.anchorMin = handle.anchorMax = handle.pivot = new Vector2(0.5f, 1f);
        handle.sizeDelta = new Vector2(120f, 12f);
        handle.anchoredPosition = new Vector2(0f, -20f);
        Rounded(handle, new Color(1f, 1f, 1f, 0.25f), 6f).raycastTarget = false;

        var title = MakeText("Title", _sheet, 50f, FontStyles.Bold, textColor, TextAlignmentOptions.TopLeft);
        Place(title.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -110f), new Vector2(-160f, -48f));
        title.text = "Where to?";

        _sheetHint = MakeText("Hint", _sheet, 30f, FontStyles.Normal, subtleText, TextAlignmentOptions.TopLeft);
        Place(_sheetHint.rectTransform, new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(48f, -160f), new Vector2(-160f, -112f));

        RectTransform close = MakeRect("CloseButton", _sheet);
        close.anchorMin = close.anchorMax = close.pivot = new Vector2(1f, 1f);
        close.sizeDelta = new Vector2(100f, 100f);
        close.anchoredPosition = new Vector2(-36f, -44f);
        BuildCloseGlyph(close, new Color(1f, 1f, 1f, 0.12f), 44f);
        close.gameObject.AddComponent<Button>().onClick.AddListener(CloseSheet);

        // Scrolling list
        RectTransform viewport = MakeRect("Viewport", _sheet);
        Place(viewport, Vector2.zero, Vector2.one, new Vector2(0f, 64f), new Vector2(0f, -180f)); // +40 hidden bottom
        viewport.gameObject.AddComponent<RectMask2D>();
        var vpImg = viewport.gameObject.AddComponent<Image>(); // makes empty space draggable
        vpImg.color = Color.clear;

        RectTransform content = MakeRect("Content", viewport);
        content.anchorMin = new Vector2(0f, 1f);
        content.anchorMax = new Vector2(1f, 1f);
        content.pivot     = new Vector2(0.5f, 1f);
        content.offsetMin = content.offsetMax = Vector2.zero;
        var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(28, 28, 8, 24);
        layout.spacing = 14f;
        layout.childControlWidth  = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth  = true;
        layout.childForceExpandHeight = false;
        content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scroll = _sheet.gameObject.AddComponent<ScrollRect>();
        scroll.viewport   = viewport;
        scroll.content    = content;
        scroll.horizontal = false;
        scroll.vertical   = true;
        scroll.movementType = ScrollRect.MovementType.Elastic;
        scroll.scrollSensitivity = 40f;

        foreach (var b in _controller.Buildings)
            _rows.Add(BuildRow(content, b));

        _sheet.gameObject.SetActive(false);
    }

    Row BuildRow(RectTransform parent, CampusDestinations.Building b)
    {
        var row = new Row { building = b };
        row.rt = MakeRect(b.name, parent);
        var le = row.rt.gameObject.AddComponent<LayoutElement>();
        le.preferredHeight = 148f;
        le.minHeight = 148f;

        row.bg = Rounded(row.rt, new Color(1f, 1f, 1f, 0.06f), 28f);
        var btn = row.rt.gameObject.AddComponent<Button>();
        btn.targetGraphic = row.bg;
        var colors = btn.colors;
        colors.pressedColor = new Color(0.8f, 0.8f, 0.8f, 1f);
        btn.colors = colors;
        btn.onClick.AddListener(() => SelectDestination(b));

        RectTransform badge = MakeRect("Badge", row.rt);
        badge.anchorMin = badge.anchorMax = new Vector2(0f, 0.5f);
        badge.pivot = new Vector2(0f, 0.5f);
        badge.sizeDelta = new Vector2(104f, 104f);
        badge.anchoredPosition = new Vector2(22f, 0f);
        row.badge = Rounded(badge, new Color(1f, 1f, 1f, 0.14f), 26f);
        row.badge.raycastTarget = false;

        row.code = MakeText("Code", badge, 36f, FontStyles.Bold, Color.white, TextAlignmentOptions.Center);
        Stretch(row.code.rectTransform);
        row.code.rectTransform.offsetMin = new Vector2(6f, 0f);
        row.code.rectTransform.offsetMax = new Vector2(-6f, 0f);
        row.code.enableAutoSizing = true;
        row.code.fontSizeMin = 20f;
        row.code.fontSizeMax = 36f;
        row.code.text = CodeFor(b);

        row.title = MakeText("Name", row.rt, 38f, FontStyles.Bold, textColor, TextAlignmentOptions.BottomLeft);
        Place(row.title.rectTransform, new Vector2(0f, 0.5f), new Vector2(1f, 1f), new Vector2(150f, 2f), new Vector2(-28f, -20f));
        row.title.text = b.name;

        row.sub = MakeText("Estimate", row.rt, 30f, FontStyles.Normal, subtleText, TextAlignmentOptions.TopLeft);
        Place(row.sub.rectTransform, new Vector2(0f, 0f), new Vector2(1f, 0.5f), new Vector2(150f, 20f), new Vector2(-28f, -4f));

        return row;
    }

    void ScrollToTop()
    {
        var scroll = _sheet.GetComponent<ScrollRect>();
        if (scroll == null) return;
        Canvas.ForceUpdateCanvases();
        scroll.verticalNormalizedPosition = 1f;
    }

    // ── UI helpers ────────────────────────────────────────────────────────────

    TextMeshProUGUI MakeText(string name, Transform parent, float size, FontStyles style, Color color, TextAlignmentOptions align)
    {
        var rt = MakeRect(name, parent);
        var t = rt.gameObject.AddComponent<TextMeshProUGUI>();
        t.fontSize      = size;
        t.fontStyle     = style;
        t.color         = color;
        t.alignment     = align;
        t.raycastTarget = false;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode  = TextOverflowModes.Ellipsis;
        return t;
    }

    static Image Rounded(RectTransform rt, Color color, float radius)
    {
        var img = rt.gameObject.AddComponent<Image>();
        img.sprite = RuntimeUISprites.RoundedRect;
        img.type   = Image.Type.Sliced;
        img.pixelsPerUnitMultiplier = 32f / Mathf.Max(radius, 1f); // sprite corner is 32 px
        img.color  = color;
        return img;
    }

    static void BuildCloseGlyph(RectTransform parent, Color bgColor, float barLength)
    {
        var bg = parent.gameObject.AddComponent<Image>();
        bg.sprite = RuntimeUISprites.Circle;
        bg.color  = bgColor;
        for (int i = 0; i < 2; i++)
        {
            RectTransform bar = MakeRect(i == 0 ? "Bar1" : "Bar2", parent);
            bar.anchorMin = bar.anchorMax = bar.pivot = new Vector2(0.5f, 0.5f);
            bar.sizeDelta = new Vector2(barLength, 7f);
            bar.localRotation = Quaternion.Euler(0f, 0f, i == 0 ? 45f : -45f);
            var img = bar.gameObject.AddComponent<Image>();
            img.color = Color.white;
            img.raycastTarget = false;
        }
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

    static void Place(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = offsetMin;
        rt.offsetMax = offsetMax;
    }

    static Color WithAlpha(Color c, float a) { c.a = a; return c; }

    static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);

#if UNITY_EDITOR
    // Fill Destinations with the project's asset when the component is added.
    void Reset()      => AutoAssign();
    void OnValidate() => AutoAssign();

    void AutoAssign()
    {
        if (destinations != null) return;
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:CampusDestinations");
        if (guids.Length == 0) return;
        destinations = UnityEditor.AssetDatabase.LoadAssetAtPath<CampusDestinations>(
            UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
    }
#endif
}
