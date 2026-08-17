namespace OsmDotRoute.Geometry;

/// <summary>
/// AABB 付きエントリの空間インデックス（REQ-RST-014）。
/// Phase 1 では線形走査による単純実装。性能要件（REQ-NFR-001/002）未達時に R-tree へ差し替える前提。
/// </summary>
/// <typeparam name="T">エントリ値型</typeparam>
internal sealed class SpatialIndex<T>
{
    private readonly List<Entry> _entries = new();

    /// <summary>エントリを追加する。</summary>
    public void Add(Aabb bounds, T value)
    {
        _entries.Add(new Entry(bounds, value));
    }

    /// <summary>
    /// 指定述語に一致する全エントリを削除する。
    /// </summary>
    public int RemoveAll(Predicate<T> match)
    {
        return _entries.RemoveAll(e => match(e.Value));
    }

    /// <summary>全エントリをクリアする。</summary>
    public void Clear() => _entries.Clear();

    /// <summary>登録エントリ数</summary>
    public int Count => _entries.Count;

    /// <summary>
    /// クエリ AABB と交差するエントリを順次返す。
    /// </summary>
    public IEnumerable<T> Query(Aabb queryBounds)
    {
        foreach (var e in _entries)
        {
            if (e.Bounds.Intersects(queryBounds))
            {
                yield return e.Value;
            }
        }
    }

    /// <summary>
    /// 線分（<paramref name="p1"/>-<paramref name="p2"/>）と AABB が交差するエントリを順次返す。
    /// </summary>
    public IEnumerable<T> Query(GeoCoordinate p1, GeoCoordinate p2)
    {
        foreach (var e in _entries)
        {
            if (e.Bounds.IntersectsSegment(p1, p2))
            {
                yield return e.Value;
            }
        }
    }

    private readonly record struct Entry(Aabb Bounds, T Value);
}
