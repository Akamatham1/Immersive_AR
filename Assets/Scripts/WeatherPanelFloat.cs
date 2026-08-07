using UnityEngine;

public class WeatherPanelFloat : MonoBehaviour
{
    public float amplitude = 0.03f;
    public float speed = 1.5f;

    Vector3 origin;

    void Awake() => origin = transform.localPosition;

    void Update()
    {
        float y = Mathf.Sin(Time.unscaledTime * speed) * amplitude;
        transform.localPosition = origin + new Vector3(0f, y, 0f);
    }
}
