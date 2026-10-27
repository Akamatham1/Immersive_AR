// ─────────────────────────────────────────────────────────────────────────────
// FlipBrochureHintBuilder — Tools > Create Flip Brochure Hint.
//
// SETUP
//  1. Open ARScene and run Tools > Create Flip Brochure Hint.
//  2. It builds, in the open scene:
//       HintCanvas  (Screen Space - Overlay, sort order 120, CanvasScaler
//                    1080x1920 match 0.5, FlipBrochureHint)
//       └ SafeArea  (SafeAreaFitter)
//         └ HintCard  (bottom-centre, 160 px up, CanvasGroup, hidden)
//            ├ Icon        (64x64 flip arrows, Assets/Sprites/FlipBrochureIcon.png)
//            ├ Label       (TMP, white, 34 pt, wraps)
//            └ CloseButton (44x44, "×")
//     and wires FlipBrochureHint to ImageTarget_Video / ImageTarget_Map's
//     Observers and the VideoPlayer (looked up by name, then by content).
//  3. Check the FlipBrochureHint references in the Inspector, then save the
//     scene. Running the menu again replaces the existing HintCanvas.
//
//  The flip icon is drawn once and saved as a PNG so it's included in builds;
//  delete Assets/Sprites/FlipBrochureIcon.png to regenerate it.
// ─────────────────────────────────────────────────────────────────────────────

