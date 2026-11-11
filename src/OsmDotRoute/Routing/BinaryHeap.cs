namespace OsmDotRoute.Routing;

/// <summary>
/// Dijkstra 用最小ヒープ（バイナリヒープ）優先度付きキュー。
/// </summary>
/// <remarks>
/// <para>
/// 既存項目の decrease-key は実装せず、改善時は新エントリを <see cref="Push"/> し、
/// pop 時に <c>visited[v]</c> または「優先度が現行 cost より悪い」エントリをスキップする lazy 方式を採用する。
/// 都道府県規模（数百万頂点）では decrease-key のオーバーヘッドより lazy 方式の単純さが有利。
/// </para>
/// </remarks>
internal sealed class BinaryHeap<T>
{
    private readonly List<Entry> _items = new();

    public int Count => _items.Count;

    public void Push(T value, double priority)
    {
        _items.Add(new Entry(value, priority));
        SiftUp(_items.Count - 1);
    }

    public bool TryPop(out T value, out double priority)
    {
        if (_items.Count == 0)
        {
            value = default!;
            priority = 0;
            return false;
        }

        var top = _items[0];
        var lastIndex = _items.Count - 1;
        if (lastIndex == 0)
        {
            _items.RemoveAt(0);
        }
        else
        {
            _items[0] = _items[lastIndex];
            _items.RemoveAt(lastIndex);
            SiftDown(0);
        }

        value = top.Value;
        priority = top.Priority;
        return true;
    }

    private void SiftUp(int i)
    {
        while (i > 0)
        {
            var parent = (i - 1) >> 1;
            if (_items[i].Priority < _items[parent].Priority)
            {
                (_items[i], _items[parent]) = (_items[parent], _items[i]);
                i = parent;
            }
            else
            {
                break;
            }
        }
    }

    private void SiftDown(int i)
    {
        var n = _items.Count;
        while (true)
        {
            var left = (i << 1) + 1;
            var right = left + 1;
            var smallest = i;
            if (left < n && _items[left].Priority < _items[smallest].Priority) smallest = left;
            if (right < n && _items[right].Priority < _items[smallest].Priority) smallest = right;
            if (smallest == i) break;
            (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
            i = smallest;
        }
    }

    private readonly record struct Entry(T Value, double Priority);
}
