namespace OsmDotRoute;

/// <summary>
/// 難所エリア（REQ-RST-004〜007）。
/// 「客観的事実（道路状況種別）」のみを保持し、速度低下係数・通行可否は <see cref="VehicleProfile"/> 側で規定される。
/// 組込みタイプは <see cref="DifficultyTypes"/> を参照。ユーザー定義タイプも使用可（REQ-PRF-013）。
/// </summary>
public sealed class DifficultyArea : RestrictedArea
{
    /// <summary>ポリゴンで定義される難所エリアを作成する（REQ-RST-004）。</summary>
    /// <param name="id">制約 ID</param>
    /// <param name="polygon">難所領域を表すポリゴン</param>
    /// <param name="difficultyType">難所タイプ文字列（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <exception cref="ArgumentNullException"><paramref name="polygon"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    public DifficultyArea(RestrictedAreaId id, GeoPolygon polygon, string difficultyType, string? tag = null)
        : base(id, tag)
    {
        ArgumentNullException.ThrowIfNull(polygon);
        ValidateDifficultyType(difficultyType);
        Polygon = polygon;
        MeshCodes = null;
        DifficultyType = difficultyType;
    }

    /// <summary>単一メッシュコードで定義される難所エリアを作成する（REQ-RST-005）。</summary>
    /// <param name="id">制約 ID</param>
    /// <param name="meshCode">難所領域とする単一メッシュコード</param>
    /// <param name="difficultyType">難所タイプ文字列（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <exception cref="ArgumentException"><paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    public DifficultyArea(RestrictedAreaId id, MeshCode meshCode, string difficultyType, string? tag = null)
        : base(id, tag)
    {
        ValidateDifficultyType(difficultyType);
        Polygon = null;
        MeshCodes = new[] { meshCode };
        DifficultyType = difficultyType;
    }

    /// <summary>
    /// 複数メッシュコードで定義される難所エリアを作成する（REQ-RST-006）。
    /// 異なる階層（8〜10 桁）の混在を許容する。
    /// </summary>
    /// <param name="id">制約 ID</param>
    /// <param name="meshCodes">難所領域とするメッシュコード集合</param>
    /// <param name="difficultyType">難所タイプ文字列（<see cref="DifficultyTypes"/> 参照）</param>
    /// <param name="tag">一括削除用の任意タグ（REQ-RST-010）</param>
    /// <exception cref="ArgumentNullException"><paramref name="meshCodes"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="meshCodes"/> が空、または <paramref name="difficultyType"/> が空文字/null（REQ-RST-007）</exception>
    public DifficultyArea(RestrictedAreaId id, IEnumerable<MeshCode> meshCodes, string difficultyType, string? tag = null)
        : base(id, tag)
    {
        ArgumentNullException.ThrowIfNull(meshCodes);
        ValidateDifficultyType(difficultyType);
        var array = meshCodes.ToArray();
        if (array.Length == 0)
        {
            throw new ArgumentException("メッシュコードは 1 つ以上指定してください。", nameof(meshCodes));
        }
        Polygon = null;
        MeshCodes = array;
        DifficultyType = difficultyType;
    }

    /// <summary>ポリゴン領域。メッシュ指定で作成された場合は <c>null</c>。</summary>
    public GeoPolygon? Polygon { get; }

    /// <summary>
    /// メッシュコード集合。ポリゴン指定で作成された場合は <c>null</c>。
    /// 単一メッシュ指定でも要素数 1 のリストとして保持される。
    /// </summary>
    public IReadOnlyList<MeshCode>? MeshCodes { get; }

    /// <summary>
    /// 難所タイプ文字列。プロファイルが反応（速度係数・通行可否）を決定する（REQ-PRF-011）。
    /// </summary>
    public string DifficultyType { get; }

    private static void ValidateDifficultyType(string difficultyType)
    {
        if (string.IsNullOrWhiteSpace(difficultyType))
        {
            throw new ArgumentException(
                "難所タイプ文字列は空文字または null にできません（REQ-RST-007）。",
                nameof(difficultyType));
        }
    }
}
