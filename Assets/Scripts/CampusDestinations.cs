// ─────────────────────────────────────────────────────────────────────────────
// CampusDestinations — the buildings you can get directions to, positioned in
// LITCampusMap image pixels.
//
// SETUP
//  1. Project window: right-click > Create > Immersive AR > Campus Destinations.
//  2. Select the asset, click the ⋮ (context) menu at the top-right of the
//     Inspector and choose "Load LIT defaults" to fill in the 11 LIT
//     buildings. (This replaces the current list. Undo works.)
//  3. Tweak pixel positions if a pin lands off its building. Pixels are x right
//     / y DOWN from the top-left of the 1000 x 670 image, the same convention
//     as MapCalibration's control points.
//  4. Assign the asset to DirectionsController.destinations.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "CampusDestinations", menuName = "Immersive AR/Campus Destinations")]
public class CampusDestinations : ScriptableObject
{
    [Serializable]
    public class Building
    {
        public int     number;
        public string  name;
        [Tooltip("Short code shown in the directions label, e.g. \"TC\". Optional.")]
        public string  shortCode;
        [Tooltip("Building location on the map image: x right, y DOWN from the top-left.")]
        public Vector2 pixel;

        /// <summary>Short code, or the full name when there isn't one.</summary>
        public string Label => string.IsNullOrEmpty(shortCode) ? name : shortCode;

        /// <summary>"2. Technology Center (TC)" style, for pickers.</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(shortCode) ? $"{number}. {name}" : $"{number}. {name} ({shortCode})";
    }

    [Tooltip("Pixel size of the map image the positions refer to.")]
    public Vector2Int imageSize = new Vector2Int(1000, 670);

    public List<Building> buildings = new List<Building>();

    [ContextMenu("Load LIT defaults")]
    void LoadLitDefaults()
    {
#if UNITY_EDITOR
        UnityEditor.Undo.RecordObject(this, "Load LIT defaults");
#endif
        imageSize = new Vector2Int(1000, 670);
        buildings = new List<Building>
        {
            B(1,  "Cecil Beeson",                "CB",  568, 224),
            B(2,  "Technology Center",           "TC",  626, 289),
            B(3,  "Eagles' Nest",                "EN",  488, 149),
            B(4,  "ExxonMobil PATC",             "",    484, 222),
            B(5,  "Process Tech Inside Unit",    "T2",  462, 180),
            B(6,  "Technical Arts IV",           "TA4", 483, 269),
            B(7,  "Technical Arts V",            "TA5", 502, 310),
            B(8,  "Szuch Multi-Purpose Center",  "MPC", 430, 520),
            B(9,  "Tommy Williams ITC",          "",    864, 261),
            B(10, "WAHTC",                       "",    666, 343),
            B(11, "Advanced Technology Center",  "ATC", 830, 144),
        };
#if UNITY_EDITOR
        UnityEditor.EditorUtility.SetDirty(this);
#endif
        Debug.Log($"[CampusDestinations] Loaded {buildings.Count} LIT buildings.", this);
    }

    static Building B(int number, string name, string code, float x, float y) =>
        new Building { number = number, name = name, shortCode = code, pixel = new Vector2(x, y) };
}
