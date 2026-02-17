using UnityEngine;

using UnityEngine;
using UnityEngine.Video;

public class LivePlayer : MonoBehaviour
{
    public VideoPlayer videoPlayer;

    void Start()
    {
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = "https://wmse-us-ea1.wetmet.net/live/202-10-01/chunks.m3u8?nimblesessionid=29345816&wmsAuthSign=c2VydmVyX3RpbWU9Mi8xNi8yMDI2IDg6Mjc6MzYgUE0maGFzaF92YWx1ZT1xSDNKSUhST1BERHBBc2VGdHJwMklRPT0mdmFsaWRtaW51dGVzPTMw";
        videoPlayer.renderMode = VideoRenderMode.RenderTexture;
        videoPlayer.Prepare();

        videoPlayer.prepareCompleted += (vp) =>
        {
            vp.Play();
        };

        videoPlayer.errorReceived += (vp, msg) =>
        {
            Debug.LogError("Video Error: " + msg);
        };
    }
}
