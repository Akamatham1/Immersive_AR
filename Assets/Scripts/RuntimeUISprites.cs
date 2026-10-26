// ─────────────────────────────────────────────────────────────────────────────
// RuntimeUISprites — small procedurally drawn sprites (circle, rounded
// rectangle, up-arrow) for UI built at runtime. Unity's built-in UI sprites
// ("UI/Skin/Knob" etc.) can only be loaded in the Editor, so CampusNavigator,
// DirectionLine.Create and MapFullscreenToggle draw their own instead.
//
// SETUP
//  None. Sprites are created on first use and cached for the session.
// ─────────────────────────────────────────────────────────────────────────────

using UnityEngine;

public static class RuntimeUISprites
{
    static Sprite _circle, _roundedRect, _arrow;

    /// <summary>Anti-aliased white disc, 128 x 128. Tint with Image.color.</summary>
    public static Sprite Circle
    {
        get
        {
            if (_circle != null) return _circle;
            const int size = 128;
            var tex = NewTexture(size, size, "Circle");
            var px = new Color32[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r));
                px[y * size + x] = White(Mathf.Clamp01(r - d));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _circle = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>
    /// White rounded rectangle with a 32 px corner radius, 9-sliced. Use with
    /// Image.type = Sliced; pixelsPerUnitMultiplier changes the on-screen radius.
    /// </summary>
    public static Sprite RoundedRect
    {
        get
        {
            if (_roundedRect != null) return _roundedRect;
            const int size = 96, radius = 32;
            var tex = NewTexture(size, size, "RoundedRect");
            var px = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                // Distance outside the inner (radius-inset) rectangle.
                float cx = Mathf.Clamp(x + 0.5f, radius, size - radius);
                float cy = Mathf.Clamp(y + 0.5f, radius, size - radius);
                float d  = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(cx, cy));
                px[y * size + x] = White(Mathf.Clamp01(radius - d));
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _roundedRect = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f,
                0, SpriteMeshType.FullRect, new Vector4(radius, radius, radius, radius));
        }
    }

    /// <summary>White navigation arrow pointing UP, 128 x 128 (fallback when no arrow sprite is assigned).</summary>
    public static Sprite Arrow
    {
        get
        {
            if (_arrow != null) return _arrow;
            const int size = 128;
            var tex = NewTexture(size, size, "Arrow");
            var px = new Color32[size * size];
            // Arrowhead: tip (64,120), base corners (16,24)/(112,24), notch (64,48).
            Vector2 tip = new Vector2(64, 120), left = new Vector2(16, 20), right = new Vector2(112, 20), notch = new Vector2(64, 46);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                Vector2 p = new Vector2(x + 0.5f, y + 0.5f);
                bool inside = InTriangle(p, tip, left, notch) || InTriangle(p, tip, notch, right);
                px[y * size + x] = White(inside ? 1f : 0f);
            }
            tex.SetPixels32(px);
            tex.Apply(false, true);
            return _arrow = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    static Texture2D NewTexture(int w, int h, string name) =>
        new Texture2D(w, h, TextureFormat.RGBA32, false)
        {
            name       = "RuntimeUISprites." + name,
            wrapMode   = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
        };

    static Color32 White(float alpha) => new Color32(255, 255, 255, (byte)Mathf.RoundToInt(alpha * 255f));

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
}
