using UnityEngine;
using UnityEngine.Video;

public class LivePlayer : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public GameObject errorOverlay;

    [SerializeField] float retryDelay = 5f;
    [SerializeField] string videoFileName = "video.mp4";

    string StreamUrl => System.IO.Path.Combine(Application.streamingAssetsPath, videoFileName);

    void Start()
    {
        if (errorOverlay != null) errorOverlay.SetActive(false);

        // Render video directly onto this quad's material — no RenderTexture asset needed.
        videoPlayer.renderMode              = VideoRenderMode.MaterialOverride;
        videoPlayer.targetMaterialRenderer  = GetComponent<Renderer>();
        videoPlayer.targetMaterialProperty  = "_MainTex";
        videoPlayer.playOnAwake             = false;

        BeginStream();
    }

    void BeginStream()
    {
        videoPlayer.prepareCompleted -= OnPrepared;
        videoPlayer.errorReceived    -= OnError;

        videoPlayer.source = VideoSource.Url;
        videoPlayer.url    = StreamUrl;

        videoPlayer.prepareCompleted += OnPrepared;
        videoPlayer.errorReceived    += OnError;

        videoPlayer.Prepare();
    }

    void OnPrepared(VideoPlayer vp)
    {
        if (errorOverlay != null) errorOverlay.SetActive(false);
        // Don't Play() here — Observer.cs calls Play()/Pause() based on tracking state.
    }

    void OnError(VideoPlayer vp, string msg)
    {
        Debug.LogError($"Video error: {msg}");
        if (errorOverlay != null) errorOverlay.SetActive(true);
        Invoke(nameof(Retry), retryDelay);
    }

    void Retry()
    {
        videoPlayer.Stop();
        BeginStream();
    }
}
