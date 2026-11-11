using System;
using OsmDotRoute;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// エッジの端点 + シェイプ点列から外接矩形 (AABB) を計算する。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.5。仕様書 §4.4 Edge AABB Table のエントリを bake する。
/// </para>
/// <para>
/// ホットパス外の処理（PBF 抽出時 1 回計算してファイルに書出）だが、
/// 都道府県単位で数百万エッジを処理するため <see cref="ReadOnlySpan{T}"/> ベースで
/// ゼロアロケート実装する。
/// </para>
/// </remarks>
internal static class EdgeAabbCalculator
{
    /// <summary>
    /// エッジの端点 (<paramref name="fromVertex"/> / <paramref name="toVertex"/>) と
    /// 中間シェイプ点列 (<paramref name="shape"/>) を含む最小の外接矩形を返す。
    /// </summary>
    public static Aabb Compute(
        GeoCoordinate fromVertex,
        GeoCoordinate toVertex,
        ReadOnlySpan<GeoCoordinate> shape)
    {
        double minLon = fromVertex.Longitude;
        double maxLon = fromVertex.Longitude;
        double minLat = fromVertex.Latitude;
        double maxLat = fromVertex.Latitude;

        Expand(toVertex, ref minLon, ref minLat, ref maxLon, ref maxLat);

        for (int i = 0; i < shape.Length; i++)
            Expand(shape[i], ref minLon, ref minLat, ref maxLon, ref maxLat);

        return new Aabb(minLon, minLat, maxLon, maxLat);
    }

    private static void Expand(
        GeoCoordinate p,
        ref double minLon, ref double minLat,
        ref double maxLon, ref double maxLat)
    {
        if (p.Longitude < minLon) minLon = p.Longitude;
        else if (p.Longitude > maxLon) maxLon = p.Longitude;
        if (p.Latitude < minLat) minLat = p.Latitude;
        else if (p.Latitude > maxLat) maxLat = p.Latitude;
    }
}
