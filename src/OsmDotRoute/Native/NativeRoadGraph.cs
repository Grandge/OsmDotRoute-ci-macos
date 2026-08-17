using System.Buffers.Binary;
using System.Text;
using OsmDotRoute.Geometry;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Profiles;
using OsmDotRoute.Routing;

namespace OsmDotRoute.Native;

/// <summary>
/// <c>.odrg</c> を <see cref="System.IO.MemoryMappedFiles.MemoryMappedFile"/> + <see cref="ReadOnlySpan{T}"/>
/// でゼロコピー読込する <see cref="IRoadGraph"/> 実装（Phase 3 ステップ 3A.3e）。
/// </summary>
/// <remarks>
/// <para>
/// 起動時に CSR インデックス（<c>firstOutEdge</c> + <c>outEntries</c>）を構築し、
/// ランタイムは <see cref="GetEdgeEnumerator"/> から O(1) で頂点別エッジ列挙を提供する。
/// </para>
/// <para>
/// シェイプは初回 <see cref="GetEdgeShape"/> 呼出時に <see cref="GeoCoordinate"/> 配列へ詰替えてキャッシュし、
/// 以降はゼロコピーで <see cref="ReadOnlySpan{T}"/> を返す。詰替えが必要な理由は <c>.odrg</c> 内 <c>OdrgVertex(Lon, Lat)</c>
/// と既存 <see cref="GeoCoordinate"/>(Lat, Lon) のフィールド順が逆で <c>MemoryMarshal.Cast</c> 不可のため
/// （Phase 3 ステップ 3A.3e §2.7 F4）。
/// </para>
/// <para>
/// エッジ距離（<see cref="OsmDotRoute.Routing.RoadEdge.DistanceM"/>）は <c>.odrg</c> に直接保持されないため、
/// 端点 + 中間シェイプ点列の Haversine 距離合算で初回算出し、エッジごとにキャッシュする。
/// </para>
/// </remarks>
internal sealed class NativeRoadGraph : IRoadGraph
{
    private readonly OdrgMmfHandle _mmf;
    private readonly OdrgSectionDirectory _directory;

    // セクションオフセット (HEADER + SECTION TABLE から抽出)
    private readonly long _vertexOffset;
    private readonly long _edgeOffset;
    private readonly long _shapeOffset;
    private readonly long _edgeAabbOffset;
    private readonly long _bakedEntriesOffset;

    // SPATIAL_INDEX (R-tree) セクション
    private readonly long _rtreeNodesOffset;
    private readonly uint _rtreeNodeCount;
    private readonly uint _rtreeRootIndex;
    private readonly uint _rtreeBranchingFactor;
    private readonly uint _rtreeHeight;

    // HEADER 値
    private readonly uint _vertexCount;
    private readonly int _edgeCount;
    private readonly GeoBounds _bounds;
    private readonly GeoBounds? _requestedBounds;

    // BAKED_PROFILE name → slot index
    private readonly Dictionary<string, int> _profileSlotByName;

    // CSR インデックス
    private readonly uint[] _firstOutEdge;
    private readonly OutEdgeEntry[] _outEntries;

    // キャッシュ（lazy 初期化、エッジ ID 添字）
    private readonly GeoCoordinate[]?[] _shapeCache;
    private readonly float[] _distanceCache;
    private readonly bool[] _distanceCached;

    private bool _disposed;

    /// <summary>
    /// 指定パスの <c>.odrg</c> ファイルをロードして道路グラフを構築する。
    /// </summary>
    /// <exception cref="OdrgFormatException"><c>.odrg</c> 形式不正。</exception>
    public NativeRoadGraph(string odrgPath)
        : this(OdrgMmfHandle.Open(odrgPath))
    {
    }

    /// <summary>
    /// マネージドバイト列（fetch 済み <c>.odrg</c> など）から道路グラフを構築する
    /// （Phase 3 ステップ 3J.2、ブラウザ WASM など MMF が使えない環境向け）。
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="odrgData"/> が空。</exception>
    /// <exception cref="OdrgFormatException"><c>.odrg</c> 形式不正。</exception>
    public NativeRoadGraph(ReadOnlyMemory<byte> odrgData)
        : this(OdrgMmfHandle.CreateFromMemory(odrgData))
    {
    }

