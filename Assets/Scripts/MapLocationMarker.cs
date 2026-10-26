// ─────────────────────────────────────────────────────────────────────────────
// MapLocationMarker — "you are here" dot, heading arrow and accuracy ring on
// the campus map.
//
// SETUP
//  1. Under MapPanel, create a UI Image "LocationMarker" (about 36 x 36, a
//     round sprite such as UI/Skin "Knob", blue). Add this component to it.
//     Its anchors/pivot are set to centre automatically.
//  2. Children of LocationMarker (optional, leave either one out if not wanted):
//       - "AccuracyRing": UI Image, round sprite, about 100 x 100. It's resized
//         at runtime and its colour comes from `ringColor`. Children draw OVER
//         their parent in uGUI. To put the ring behind the dot, give
//         LocationMarker's Image alpha 0 and add a "Dot" Image child BELOW
//         AccuracyRing in the hierarchy.
//       - "Arrow": UI Image of an arrow pointing UP, offset or pivoted so it
//         sits around the dot. Rotated to the phone's heading.
//     Assign them to `accuracyRing` / `arrow` (auto-found by those names).
//  3. Assign `calibration` (a MapCalibration asset with 3+ points). `gps` and
//     `mapImage` are found automatically: the GPSLocationProvider in the
//     scene (created if missing) and the nearest parent Image (MapPanel).
//  4. MapFullscreenToggle copies this marker onto the fullscreen map
//     automatically.
//
// BEHAVIOUR
//  - Hidden (faded out) while GPS isn't ready, the calibration is invalid, or
//    the position falls outside the image.
//  - Handles Preserve Aspect: maps image pixels onto the letterboxed area the
//    sprite is actually drawn in, flipping image y-down to UI y-up.
//  - Position and heading are smoothed. The ring diameter is
//    2 x horizontalAccuracy, converted to map pixels through the calibration
//    scale, with a gentle pulse.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RectTransform))]
public class MapLocationMarker : MonoBehaviour
{
    [Header("References")]
    public MapCalibration calibration;
    [Tooltip("Found in the scene (or created) when empty.")]
    public GPSLocationProvider gps;
    [Tooltip("The map Image this marker sits on. Defaults to the nearest parent Image.")]
    public Image mapImage;
    public RectTransform arrow;
    public RectTransform accuracyRing;

    [Header("Motion")]
    [Tooltip("Higher = snappier. Exponential smoothing rate per second.")]
    public float positionSmoothing = 6f;
    public float headingSmoothing  = 8f;
    [Tooltip("Pixels (image space) outside the image edge before the marker hides.")]
    public float edgeMargin = 0f;
    public float fadeSpeed  = 6f;

    [Header("Accuracy ring")]
    public Color ringColor = new Color(0.2f, 0.55f, 1f, 0.25f);
    [Tooltip("On-screen diameter limits, in canvas units.")]
    public float minRingDiameter = 40f;
    public float maxRingDiameter = 600f;
    [Range(0f, 0.3f)] public float pulseAmount = 0.08f;
    public float pulseSpeed = 2f;

    [Tooltip("Keep the dot/arrow a constant size when the parent map is scaled (fullscreen zoom). " +
             "Set automatically on the fullscreen copy.")]
    public bool compensateParentScale;

    RectTransform _rt;
    CanvasGroup   _group;
    Image         _ringImage;
    Vector2       _smoothedPos;
    float         _smoothedAngle;
    bool          _hasFix;

    void Awake()
    {
        _rt = (RectTransform)transform;
        _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 0.5f);

        if (arrow == null)        arrow        = transform.Find("Arrow") as RectTransform;
        if (accuracyRing == null) accuracyRing = transform.Find("AccuracyRing") as RectTransform;
        if (accuracyRing != null) _ringImage = accuracyRing.GetComponent<Image>();

