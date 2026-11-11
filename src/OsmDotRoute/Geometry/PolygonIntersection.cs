namespace OsmDotRoute.Geometry;

/// <summary>
/// 多角形（<see cref="GeoPolygon"/>）と点・線分の交差判定ユーティリティ（REQ-RST-013）。
/// </summary>
/// <remarks>
/// 経度を x、緯度を y として 2D 平面近似で判定する（日本国内ユースケース前提、要件 §5.1）。
/// Hole は包含・線分交差の両方で「リング外として扱う」よう除外する。
/// </remarks>
internal static class PolygonIntersection
{
    /// <summary>
    /// 点が多角形の内部（外周内 かつ Hole 外）にあるかを判定する。境界線上は内部扱い。
    /// </summary>
    public static bool Contains(GeoPolygon polygon, GeoCoordinate point)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        if (!RingContains(polygon.OuterBoundary, point)) return false;
        foreach (var hole in polygon.Holes)
        {
            // Hole の境界線上は「Hole 内」とみなし、ポリゴン内には含めない
            if (RingContains(hole, point)) return false;
        }
        return true;
    }

    /// <summary>
    /// 線分（<paramref name="p1"/>-<paramref name="p2"/>）が多角形と交差するかを判定する。
    /// 端点が内部にある／辺と交差する／線分全体が Hole 内に収まらず実領域を横切るケースを真とする。
    /// </summary>
    public static bool IntersectsSegment(GeoPolygon polygon, GeoCoordinate p1, GeoCoordinate p2)
    {
        ArgumentNullException.ThrowIfNull(polygon);

        // 1) 端点が実領域（外周内 かつ Hole 外）にあれば交差
        if (Contains(polygon, p1) || Contains(polygon, p2)) return true;

        // 2) 外周の任意の辺と交差すれば交差（Hole の辺は「実領域の境界」のため交差扱い）
        if (SegmentIntersectsRing(polygon.OuterBoundary, p1, p2)) return true;
        foreach (var hole in polygon.Holes)
        {
            if (SegmentIntersectsRing(hole, p1, p2)) return true;
        }
        return false;
    }

    /// <summary>
    /// 多角形の外接矩形（Hole は無視、外周のみから算出）を返す。
    /// </summary>
    public static Aabb ComputeBoundingBox(GeoPolygon polygon)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        return Aabb.FromCoordinates(polygon.OuterBoundary);
    }

    /// <summary>
    /// Ray Casting によるリング内外判定。境界線上は内側扱い。
    /// </summary>
    private static bool RingContains(IReadOnlyList<GeoCoordinate> ring, GeoCoordinate point)
    {
        var n = ring.Count;
        if (n < 3) return false;

        double px = point.Longitude;
        double py = point.Latitude;
        var inside = false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            double xi = ring[i].Longitude, yi = ring[i].Latitude;
            double xj = ring[j].Longitude, yj = ring[j].Latitude;

            // 境界線上判定
            if (IsPointOnSegment(px, py, xi, yi, xj, yj)) return true;

            var intersect = ((yi > py) != (yj > py))
                && (px < (xj - xi) * (py - yi) / (yj - yi) + xi);
            if (intersect) inside = !inside;
        }
        return inside;
    }

    private static bool SegmentIntersectsRing(IReadOnlyList<GeoCoordinate> ring, GeoCoordinate p1, GeoCoordinate p2)
    {
        var n = ring.Count;
        if (n < 2) return false;
        for (int i = 0, j = n - 1; i < n; j = i++)
        {
            if (SegmentsIntersect(p1, p2, ring[j], ring[i])) return true;
        }
        return false;
    }

    /// <summary>
    /// 2 線分の交差判定（共線・端点共有も真として扱う）。
    /// </summary>
    private static bool SegmentsIntersect(GeoCoordinate a, GeoCoordinate b, GeoCoordinate c, GeoCoordinate d)
    {
        double ax = a.Longitude, ay = a.Latitude;
        double bx = b.Longitude, by = b.Latitude;
        double cx = c.Longitude, cy = c.Latitude;
        double dx = d.Longitude, dy = d.Latitude;

        var d1 = Orient(cx, cy, dx, dy, ax, ay);
        var d2 = Orient(cx, cy, dx, dy, bx, by);
        var d3 = Orient(ax, ay, bx, by, cx, cy);
        var d4 = Orient(ax, ay, bx, by, dx, dy);

        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0))
            && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
        {
            return true;
        }

        // 共線・端点接触
        if (d1 == 0 && IsPointOnSegment(ax, ay, cx, cy, dx, dy)) return true;
        if (d2 == 0 && IsPointOnSegment(bx, by, cx, cy, dx, dy)) return true;
        if (d3 == 0 && IsPointOnSegment(cx, cy, ax, ay, bx, by)) return true;
        if (d4 == 0 && IsPointOnSegment(dx, dy, ax, ay, bx, by)) return true;

        return false;
    }

    private static double Orient(double ax, double ay, double bx, double by, double cx, double cy)
    {
        // (b-a) × (c-a) の z 成分。正=反時計回り、負=時計回り、0=共線
        return (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
    }

    private static bool IsPointOnSegment(double px, double py, double ax, double ay, double bx, double by)
    {
        // 端点 a-b の線分上に (px,py) があるか（共線かつ AABB 内）
        var cross = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
        if (cross != 0.0) return false;
        if (px < Math.Min(ax, bx) || px > Math.Max(ax, bx)) return false;
        if (py < Math.Min(ay, by) || py > Math.Max(ay, by)) return false;
        return true;
    }
}
