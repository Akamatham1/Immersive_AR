// ─────────────────────────────────────────────────────────────────────────────
// VideoControlsVisibility — keeps the video controls (ButtonUI) hidden until
// the video actually starts playing, then fades them in. They stay up while
// paused and fade out when the target is lost or a non-looping video ends.
//
// SETUP
//  1. Add this component to ButtonUI's parent (CanvasButtons > SafeArea) or
//     any other always-active object. NOT on ButtonUI itself: Observer and
//     FullscreenVideoToggle deactivate ButtonUI, which would stop this script.
//  2. Assign:
//       - observer:    ImageTarget_Video's Observer
//       - videoPlayer: the VideoPlayer (defaults to observer.videoPlayer)
//       - controls:    ButtonUI (defaults to observer.hideUI)
//     A CanvasGroup is added to ButtonUI if it doesn't have one.
//  3. Leave Observer.hideUI set to ButtonUI: other scripts rely on it
//     (fullscreen toggles, CampusNavigator). Note that Observer deactivates
//     ButtonUI instantly on target lost, so the fade-out is only visible for
//     a video ending.
//
// BEHAVIOUR
//  - Hidden at start (alpha 0, not clickable).
//  - Shown when the video starts playing: videoPlayer.started, plus polling
//    isPlaying (covers resumes after pause and a start that fired before this
//    component subscribed). ButtonUI is re-activated if something else left
//    it switched off (not during fullscreen, which hides it on purpose).
//  - Never hidden while observer.SuppressTrackingReactions is true
//    (fullscreen video). A target loss during fullscreen is applied when
//    fullscreen ends, unless the target was found again meanwhile.
//  - Fades use unscaled time, since PlayPauseToggle sets Time.timeScale = 0.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;
using UnityEngine.Video;

public class VideoControlsVisibility : MonoBehaviour
{
    [Header("References")]
    [Tooltip("Defaults to the observer's videoPlayer.")]
    public VideoPlayer videoPlayer;
    [Tooltip("ButtonUI. Defaults to the observer's hideUI.")]
    public GameObject  controls;
    [Tooltip("ImageTarget_Video's Observer. Found by its videoPlayer when empty.")]
    public Observer    observer;

    [Header("Look")]
    public float fadeDuration = 0.25f;

    [Header("Debug")]
    [Tooltip("Log why the controls are shown/hidden (Console).")]
    public bool debugLog = true;

    CanvasGroup _group;
    bool _visible;
    bool _hideAfterSuppression; // target lost while fullscreen
    bool  _loggedVisible;
    float _nextDiag;

    bool Suppressed => observer != null && observer.SuppressTrackingReactions;