        // Taps go through to the map (opens fullscreen); the marker never eats them.
        foreach (var g in GetComponentsInChildren<Graphic>(true))
            g.raycastTarget = false;

        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;
        _group.alpha          = 0f;
    }

    void Start()
    {
        if (mapImage == null && transform.parent != null)
            mapImage = transform.parent.GetComponentInParent<Image>();

        if (gps == null)
            gps = GPSLocationProvider.Instance != null
                ? GPSLocationProvider.Instance
                : FindFirstObjectByType<GPSLocationProvider>();
        if (gps == null)
        {
            Debug.Log("[MapLocationMarker] No GPSLocationProvider in the scene — creating one.");
            gps = new GameObject("GPSLocationProvider").AddComponent<GPSLocationProvider>();
        }

        if (calibration == null)
            Debug.LogWarning("[MapLocationMarker] No MapCalibration assigned — marker stays hidden.", this);
    }

    /// <summary>True while the marker has a position on the map (its smoothed anchoredPosition is current).</summary>
    public bool IsShowing { get; private set; }

    /// <summary>Re-targets the marker onto another map Image (used for the fullscreen copy).</summary>
    public void SetMapImage(Image image, bool keepConstantSize)
    {
        mapImage = image;
        compensateParentScale = keepConstantSize;
        _hasFix = false; // snap on next update instead of sliding across
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        bool visible = TryGetTarget(out Vector2 targetPos, out float pixelsToLocal);

        if (visible)
        {
            float targetAngle = calibration.HeadingToImageAngle(gps.TrueHeading);
            if (!_hasFix)
            {
                _smoothedPos   = targetPos;
                _smoothedAngle = targetAngle;
                _hasFix = true;
            }
            else
            {
                _smoothedPos   = Vector2.Lerp(_smoothedPos, targetPos, 1f - Mathf.Exp(-positionSmoothing * dt));
                _smoothedAngle = Mathf.LerpAngle(_smoothedAngle, targetAngle, 1f - Mathf.Exp(-headingSmoothing * dt));
            }

            _rt.anchoredPosition = _smoothedPos;
            if (arrow != null)
                arrow.localRotation = Quaternion.Euler(0f, 0f, -_smoothedAngle); // UI z+ is counter-clockwise

            UpdateRing(pixelsToLocal);
        }

        float parentScale = ParentScale();
        _rt.localScale = compensateParentScale ? Vector3.one / parentScale : Vector3.one;

        IsShowing = visible;
        _group.alpha = Mathf.MoveTowards(_group.alpha, visible ? 1f : 0f, fadeSpeed * dt);
        if (!visible && _group.alpha <= 0f)
            _hasFix = false; // next appearance snaps into place
    }

    /// <summary>
    /// Target anchoredPosition for the current GPS fix and the image-pixel →
    /// map-local scale, or false if the marker should be hidden.
    /// </summary>
    bool TryGetTarget(out Vector2 anchoredPos, out float pixelsToLocal)
    {
        anchoredPos = default;
        pixelsToLocal = 0f;

        if (gps == null || !gps.IsReady || calibration == null || !calibration.IsValid ||
            mapImage == null || mapImage.sprite == null)
            return false;

        Vector2 px = calibration.GeoToPixel(gps.Latitude, gps.Longitude);
        if (float.IsNaN(px.x) || !calibration.IsInsideImage(px, edgeMargin))
            return false;

        Vector2 local = PixelToMapLocal(mapImage, px, calibration.imageSize, out pixelsToLocal);
        anchoredPos = MapLocalToAnchored(_rt, mapImage, local);
        return true;
    }

    // ── Shared map-space helpers (also used by DirectionLine) ─────────────────

    /// <summary>
    /// Image pixel (x right, y DOWN from top-left of a <paramref name="imageSize"/>
    /// image) → point in the map Image's local space (y up), within the area
    /// the sprite is actually drawn in. <paramref name="pixelsToLocal"/> is the
    /// local units per image pixel.
    /// </summary>
    public static Vector2 PixelToMapLocal(Image map, Vector2 pixel, Vector2 imageSize, out float pixelsToLocal)
    {
        Rect drawn = DisplayedRect(map);
        Vector2 uv = new Vector2(pixel.x / imageSize.x, 1f - pixel.y / imageSize.y); // y-down → y-up
        pixelsToLocal = drawn.width / imageSize.x;
        return new Vector2(drawn.xMin + uv.x * drawn.width, drawn.yMin + uv.y * drawn.height);
    }

    /// <summary>
    /// Point in <paramref name="map"/>'s local space → anchoredPosition for
    /// <paramref name="target"/>, whose anchors must be centred (min = max = 0.5).
    /// </summary>
    public static Vector2 MapLocalToAnchored(RectTransform target, Image map, Vector2 mapLocal)
    {
        RectTransform parent = target.parent as RectTransform;
        RectTransform space  = map.rectTransform;
        if (parent != space)
        {
            // Target isn't a direct child of the map: go through world space.
            Vector3 world = space.TransformPoint(mapLocal);
            mapLocal = parent != null ? (Vector2)parent.InverseTransformPoint(world) : (Vector2)world;
            space = parent;
        }
        // Anchors are centred, so anchoredPosition is relative to the parent rect's centre.
        return space != null ? mapLocal - space.rect.center : mapLocal;
    }

    /// <summary>The part of the Image's rect the sprite is actually drawn in (letterboxed with Preserve Aspect).</summary>
    public static Rect DisplayedRect(Image image)
    {
        Rect r = image.rectTransform.rect;
        if (!image.preserveAspect || image.sprite == null) return r;

        float spriteAspect = image.sprite.rect.width / Mathf.Max(image.sprite.rect.height, 1f);
        float rectAspect   = r.width / Mathf.Max(r.height, 0.0001f);

        if (spriteAspect > rectAspect)
        {
            float h = r.width / spriteAspect;
            return new Rect(r.xMin, r.center.y - h * 0.5f, r.width, h);
        }
        float w = r.height * spriteAspect;
        return new Rect(r.center.x - w * 0.5f, r.yMin, w, r.height);
    }

    void UpdateRing(float pixelsToLocal)
    {
        if (accuracyRing == null) return;

        float accuracy = Mathf.Max(gps.HorizontalAccuracy, 0f);
        float diameterLocal = 2f * calibration.MetresToPixels(accuracy) * pixelsToLocal;

        // Ring is a child of the (possibly counter-scaled) marker; size it in
        // on-screen canvas units so it still grows with the map zoom.
        float s = compensateParentScale ? ParentScale() : 1f;
        float d = Mathf.Clamp(diameterLocal * s, minRingDiameter, maxRingDiameter);
        accuracyRing.sizeDelta = new Vector2(d, d);

        // Gentle breathe: grows slightly and fades a little as it expands.
        float wave  = Mathf.Sin(Time.unscaledTime * pulseSpeed * Mathf.PI) * 0.5f + 0.5f; // 0..1
        float pulse = 1f + pulseAmount * wave;
        accuracyRing.localScale = new Vector3(pulse, pulse, 1f);

        if (_ringImage != null)
        {
            Color c = ringColor;
            c.a *= 1f - 0.3f * wave;
            _ringImage.color = c;
        }
    }

    float ParentScale()
    {
        Transform p = _rt.parent;
        return p != null ? Mathf.Max(Mathf.Abs(p.localScale.x), 0.0001f) : 1f;
    }
}