    private NativeRoadGraph(OdrgMmfHandle handle)
    {
        _mmf = handle;
        try
        {
            _directory = OdrgSectionDirectory.Parse(_mmf.GetRawSpan(0, checked((int)_mmf.ViewLength)));

            var header = _directory.Header;
            _vertexCount = checked((uint)header.VertexCount);
            _edgeCount = checked((int)header.EdgeCount);

            _vertexOffset = checked((long)_directory.FindSection(OdrgFormat.SectionVertexTable).Offset);
            _edgeOffset = checked((long)_directory.FindSection(OdrgFormat.SectionEdgeTable).Offset);
            _shapeOffset = checked((long)_directory.FindSection(OdrgFormat.SectionEdgeShapeBuffer).Offset);
            _edgeAabbOffset = checked((long)_directory.FindSection(OdrgFormat.SectionEdgeAabbTable).Offset);

            ParseBakedProfileTable(out _profileSlotByName, out _bakedEntriesOffset);

            ParseRTreeHeader(
                out _rtreeNodesOffset,
                out _rtreeNodeCount,
                out _rtreeRootIndex,
                out _rtreeBranchingFactor,
                out _rtreeHeight);

            BuildCsrIndex(out _firstOutEdge, out _outEntries);

            _shapeCache = new GeoCoordinate[_edgeCount][];
            _distanceCache = new float[_edgeCount];
            _distanceCached = new bool[_edgeCount];

            var bbox = header.Bbox;
            _bounds = new GeoBounds(
                new GeoCoordinate(bbox.MinLat, bbox.MinLon),
                new GeoCoordinate(bbox.MaxLat, bbox.MaxLon));

            if (header.HasRequestedBbox)
            {
                var rb = header.RequestedBbox;
                if (rb.MinLon < rb.MaxLon && rb.MinLat < rb.MaxLat)
                    _requestedBounds = new GeoBounds(
                        new GeoCoordinate(rb.MinLat, rb.MinLon),
                        new GeoCoordinate(rb.MaxLat, rb.MaxLon));
            }
        }
        catch
        {
            _mmf.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public uint VertexCount
    {
        get
        {
            ThrowIfDisposed();
            return _vertexCount;
        }
    }

    /// <inheritdoc/>
    public long EdgeCount
    {
        get
        {
            ThrowIfDisposed();
            return _edgeCount;
        }
    }

    /// <inheritdoc/>
    public GeoBounds GetBounds()
    {
        ThrowIfDisposed();
        return _bounds;
    }

    /// <summary>
    /// 抽出要求時の bbox（RequestedBbox）。v1.1 以降で有効、それ未満は null。
    /// </summary>
    internal GeoBounds? RequestedBounds
    {
        get { ThrowIfDisposed(); return _requestedBounds; }
    }

    /// <inheritdoc/>
    public GeoCoordinate GetVertex(uint vertexId)
    {
        ThrowIfDisposed();
        if (vertexId >= _vertexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexId), vertexId,
                $"vertexId out of range (0..{_vertexCount - 1}).");
        }
        var span = _mmf.GetSpan<OdrgVertex>(
            _vertexOffset + (long)vertexId * OdrgFormat.VertexSize, 1);
        var v = span[0];
        return new GeoCoordinate(v.Lat, v.Lon);
    }

    /// <inheritdoc/>
    public IRoadGraphEdgeEnumerator GetEdgeEnumerator(uint vertexId)
    {
        ThrowIfDisposed();
        if (vertexId >= _vertexCount)
        {
            throw new ArgumentOutOfRangeException(nameof(vertexId), vertexId,
                $"vertexId out of range (0..{_vertexCount - 1}).");
        }
        return new NativeEdgeEnumerator(this, vertexId);
    }

