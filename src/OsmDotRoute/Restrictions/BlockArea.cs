namespace OsmDotRoute;

/// <summary>
/// 進入不可エリア（REQ-RST-001〜003）。
/// ポリゴンまたは 1 つ以上のメッシュコードのいずれかで領域を定義する。
/// </summary>
public sealed class BlockArea : RestrictedArea
{
    /// <summary>ポリゴンで定義される進入不可エリアを作成する（REQ-RST-001）。</summary>
    /// <param name="id">制約 ID</param>
    /// <param name="polygon">進入不可領域を表すポリゴン</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> が <c>null</c></exception>
    public BlockArea(RestrictedAreaId id, GeoPolygon polygon, string? tag = null)
        : base(id, tag)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        Polygon = polygon;
        MeshCodes = null;
    }

    /// <summary>単一メッシュコードで定義される進入不可エリアを作成する（REQ-RST-002）。</summary>
    /// <param name="id">制約 ID</param>
    /// <param name="meshCode">進入不可とする単一メッシュコード</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    public BlockArea(RestrictedAreaId id, MeshCode meshCode, string? tag = null)
        : base(id, tag)
    {
        Polygon = null;
        MeshCodes = new[] { meshCode };
    }

    /// <summary>
    /// 複数メッシュコードで定義される進入不可エリアを作成する（REQ-RST-003）。
    /// 異なる階層（8〜10 桁）の混在を許容する。
    /// </summary>
    /// <param name="id">制約 ID</param>
    /// <param name="meshCodes">進入不可とするメッシュコード集合</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <exception cref="ArgumentNullException"><paramref name="meshCodes"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="meshCodes"/> が空</exception>
    public BlockArea(RestrictedAreaId id, IEnumerable<MeshCode> meshCodes, string? tag = null)
        : base(id, tag)
    {
        ArgumentNullException.ThrowIfNull(meshCodes);
        var array = meshCodes.ToArray();
        if (array.Length == 0)
        {
            throw new ArgumentException("メッシュコードは 1 つ以上指定してください。", nameof(meshCodes));
        }
        Polygon = null;
        MeshCodes = array;
    }

    /// <summary>ポリゴン領域。メッシュ指定で作成された場合は <c>null</c>。</summary>
    public GeoPolygon? Polygon { get; }

    /// <summary>
    /// メッシュコード集合。ポリゴン指定で作成された場合は <c>null</c>。
    /// 単一メッシュ指定でも要素数 1 のリストとして保持される（REQ-RST-002/003 共通表現）。
    /// </summary>
    public IReadOnlyList<MeshCode>? MeshCodes { get; }
}
