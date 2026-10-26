// ─────────────────────────────────────────────────────────────────────────────
// CompassArrow — screen-space arrow that points toward the selected building,
// relative to where the phone is facing.
//
// SETUP
//  1. In the screen-space canvas (CanvasButtons > SafeArea, NOT ButtonUI,
//     which hides when the target is lost), add a UI Image "CompassArrow" with
//     an arrow sprite pointing UP (about 160 x 160). Add this component.
//  2. With "Place At Top Center" on, it anchors itself top-centre, inside the
//     safe area: it adds the notch/status-bar inset itself unless a parent
//     already has a SafeAreaFitter. Turn it off to position it yourself.
//  3. `controller` and `gps` are found automatically.
//  4. Editor: the heading comes from GPSLocationProvider's fake heading.
//     Change it in Play mode and the arrow should swing the other way.
//
// MATH
//  bearing  = compass bearing of the map line user → destination (image
//             direction converted through MapCalibration, so the map's
//             rotation is corrected).
//  rotation = bearing − trueHeading (clockwise), smoothed with LerpAngle.
//  Hidden when there's no destination, the compass or GPS isn't available,
//  or you've arrived.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

[RequireComponent(typeof(RectTransform))]
public class CompassArrow : MonoBehaviour
{
    [Tooltip("Found in the scene when empty.")]
    public DirectionsController controller;
    [Tooltip("Found in the scene when empty.")]
    public GPSLocationProvider gps;

    [Header("Placement")]
    public bool  placeAtTopCenter = true;
    [Tooltip("Distance (canvas units) from the top of the safe area.")]
    public float topOffset = 40f;

    [Header("Behaviour")]
    [Tooltip("Higher = snappier. Exponential smoothing rate per second.")]
    public float smoothing = 6f;
    public float fadeSpeed = 6f;
    public bool  hideWhenArrived = true;

    RectTransform _rt;
    CanvasGroup   _group;
    Canvas        _canvas;
    bool          _parentHandlesSafeArea;
    float         _angle;
    bool          _hasAngle;

    void Awake()
    {
        _rt = (RectTransform)transform;
        _group = GetComponent<CanvasGroup>();
        if (_group == null) _group = gameObject.AddComponent<CanvasGroup>();
        _group.blocksRaycasts = false;
        _group.interactable   = false;
        _group.alpha          = 0f;
    }

    void Start()
    {
        if (controller == null) controller = FindFirstObjectByType<DirectionsController>();
        if (gps == null)
            gps = GPSLocationProvider.Instance != null
                ? GPSLocationProvider.Instance
                : FindFirstObjectByType<GPSLocationProvider>();

        _canvas = GetComponentInParent<Canvas>();
        if (_canvas != null) _canvas = _canvas.rootCanvas;
        _parentHandlesSafeArea = transform.parent != null &&
                                 transform.parent.GetComponentInParent<SafeAreaFitter>() != null;
    }

    void Update()
    {
        float dt = Time.unscaledDeltaTime;
        if (placeAtTopCenter) PlaceTopCenter();

        bool show = TryGetTargetAngle(out float target);
        if (show)
        {
            _angle = _hasAngle ? Mathf.LerpAngle(_angle, target, 1f - Mathf.Exp(-smoothing * dt)) : target;
            _hasAngle = true;
            _rt.localRotation = Quaternion.Euler(0f, 0f, -_angle); // clockwise on screen
        }

        _group.alpha = Mathf.MoveTowards(_group.alpha, show ? 1f : 0f, fadeSpeed * dt);
        if (!show && _group.alpha <= 0f) _hasAngle = false; // snap when it reappears
    }

    /// <summary>Clockwise screen rotation that points at the destination; false when the arrow should hide.</summary>
    bool TryGetTargetAngle(out float angle)
    {
        angle = 0f;
        if (controller == null || gps == null || !gps.HasHeading) return false;
        if (!controller.HasDestination || (hideWhenArrived && controller.HasArrived)) return false;
        if (!controller.TryGetUserPixel(out Vector2 user) ||
            !controller.TryGetDestinationPixel(out Vector2 dest)) return false;

        Vector2 delta = dest - user;
        if (delta.sqrMagnitude < 0.01f) return false;

        float bearing = controller.calibration.ImageDirectionToBearing(delta);
        angle = Mathf.DeltaAngle(0f, bearing - gps.TrueHeading);
        return true;
    }

    void PlaceTopCenter()
    {
        _rt.anchorMin = _rt.anchorMax = new Vector2(0.5f, 1f);
        _rt.pivot = new Vector2(0.5f, 0.5f); // rotate around the centre

        float inset = 0f;
        if (!_parentHandlesSafeArea && _canvas != null && _canvas.scaleFactor > 0f)
            inset = (Screen.height - Screen.safeArea.yMax) / _canvas.scaleFactor;

        _rt.anchoredPosition = new Vector2(0f, -(inset + topOffset + _rt.rect.height * 0.5f));
    }
}
