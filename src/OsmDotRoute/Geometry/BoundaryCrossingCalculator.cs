namespace OsmDotRoute.Geometry;

/// <summary>
/// ルート形状と矩形範囲の交点・交点からの距離／所要時間を算出する（REQ-RTE-010〜012、Ver. 1.3.0）。
/// </summary>
/// <remarks>
/// <para>
/// 交点は Shape の線分と矩形辺との厳密な交点を線形補間で求める（最寄り Shape 頂点で代用しない）。
/// 矩形辺は緯線・経線に平行なため、度空間での媒介変数 t は緯度補正コサインを掛けた局所平面上の t と
/// 厳密に一致する（分子・分母が同じ定数倍になるため）。よって t の算出は度空間で行い、
/// 距離のみ <see cref="GeoMath.HaversineMeters"/> で積算する。
/// </para>
/// <para>
/// 交点が複数ある場合は「範囲内側の端点に近い側の交点」を採用する（Q A-4 確定）。
/// 返す距離・所要時間はその交点から範囲外側の端点までのルート上の値であり、
/// 途中で範囲内に戻る区間があってもそれを含む。
/// </para>
/// </remarks>
internal static class BoundaryCrossingCalculator
{
    /// <summary>
    /// 算出済みルートと矩形範囲から交点判定を行う。
    /// </summary>
    /// <param name="route">起点 <paramref name="from"/> から終点 <paramref name="to"/> へのルート</param>
    /// <param name="from">起点 A（利用者が指定した生の座標。スナップ後座標ではない）</param>
    /// <param name="to">終点 B（同上）</param>
    /// <param name="bounds">矩形範囲 R</param>
    public static BoundaryCrossingResult Compute(
        Route route, GeoCoordinate from, GeoCoordinate to, MapBounds bounds)
    {
        if (!IsValidBounds(bounds) || !IsFinite(from) || !IsFinite(to))
        {
            return BoundaryCrossingResult.Status(BoundaryCrossingKind.InvalidParameter);
        }

        var shape = route.Shape.Span;
        if (shape.Length == 0)
        {
            return BoundaryCrossingResult.Status(BoundaryCrossingKind.RouteSearchError);
        }

        // 内外判定は「指定した生の座標」で行う（Q A-6 確定）。
        bool fromInside = bounds.Contains(from);
        bool toInside = bounds.Contains(to);

        if (fromInside && toInside)
        {
            // 矩形は凸なので、全頂点が範囲内なら線分も全て範囲内。頂点判定のみで厳密。
            for (int i = 0; i < shape.Length; i++)
            {
                if (!bounds.Contains(shape[i]))
                {
                    return BoundaryCrossingResult.Status(BoundaryCrossingKind.RouteSearchError);
                }
            }
            return BoundaryCrossingResult.Status(BoundaryCrossingKind.BothInside);
        }

        if (!fromInside && !toInside)
        {
            // 矩形の外側は非凸なので、頂点が全て範囲外でも線分が範囲内を貫く場合がある。線分交差判定が必要。
            for (int i = 0; i < shape.Length - 1; i++)
            {
                if (SegmentIntersectsBounds(shape[i], shape[i + 1], bounds))
                {
                    return BoundaryCrossingResult.Status(BoundaryCrossingKind.RouteSearchError);
                }
            }
            if (shape.Length == 1 && bounds.Contains(shape[0]))
            {
                return BoundaryCrossingResult.Status(BoundaryCrossingKind.RouteSearchError);
            }
            return BoundaryCrossingResult.Status(BoundaryCrossingKind.BothOutside);
        }

        // ここから片側のみ範囲外。範囲内側の端点から Shape を辿り、最初に範囲外へ出る交点を採る。
        bool forward = fromInside;                      // A が範囲内 → Shape を前向きに辿る（範囲外は B）
        var kind = forward ? BoundaryCrossingKind.PointBOutside : BoundaryCrossingKind.PointAOutside;

        int startIndex = forward ? 0 : shape.Length - 1;
        int endIndex = forward ? shape.Length - 1 : 0;
        int step = forward ? 1 : -1;

        // 範囲内と判定した端点のスナップ先が範囲外の場合、端点判定とルート形状が矛盾する。
        if (!bounds.Contains(shape[startIndex]))
        {
            return BoundaryCrossingResult.Status(BoundaryCrossingKind.RouteSearchError);
        }

        int insideIndex = -1;
        double t = 0.0;
        for (int i = startIndex; i != endIndex; i += step)
        {
            var q = shape[i + step];
            if (bounds.Contains(q)) continue;
            t = ExitParameter(shape[i], q, bounds);
            insideIndex = i;
            break;
        }

        if (insideIndex < 0)
        {
            // 範囲外側の端点までルートが範囲内に留まっている（交点なし）→ 端点判定と矛盾。
            return BoundaryCrossingResult.Status(BoundaryCrossingKind.RouteSearchError);
        }

        int outsideIndex = insideIndex + step;
        var crossing = Lerp(shape[insideIndex], shape[outsideIndex], t);

        // 交点 → 範囲外側の端点までのルート上距離（Shape 頂点列の Haversine 積算）。
        double distance = GeoMath.HaversineMeters(crossing, shape[outsideIndex]);
        for (int i = outsideIndex; i != endIndex; i += step)
        {
            distance += GeoMath.HaversineMeters(shape[i], shape[i + step]);
        }

        double duration = DurationToOutsidePoint(route, insideIndex, outsideIndex, t, forward);

        return new BoundaryCrossingResult(kind, crossing, distance, duration);
    }

