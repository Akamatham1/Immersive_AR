using UnityEngine;
using UnityEditor;
using UnityEngine.UI;
using TMPro;

// Place this file in Assets/Editor/
// Then go to Tools > Create Weather Panel to auto-build the UI
public class WeatherPanelBuilder : EditorWindow
{
    [MenuItem("Tools/Create Weather Panel")]
    public static void BuildWeatherPanel()
    {
        // ── Find or create Canvas ──────────────────────────────────────────
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasGO = new GameObject("Canvas");
            canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();
        }

        // ── Root Panel ────────────────────────────────────────────────────
        GameObject panel = new GameObject("WeatherPanel");
        panel.transform.SetParent(canvas.transform, false);

        RectTransform panelRect = panel.AddComponent<RectTransform>();
        panelRect.sizeDelta = new Vector2(520, 340);
        panelRect.localScale = new Vector3(0.003f, 0.003f, 0.003f); // AR scale

        // Dark glassmorphism background
        Image bg = panel.AddComponent<Image>();
        bg.color = new Color(0.04f, 0.08f, 0.16f, 0.92f);

        // Rounded corners via pixel-perfect sprite (use built-in rounded rect)
        bg.type = Image.Type.Sliced;

        // Add scripts
        panel.AddComponent<WeatherManager>();
        panel.AddComponent<BillboardFaceCamera>();

        // ── Header Bar ───────────────────────────────────────────────────
        GameObject header = CreateChild(panel, "Header");
        RectTransform headerRect = header.GetComponent<RectTransform>();
        headerRect.anchorMin = new Vector2(0, 1);
        headerRect.anchorMax = new Vector2(1, 1);
        headerRect.pivot = new Vector2(0.5f, 1f);
        headerRect.sizeDelta = new Vector2(0, 60);
        headerRect.anchoredPosition = Vector2.zero;

        Image headerBg = header.AddComponent<Image>();
        headerBg.color = new Color(0.09f, 0.45f, 0.95f, 1f); // vivid blue

        // Station label
        TextMeshProUGUI stationLabel = CreateTMP(header, "StationLabel", "📍 LIT BEAUMONT — LIVE WEATHER", 13);
        RectTransform slRect = stationLabel.GetComponent<RectTransform>();
        slRect.anchorMin = Vector2.zero;
        slRect.anchorMax = Vector2.one;
        slRect.offsetMin = new Vector2(16, 0);
        slRect.offsetMax = new Vector2(-16, 0);
        stationLabel.alignment = TextAlignmentOptions.MidlineLeft;
        stationLabel.fontStyle = FontStyles.Bold;
        stationLabel.color = Color.white;

        // ── Temperature (big center) ──────────────────────────────────────
        TextMeshProUGUI tempText = CreateTMP(panel, "TemperatureText", "––°F", 72);
        RectTransform tempRect = tempText.GetComponent<RectTransform>();
        tempRect.anchorMin = new Vector2(0, 0.45f);
        tempRect.anchorMax = new Vector2(0.5f, 0.85f);
        tempRect.offsetMin = new Vector2(20, 0);
        tempRect.offsetMax = new Vector2(0, 0);
        tempText.alignment = TextAlignmentOptions.MidlineLeft;
        tempText.fontStyle = FontStyles.Bold;
        tempText.color = Color.white;
        tempText.enableAutoSizing = true;
        tempText.fontSizeMin = 20;
        tempText.fontSizeMax = 72;

        // ── Forecast label (right of temp) ────────────────────────────────
        TextMeshProUGUI rainText = CreateTMP(panel, "RainText", "–– Conditions", 14);
        RectTransform rainRect = rainText.GetComponent<RectTransform>();
        rainRect.anchorMin = new Vector2(0.5f, 0.55f);
        rainRect.anchorMax = new Vector2(1f, 0.85f);
        rainRect.offsetMin = new Vector2(10, 0);
        rainRect.offsetMax = new Vector2(-16, 0);
        rainText.alignment = TextAlignmentOptions.MidlineLeft;
        rainText.color = new Color(0.6f, 0.85f, 1f, 1f);
        rainText.enableWordWrapping = true;

