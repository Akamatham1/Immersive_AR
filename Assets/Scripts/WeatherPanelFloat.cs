using UnityEngine;

public class WeatherPanelFloat : MonoBehaviour
{
    public float amplitude = 0.004f;
    public float speed = 0.7f;

    Vector3 origin;

    void Awake() => origin = transform.localPosition;

    void Update()
    {
        float y = Mathf.Sin(Time.unscaledTime * speed) * amplitude;
        transform.localPosition = origin + new Vector3(0f, y, 0f);
    }
}