    void Awake()
    {
        if (observer == null)
        {
            foreach (var o in FindObjectsByType<Observer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (videoPlayer == null ? o.videoPlayer != null : o.videoPlayer == videoPlayer) { observer = o; break; }
        }
        if (observer != null)
        {
            if (videoPlayer == null) videoPlayer = observer.videoPlayer;
            if (controls == null && observer.hideUI != null) controls = observer.hideUI;
        }

        if (controls == null)
        {
            Debug.LogWarning("[VideoControlsVisibility] No controls assigned.", this);
            enabled = false;
            return;
        }
        if (controls == gameObject)
            Debug.LogWarning("[VideoControlsVisibility] Put this on ButtonUI's parent, not ButtonUI: " +
                             "it stops running when ButtonUI is deactivated.", this);

        _group = controls.GetComponent<CanvasGroup>();
        if (_group == null) _group = controls.AddComponent<CanvasGroup>();
        Apply(0f);
        Log($"refs: observer={Path(observer)}, videoPlayer={Path(videoPlayer)}, controls={Path(controls)}");
    }

    void OnEnable()
    {
        if (videoPlayer != null)
        {
            videoPlayer.started          += OnVideoStarted;
            videoPlayer.loopPointReached += OnVideoEnded;
        }
        if (observer != null)
        {
            observer.OnTargetLost.AddListener(OnTargetLost);
            observer.OnTargetFound.AddListener(OnTargetFound);
        }
    }

    void OnDisable()
    {
        if (videoPlayer != null)
        {
            videoPlayer.started          -= OnVideoStarted;
            videoPlayer.loopPointReached -= OnVideoEnded;
        }
        if (observer != null)
        {
            observer.OnTargetLost.RemoveListener(OnTargetLost);
            observer.OnTargetFound.RemoveListener(OnTargetFound);
        }
    }

    void Update()
    {
        if (_group == null) return;

        if (!_visible && videoPlayer != null && videoPlayer.isPlaying)
            _visible = true;

        if (_hideAfterSuppression && !Suppressed)
        {
            _hideAfterSuppression = false;
            _visible = false;
        }

        // Something else may have switched ButtonUI off (e.g. an Observer of
        // another target owns it as hideUI). Fullscreen hides it on purpose.
        if (_visible && !controls.activeSelf && !Suppressed)
        {
            Log("controls were inactive — activating");
            controls.SetActive(true);
        }

        if (debugLog && _visible != _loggedVisible)
        {
            _loggedVisible = _visible;
            Log(_visible ? "showing controls" : "hiding controls");
            _nextDiag = Time.unscaledTime + 1f; // report the result shortly after
        }
        if (debugLog && _visible && Time.unscaledTime >= _nextDiag)
        {
            _nextDiag = Time.unscaledTime + 3f;
            var sb = new System.Text.StringBuilder();
            foreach (var s in controls.GetComponentsInChildren<UnityEngine.UI.Selectable>(true))
                sb.Append($" {s.name}(active={s.gameObject.activeInHierarchy}, enabled={s.enabled})");
            var canvas = controls.GetComponentInParent<Canvas>(true);
            Log($"visible: alpha={_group.alpha:0.00} controlsActive={controls.activeInHierarchy} " +
                $"canvas={(canvas != null ? canvas.name + (canvas.enabled ? "" : " (disabled)") : "none")} " +
                $"isPlaying={(videoPlayer != null && videoPlayer.isPlaying)} buttons:{sb}");
        }
        if (debugLog && !_visible && Time.unscaledTime >= _nextDiag)
        {
            _nextDiag = Time.unscaledTime + 2f;
            if (videoPlayer == null) Log("hidden: no VideoPlayer");
            else Log($"hidden: isPlaying={videoPlayer.isPlaying} isPrepared={videoPlayer.isPrepared} " +
                     $"enabled={videoPlayer.isActiveAndEnabled} source={videoPlayer.source} " +
                     $"clip={(videoPlayer.clip != null ? videoPlayer.clip.name : "-")} url={videoPlayer.url} " +
                     $"time={videoPlayer.time:0.0} suppressed={Suppressed} controlsActive={controls.activeInHierarchy}");
        }

        float target = _visible ? 1f : 0f;
        if (!Mathf.Approximately(_group.alpha, target))
        {
            float step = fadeDuration > 0f ? Time.unscaledDeltaTime / fadeDuration : 1f;
            Apply(Mathf.MoveTowards(_group.alpha, target, step));
        }
    }

    void OnVideoStarted(VideoPlayer _) { Log("video started"); _visible = true; }

    void OnVideoEnded(VideoPlayer vp)
    {
        if (!vp.isLooping) Hide();
    }

    void OnTargetFound() { Log("target found"); _hideAfterSuppression = false; }

    void OnTargetLost() { Log("target lost"); Hide(); }

    void Hide()
    {
        if (Suppressed) { _hideAfterSuppression = true; return; }
        _visible = false;
    }

    void Log(string msg)
    {
        if (debugLog) Debug.Log("[VideoControlsVisibility] " + msg, this);
    }

    static string Path(Component c)
    {
        if (c == null) return "none";
        string p = c.name;
        for (var t = c.transform.parent; t != null; t = t.parent) p = t.name + "/" + p;
        return p;
    }

    static string Path(GameObject g) => g != null ? Path(g.transform) : "none";

    void Apply(float alpha)
    {
        _group.alpha = alpha;
        // Clickable only once mostly faded in, so hidden buttons can't be hit.
        _group.interactable = _group.blocksRaycasts = alpha > 0.5f;
    }
}
