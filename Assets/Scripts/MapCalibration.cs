// ─────────────────────────────────────────────────────────────────────────────
// MapCalibration — ties pixels on the stylized campus map to real GPS
// coordinates so a live location can be drawn on it.
//
// SETUP
//  1. Project window: right-click > Create > Immersive AR > Map Calibration.
//     Name it e.g. "LITCampusMapCalibration".
//  2. Leave Image Size at 1000 x 670 (LITCampusMap's pixel size).
//  3. Add control points: recognizable spots spread across the map (building
//     corners, path junctions, the ends of the campus). For each one:
//       - Pixel: x right / y DOWN from the image's top-left corner, in image
//         pixels (read them off in any image editor, e.g. Paint's status bar).
//       - Latitude / Longitude: the same spot from Google Maps (right-click >
//         copy coordinates), or measured on site with GPSLocationProvider.
//     3 points give an exact fit; use 4–8 points spread out (not in a line)
//     for a least-squares fit that averages out the map's stylization.
//  4. Watch the Console. Each edit logs every point's residual error in pixels
//     and flags outliers, so a mistyped coordinate stands out.
//  5. Assign the asset to MapLocationMarker.calibration.
//
// NOTES
//  - The fit is a full 2D affine transform (translation, rotation, uneven
//    scale, shear), solved in a local east/north metre frame centred on the
//    control points. That is still affine in (lon, lat), but it's numerically
//    stable and gives metres-per-pixel directly.
//  - Because the map is not to scale, expect a few to a few dozen pixels of
//    residual. A point far worse than the rest is usually a typo or a swapped
//    lat/lon.
// ─────────────────────────────────────────────────────────────────────────────

using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

[CreateAssetMenu(fileName = "MapCalibration", menuName = "Immersive AR/Map Calibration")]
public class MapCalibration : ScriptableObject
{
    [Serializable]
    public class ControlPoint
    {
        [Tooltip("Optional name, used in Console logs.")]
        public string label;
        [Tooltip("Pixel on the source image: x right, y DOWN from the top-left corner.")]
        public Vector2 pixel;
        public double latitude;
        public double longitude;

        [NonSerialized] public float residual; // pixels, filled by the solver
    }

    [Tooltip("Pixel size of the map image the control points refer to.")]
    public Vector2Int imageSize = new Vector2Int(1000, 670);

    public List<ControlPoint> points = new List<ControlPoint>();

    [Tooltip("Points whose residual exceeds this many times the average get flagged in the Console.")]
    public float outlierFactor = 2f;

    // Metres per degree (WGS84 approximation; plenty for a campus).
    const double M_PER_DEG_LAT = 110574.0;
    const double M_PER_DEG_LON_EQUATOR = 111320.0;

    // px = A·(east, north, 1), py = B·(east, north, 1)  (image pixels, y down)
    double[] _ax, _ay;
    double   _lat0, _lon0, _mPerDegLon;
    bool     _solved;
    bool     _dirty = true;
    float    _averageError;

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>True when 3+ non-collinear control points produced a transform.</summary>
    public bool IsValid { get { EnsureSolved(); return _solved; } }

    /// <summary>Mean distance (pixels) between each control point and where the fit puts it.</summary>
    public float AverageError { get { EnsureSolved(); return _averageError; } }

    /// <summary>
    /// GPS → image pixel (x right, y DOWN from the top-left), not clamped.
    /// Returns (NaN, NaN) when the calibration isn't valid.
    /// </summary>
    public Vector2 GeoToPixel(double latitude, double longitude)
    {
        EnsureSolved();
        if (!_solved) return new Vector2(float.NaN, float.NaN);
        ToLocalMetres(latitude, longitude, out double e, out double n);
        return new Vector2(
            (float)(_ax[0] * e + _ax[1] * n + _ax[2]),
            (float)(_ay[0] * e + _ay[1] * n + _ay[2]));
    }

    /// <summary>Whether a pixel lies inside the image.</summary>
    public bool IsInsideImage(Vector2 pixel, float margin = 0f) =>
        pixel.x >= -margin && pixel.y >= -margin &&
        pixel.x <= imageSize.x + margin && pixel.y <= imageSize.y + margin;

