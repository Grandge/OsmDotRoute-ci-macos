namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// .odrg 仕様書 §4.4 Edge AABB Table のエントリ表現。
/// </summary>
/// <param name="MinLon">経度最小。</param>
/// <param name="MinLat">緯度最小。</param>
/// <param name="MaxLon">経度最大。</param>
/// <param name="MaxLat">緯度最大。</param>
/// <remarks>
/// <para>
/// 4 double 固定 32 byte レイアウト。<see cref="System.Runtime.InteropServices.LayoutKind.Sequential"/>
/// Pack=1 で .odrg バイナリにそのまま書き出せる。
/// </para>
/// <para>
/// 既存の <c>OsmDotRoute.Geometry.Aabb</c>（GeoCoordinate ネスト）とは別物。
/// あちらはランタイム制約交差判定用、こちらは .odrg I/O 用にレイアウト固定。
/// </para>
/// </remarks>
internal readonly record struct Aabb(double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    /// <summary>指定座標を本 AABB が内包するかを返す（境界を含む）。</summary>
    public bool Contains(double lon, double lat) =>
        lon >= MinLon && lon <= MaxLon && lat >= MinLat && lat <= MaxLat;
}
