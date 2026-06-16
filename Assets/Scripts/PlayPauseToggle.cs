using UnityEngine;
using UnityEngine.UI;

public class PlayPauseToggle : MonoBehaviour
{
    public Sprite playSprite;
    public Sprite pauseSprite;

    private Image iconImage;
    private bool isPaused = false;

    void Awake()
    {
        iconImage = GetComponent<Image>();
        iconImage.sprite = pauseSprite; // default state
    }

    public void TogglePlayPause()
    {
        isPaused = !isPaused;

        Time.timeScale = isPaused ? 0f : 1f;
        iconImage.sprite = isPaused ? playSprite : pauseSprite;
    }
}