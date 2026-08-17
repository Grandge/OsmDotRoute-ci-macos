namespace OsmDotRoute;

/// <summary>
/// 動的制約エリアの一意 ID（REQ-RST-008）。
/// </summary>
/// <param name="Value">内部 GUID 値</param>
public readonly record struct RestrictedAreaId(Guid Value)
{
    /// <summary>新規 ID を生成する。</summary>
    public static RestrictedAreaId New() => new(Guid.NewGuid());

    /// <summary>GUID 文字列表現を返す。</summary>
    public override string ToString() => Value.ToString();
}
