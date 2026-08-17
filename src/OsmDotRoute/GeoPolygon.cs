namespace OsmDotRoute;

/// <summary>
/// 緯度経度頂点列で定義される多角形。Hole（穴）を 0 個以上持つことができる。
/// GeoJSON Polygon と同等の表現（外側境界 + 内側境界配列）。
/// </summary>
public sealed class GeoPolygon
{
    /// <summary>
    /// 外側境界のみを持つ多角形を作成する。
    /// </summary>
    /// <param name="outerBoundary">外側境界の頂点列（3 頂点以上）</param>
    /// <exception cref="ArgumentNullException"><paramref name="outerBoundary"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="outerBoundary"/> が 3 頂点未満</exception>
    public GeoPolygon(IReadOnlyList<GeoCoordinate> outerBoundary)
        : this(outerBoundary, Array.Empty<IReadOnlyList<GeoCoordinate>>())
    {
    }

    /// <summary>
    /// 外側境界と Hole を持つ多角形を作成する。
    /// </summary>
    /// <param name="outerBoundary">外側境界の頂点列（3 頂点以上）</param>
    /// <param name="holes">内側境界（Hole）の頂点列配列。各リングは 3 頂点以上</param>
    /// <exception cref="ArgumentNullException"><paramref name="outerBoundary"/> または <paramref name="holes"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="outerBoundary"/> が 3 頂点未満</exception>
    public GeoPolygon(IReadOnlyList<GeoCoordinate> outerBoundary, IReadOnlyList<IReadOnlyList<GeoCoordinate>> holes)
    {
        ArgumentNullException.ThrowIfNull(outerBoundary);
        ArgumentNullException.ThrowIfNull(holes);
        if (outerBoundary.Count < 3)
        {
            throw new ArgumentException("外側境界は 3 頂点以上必要です。", nameof(outerBoundary));
        }

        OuterBoundary = outerBoundary;
        Holes = holes;
    }

    /// <summary>外側境界の頂点列</summary>
    public IReadOnlyList<GeoCoordinate> OuterBoundary { get; }

    /// <summary>内側境界（Hole）の頂点列配列</summary>
    public IReadOnlyList<IReadOnlyList<GeoCoordinate>> Holes { get; }
}
