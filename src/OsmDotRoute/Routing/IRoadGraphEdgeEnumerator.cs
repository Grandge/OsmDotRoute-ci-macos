namespace OsmDotRoute.Routing;

/// <summary>
/// 道路グラフのエッジ列挙インターフェース。Itinero <c>EdgeEnumerator</c> 相当の API を提供する。
/// </summary>
internal interface IRoadGraphEdgeEnumerator
{
    /// <summary>次のエッジへ進める。最初の呼び出しで先頭エッジに位置する。</summary>
    /// <returns>位置すべきエッジがある場合 <c>true</c>、無ければ <c>false</c></returns>
    bool MoveNext();

    /// <summary>現在のエッジ ID</summary>
    uint EdgeId { get; }

    /// <summary>現在のエッジの始点頂点 ID</summary>
    uint From { get; }

    /// <summary>現在のエッジの終点頂点 ID</summary>
    uint To { get; }

    /// <summary>
    /// 現在のエッジのプロファイルインデックス。Itinero 系では内部で OSM タグ集合解決に使用、
    /// Native 系では未使用（互換性のため保持、3C で廃止検討）。
    /// </summary>
    ushort EdgeProfileIndex { get; }

    /// <summary>現在のエッジの距離（メートル）</summary>
    float DistanceM { get; }

    /// <summary>
    /// 現在のエッジのデータが反転格納されているかを示すフラグ。
    /// Itinero では同一物理エッジに対し片方向の代表エッジのみ格納し、反対方向アクセス時はフラグで識別する。
    /// </summary>
    bool DataInverted { get; }

    /// <summary>
    /// 現在のエッジの中間シェイプ座標列（端点 <see cref="From"/> / <see cref="To"/> は含まない）。
    /// 直線エッジの場合は空配列。
    /// </summary>
    IReadOnlyList<GeoCoordinate> Shape { get; }
}