    /// <summary>
    /// 交点から範囲外側の端点までの所要時間（秒）を <see cref="Route.CumulativeDurationsSec"/> から求める。
    /// 交点が属する線分内は t による線形補間（線分内は速度一定のため厳密）。
    /// </summary>
    private static double DurationToOutsidePoint(
        Route route, int insideIndex, int outsideIndex, double t, bool forward)
    {
        var cumulative = route.CumulativeDurationsSec.Span;
        if (cumulative.Length != route.Shape.Length || cumulative.Length == 0) return 0.0;

        double atInside = cumulative[insideIndex];
        double atCrossing = atInside + t * (cumulative[outsideIndex] - atInside);
        double result = forward ? cumulative[^1] - atCrossing : atCrossing - cumulative[0];
        return result < 0.0 ? 0.0 : result;
    }

    /// <summary>
    /// 範囲内の点 <paramref name="inside"/> から範囲外の点 <paramref name="outside"/> へ向かう線分が
    /// 矩形境界を出る位置の媒介変数 t（[0,1]、<paramref name="inside"/> 側が 0）を返す。
    /// </summary>
    private static double ExitParameter(GeoCoordinate inside, GeoCoordinate outside, MapBounds bounds)
    {
        double t = 1.0;
        if (outside.Latitude < bounds.MinLatitude)
        {
            t = Math.Min(t, (bounds.MinLatitude - inside.Latitude) / (outside.Latitude - inside.Latitude));
        }
        if (outside.Latitude > bounds.MaxLatitude)
        {
            t = Math.Min(t, (bounds.MaxLatitude - inside.Latitude) / (outside.Latitude - inside.Latitude));
        }
        if (outside.Longitude < bounds.MinLongitude)
        {
            t = Math.Min(t, (bounds.MinLongitude - inside.Longitude) / (outside.Longitude - inside.Longitude));
        }
        if (outside.Longitude > bounds.MaxLongitude)
        {
            t = Math.Min(t, (bounds.MaxLongitude - inside.Longitude) / (outside.Longitude - inside.Longitude));
        }
        if (double.IsNaN(t) || t < 0.0) return 0.0;
        return t > 1.0 ? 1.0 : t;
    }

    /// <summary>線分 (<paramref name="a"/>, <paramref name="b"/>) が矩形範囲と交差（接触含む）するかを判定する（Liang-Barsky）。</summary>
    private static bool SegmentIntersectsBounds(GeoCoordinate a, GeoCoordinate b, MapBounds bounds)
    {
        double dLon = b.Longitude - a.Longitude;
        double dLat = b.Latitude - a.Latitude;
        double t0 = 0.0;
        double t1 = 1.0;

        return Clip(-dLon, a.Longitude - bounds.MinLongitude, ref t0, ref t1)
            && Clip(dLon, bounds.MaxLongitude - a.Longitude, ref t0, ref t1)
            && Clip(-dLat, a.Latitude - bounds.MinLatitude, ref t0, ref t1)
            && Clip(dLat, bounds.MaxLatitude - a.Latitude, ref t0, ref t1);

        static bool Clip(double p, double q, ref double t0, ref double t1)
        {
            if (p == 0.0) return q >= 0.0;      // 境界と平行: 範囲内側にあるかどうかだけで決まる
            double r = q / p;
            if (p < 0.0)
            {
                if (r > t1) return false;
                if (r > t0) t0 = r;
            }
            else
            {
                if (r < t0) return false;
                if (r < t1) t1 = r;
            }
            return true;
        }
    }

    /// <summary>線分上の媒介変数 t の位置を度空間で線形補間する。</summary>
    private static GeoCoordinate Lerp(GeoCoordinate a, GeoCoordinate b, double t)
        => new(
            a.Latitude + (b.Latitude - a.Latitude) * t,
            a.Longitude + (b.Longitude - a.Longitude) * t);

    /// <summary>矩形範囲が有効（南北・東西が正順、面積非ゼロ、値が定義域内）かを判定する。</summary>
    public static bool IsValidBounds(MapBounds bounds)
    {
        if (!IsFinite(bounds.SouthWest) || !IsFinite(bounds.NorthEast)) return false;
        if (bounds.MinLatitude >= bounds.MaxLatitude) return false;
        if (bounds.MinLongitude >= bounds.MaxLongitude) return false;
        if (bounds.MinLatitude < -90.0 || bounds.MaxLatitude > 90.0) return false;
        if (bounds.MinLongitude < -180.0 || bounds.MaxLongitude > 180.0) return false;
        return true;
    }

    private static bool IsFinite(GeoCoordinate c)
        => double.IsFinite(c.Latitude) && double.IsFinite(c.Longitude);
}