    /// <summary>
    /// Clockwise angle (degrees) from the image's "up" to true north as drawn
    /// on the map. 0 = north-up map; 90 = north points right.
    /// </summary>
    public float RotationDegrees
    {
        get
        {
            EnsureSolved();
            if (!_solved) return 0f;
            return DirectionAngle(0.0, 1.0); // one metre north
        }
    }

    /// <summary>
    /// Converts a compass heading (degrees clockwise from true north) to a
    /// clockwise angle from the image's up axis. Unlike adding
    /// <see cref="RotationDegrees"/>, this also accounts for shear/uneven scale.
    /// </summary>
    public float HeadingToImageAngle(float trueHeading)
    {
        EnsureSolved();
        if (!_solved) return trueHeading;
        double rad = trueHeading * Mathf.Deg2Rad;
        return DirectionAngle(Math.Sin(rad), Math.Cos(rad));
    }

    /// <summary>
    /// Inverse of <see cref="HeadingToImageAngle"/>: a direction on the image
    /// (pixel delta, x right / y DOWN) → compass bearing in degrees clockwise
    /// from true north, 0..360. For a plain rotated map this equals
    /// (clockwise angle from image-up) − <see cref="RotationDegrees"/>.
    /// </summary>
    public float ImageDirectionToBearing(Vector2 pixelDelta)
    {
        EnsureSolved();
        double dx = pixelDelta.x, dy = pixelDelta.y;
        if (!_solved)
            return Mathf.Repeat((float)(Math.Atan2(dx, -dy) * 180.0 / Math.PI), 360f); // assume north-up

        // Invert the linear part [ax0 ax1; ay0 ay1] · (east, north) = (dx, dy).
        double det = _ax[0] * _ay[1] - _ax[1] * _ay[0];
        double east  = ( _ay[1] * dx - _ax[1] * dy) / det;
        double north = (-_ay[0] * dx + _ax[0] * dy) / det;
        return Mathf.Repeat((float)(Math.Atan2(east, north) * 180.0 / Math.PI), 360f);
    }

    /// <summary>Average image pixels per metre (geometric mean of the fit's axis scales).</summary>
    public float PixelsPerMetre
    {
        get
        {
            EnsureSolved();
            if (!_solved) return 0f;
            double det = _ax[0] * _ay[1] - _ax[1] * _ay[0];
            return (float)Math.Sqrt(Math.Abs(det));
        }
    }

    public float MetresToPixels(float metres) => metres * PixelsPerMetre;

    /// <summary>Residual error (pixels) of control point <paramref name="index"/>; NaN if unsolved.</summary>
    public float GetResidual(int index)
    {
        EnsureSolved();
        return _solved && index >= 0 && index < points.Count ? points[index].residual : float.NaN;
    }

    /// <summary>Re-solves on next use. Call after changing points from code.</summary>
    public void MarkDirty() => _dirty = true;

    // ── Unity callbacks ───────────────────────────────────────────────────────

    void OnEnable() => _dirty = true;

    void OnValidate()
    {
        _dirty = true;
        EnsureSolved();
        LogReport();
    }

    // ── Solver ────────────────────────────────────────────────────────────────

