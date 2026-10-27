// ─────────────────────────────────────────────────────────────────────────────
// DirectionsController — destination picker + distance/time readout for
// straight-line campus directions.
//
// SETUP
//  1. In the screen-space canvas (CanvasButtons > SafeArea, NOT ButtonUI,
//     which hides when the target is lost), add a TextMeshPro Dropdown
//     ("Destination") and a TextMeshPro text label ("DirectionsLabel").
//  2. Add this component anywhere (e.g. on the dropdown) and assign:
//       - destinations: the CampusDestinations asset ("Load LIT defaults")
//       - calibration:  the same MapCalibration as MapLocationMarker
//       - dropdown / label
//     gps is found automatically.
//     Optional: put the dropdown/label/arrow under one container, assign it
//     to uiRoot and ImageTarget to showWhileActive. The directions UI then
//     stays hidden on the intro screen until AR starts. Keep this component
//     outside uiRoot.
//  3. The dropdown's options are rebuilt at Start: "None" + the buildings.
//  4. DirectionLine and CompassArrow find this component on their own.
//  5. Editor: set a fake lat/lon on GPSLocationProvider to test. Moving it
//     near a building's position shows the "arrived" message.
//
//  CampusNavigator adds and configures this component itself (no dropdown or
//  label needed). Manual setup above is only for a custom UI.
//
// LABEL
//  "TC — ~120 m · ~2 min", where metres = straight-line pixel distance /
//  pixels-per-metre × detourFactor (1.3) and minutes = metres / 1.4 m/s
//  (rounded up, at least 1). Within max(15 m, GPS accuracy) of the building
//  (straight-line, no detour factor) it shows "You've arrived at <name>".
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public class DirectionsController : MonoBehaviour
{
    [Header("Data")]
    public CampusDestinations destinations;
    public MapCalibration     calibration;
    [Tooltip("Found in the scene when empty.")]
    public GPSLocationProvider gps;

    [Header("UI")]
    public TMP_Dropdown dropdown;
    public TMP_Text     label;
    [Tooltip("Optional: container for the directions UI (dropdown, label, arrow). Must not contain this component.")]
    public GameObject   uiRoot;
    [Tooltip("Optional: uiRoot is shown only while this is active, e.g. ImageTarget, which stays off until AR starts.")]
    public GameObject   showWhileActive;

    [Header("Estimates")]
    [Tooltip("Walking paths are longer than a straight line.")]
    public float detourFactor   = 1.3f;
    [Tooltip("Average walking speed, metres per second.")]
    public float walkingSpeed   = 1.4f;
    [Tooltip("Minimum arrival radius in metres (GPS accuracy is used when larger).")]
    public float arrivalRadius  = 15f;
    [Tooltip("Seconds between label refreshes.")]
    public float labelInterval  = 0.25f;

    [Header("Navigation")]
    [Tooltip("Selecting a destination starts navigation. Turn off to start it from a " +
             "Start button (StartNavigation) instead.")]
    public bool startOnSelect = true;

    /// <summary>Raised with the new destination (null = none).</summary>
    public event Action<CampusDestinations.Building> DestinationChanged;

    /// <summary>The selected building, or null.</summary>
    public CampusDestinations.Building Selected { get; private set; }
    public bool HasDestination => Selected != null;

    /// <summary>True while the user is within the arrival radius of the destination.</summary>
    public bool HasArrived { get; private set; }

    /// <summary>
    /// True once navigation to the selected destination has started (on
    /// selection, or on StartNavigation() when startOnSelect is off). False
    /// after arrival, EndNavigation(), ClearDestination() or selecting "None".
    /// </summary>
    public bool IsNavigating { get; private set; }

    /// <summary>Current status line ("TC — ~120 m · ~2 min", "You've arrived at …"), or null with no destination.</summary>
    public string StatusText { get; private set; }

    /// <summary>The destination list (empty when no asset is assigned).</summary>
    public IReadOnlyList<CampusDestinations.Building> Buildings =>
        destinations != null ? destinations.buildings : (IReadOnlyList<CampusDestinations.Building>)Array.Empty<CampusDestinations.Building>();

    float _nextLabel;

    void Start()
    {
        if (gps == null)
            gps = GPSLocationProvider.Instance != null
                ? GPSLocationProvider.Instance
                : FindFirstObjectByType<GPSLocationProvider>();

        if (dropdown != null)
        {
            var options = new List<string> { "None" };
            if (destinations != null)
                foreach (var b in destinations.buildings) options.Add(b.DisplayName);
            dropdown.ClearOptions();
            dropdown.AddOptions(options);
            dropdown.SetValueWithoutNotify(0);
            dropdown.onValueChanged.AddListener(i => Select(i - 1));
        }

        if (destinations == null)
            Debug.LogWarning("[DirectionsController] No CampusDestinations assigned.", this);

        Select(-1);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>Selects building <paramref name="index"/> in the list (-1 = none).</summary>
    public void Select(int index)
    {
        var list = destinations != null ? destinations.buildings : null;
        Selected = list != null && index >= 0 && index < list.Count ? list[index] : null;
        HasArrived = false;
        IsNavigating = Selected != null && startOnSelect;

        if (dropdown != null && dropdown.value != index + 1)
            dropdown.SetValueWithoutNotify(index + 1);

        _nextLabel = 0f; // refresh now
        RefreshLabel();
        DestinationChanged?.Invoke(Selected);
    }

    /// <summary>Selects <paramref name="building"/> (null = none).</summary>
    public void Select(CampusDestinations.Building building) =>
        Select(building != null && destinations != null ? destinations.buildings.IndexOf(building) : -1);

    public void ClearDestination() => Select(-1);

    /// <summary>Starts navigating to the selected destination (e.g. from a Start button).</summary>
    public void StartNavigation()
    {
        if (Selected != null && !HasArrived) IsNavigating = true;
    }

    /// <summary>Ends navigation but keeps the selected destination.</summary>
    public void EndNavigation() => IsNavigating = false;

    /// <summary>Destination in calibration image pixels (x right, y down).</summary>
    public bool TryGetDestinationPixel(out Vector2 pixel) => TryGetBuildingPixel(Selected, out pixel);

    /// <summary>Any building's position in calibration image pixels (x right, y down).</summary>
    public bool TryGetBuildingPixel(CampusDestinations.Building building, out Vector2 pixel)
    {
        pixel = default;
        if (building == null || destinations == null) return false;
        pixel = building.pixel;
        // Destinations and calibration normally share 1000x670; rescale if not.
        if (calibration != null && destinations.imageSize != calibration.imageSize && destinations.imageSize.x > 0)
            pixel = Vector2.Scale(pixel, new Vector2(
                (float)calibration.imageSize.x / destinations.imageSize.x,
                (float)calibration.imageSize.y / Mathf.Max(destinations.imageSize.y, 1)));
        return true;
    }

    /// <summary>
    /// Walking estimate to <paramref name="building"/>: straight-line metres,
    /// rounded walking metres (× detourFactor) and minutes (at least 1).
    /// </summary>
    public bool TryEstimate(CampusDestinations.Building building,
                            out float straightMetres, out int walkMetres, out int minutes)
    {
        straightMetres = 0f; walkMetres = 0; minutes = 0;
        if (!TryGetUserPixel(out Vector2 user) || !TryGetBuildingPixel(building, out Vector2 dest)) return false;
        float ppm = calibration.PixelsPerMetre;
        if (ppm <= 0f) return false;

        straightMetres = Vector2.Distance(user, dest) / ppm;
        float walk = straightMetres * detourFactor;
        walkMetres = RoundDistance(walk);
        minutes    = Mathf.Max(1, Mathf.CeilToInt(walk / Mathf.Max(walkingSpeed, 0.1f) / 60f));
        return true;
    }

    /// <summary>"~120 m · ~2 min" for <paramref name="building"/>, or null when GPS/calibration isn't ready.</summary>
    public string EstimateText(CampusDestinations.Building building) =>
        TryEstimate(building, out _, out int m, out int min) ? $"~{m} m · ~{min} min" : null;

    /// <summary>Arrival radius in metres: arrivalRadius, or the GPS accuracy when that's larger.</summary>
    public float ArrivalRadius => Mathf.Max(arrivalRadius, gps != null ? gps.HorizontalAccuracy : 0f);

    /// <summary>User position in calibration image pixels; false if GPS or calibration isn't ready.</summary>
    public bool TryGetUserPixel(out Vector2 pixel)
    {
        pixel = default;
        if (gps == null || !gps.IsReady || calibration == null || !calibration.IsValid) return false;
        pixel = calibration.GeoToPixel(gps.Latitude, gps.Longitude);
        return !float.IsNaN(pixel.x);
    }

    /// <summary>Straight-line distance in metres from the user to the destination.</summary>
    public bool TryGetStraightDistance(out float metres)
    {
        metres = 0f;
        if (!TryGetUserPixel(out Vector2 user) || !TryGetDestinationPixel(out Vector2 dest)) return false;
        float ppm = calibration.PixelsPerMetre;
        if (ppm <= 0f) return false;
        metres = Vector2.Distance(user, dest) / ppm;
        return true;
    }

    // ── Label ─────────────────────────────────────────────────────────────────

    void Update()
    {
        if (uiRoot != null && showWhileActive != null && uiRoot != gameObject)
        {
            bool show = showWhileActive.activeInHierarchy;
            if (uiRoot.activeSelf != show) uiRoot.SetActive(show);
        }

        if (Time.unscaledTime < _nextLabel) return;
        _nextLabel = Time.unscaledTime + labelInterval;
        RefreshLabel();
    }

    void RefreshLabel()
    {
        if (Selected == null)
        {
            HasArrived = false;
            SetLabel(null);
            return;
        }

        if (calibration == null || !calibration.IsValid)
        {
            SetLabel($"{Selected.Label} — map not calibrated");
            return;
        }

        if (!TryEstimate(Selected, out float straight, out int metres, out int minutes))
        {
            HasArrived = false;
            SetLabel($"{Selected.Label} — {(gps != null ? gps.Status : "no GPS")}");
            return;
        }

        HasArrived = straight <= ArrivalRadius;
        if (HasArrived)
        {
            IsNavigating = false; // stays ended even if GPS drifts back out
            SetLabel($"You've arrived at {Selected.name}");
            return;
        }

        SetLabel($"{Selected.Label} — ~{metres} m · ~{minutes} min");
    }

    /// <summary>Nearest 5 m under 100 m, nearest 10 m above — it's an estimate.</summary>
    static int RoundDistance(float m) =>
        m < 100f ? Mathf.Max(5, Mathf.RoundToInt(m / 5f) * 5) : Mathf.RoundToInt(m / 10f) * 10;

    void SetLabel(string text)
    {
        StatusText = string.IsNullOrEmpty(text) ? null : text;
        if (label == null) return;
        bool show = StatusText != null;
        // Toggle the component, not the GameObject, so this works even if
        // the controller sits on the label itself.
        if (label.enabled != show) label.enabled = show;
        if (show && label.text != text) label.text = text;
    }
}
