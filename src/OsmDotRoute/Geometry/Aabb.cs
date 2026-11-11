namespace OsmDotRoute.Geometry;

/// <summary>
/// 緯度経度の軸並行矩形（Axis-Aligned Bounding Box）。
/// 制約管理（REQ-RST-014）・メッシュコード変換（REQ-RST-017）等で使用する内部値型。
/// </summary>
/// <remarks>
/// <see cref="GeoBounds"/> と構造的に同等だが、用途が異なる（<c>GeoBounds</c> は <see cref="OsmDotRoute.Routing.IRoadGraph"/>
/// の全体範囲、<c>Aabb</c> は制約交差判定・メッシュ矩形）。統合可能性は Phase 2 で再評価する。
/// </remarks>
internal readonly record struct Aabb(GeoCoordinate SouthWest, GeoCoordinate NorthEast)
{
    /// <summary>南西端の緯度</summary>
    public double MinLatitude => SouthWest.Latitude;

    /// <summary>南西端の経度</summary>
    public double MinLongitude => SouthWest.Longitude;

    /// <summary>北東端の緯度</summary>
    public double MaxLatitude => NorthEast.Latitude;

    /// <summary>北東端の経度</summary>
    public double MaxLongitude => NorthEast.Longitude;

    /// <summary>
    /// 別の AABB と交差（共有領域あり、境界接触含む）するかを判定する。
    /// </summary>
    public bool Intersects(Aabb other)
    {
        if (MaxLatitude < other.MinLatitude) return false;
        if (MinLatitude > other.MaxLatitude) return false;
        if (MaxLongitude < other.MinLongitude) return false;
        if (MinLongitude > other.MaxLongitude) return false;
        return true;
    }

    /// <summary>
    /// 指定座標を内部（境界含む）に含むかを判定する。
    /// </summary>
    public bool Contains(GeoCoordinate point)
    {
        return point.Latitude >= MinLatitude
            && point.Latitude <= MaxLatitude
            && point.Longitude >= MinLongitude
            && point.Longitude <= MaxLongitude;
    }

    /// <summary>
    /// 線分（<paramref name="p1"/>-<paramref name="p2"/>）が本 AABB と交差（境界接触・端点包含含む）するかを判定する。
    /// </summary>
    /// <remarks>
    /// Liang-Barsky 法によるパラメトリッククリッピング。経度を x、緯度を y として 2D 平面近似で判定する
    /// （日付変更線跨ぎは Phase 1 スコープ外。要件 §5.1 で日本国内ユースケースに限定）。
    /// </remarks>
    public bool IntersectsSegment(GeoCoordinate p1, GeoCoordinate p2)
    {
        // 端点が内部にあれば即交差
        if (Contains(p1) || Contains(p2)) return true;

        double x1 = p1.Longitude, y1 = p1.Latitude;
        double x2 = p2.Longitude, y2 = p2.Latitude;
        double dx = x2 - x1;
        double dy = y2 - y1;

        double tEnter = 0.0, tLeave = 1.0;

        // 4 辺（左・右・下・上）に対し Liang-Barsky のクリッピングパラメータを更新
        if (!ClipParameter(-dx, x1 - MinLongitude, ref tEnter, ref tLeave)) return false;
        if (!ClipParameter(dx, MaxLongitude - x1, ref tEnter, ref tLeave)) return false;
        if (!ClipParameter(-dy, y1 - MinLatitude, ref tEnter, ref tLeave)) return false;
        if (!ClipParameter(dy, MaxLatitude - y1, ref tEnter, ref tLeave)) return false;

        return tEnter <= tLeave;
    }

    /// <summary>
    /// 本 AABB と別 AABB を両方包含する最小の AABB を返す。
    /// </summary>
    public Aabb Union(Aabb other)
    {
        return new Aabb(
            new GeoCoordinate(
                Math.Min(MinLatitude, other.MinLatitude),
                Math.Min(MinLongitude, other.MinLongitude)),
            new GeoCoordinate(
                Math.Max(MaxLatitude, other.MaxLatitude),
                Math.Max(MaxLongitude, other.MaxLongitude)));
    }

    /// <summary>
    /// 座標列の外接矩形を計算する。座標が 0 件の場合は <see cref="ArgumentException"/>。
    /// </summary>
    public static Aabb FromCoordinates(IEnumerable<GeoCoordinate> coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);
        var minLat = double.PositiveInfinity;
        var minLon = double.PositiveInfinity;
        var maxLat = double.NegativeInfinity;
        var maxLon = double.NegativeInfinity;
        var count = 0;
        foreach (var c in coordinates)
        {
            if (c.Latitude < minLat) minLat = c.Latitude;
            if (c.Latitude > maxLat) maxLat = c.Latitude;
            if (c.Longitude < minLon) minLon = c.Longitude;
            if (c.Longitude > maxLon) maxLon = c.Longitude;
            count++;
        }
        if (count == 0)
        {
            throw new ArgumentException("座標が 1 つも与えられていません。", nameof(coordinates));
        }
        return new Aabb(new GeoCoordinate(minLat, minLon), new GeoCoordinate(maxLat, maxLon));
    }

    private static bool ClipParameter(double p, double q, ref double tEnter, ref double tLeave)
    {
        if (p == 0.0)
        {
            // 線分が辺と平行。q < 0 なら外側、それ以外は判定継続
            return q >= 0.0;
        }
        var t = q / p;
        if (p < 0.0)
        {
            if (t > tLeave) return false;
            if (t > tEnter) tEnter = t;
        }
        else
        {
            if (t < tEnter) return false;
            if (t < tLeave) tLeave = t;
        }
        return true;
    }
}