    /// <inheritdoc/>
    public RoadEdge GetEdge(uint edgeId)
    {
        ThrowIfDisposed();
        var edge = ReadEdge(edgeId);
        var shape = GetOrBuildShape(edgeId);
        var distance = GetOrComputeDistance(edgeId, edge, shape);
        return new RoadEdge(
            edgeId,
            edge.FromVertexId,
            edge.ToVertexId,
            edgeProfileIndex: 0,  // Native 系では未使用 (§2.6.1)
            distance,
            dataInverted: false,
            shape);
    }

    /// <inheritdoc/>
    public ReadOnlySpan<GeoCoordinate> GetEdgeShape(uint edgeId)
    {
        ThrowIfDisposed();
        return GetOrBuildShape(edgeId);
    }

    /// <inheritdoc/>
    public IEnumerable<uint> QueryEdgesByAabb(Aabb queryBounds)
    {
        ThrowIfDisposed();
        return QueryEdgesByAabbCore(queryBounds);
    }

    private IEnumerable<uint> QueryEdgesByAabbCore(Aabb queryBounds)
    {
        var qbox = new OdrgBbox(
            queryBounds.MinLongitude,
            queryBounds.MinLatitude,
            queryBounds.MaxLongitude,
            queryBounds.MaxLatitude);

        uint[] buffer;
        int hits;
        int capacity = 1024;
        do
        {
            buffer = new uint[capacity];
            hits = NativeRTreeQuery.Query(GetRTreeNodes(), RTreeRootIndex, GetEdgeAabbs(), qbox, buffer);
            capacity = hits;  // overrun の場合は次回 hits 件まで拡大
        } while (hits > buffer.Length);

        for (int i = 0; i < hits; i++) yield return buffer[i];
    }

    /// <inheritdoc/>
    public EdgeEvaluation EvaluateEdge(IRoadGraphEdgeEnumerator en, ProfileEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(en);
        ArgumentNullException.ThrowIfNull(evaluator);
        ThrowIfDisposed();
        return EvaluateByEdgeId(en.EdgeId, evaluator);
    }

