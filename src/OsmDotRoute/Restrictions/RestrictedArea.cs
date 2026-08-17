namespace OsmDotRoute;

/// <summary>
/// 動的制約エリアの抽象基底（REQ-RST-001〜011）。
/// 具象として <see cref="BlockArea"/>（進入不可）と <see cref="DifficultyArea"/>（難所）がある。
/// </summary>
public abstract class RestrictedArea
{
    /// <summary>
    /// 派生クラスから ID とタグを設定する。
    /// </summary>
    /// <param name="id">エリア ID</param>
    /// <param name="tag">タグ文字列（一括削除用、REQ-RST-010）。null 可</param>
    protected RestrictedArea(RestrictedAreaId id, string? tag)
    {
        Id = id;
        Tag = tag;
    }

    /// <summary>エリアの一意 ID</summary>
    public RestrictedAreaId Id { get; }

    /// <summary>タグ文字列（タグ単位での一括削除に使用、REQ-RST-010）</summary>
    public string? Tag { get; }
}
