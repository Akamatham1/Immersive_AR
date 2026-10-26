// ─────────────────────────────────────────────────────────────────────────────
// GPSLocationProvider — device GPS + compass for the campus map marker.
//
// SETUP
//  1. Add this to any always-active GameObject in ARScene (e.g. an empty
//     "GPS" object). NOT under the ImageTarget: Vuforia toggles that hierarchy
//     with tracking. If none exists, MapLocationMarker creates one at runtime.
//  2. Android: Unity adds ACCESS_FINE_LOCATION to the manifest because this
//     script uses Input.location. Leave Player Settings > Other Settings >
//     "Low Accuracy Location" OFF so GPS can refine the fix.
//  3. Editor testing: tick "Use Fake In Editor" and enter a lat/lon on campus
//     plus a heading. Change them in Play mode to move the marker. The Editor
//     has no GPS or compass, so they're always faked there.
//  4. Show `Status` in a TMP label if you want users to see why the marker is
//     missing. Restart() forces an immediate retry (e.g. from a button).
//
// BEHAVIOUR
//  - Never gives up while enabled. If no fix arrives within `initTimeout`
//    (60s), or the service fails or drops out, it reports "Searching for GPS…
//    (try moving outdoors)" and retries every `retryInterval` (10s) until a
//    fix comes in. If location services are off or permission was denied, it
//    re-checks quietly on the same interval (no repeated permission popups),
//    so turning Location on, or granting it in Settings, picks up on its own.
//  - desiredAccuracy 10m lets Android hand over a quick Wi-Fi/cell fix
//    indoors. The first valid reading is exposed straight away (IsReady =
//    true, with a large HorizontalAccuracy, so the marker shows a big ring)
//    and keeps updating as GPS tightens it.
//  - Each state change is logged once; repeated retries don't spam.
//  - trueHeading only becomes meaningful once location is running (Android
//    needs a fix to correct magnetic → true north).
//  - Uses realtime waits, so it still works while PlayPauseToggle has
//    Time.timeScale at 0.
// ─────────────────────────────────────────────────────────────────────────────