    /// <inheritdoc/>
    public EdgeEvaluation EvaluateEdge(RoadEdge edge, ProfileEvaluator evaluator)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(evaluator);
        ThrowIfDisposed();
        return EvaluateByEdgeId(edge.EdgeId, evaluator);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _mmf.Dispose();
    }

    /// <summary>
    /// SPATIAL_INDEX セクションの R-tree ノード列を Span として取得する（Phase 3 ステップ 3A.4）。
    /// </summary>
    /// <remarks>
    /// ゼロコピー（<c>MemoryMarshal.Cast&lt;byte, OdrgRTreeNode&gt;</c> 経由）。
    /// Span ライフタイムは本インスタンスの <see cref="Dispose"/> まで。
    /// </remarks>
    internal ReadOnlySpan<OdrgRTreeNode> GetRTreeNodes()
    {
        ThrowIfDisposed();
        return _mmf.GetSpan<OdrgRTreeNode>(_rtreeNodesOffset, (int)_rtreeNodeCount);
    }

    /// <summary>R-tree ルートノードのインデックス（<see cref="GetRTreeNodes"/> の添字）。</summary>
    internal uint RTreeRootIndex
    {
        get { ThrowIfDisposed(); return _rtreeRootIndex; }
    }

    /// <summary>R-tree 分岐数 M（STR pack、v0.2 初期値 = 16）。</summary>
    internal uint RTreeBranchingFactor
    {
        get { ThrowIfDisposed(); return _rtreeBranchingFactor; }
    }

    /// <summary>R-tree ツリー高（ルート含む段数）。</summary>
    internal uint RTreeHeight
    {
        get { ThrowIfDisposed(); return _rtreeHeight; }
    }

    /// <summary>R-tree ノード総数。</summary>
    internal uint RTreeNodeCount
    {
        get { ThrowIfDisposed(); return _rtreeNodeCount; }
    }

    /// <summary>
    /// EDGE_AABB セクション全体（エッジ ID 添字、<c>OdrgBbox</c> 32 byte × <c>EdgeCount</c>）の Span を返す。
    /// </summary>
    /// <remarks>
    /// ゼロコピー。3A.4 NativeRTreeQuery.Nearest と Brute-force 突合テストで利用。
    /// Span ライフタイムは本インスタンスの <see cref="Dispose"/> まで。
    /// </remarks>
    internal ReadOnlySpan<OdrgBbox> GetEdgeAabbs()
    {
        ThrowIfDisposed();
        return _mmf.GetSpan<OdrgBbox>(_edgeAabbOffset, _edgeCount);
    }

    /// <summary>
    /// 指定プロファイル名で指定エッジが通行可能か (BAKED_PROFILE.Flags bit 0) を直読する
    /// （Phase 3 ステップ 3A.5b、計画書 §2.12 Q2）。
    /// </summary>
    /// <remarks>
    /// <see cref="EvaluateByEdgeId"/> の CanPass 部分のみ抽出。方向制限 (Forward/Backward) は経路探索層で
    /// 別途処理するため、本 API では「いずれかの方向で通行可能か」のみを判定する
    /// （= <c>BakedProfileEntry.Flags &amp; 0x01</c> を返す）。
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">エッジ ID が範囲外。</exception>
    /// <exception cref="InvalidOperationException">プロファイル名が <c>.odrg</c> に未登録。</exception>
    internal bool CanPass(uint edgeId, string profileName)
    {
        ThrowIfDisposed();
        if (edgeId >= _edgeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeId), edgeId,
                $"edgeId out of range (0..{_edgeCount - 1}).");
        }
        if (!_profileSlotByName.TryGetValue(profileName, out var slot))
        {
            throw new InvalidOperationException(
                $"プロファイル '{profileName}' は .odrg の BAKED_PROFILE に存在しません。");
        }

        long entryOffset = _bakedEntriesOffset
            + ((long)slot * _edgeCount + edgeId) * OdrgFormat.BakedProfileEntrySize;
        var entrySpan = _mmf.GetSpan<OdrgBakedProfileEntry>(entryOffset, 1);
        return (entrySpan[0].Flags & 0x01) != 0;
    }

    /// <summary>プロファイル名が <c>.odrg</c> の BAKED_PROFILE に登録済みか。</summary>
    internal bool HasProfile(string profileName)
    {
        ThrowIfDisposed();
        return _profileSlotByName.ContainsKey(profileName);
    }

    /// <summary><c>.odrg</c> にベイク済みのプロファイル名一覧（スロット順、Phase 3 ステップ 3J.3）。</summary>
    internal IReadOnlyList<string> ProfileNames
    {
        get
        {
            ThrowIfDisposed();
            var names = new string[_profileSlotByName.Count];
            foreach (var (name, slot) in _profileSlotByName)
            {
                names[slot] = name;
            }
            return names;
        }
    }

    internal OdrgEdge ReadEdge(uint edgeId)
    {
        if (edgeId >= _edgeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeId), edgeId,
                $"edgeId out of range (0..{_edgeCount - 1}).");
        }
        var span = _mmf.GetSpan<OdrgEdge>(
            _edgeOffset + (long)edgeId * OdrgFormat.EdgeSize, 1);
        return span[0];
    }

    internal uint GetFirstOutEntry(uint vertexId) => _firstOutEdge[vertexId];

    internal uint GetOutEntryEnd(uint vertexId) => _firstOutEdge[vertexId + 1];

    internal OutEdgeEntry GetOutEntry(uint entryIndex) => _outEntries[entryIndex];

    internal GeoCoordinate[] GetOrBuildShape(uint edgeId)
    {
        var cached = _shapeCache[edgeId];
        if (cached is not null) return cached;

        var edge = ReadEdge(edgeId);
        var arr = BuildShape(edge);
        _shapeCache[edgeId] = arr;
        return arr;
    }

    private GeoCoordinate[] BuildShape(OdrgEdge edge)
    {
        int count = checked((int)edge.ShapePointCount);
        if (count == 0) return Array.Empty<GeoCoordinate>();

        long byteOffset = _shapeOffset + (long)edge.ShapeOffset;
        var src = _mmf.GetSpan<OdrgVertex>(byteOffset, count);
        var dst = new GeoCoordinate[count];
        for (int i = 0; i < count; i++)
        {
            dst[i] = new GeoCoordinate(src[i].Lat, src[i].Lon);
        }
        return dst;
    }

    internal float GetOrComputeDistance(uint edgeId, OdrgEdge edge, GeoCoordinate[] shape)
    {
        if (_distanceCached[edgeId]) return _distanceCache[edgeId];

        var from = GetVertex(edge.FromVertexId);
        var to = GetVertex(edge.ToVertexId);

        double total = 0.0;
        var prev = from;
        for (int i = 0; i < shape.Length; i++)
        {
            total += GeoMath.HaversineMeters(prev, shape[i]);
            prev = shape[i];
        }
        total += GeoMath.HaversineMeters(prev, to);

        var distance = (float)total;
        _distanceCache[edgeId] = distance;
        _distanceCached[edgeId] = true;
        return distance;
    }

    private EdgeEvaluation EvaluateByEdgeId(uint edgeId, ProfileEvaluator evaluator)
    {
        if (edgeId >= _edgeCount)
        {
            throw new ArgumentOutOfRangeException(nameof(edgeId), edgeId,
                $"edgeId out of range (0..{_edgeCount - 1}).");
        }
        if (!_profileSlotByName.TryGetValue(evaluator.Name, out var slot))
        {
            throw new InvalidOperationException(
                $"プロファイル '{evaluator.Name}' は .odrg の BAKED_PROFILE に存在しません。");
        }

        long entryOffset = _bakedEntriesOffset
            + ((long)slot * _edgeCount + edgeId) * OdrgFormat.BakedProfileEntrySize;
        var entrySpan = _mmf.GetSpan<OdrgBakedProfileEntry>(entryOffset, 1);
        var entry = entrySpan[0];

        bool canPass = (entry.Flags & 0x01) != 0;
        bool forward = (entry.Flags & 0x02) != 0;
        bool backward = (entry.Flags & 0x04) != 0;

        if (!canPass)
        {
            return new EdgeEvaluation(false, 0f, OnewayDirection.Bidirectional);
        }

        OnewayDirection oneway = (forward, backward) switch
        {
            (true, true) => OnewayDirection.Bidirectional,
            (true, false) => OnewayDirection.Forward,
            (false, true) => OnewayDirection.Backward,
            _ => OnewayDirection.Bidirectional,  // forward/backward 両方 0 は通行不可と論理的に矛盾するが、bake 上稀ケース
        };

        return new EdgeEvaluation(true, entry.SpeedKmh, oneway);
    }

    private void ParseBakedProfileTable(out Dictionary<string, int> nameMap, out long entriesOffset)
    {
        var section = _directory.FindSection(OdrgFormat.SectionBakedProfileTable);
        long baseOff = checked((long)section.Offset);

        var headerSpan = _mmf.GetRawSpan(baseOff, OdrgFormat.BakedProfileTableHeaderSize);
        uint profileCount = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan[..4]);
        uint entrySize = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan.Slice(4, 4));
        if (entrySize != OdrgFormat.BakedProfileEntrySize)
        {
            throw new OdrgFormatException(
                $"Unsupported BAKED_PROFILE entrySize: {entrySize}, expected {OdrgFormat.BakedProfileEntrySize}.");
        }

        long nameTableOff = baseOff + OdrgFormat.BakedProfileTableHeaderSize;
        long nameBufOff = nameTableOff + (long)profileCount * OdrgFormat.BakedProfileNameTableEntrySize;
        var nameTableSpan = _mmf.GetRawSpan(
            nameTableOff, (int)profileCount * OdrgFormat.BakedProfileNameTableEntrySize);

        var names = new Dictionary<string, int>((int)profileCount, StringComparer.Ordinal);
        uint totalNameLen = 0;
        for (int p = 0; p < profileCount; p++)
        {
            int o = p * OdrgFormat.BakedProfileNameTableEntrySize;
            uint nameOff = BinaryPrimitives.ReadUInt32LittleEndian(nameTableSpan.Slice(o, 4));
            uint nameLen = BinaryPrimitives.ReadUInt32LittleEndian(nameTableSpan.Slice(o + 4, 4));
            var nameSpan = _mmf.GetRawSpan(nameBufOff + nameOff, (int)nameLen);
            names[Encoding.UTF8.GetString(nameSpan)] = p;
            totalNameLen += nameLen;
        }

        nameMap = names;
        entriesOffset = nameBufOff + totalNameLen;
    }

    private void ParseRTreeHeader(
        out long nodesOffset,
        out uint nodeCount,
        out uint rootIndex,
        out uint branchingFactor,
        out uint height)
    {
        var section = _directory.FindSection(OdrgFormat.SectionEdgeSpatialIndex);
        long baseOff = checked((long)section.Offset);

        var headerSpan = _mmf.GetRawSpan(baseOff, OdrgFormat.RTreeHeaderSize);
        nodeCount = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan[..4]);
        rootIndex = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan.Slice(4, 4));
        branchingFactor = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan.Slice(8, 4));
        height = BinaryPrimitives.ReadUInt32LittleEndian(headerSpan.Slice(12, 4));

        long expectedLen = OdrgFormat.RTreeHeaderSize + (long)nodeCount * OdrgFormat.RTreeNodeSize;
        if ((long)section.Length != expectedLen)
        {
            throw new OdrgFormatException(
                $"R-tree section length mismatch: expected {expectedLen}, got {section.Length} (nodeCount={nodeCount}).");
        }
        if (nodeCount > 0 && rootIndex >= nodeCount)
        {
            throw new OdrgFormatException(
                $"R-tree rootIndex out of range: rootIndex={rootIndex}, nodeCount={nodeCount}.");
        }

        nodesOffset = baseOff + OdrgFormat.RTreeHeaderSize;
    }

    private void BuildCsrIndex(out uint[] firstOutEdge, out OutEdgeEntry[] outEntries)
    {
        var edgeSpan = _mmf.GetSpan<OdrgEdge>(_edgeOffset, _edgeCount);

        // 各頂点の出辺数を数える (from / to の両方に 1 ずつ)
        var counts = new uint[_vertexCount + 1];
        for (int e = 0; e < _edgeCount; e++)
        {
            counts[edgeSpan[e].FromVertexId + 1]++;
            counts[edgeSpan[e].ToVertexId + 1]++;
        }
        // 累積和で firstOutEdge を構築
        firstOutEdge = new uint[_vertexCount + 1];
        for (int i = 1; i <= _vertexCount; i++)
        {
            firstOutEdge[i] = firstOutEdge[i - 1] + counts[i];
        }

        outEntries = new OutEdgeEntry[2 * _edgeCount];
        var cursor = new uint[_vertexCount];
        for (int e = 0; e < _edgeCount; e++)
        {
            var edge = edgeSpan[e];
            uint from = edge.FromVertexId;
            uint to = edge.ToVertexId;

            uint fromSlot = firstOutEdge[from] + cursor[from]++;
            outEntries[fromSlot] = new OutEdgeEntry((uint)e, IsReversed: false);

            if (to != from)  // 自己ループは from 側 1 エントリのみ
            {
                uint toSlot = firstOutEdge[to] + cursor[to]++;
                outEntries[toSlot] = new OutEdgeEntry((uint)e, IsReversed: true);
            }
        }

        // 自己ループにより 2 倍未満になる場合は firstOutEdge を補正
        // （cursor[v] が想定より少なくなるため、firstOutEdge 末尾を実エントリ数に合わせる必要がある）
        uint actualCount = 0;
        for (int v = 0; v < _vertexCount; v++) actualCount += cursor[v];
        if (actualCount != outEntries.Length)
        {
            // 想定通り 2*E になっていない場合は trim
            var trimmed = new OutEdgeEntry[actualCount];
            var newFirst = new uint[_vertexCount + 1];
            uint dstCursor = 0;
            for (uint v = 0; v < _vertexCount; v++)
            {
                newFirst[v] = dstCursor;
                uint srcStart = firstOutEdge[v];
                uint srcEnd = srcStart + cursor[v];
                for (uint k = srcStart; k < srcEnd; k++)
                {
                    trimmed[dstCursor++] = outEntries[k];
                }
            }
            newFirst[_vertexCount] = dstCursor;
            firstOutEdge = newFirst;
            outEntries = trimmed;
        }
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
}