    void EnsureSolved()
    {
        if (!_dirty) return;
        _dirty = false;
        _solved = false;
        _averageError = float.NaN;

        int n = points != null ? points.Count : 0;
        if (n < 3) return;

        // Local frame centred on the control points.
        _lat0 = 0; _lon0 = 0;
        foreach (var p in points) { _lat0 += p.latitude; _lon0 += p.longitude; }
        _lat0 /= n; _lon0 /= n;
        _mPerDegLon = M_PER_DEG_LON_EQUATOR * Math.Cos(_lat0 * Math.PI / 180.0);

        // Normal equations (MᵀM)·a = Mᵀb with rows M = [east, north, 1].
        // With exactly 3 points this is the exact solve.
        var mtm = new double[3, 3];
        var mtx = new double[3];
        var mty = new double[3];
        foreach (var p in points)
        {
            ToLocalMetres(p.latitude, p.longitude, out double e, out double nn);
            double[] row = { e, nn, 1.0 };
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++) mtm[i, j] += row[i] * row[j];
                mtx[i] += row[i] * p.pixel.x;
                mty[i] += row[i] * p.pixel.y;
            }
        }

        _ax = Solve3(mtm, mtx);
        _ay = Solve3(mtm, mty);
        if (_ax == null || _ay == null) return; // collinear / duplicate points

        _solved = true;

        double sum = 0;
        foreach (var p in points)
        {
            Vector2 fit = GeoToPixel(p.latitude, p.longitude);
            p.residual = Vector2.Distance(fit, p.pixel);
            sum += p.residual;
        }
        _averageError = (float)(sum / n);
    }

    void ToLocalMetres(double lat, double lon, out double east, out double north)
    {
        east  = (lon - _lon0) * _mPerDegLon;
        north = (lat - _lat0) * M_PER_DEG_LAT;
    }

    /// <summary>Clockwise angle from image-up of a ground direction given in east/north metres.</summary>
    float DirectionAngle(double east, double north)
    {
        double dx = _ax[0] * east + _ax[1] * north; // pixels, x right
        double dy = _ay[0] * east + _ay[1] * north; // pixels, y down
        // Image-up is -y; clockwise angle from up = atan2(right, up).
        return (float)(Math.Atan2(dx, -dy) * 180.0 / Math.PI);
    }

    /// <summary>Gaussian elimination with partial pivoting; null when singular.</summary>
    static double[] Solve3(double[,] a, double[] b)
    {
        var m = new double[3, 4];
        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++) m[i, j] = a[i, j];
            m[i, 3] = b[i];
        }

        // Scale-aware singularity threshold.
        double maxAbs = 0;
        foreach (double v in a) maxAbs = Math.Max(maxAbs, Math.Abs(v));
        double eps = Math.Max(maxAbs, 1.0) * 1e-12;

        for (int col = 0; col < 3; col++)
        {
            int pivot = col;
            for (int r = col + 1; r < 3; r++)
                if (Math.Abs(m[r, col]) > Math.Abs(m[pivot, col])) pivot = r;
            if (Math.Abs(m[pivot, col]) < eps) return null;

            if (pivot != col)
                for (int c = 0; c < 4; c++) (m[col, c], m[pivot, c]) = (m[pivot, c], m[col, c]);

            for (int r = 0; r < 3; r++)
            {
                if (r == col) continue;
                double f = m[r, col] / m[col, col];
                for (int c = col; c < 4; c++) m[r, c] -= f * m[col, c];
            }
        }

        return new[] { m[0, 3] / m[0, 0], m[1, 3] / m[1, 1], m[2, 3] / m[2, 2] };
    }

    // ── Diagnostics ───────────────────────────────────────────────────────────

    void LogReport()
    {
        int n = points != null ? points.Count : 0;
        if (n < 3)
        {
            Debug.Log($"[MapCalibration] {name}: {n} control point(s) — need at least 3.", this);
            return;
        }
        if (!_solved)
        {
            Debug.LogWarning($"[MapCalibration] {name}: points are collinear or duplicated — can't solve. " +
                             "Spread them across the map.", this);
            return;
        }

        var sb = new StringBuilder();
        sb.Append($"[MapCalibration] {name}: {n} points, {(n == 3 ? "exact" : "least-squares")} fit, " +
                  $"avg error {_averageError:F1}px, rotation {RotationDegrees:F1}°, " +
                  $"{PixelsPerMetre:F3} px/m");
        bool anyOutlier = false;
        for (int i = 0; i < n; i++)
        {
            var p = points[i];
            bool outlier = n > 3 && p.residual > outlierFactor * Mathf.Max(_averageError, 1f);
            anyOutlier |= outlier;
            string label = string.IsNullOrEmpty(p.label) ? $"#{i}" : $"#{i} {p.label}";
            sb.Append($"\n  {label}: {p.residual:F1}px{(outlier ? "   <-- check this point" : "")}");
        }

        if (anyOutlier) Debug.LogWarning(sb.ToString(), this);
        else            Debug.Log(sb.ToString(), this);
    }
}
