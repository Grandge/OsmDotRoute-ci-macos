namespace OsmDotRoute.Routing;

/// <summary>
/// エッジ ID で直接取得できるエッジ情報（<see cref="IRoadGraph.GetEdge"/> の返却型）。
/// 経路探索でスナップ点（<see cref="IRoadSnapper"/> 経由）からエッジ両端点を解決する用途、
/// および <see cref="RouteBuilder"/> での経路復元時にシェイプを取り出す用途で使用する。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="From"/> / <see cref="To"/> はストレージ上の正規方向の端点 ID。
/// </para>
/// <para>
/// <see cref="DataInverted"/> が <c>false</c> の場合、ストレージ方向 = OSM デジタイズ方向。
/// 真の場合は逆。Phase 1 では Itinero の挙動として、エッジ ID 直接取得時は基本的に <c>false</c> となる。
/// </para>
/// </remarks>
internal sealed class RoadEdge
{
    public RoadEdge(
        uint edgeId,
        uint from,
        uint to,
        ushort edgeProfileIndex,
        float distanceM,
        bool dataInverted,
        IReadOnlyList<GeoCoordinate> shape)
    {
        ArgumentNullException.ThrowIfNull(shape);
        EdgeId = edgeId;
        From = from;
        To = to;
        EdgeProfileIndex = edgeProfileIndex;
        DistanceM = distanceM;
        DataInverted = dataInverted;
        Shape = shape;
    }

    public uint EdgeId { get; }
    public uint From { get; }
    public uint To { get; }
    public ushort EdgeProfileIndex { get; }
    public float DistanceM { get; }
    public bool DataInverted { get; }

    /// <summary>端点 (<see cref="From"/> / <see cref="To"/>) を含まない中間シェイプ。ストレージ順。</summary>
    public IReadOnlyList<GeoCoordinate> Shape { get; }
}