        // ── Divider ───────────────────────────────────────────────────────
        GameObject divider = CreateChild(panel, "Divider");
        RectTransform divRect = divider.GetComponent<RectTransform>();
        divRect.anchorMin = new Vector2(0.05f, 0.42f);
        divRect.anchorMax = new Vector2(0.95f, 0.42f);
        divRect.sizeDelta = new Vector2(0, 1);
        Image divImg = divider.AddComponent<Image>();
        divImg.color = new Color(1f, 1f, 1f, 0.12f);

        // ── Bottom Row: Wind | Humidity ───────────────────────────────────
        // Wind icon + text
        TextMeshProUGUI windText = CreateTMP(panel, "WindText", "💨  –– mph ––", 15);
        RectTransform windRect = windText.GetComponent<RectTransform>();
        windRect.anchorMin = new Vector2(0f, 0.08f);
        windRect.anchorMax = new Vector2(0.5f, 0.40f);
        windRect.offsetMin = new Vector2(20, 0);
        windRect.offsetMax = new Vector2(0, 0);
        windText.alignment = TextAlignmentOptions.MidlineLeft;
        windText.color = new Color(0.85f, 0.95f, 1f, 1f);

        // Humidity icon + text
        TextMeshProUGUI humidityText = CreateTMP(panel, "HumidityText", "💧  ––%", 15);
        RectTransform humRect = humidityText.GetComponent<RectTransform>();
        humRect.anchorMin = new Vector2(0.5f, 0.08f);
        humRect.anchorMax = new Vector2(1f, 0.40f);
        humRect.offsetMin = new Vector2(10, 0);
        humRect.offsetMax = new Vector2(-16, 0);
        humidityText.alignment = TextAlignmentOptions.MidlineLeft;
        humidityText.color = new Color(0.85f, 0.95f, 1f, 1f);

        // ── Last Updated footer ───────────────────────────────────────────
        TextMeshProUGUI updatedText = CreateTMP(panel, "LastUpdatedText", "Fetching data...", 10);
        RectTransform updRect = updatedText.GetComponent<RectTransform>();
        updRect.anchorMin = new Vector2(0f, 0f);
        updRect.anchorMax = new Vector2(1f, 0.10f);
        updRect.offsetMin = new Vector2(16, 0);
        updRect.offsetMax = new Vector2(-16, 0);
        updatedText.alignment = TextAlignmentOptions.MidlineRight;
        updatedText.color = new Color(1f, 1f, 1f, 0.35f);

        // ── Wire up WeatherManager references ─────────────────────────────
        WeatherManager wm = panel.GetComponent<WeatherManager>();
        wm.temperatureText = tempText;
        wm.humidityText    = humidityText;
        wm.windText        = windText;
        wm.rainText        = rainText;
        wm.lastUpdatedText = updatedText;

        // ── Done ──────────────────────────────────────────────────────────
        Selection.activeGameObject = panel;
        Debug.Log("✅ Weather Panel created! Drag it onto your Vuforia ImageTarget.");
        EditorUtility.DisplayDialog("Done!", 
            "Weather Panel created!\n\nDrag WeatherPanel onto your Vuforia ImageTarget in the Hierarchy.", 
            "OK");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static GameObject CreateChild(GameObject parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent.transform, false);
        go.AddComponent<RectTransform>();
        return go;
    }

    static TextMeshProUGUI CreateTMP(GameObject parent, string name, string defaultText, float fontSize)
    {
        GameObject go = CreateChild(parent, name);
        TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text = defaultText;
        tmp.fontSize = fontSize;
        tmp.color = Color.white;
        tmp.raycastTarget = false;
        return tmp;
    }
}
