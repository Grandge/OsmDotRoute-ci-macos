using OsmDotRoute.Geometry;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Routing;

namespace OsmDotRoute.Native;

/// <summary>
/// <see cref="NativeRoadGraph"/> の頂点出辺列挙器（Phase 3 ステップ 3A.3e）。
/// CSR の <see cref="OutEdgeEntry"/> 配列をカーソルで走査し、各反復で現在エッジの詳細を <c>.odrg</c>
/// から読出して <see cref="IRoadGraphEdgeEnumerator"/> プロパティ経由で公開する。
/// </summary>
internal sealed class NativeEdgeEnumerator : IRoadGraphEdgeEnumerator
{
    private readonly NativeRoadGraph _graph;
    private readonly uint _startVertex;
    private readonly uint _endExclusive;
    private uint _cursor;
    private bool _started;

    private uint _edgeId;
    private uint _from;
    private uint _to;
    private bool _dataInverted;
    private float _distance;
    private IReadOnlyList<GeoCoordinate>? _shape;

    public NativeEdgeEnumerator(NativeRoadGraph graph, uint startVertex)
    {
        _graph = graph;
        _startVertex = startVertex;
        _cursor = graph.GetFirstOutEntry(startVertex);
        _endExclusive = graph.GetOutEntryEnd(startVertex);
        _started = false;
    }

    /// <inheritdoc/>
    public bool MoveNext()
    {
        if (_started)
        {
            _cursor++;
        }
        else
        {
            _started = true;
        }
        if (_cursor >= _endExclusive) return false;

        var entry = _graph.GetOutEntry(_cursor);
        _edgeId = entry.EdgeId;
        _dataInverted = entry.IsReversed;

        var edge = _graph.ReadEdge(_edgeId);
        if (_dataInverted)
        {
            _from = edge.ToVertexId;
            _to = edge.FromVertexId;
        }
        else
        {
            _from = edge.FromVertexId;
            _to = edge.ToVertexId;
        }
        var shape = _graph.GetOrBuildShape(_edgeId);
        _shape = shape;
        _distance = _graph.GetOrComputeDistance(_edgeId, edge, shape);
        return true;
    }

    /// <inheritdoc/>
    public uint EdgeId => _edgeId;

    /// <inheritdoc/>
    public uint From => _from;

    /// <inheritdoc/>
    public uint To => _to;

    /// <inheritdoc/>
    public ushort EdgeProfileIndex => 0;  // Native 系では未使用 (§2.6.1)

    /// <inheritdoc/>
    public float DistanceM => _distance;

    /// <inheritdoc/>
    public bool DataInverted => _dataInverted;

    /// <inheritdoc/>
    public IReadOnlyList<GeoCoordinate> Shape
        => _shape ?? Array.Empty<GeoCoordinate>();
}
