// ─────────────────────────────────────────────────────────────────────────────
// DirectionLine — straight line on the campus map from "you are here" to the
// selected building, plus a pin on the building.
//
// SETUP
//  1. Under MapPanel, add a UI Image "DirectionLine" (no sprite needed: a
//     solid bar. For dashes, use a dash sprite with Image Type = Tiled).
//     Add this component to it. Its anchors/pivot are set at runtime.
//  2. Add a UI Image "DestinationPin" under MapPanel too (e.g. a round or
//     teardrop sprite, about 30 x 30). For a teardrop, set its pivot to the
//     tip (0.5, 0). Assign it to `pin`.
//  3. Sibling order under MapPanel (top to bottom = back to front):
//         DirectionLine, DestinationPin, LocationMarker
//     so the line sits over the map but under the pin and the GPS dot.
//  4. `controller`, `marker` and `mapImage` are found automatically
//     (DirectionsController in the scene, a MapLocationMarker sibling, and the
//     parent Image).
//
// BEHAVIOUR
//  - Uses the same pixel → local conversion as MapLocationMarker (Preserve
//    Aspect letterboxing, image y-down → UI y-up). The line starts at the
//    marker's smoothed position so it stays attached to the dot.
//  - If you're off the map, the line starts where it crosses the map's edge.
//  - Line hidden when there's no destination or GPS isn't ready. The pin shows
//    whenever a destination is selected.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class DirectionLine : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Found in the scene when empty.")]
    public DirectionsController controller;
    [Tooltip("Optional: start the line at this marker's smoothed position. Auto-found among siblings.")]
    public MapLocationMarker marker;
    [Tooltip("Map Image the line is drawn on. Defaults to the nearest parent Image.")]
    public Image mapImage;
    public RectTransform pin;

    [Header("Look")]
    public float thickness = 6f;
    public Color color     = new Color(1f, 0.55f, 0.1f, 0.95f);
    [Tooltip("Gap (map-local units) left between the GPS dot and the line start.")]
    public float startGap  = 16f;
    [Tooltip("Gap (map-local units) left between the line end and the pin.")]
    public float endGap    = 6f;
    public float fadeSpeed = 6f;
    [Tooltip("Keep line thickness and pin size constant when the parent map is scaled (fullscreen zoom).")]
    public bool  compensateParentScale;

    RectTransform _rt;
    Image         _image;
    CanvasGroup   _lineGroup, _pinGroup;

    void Awake()
    {
        // On the map itself it would recolour, re-pivot and fade out the map.
        if (GetComponentInChildren<MapLocationMarker>(true) != null)
        {
            Debug.LogError("[DirectionLine] This component is on the map Image (it has the MapLocationMarker " +
                           "under it). Remove it here and add it to a separate child Image of MapPanel instead.", this);
            enabled = false;
            return;
        }

        _rt = (RectTransform)transform;
        _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);
        _rt.pivot = new Vector2(0f, 0.5f); // stretches out from the start point

        _image = GetComponent<Image>();
        if (_image != null) { _image.raycastTarget = false; _image.color = color; }

        _lineGroup = EnsureGroup(gameObject);
        if (pin != null)
        {
            pin.anchorMin = pin.anchorMax = new Vector2(0.5f, 0.5f);
            foreach (var g in pin.GetComponentsInChildren<Graphic>(true)) g.raycastTarget = false;
            _pinGroup = EnsureGroup(pin.gameObject);
        }
    }

    void Start()
    {
        if (_rt == null) return; // disabled in Awake
        if (mapImage == null && transform.parent != null)
            mapImage = transform.parent.GetComponentInParent<Image>();
        if (controller == null)
            controller = FindFirstObjectByType<DirectionsController>();
        if (marker == null && transform.parent != null)
            marker = transform.parent.GetComponentInChildren<MapLocationMarker>(true);
    }

    static CanvasGroup EnsureGroup(GameObject go)
    {
        var g = go.GetComponent<CanvasGroup>();
        if (g == null) g = go.AddComponent<CanvasGroup>();
        g.blocksRaycasts = false;
        g.interactable   = false;
        g.alpha          = 0f;
        return g;
    }

    // LateUpdate: runs after MapLocationMarker.Update has moved the dot.
    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        bool showPin = false, showLine = false;
        float inv = 1f / ParentScale(); // 1 unless compensating for zoom

        if (controller != null && mapImage != null && mapImage.sprite != null &&
            controller.calibration != null &&
            controller.TryGetDestinationPixel(out Vector2 destPx))
        {
            Vector2 imageSize = controller.calibration.imageSize;
            Vector2 destLocal = MapLocationMarker.PixelToMapLocal(mapImage, destPx, imageSize, out _);

            if (pin != null)
            {
                pin.anchoredPosition = MapLocationMarker.MapLocalToAnchored(pin, mapImage, destLocal);
                pin.localScale = Vector3.one * inv;
                showPin = true;
            }

            if (TryGetStartLocal(imageSize, out Vector2 startLocal, out bool fromMarker))
                showLine = LayoutLine(startLocal, destLocal, (fromMarker ? startGap : 0f) * inv, inv);
        }

        Fade(_lineGroup, showLine, dt);
        if (_pinGroup != null) Fade(_pinGroup, showPin, dt);
        if (_image != null && _image.color != color) _image.color = color;
    }

    /// <summary>Line start in map-local space, clipped to the map's drawn area.</summary>
    bool TryGetStartLocal(Vector2 imageSize, out Vector2 startLocal, out bool fromMarker)
    {
        startLocal = default;
        fromMarker = false;
        if (!controller.TryGetUserPixel(out Vector2 userPx)) return false;

        // Prefer the dot's smoothed position so the line stays glued to it.
        if (marker != null && marker.IsShowing && marker.transform.parent == mapImage.transform)
        {
            var mrt = (RectTransform)marker.transform;
            startLocal = mrt.anchoredPosition + mapImage.rectTransform.rect.center;
            fromMarker = true;
            return true;
        }

        startLocal = MapLocationMarker.PixelToMapLocal(mapImage, userPx, imageSize, out _);
        // Off the map: start where the line enters the drawn image.
        if (!controller.calibration.IsInsideImage(userPx))
        {
            Vector2 dest = MapLocationMarker.PixelToMapLocal(mapImage,
                controller.TryGetDestinationPixel(out Vector2 d) ? d : userPx, imageSize, out _);
            if (!ClipStartToRect(MapLocationMarker.DisplayedRect(mapImage), ref startLocal, dest))
                return false;
        }
        return true;
    }

    /// <summary>Positions/stretches/rotates this rect from start to end. False if too short to draw.</summary>
    bool LayoutLine(Vector2 startLocal, Vector2 endLocal, float gapAtStart, float scale)
    {
        Vector2 delta = endLocal - startLocal;
        float   full  = delta.magnitude;
        float   len   = full - gapAtStart - endGap * scale;
        if (len <= 1f * scale) return false;

        Vector2 dir   = delta / full;
        Vector2 from  = startLocal + dir * gapAtStart;

        _rt.anchoredPosition = MapLocationMarker.MapLocalToAnchored(_rt, mapImage, from);
        _rt.sizeDelta        = new Vector2(len, thickness * scale);
        _rt.localRotation    = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        return true;
    }

    /// <summary>
    /// Moves <paramref name="start"/> (outside <paramref name="r"/>) to where
    /// the segment start→end enters the rect (Liang–Barsky). False if it never does.
    /// </summary>
    static bool ClipStartToRect(Rect r, ref Vector2 start, Vector2 end)
    {
        Vector2 d = end - start;
        float t0 = 0f, t1 = 1f;
        float[] p = { -d.x, d.x, -d.y, d.y };
        float[] q = { start.x - r.xMin, r.xMax - start.x, start.y - r.yMin, r.yMax - start.y };
        for (int i = 0; i < 4; i++)
        {
            if (Mathf.Approximately(p[i], 0f)) { if (q[i] < 0f) return false; continue; }
            float t = q[i] / p[i];
            if (p[i] < 0f) t0 = Mathf.Max(t0, t);
            else           t1 = Mathf.Min(t1, t);
            if (t0 > t1) return false;
        }
        start += d * t0;
        return true;
    }

    void Fade(CanvasGroup g, bool show, float dt) =>
        g.alpha = Mathf.MoveTowards(g.alpha, show ? 1f : 0f, fadeSpeed * dt);

    float ParentScale()
    {
        if (!compensateParentScale || _rt.parent == null) return 1f;
        return Mathf.Max(Mathf.Abs(_rt.parent.localScale.x), 0.0001f);
    }

    // ── Runtime construction (used by CampusNavigator / MapFullscreenToggle) ──

    /// <summary>
    /// Builds a line and a destination pin on <paramref name="map"/>, placed just
    /// before <paramref name="marker"/> in sibling order (line under pin under
    /// GPS dot). Sizes are in the map's local units.
    /// </summary>
    public static DirectionLine Create(Image map, MapLocationMarker marker, DirectionsController controller,
                                       Color color, float thickness, float pinSize, bool constantSize)
    {
        Transform parent = map.transform;
        int layer = map.gameObject.layer;

        // Pin: coloured disc with a white centre.
        var pinGo = new GameObject("DestinationPin", typeof(RectTransform));
        pinGo.layer = layer;
        var pinRt = (RectTransform)pinGo.transform;
        pinRt.SetParent(parent, false);
        pinRt.anchorMin = pinRt.anchorMax = pinRt.pivot = new Vector2(0.5f, 0.5f);
        pinRt.sizeDelta = Vector2.one * pinSize;
        var pinImg = pinGo.AddComponent<Image>();
        pinImg.sprite = RuntimeUISprites.Circle;
        pinImg.color  = color;
        pinImg.raycastTarget = false;

        var dotGo = new GameObject("Centre", typeof(RectTransform));
        dotGo.layer = layer;
        var dotRt = (RectTransform)dotGo.transform;
        dotRt.SetParent(pinRt, false);
        dotRt.anchorMin = dotRt.anchorMax = dotRt.pivot = new Vector2(0.5f, 0.5f);
        dotRt.sizeDelta = Vector2.one * pinSize * 0.4f;
        var dotImg = dotGo.AddComponent<Image>();
        dotImg.sprite = RuntimeUISprites.Circle;
        dotImg.color  = Color.white;
        dotImg.raycastTarget = false;

        // Line: created inactive so Awake sees the fields set below.
        var lineGo = new GameObject("DirectionLine", typeof(RectTransform));
        lineGo.layer = layer;
        lineGo.SetActive(false);
        lineGo.transform.SetParent(parent, false);
        var lineImg = lineGo.AddComponent<Image>();
        lineImg.raycastTarget = false;

        var line = lineGo.AddComponent<DirectionLine>();
        line.mapImage   = map;
        line.marker     = marker;
        line.controller = controller;
        line.pin        = pinRt;
        line.color      = color;
        line.thickness  = thickness;
        line.startGap   = pinSize * 0.6f;
        line.endGap     = pinSize * 0.5f;
        line.compensateParentScale = constantSize;

        if (marker != null && marker.transform.parent == parent)
        {
            int i = marker.transform.GetSiblingIndex();
            lineGo.transform.SetSiblingIndex(i);     // marker moves to i+1
            pinRt.SetSiblingIndex(i + 1);            // marker moves to i+2
        }

        lineGo.SetActive(true);
        return line;
    }
}