using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public static class FlipBrochureHintBuilder
{
    const string IconPath = "Assets/Sprites/FlipBrochureIcon.png";
    static readonly Color CardColor = new Color32(0x1F, 0x38, 0x64, 235); // #1F3864 @ 92%

    [MenuItem("Tools/Create Flip Brochure Hint")]
    public static void Build()
    {
        var existing = GameObject.Find("HintCanvas");
        if (existing != null)
        {
            if (!EditorUtility.DisplayDialog("Flip Brochure Hint",
                    "HintCanvas already exists in the scene. Replace it?", "Replace", "Cancel"))
                return;
            Undo.DestroyObjectImmediate(existing);
        }

        // ── Canvas ────────────────────────────────────────────────────────────
        var canvasGo = new GameObject("HintCanvas", typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(canvasGo, "Create Flip Brochure Hint");
        canvasGo.layer = LayerMask.NameToLayer("UI");

        var canvas = canvasGo.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 120; // above MapFullscreenToggle (100) and CampusNavigator (110)

        var scaler = canvasGo.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.screenMatchMode     = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight  = 0.5f;
        canvasGo.AddComponent<GraphicRaycaster>();

        var safe = NewRect("SafeArea", canvasGo.transform);
        safe.anchorMin = Vector2.zero;
        safe.anchorMax = Vector2.one;
        safe.offsetMin = safe.offsetMax = Vector2.zero;
        safe.gameObject.AddComponent<SafeAreaFitter>();

        // ── Card ──────────────────────────────────────────────────────────────
        var card = NewRect("HintCard", safe);
        card.anchorMin = card.anchorMax = new Vector2(0.5f, 0f);
        card.pivot = new Vector2(0.5f, 0f);
        card.anchoredPosition = new Vector2(0f, 160f);
        card.sizeDelta = new Vector2(900f, 120f); // width is re-capped at runtime

        var bg = card.gameObject.AddComponent<Image>();
        bg.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
        bg.type   = Image.Type.Sliced;
        bg.color  = CardColor;

        var group = card.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = group.blocksRaycasts = false;

        var layout = card.gameObject.AddComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(24, 24, 24, 24);
        layout.spacing = 20f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = layout.childControlHeight = true;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var fitter = card.gameObject.AddComponent<ContentSizeFitter>();
        fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
        fitter.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;

        // Icon
        var icon = NewRect("Icon", card);
        var iconImg = icon.gameObject.AddComponent<Image>();
        iconImg.sprite = LoadOrCreateIcon();
        iconImg.color  = Color.white;
        iconImg.preserveAspect = true;
        iconImg.raycastTarget  = false;
        Fixed(icon, 64f);

        // Label
        var labelRt = NewRect("Label", card);
        var label = labelRt.gameObject.AddComponent<TextMeshProUGUI>();
        label.text = "Flip the brochure to explore the AR campus map";
        label.fontSize = 34f;
        label.color = Color.white;
        label.textWrappingMode = TextWrappingModes.Normal;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
        labelRt.gameObject.AddComponent<LayoutElement>().flexibleWidth = 1f;

        // Close button: transparent 44x44 hit area with a "×" glyph.
        var close = NewRect("CloseButton", card);
        var hit = close.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        var button = close.gameObject.AddComponent<Button>();
        button.targetGraphic = hit;
        button.transition = Selectable.Transition.None;
        Fixed(close, 44f);

        var glyphRt = NewRect("Glyph", close);
        glyphRt.anchorMin = Vector2.zero;
        glyphRt.anchorMax = Vector2.one;
        glyphRt.offsetMin = glyphRt.offsetMax = Vector2.zero;
        var glyph = glyphRt.gameObject.AddComponent<TextMeshProUGUI>();
        glyph.text = "×";
        glyph.fontSize = 40f;
        glyph.color = new Color(1f, 1f, 1f, 0.75f);
        glyph.alignment = TextAlignmentOptions.Center;
        glyph.raycastTarget = false;

        // ── Controller ────────────────────────────────────────────────────────
        var hint = canvasGo.AddComponent<FlipBrochureHint>();
        hint.card        = group;
        hint.icon        = icon;
        hint.label       = label;
        hint.closeButton = button;
        FlipBrochureHint.FindObservers(out hint.videoObserver, out hint.mapObserver);
        if (hint.videoObserver != null) hint.videoPlayer = hint.videoObserver.videoPlayer;

        EnsureEventSystem();
        EditorSceneManager.MarkSceneDirty(canvasGo.scene);
        Selection.activeGameObject = canvasGo;

        string Name(Object o) => o != null ? o.name : "NOT FOUND";
        Debug.Log($"[FlipBrochureHint] Built HintCanvas. Video observer: {Name(hint.videoObserver)}, " +
                  $"map observer: {Name(hint.mapObserver)}, video player: {Name(hint.videoPlayer)}. " +
                  "Check the references, then save the scene.", hint);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static RectTransform NewRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = parent.gameObject.layer;
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        return rt;
    }

    static void Fixed(RectTransform rt, float size)
    {
        var le = rt.gameObject.AddComponent<LayoutElement>();
        le.minWidth  = le.preferredWidth  = size;
        le.minHeight = le.preferredHeight = size;
        le.flexibleWidth = le.flexibleHeight = 0f;
    }

    static void EnsureEventSystem()
    {
        if (Object.FindFirstObjectByType<EventSystem>(FindObjectsInactive.Include) != null) return;
        var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
        Undo.RegisterCreatedObjectUndo(es, "Create EventSystem");
    }

    // ── Flip icon ─────────────────────────────────────────────────────────────

    static Sprite LoadOrCreateIcon()
    {
        var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        if (sprite != null) return sprite;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IconPath));
            var tex = DrawFlipIcon(128);
            File.WriteAllBytes(IconPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(IconPath);

            var importer = (TextureImporter)AssetImporter.GetAtPath(IconPath);
            importer.textureType         = TextureImporterType.Sprite;
            importer.spriteImportMode    = SpriteImportMode.Single;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled       = false;
            importer.SaveAndReimport();
            sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[FlipBrochureHint] Couldn't create the flip icon, using Knob: " + e.Message);
        }
        return sprite != null ? sprite : AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
    }

    /// <summary>Two curved arrows chasing each other around a circle (counter-clockwise), white on transparent.</summary>
    static Texture2D DrawFlipIcon(int size)
    {
        const int ss = 4; // supersampling per axis
        float c = size * 0.5f, radius = size * 0.34f, half = size * 0.055f;
        float head = size * 0.16f, headHalfWidth = size * 0.13f;

        // Each arc runs counter-clockwise from start to end (degrees), arrowhead at the end.
        Vector2[] arcs = { new Vector2(20f, 150f), new Vector2(200f, 330f) };

        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        var px = new Color32[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            int hits = 0;
            for (int sy = 0; sy < ss; sy++)
            for (int sx = 0; sx < ss; sx++)
            {
                var p = new Vector2(x + (sx + 0.5f) / ss, y + (sy + 0.5f) / ss) - new Vector2(c, c);
                if (InIcon(p, arcs, radius, half, head, headHalfWidth)) hits++;
            }
            px[y * size + x] = new Color32(255, 255, 255, (byte)(255 * hits / (ss * ss)));
        }
        tex.SetPixels32(px);
        tex.Apply();
        return tex;
    }

    static bool InIcon(Vector2 p, Vector2[] arcs, float radius, float half, float head, float headHalfWidth)
    {
        float r = p.magnitude;
        float a = Mathf.Atan2(p.y, p.x) * Mathf.Rad2Deg;
        if (a < 0f) a += 360f;

        foreach (var arc in arcs)
        {
            // Ring segment (stops short of the end so the arrowhead tip is sharp).
            if (Mathf.Abs(r - radius) <= half && a >= arc.x && a <= arc.y - 4f) return true;

            // Arrowhead: triangle at the arc end pointing along the CCW tangent.
            float e = arc.y * Mathf.Deg2Rad;
            var end     = new Vector2(Mathf.Cos(e), Mathf.Sin(e)) * radius;
            var tangent = new Vector2(-Mathf.Sin(e), Mathf.Cos(e));
            var normal  = new Vector2(Mathf.Cos(e), Mathf.Sin(e));
            var tip   = end + tangent * head;
            var baseA = end + normal * headHalfWidth - tangent * head * 0.15f;
            var baseB = end - normal * headHalfWidth - tangent * head * 0.15f;
            if (InTriangle(p, tip, baseA, baseB)) return true;
        }
        return false;
    }

    static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
    {
        float d1 = Cross(p, a, b), d2 = Cross(p, b, c), d3 = Cross(p, c, a);
        bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
        return !(neg && pos);
    }

    static float Cross(Vector2 p, Vector2 a, Vector2 b) => (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
}
