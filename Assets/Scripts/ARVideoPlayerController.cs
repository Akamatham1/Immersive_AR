using UnityEngine;
using UnityEngine.Video;
using UnityEngine.UI;
using UnityEngine.XR; // Required for AR

public class ARVideoPlayerController : MonoBehaviour
{
    public VideoPlayer videoPlayer;
    public Button playButton;
    public Button pauseButton;

    // If using a 3D object to display the video, assign it here
    public Renderer targetRenderer; // e.g., a Plane, Quad

    // If you're instantiating the video on a detected plane, you might need this
    //public ARPlaneManager arPlaneManager; 

    void Start()
    {
        if (videoPlayer == null)
        {
            videoPlayer = GetComponent<VideoPlayer>();
        }

        // Setup the target texture for the video player (if using Render Texture)
        // If you're rendering directly to a Material on a 3D object, this part might be different
        // based on your setup.
        if (videoPlayer.renderMode == VideoRenderMode.RenderTexture && videoPlayer.targetTexture == null)
        {
            // You'll need to create and assign a Render Texture in the Inspector.
            // For example: videoPlayer.targetTexture = new RenderTexture(1920, 1080, 24);
            Debug.LogWarning("VideoPlayer is set to Render Texture mode but no target texture is assigned.");
        }

        if (playButton != null)
        {
            playButton.onClick.AddListener(PlayVideo);
        }
        if (pauseButton != null)
        {
            pauseButton.onClick.AddListener(PauseVideo);
        }

        // Optional: Example of placing the video on a detected plane
        // If (arPlaneManager != null)
        // {
        //     arPlaneManager.planesChanged += OnPlanesChanged;
        // }
    }

    public void PlayVideo()
    {
        if (videoPlayer != null && !videoPlayer.isPlaying)
        {
            // Ensure the video is prepared before playing
            if (!videoPlayer.isPrepared)
            {
                videoPlayer.Prepare();
                videoPlayer.prepareCompleted += (vp) => {
                    videoPlayer.Play();
                    Debug.Log("Video prepared and playing.");
                };
            }
            else
            {
                videoPlayer.Play();
                Debug.Log("Video is playing.");
            }
        }
    }

    public void PauseVideo()
    {
        if (videoPlayer != null && videoPlayer.isPlaying)
        {
            videoPlayer.Pause();
            Debug.Log("Video is paused.");
        }
    }

    // Example for dynamically placing the video on a detected plane
    // void OnPlanesChanged(ARPlanesChangedEventArgs args)
    // {
    //     if (args.added.Count > 0)
    //     {
    //         ARPlane firstPlane = args.added[0];
    //         GameObject videoDisplay = GameObject.CreatePrimitive(PrimitiveType.Plane);
    //         videoDisplay.transform.parent = firstPlane.transform;
    //         videoDisplay.transform.localPosition = Vector3.zero;
    //         videoDisplay.transform.localRotation = Quaternion.identity;
    //         videoDisplay.transform.localScale = new Vector3(firstPlane.size.x, 1, firstPlane.size.y);
    //         // Assign Render Texture to the material of this videoDisplay
    //         Material videoMaterial = new Material(Shader.Find("Standard"));
    //         videoMaterial.mainTexture = videoPlayer.targetTexture;
    //         videoDisplay.GetComponent<Renderer>().material = videoMaterial;
    //         // You might want to disable further plane detection after placing the video
    //         // arPlaneManager.enabled = false;
    //     }
    // }
}