using System.Collections;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class GPSLocationProvider : MonoBehaviour
{
    public enum State { Idle, RequestingPermission, PermissionDenied, Disabled, Initializing, Searching, Running }

    const string SEARCHING_STATUS = "Searching for GPS… (try moving outdoors)";

    [Header("Location service")]
    [Tooltip("10m lets Android use Wi-Fi/cell for a quick first fix; GPS refines it.")]
    public float desiredAccuracyInMeters = 10f;
    public float updateDistanceInMeters  = 1f;
    [Tooltip("Seconds to wait for a fix before stopping and retrying.")]
    public float initTimeout   = 60f;
    [Tooltip("Seconds between retries after a failed/timed-out fix (and between permission/service re-checks).")]
    public float retryInterval = 10f;

    [Header("Editor testing")]
    public bool   useFakeInEditor = true;
    [Tooltip("Enter a spot on campus (e.g. one of your calibration points) to test the marker.")]
    public double fakeLatitude;
    public double fakeLongitude;
    [Range(0f, 360f)] public float fakeHeading = 0f;
    public float  fakeAccuracy    = 5f;

    /// <summary>Latest latitude in degrees (WGS84).</summary>
    public double Latitude { get; private set; }
    /// <summary>Latest longitude in degrees (WGS84).</summary>
    public double Longitude { get; private set; }
    /// <summary>Radius of 68% confidence, metres. Large on a first network fix, shrinks as GPS locks on.</summary>
    public float HorizontalAccuracy { get; private set; }
    /// <summary>Degrees clockwise from true north that the top of the phone points.</summary>
    public float TrueHeading { get; private set; }
    /// <summary>True when TrueHeading is live (compass running and reporting), or faked in the Editor.</summary>
    public bool HasHeading =>
        UsingFake || (Input.compass.enabled && Input.compass.timestamp > 0 && CurrentState == State.Running);
    /// <summary>Realtime (Time.realtimeSinceStartup) of the last position update.</summary>
    public float LastUpdateTime { get; private set; }

    /// <summary>True while a position is available; lat/lon/accuracy are valid.</summary>
    public bool   IsReady      { get; private set; }
    public State  CurrentState { get; private set; } = State.Idle;
    /// <summary>Human-readable status, suitable for a UI label.</summary>
    public string Status       { get; private set; } = "Location not started";

    public static GPSLocationProvider Instance { get; private set; }

    Coroutine _loop;
    double    _lastTimestamp = -1;

    void Awake()
    {
        if (Instance == null) Instance = this;
    }

    void OnEnable()  => Restart();
    void OnDisable() => StopService();

    void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>Stops and immediately re-runs the permission + startup sequence.</summary>
    public void Restart()
    {
        StopService();
        _loop = StartCoroutine(LocationLoop());
    }

    void StopService()
    {
        if (_loop != null) { StopCoroutine(_loop); _loop = null; }
        StopDevice();
        IsReady = false;
        SetState(State.Idle, "Location stopped");
    }

    void StopDevice()
    {
        _lastTimestamp = -1;
        if (!UsingFake && Input.location.status != LocationServiceStatus.Stopped)
            Input.location.Stop();
        Input.compass.enabled = false;
    }

    bool UsingFake => Application.isEditor && useFakeInEditor;

    // ── Lifecycle loop ────────────────────────────────────────────────────────

    IEnumerator LocationLoop()
    {
        if (UsingFake)
        {
            SetState(State.Running, "Using fake Editor location");
            IsReady = true;
            yield break; // Update() feeds the fake values
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        bool askedPermission = false;
#endif

        while (true)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            // ── Permission ──
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                if (!askedPermission)
                {
                    askedPermission = true;
                    SetState(State.RequestingPermission, "Waiting for location permission…");

                    // Callbacks can arrive off the main thread; only set flags there.
                    bool answered = false;
                    var callbacks = new PermissionCallbacks();
                    callbacks.PermissionGranted          += _ => answered = true;
                    callbacks.PermissionDenied           += _ => answered = true;
                    callbacks.PermissionRequestDismissed += _ => answered = true;
                    Permission.RequestUserPermission(Permission.FineLocation, callbacks);

                    while (!answered) yield return null;
                }

                if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
                {
                    // Don't re-prompt (annoying, and Android blocks it after two
                    // denials); just notice if it's granted in Settings later.
                    SetState(State.PermissionDenied,
                        "Location permission denied — enable it in Settings > Apps to see yourself on the map");
                    yield return new WaitForSecondsRealtime(retryInterval);
                    continue;
                }
            }
#endif
            // ── Location services switch ──
            if (!Input.location.isEnabledByUser)
            {
                IsReady = false;
                SetState(State.Disabled, "Location services are off — turn on Location in quick settings");
                yield return new WaitForSecondsRealtime(retryInterval);
                continue;
            }

            // ── Start and wait for the first reading ──
            Input.compass.enabled = true;
            Input.location.Start(desiredAccuracyInMeters, updateDistanceInMeters);

            // After a failure, retries stay in Searching so they don't re-log.
            if (CurrentState != State.Searching)
                SetState(State.Initializing, "Finding your location…");

            float deadline = Time.realtimeSinceStartup + initTimeout;
            while (!TryReadLocation() &&
                   Input.location.status != LocationServiceStatus.Failed &&
                   Time.realtimeSinceStartup < deadline)
                yield return null;

            // ── Track while the service keeps delivering ──
            if (IsReady)
            {
                SetState(State.Running, $"Location active (±{HorizontalAccuracy:F0} m)");
                while (Input.location.status == LocationServiceStatus.Running ||
                       Input.location.status == LocationServiceStatus.Initializing)
                {
                    TryReadLocation();
                    if (Input.compass.enabled)
                        TrueHeading = Input.compass.trueHeading;
                    yield return null;
                }
            }

            // ── No fix, failed, or dropped: back off and retry ──
            IsReady = false;
            StopDevice();
            SetState(State.Searching, SEARCHING_STATUS);
            yield return new WaitForSecondsRealtime(retryInterval);
        }
    }

    /// <summary>Takes the latest reading if it's new. True once any valid reading has arrived.</summary>
    bool TryReadLocation()
    {
        LocationInfo info = Input.location.lastData;
        if (info.timestamp > 0 && info.timestamp != _lastTimestamp)
        {
            _lastTimestamp     = info.timestamp;
            Latitude           = info.latitude;
            Longitude          = info.longitude;
            HorizontalAccuracy = info.horizontalAccuracy;
            LastUpdateTime     = Time.realtimeSinceStartup;
            IsReady            = true;
        }
        return IsReady;
    }

    // ── Editor fake ───────────────────────────────────────────────────────────

    void Update()
    {
        if (!UsingFake) return;
        Latitude           = fakeLatitude;
        Longitude          = fakeLongitude;
        HorizontalAccuracy = fakeAccuracy;
        TrueHeading        = fakeHeading;
        LastUpdateTime     = Time.realtimeSinceStartup;
        IsReady            = true;
    }

    /// <summary>Updates state/status, logging only when the state actually changes.</summary>
    void SetState(State state, string status)
    {
        Status = status;
        if (CurrentState == state) return;
        CurrentState = state;
        Debug.Log($"[GPSLocationProvider] {state}: {status}");
    }
}
