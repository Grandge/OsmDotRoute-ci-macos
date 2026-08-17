namespace OsmDotRoute;

/// <summary>
/// 読み込み済み RouterDb の統計情報（REQ-MAP-002）。
/// </summary>
public sealed class RouterDbStatistics
{
    /// <summary>
    /// 統計情報を作成する。
    /// </summary>
    /// <param name="vertexCount">頂点数</param>
    /// <param name="edgeCount">辺数</param>
    /// <param name="southWest">経緯度範囲の南西端</param>
    /// <param name="northEast">経緯度範囲の北東端</param>
    public RouterDbStatistics(int vertexCount, int edgeCount, GeoCoordinate southWest, GeoCoordinate northEast)
    {
        VertexCount = vertexCount;
        EdgeCount = edgeCount;
        SouthWest = southWest;
        NorthEast = northEast;
    }

    /// <summary>グラフの頂点数</summary>
    public int VertexCount { get; }

    /// <summary>グラフの辺数</summary>
    public int EdgeCount { get; }

    /// <summary>経緯度範囲の南西端</summary>
    public GeoCoordinate SouthWest { get; }

    /// <summary>経緯度範囲の北東端</summary>
    public GeoCoordinate NorthEast { get; }
}
