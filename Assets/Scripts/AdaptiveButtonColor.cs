using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Reads the actual rendered screen behind each button every 0.5 seconds
/// and smoothly transitions button/text colors for legibility against the
/// AR camera background. Uses WaitForEndOfFrame so Vuforia's camera feed
/// is fully composited before sampling.
/// </summary>
public class AdaptiveButtonColor : MonoBehaviour
{
    [Header("AR Setup")]
    public Camera arCamera;
    public Button[] buttons;

    [Header("Colors")]
    [SerializeField] Color darkButtonColor  = new Color(0.08f, 0.08f, 0.08f, 0.85f);
    [SerializeField] Color lightButtonColor = new Color(0.96f, 0.96f, 0.96f, 0.85f);
    [SerializeField] float transitionSpeed  = 8f;
    [SerializeField] float sampleInterval   = 0.15f;

    Image[]           _images;
    TextMeshProUGUI[] _labels;
    Color[]           _targetButton;
    Color[]           _targetText;

    // Single reusable texture — 1 pixel per button sampled sequentially
    Texture2D _pixel;

    void Start()
    {
        _pixel = new Texture2D(1, 1, TextureFormat.RGB24, mipChain: false);

        int n = buttons.Length;
        _images       = new Image[n];
        _labels       = new TextMeshProUGUI[n];
        _targetButton = new Color[n];
        _targetText   = new Color[n];

        for (int i = 0; i < n; i++)
        {
            _images[i]       = buttons[i].GetComponent<Image>();
            _labels[i]       = buttons[i].GetComponentInChildren<TextMeshProUGUI>();
            _targetButton[i] = lightButtonColor;
            _targetText[i]   = Color.black;
        }

        StartCoroutine(SampleLoop());
    }

    /// <summary>
    /// Coroutine that waits for each frame to fully render (including the
    /// Vuforia camera background), reads the screen pixel behind every button,
    /// then sleeps for sampleInterval before repeating.
    /// </summary>
    public IEnumerator SampleLoop()
    {
        var wait = new WaitForSeconds(sampleInterval);
        while (true)
        {
            yield return wait;
            yield return SampleColors();
        }
    }

    /// <summary>
    /// Waits for end of frame then reads the rendered screen pixel at each
    /// button's center. Calculates perceived brightness and sets target colors.
    /// </summary>
    public IEnumerator SampleColors()
    {
        if (!IsARCameraReady()) yield break;

        // Wait until Vuforia has finished drawing the camera background
        yield return new WaitForEndOfFrame();

        for (int i = 0; i < buttons.Length; i++)
        {
            if (buttons[i] == null || _images[i] == null) continue;

            Vector2 screen = RectTransformUtility.WorldToScreenPoint(
                null, buttons[i].transform.position);

            int px = Mathf.Clamp((int)screen.x, 0, Screen.width  - 1);
            int py = Mathf.Clamp((int)screen.y, 0, Screen.height - 1);

            // ReadPixels reads from the current display buffer
            _pixel.ReadPixels(new Rect(px, py, 1, 1), 0, 0, recalculateMipMaps: false);
            _pixel.Apply(updateMipmaps: false);

            Color c          = _pixel.GetPixel(0, 0);
            float brightness = (c.r * 0.299f) + (c.g * 0.587f) + (c.b * 0.114f);
            bool  isLight    = brightness > 0.5f;

            _targetButton[i] = isLight ? darkButtonColor  : lightButtonColor;
            _targetText[i]   = isLight ? Color.white      : Color.black;
        }
    }

    /// <summary>
    /// Returns true when the AR camera exists, is enabled, and its
    /// GameObject is active in the hierarchy.
    /// </summary>
    public bool IsARCameraReady() =>
        arCamera != null && arCamera.isActiveAndEnabled;

    void Update()
    {
        float t = Time.deltaTime * transitionSpeed;
        for (int i = 0; i < buttons.Length; i++)
        {
            if (_images[i] != null)
                _images[i].color = Color.Lerp(_images[i].color, _targetButton[i], t);
            if (_labels[i] != null)
                _labels[i].color = Color.Lerp(_labels[i].color, _targetText[i], t);
        }
    }

    void OnDestroy()
    {
        StopAllCoroutines();
        if (_pixel != null) Destroy(_pixel);
    }
}
