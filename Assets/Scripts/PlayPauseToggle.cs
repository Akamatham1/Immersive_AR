using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

public class PlayPauseToggle : MonoBehaviour
{
    public Sprite playSprite;
    public Sprite pauseSprite;
    public VideoPlayer videoPlayer;

    private Image iconImage;
    private bool isPaused = false;

    void Awake()
    {
        iconImage = GetComponent<Image>();
        iconImage.sprite = pauseSprite;
    }

    public void TogglePlayPause()
    {
        isPaused = !isPaused;

        if (videoPlayer != null)
        {
            if (isPaused) videoPlayer.Pause();
            else videoPlayer.Play();
        }

        iconImage.sprite = isPaused ? playSprite : pauseSprite;
    }
}