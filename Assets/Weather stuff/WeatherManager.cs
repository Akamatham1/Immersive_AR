using System.Collections;
using UnityEngine;
using UnityEngine.Networking;
using TMPro;

// Attach this script to your Vuforia ImageTarget or a child of it
public class WeatherManager : MonoBehaviour
{
    [Header("UI Text References (assign in Inspector)")]
    public TextMeshProUGUI temperatureText;
    public TextMeshProUGUI humidityText;
    public TextMeshProUGUI windText;
    public TextMeshProUGUI rainText;
    public TextMeshProUGUI lastUpdatedText;

    [Header("Settings")]
    public float refreshIntervalSeconds = 300f; // refresh every 5 minutes

    // LIT Beaumont, TX coordinates
    private const float LAT = 30.086f;
    private const float LON = -94.102f;

    // Open-Meteo API - free, no key required
    private string BuildApiURL()
    {
        return $"https://api.open-meteo.com/v1/forecast" +
               $"?latitude={LAT}&longitude={LON}" +
               $"&current=temperature_2m,relative_humidity_2m,precipitation," +
               $"wind_speed_10m,wind_direction_10m,weathercode" +
               $"&temperature_unit=fahrenheit" +
               $"&wind_speed_unit=mph" +
               $"&precipitation_unit=inch" +
               $"&timezone=America%2FChicago";
    }

    void Start()
    {
        StartCoroutine(RefreshLoop());
    }

    IEnumerator RefreshLoop()
    {
        while (true)
        {
            yield return StartCoroutine(FetchWeather());
            yield return new WaitForSeconds(refreshIntervalSeconds);
        }
    }

    IEnumerator FetchWeather()
    {
        string url = BuildApiURL();
        Debug.Log("Fetching weather from: " + url);

        using (UnityWebRequest request = UnityWebRequest.Get(url))
        {
            yield return request.SendWebRequest();

            if (request.result == UnityWebRequest.Result.Success)
            {
                string json = request.downloadHandler.text;
                Debug.Log("Weather response: " + json);
                ParseAndDisplay(json);
            }
            else
            {
                Debug.LogError("Weather fetch failed: " + request.error);
                SetAllText("--", "--", "--", "--");
            }
        }
    }

    void ParseAndDisplay(string json)
    {
        WeatherResponse data = JsonUtility.FromJson<WeatherResponse>(json);

        if (data == null || data.current == null)
        {
            Debug.LogError("Failed to parse weather JSON");
            return;
        }

        float temp        = data.current.temperature_2m;
        int humidity      = data.current.relative_humidity_2m;
        float windSpeed   = data.current.wind_speed_10m;
        int windDir       = data.current.wind_direction_10m;
        float precip      = data.current.precipitation;
        int weatherCode   = data.current.weathercode;

        // Display
        if (temperatureText)
            temperatureText.text = $"{temp:F1}°F";

        if (humidityText)
            humidityText.text = $"Humidity: {humidity}%";

        if (windText)
            windText.text = $"Wind: {windSpeed:F1} mph {DegreesToCompass(windDir)}";

        if (rainText)
            rainText.text = $"Rain: {precip:F2} in  |  {GetWeatherDescription(weatherCode)}";

        if (lastUpdatedText)
            lastUpdatedText.text = $"Updated: {System.DateTime.Now:hh:mm tt}";

        Debug.Log($"Weather updated — Temp: {temp}°F, Humidity: {humidity}%, Wind: {windSpeed} mph");
    }

    void SetAllText(string temp, string humidity, string wind, string rain)
    {
        if (temperatureText)  temperatureText.text  = temp;
        if (humidityText)     humidityText.text     = humidity;
        if (windText)         windText.text         = wind;
        if (rainText)         rainText.text         = rain;
    }

    // Convert wind degrees to compass direction
    string DegreesToCompass(int degrees)
    {
        string[] directions = { "N", "NNE", "NE", "ENE", "E", "ESE", "SE", "SSE",
                                 "S", "SSW", "SW", "WSW", "W", "WNW", "NW", "NNW" };
        int index = Mathf.RoundToInt(degrees / 22.5f) % 16;
        return directions[index];
    }

    // WMO weather codes to readable descriptions
    string GetWeatherDescription(int code)
    {
        switch (code)
        {
            case 0:  return "Clear Sky";
            case 1:  return "Mainly Clear";
            case 2:  return "Partly Cloudy";
            case 3:  return "Overcast";
            case 45: return "Foggy";
            case 48: return "Icy Fog";
            case 51: return "Light Drizzle";
            case 53: return "Drizzle";
            case 55: return "Heavy Drizzle";
            case 61: return "Light Rain";
            case 63: return "Rain";
            case 65: return "Heavy Rain";
            case 71: return "Light Snow";
            case 73: return "Snow";
            case 75: return "Heavy Snow";
            case 80: return "Light Showers";
            case 81: return "Showers";
            case 82: return "Heavy Showers";
            case 95: return "Thunderstorm";
            case 96: return "Thunderstorm w/ Hail";
            case 99: return "Thunderstorm w/ Heavy Hail";
            default: return "Unknown";
        }
    }

    // ── JSON Data Classes ──────────────────────────────────────────────────────

    [System.Serializable]
    public class WeatherResponse
    {
        public CurrentWeather current;
    }

    [System.Serializable]
    public class CurrentWeather
    {
        public float temperature_2m;
        public int   relative_humidity_2m;
        public float precipitation;
        public float wind_speed_10m;
        public int   wind_direction_10m;
        public int   weathercode;
    }
}
