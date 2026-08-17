using System.Buffers.Binary;
using System.IO;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Tests.TestData;

// Core / Extractor で同名 OdrgEdge が衝突するため Core 側を明示（3C で統一予定の DRY 一時違反）
using CoreEdge = OsmDotRoute.Internal.Odrg.OdrgEdge;

namespace OsmDotRoute.Tests.Internal.Odrg;

/// <summary>
/// 津島市 `.odrg` を MMF マップし、各セクション Span 切出を OdrgReader と突合するための fixture。
/// </summary>
public sealed class TsushimaMmfFixture : IDisposable
{
    internal OdrgMmfHandle Handle { get; }
    internal OdrgSectionDirectory Directory { get; }
    internal OdrgReadResult Reference { get; }
    internal int VertexCount { get; }
    internal int EdgeCount { get; }
    internal int ProfileCount { get; }

    public TsushimaMmfFixture()
    {
        if (!File.Exists(TestPaths.TsushimaOdrg))
        {
            Assert.Fail($"リポジトリ同梱の津島.odrg が見つかりません: {TestPaths.TsushimaOdrg}");
        }

        var bytes = File.ReadAllBytes(TestPaths.TsushimaOdrg);
        Reference = OdrgReader.Parse(bytes);
        Handle = OdrgMmfHandle.Open(TestPaths.TsushimaOdrg);
        Directory = OdrgSectionDirectory.Read(Handle.ViewHandle, Handle.ViewLength);

        VertexCount = (int)Directory.Header.VertexCount;
        EdgeCount = (int)Directory.Header.EdgeCount;
        ProfileCount = (int)Directory.Header.ProfileCount;
    }

    public void Dispose() => Handle.Dispose();
}

/// <summary>
/// Phase 3 ステップ 3A.2 — <see cref="OdrgMmfHandle.GetSpan{T}"/> 各セクション切出テスト。
/// </summary>
/// <remarks>
/// 参照真値は <see cref="OdrgReader.Parse"/> の managed-copy 展開結果。各セクションの全要素が
/// MMF + Span 経路でも完全一致することを確認する（zero-copy 経路の正当性検証）。
/// </remarks>
public sealed class OdrgMmfHandleTests : IClassFixture<TsushimaMmfFixture>
{
    private readonly TsushimaMmfFixture _fixture;

    public OdrgMmfHandleTests(TsushimaMmfFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetSpan_VertexSection_MatchesOdrgReader()
    {
        var section = _fixture.Directory.FindSection(OdrgFormat.SectionVertexTable);
        var vertices = _fixture.Handle.GetSpan<OdrgVertex>(
            (long)section.Offset,
            _fixture.VertexCount);

        Assert.Equal(_fixture.VertexCount, vertices.Length);
        var expected = _fixture.Reference.Vertices;
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Longitude, vertices[i].Lon);
            Assert.Equal(expected[i].Latitude, vertices[i].Lat);
        }
    }

    [Fact]
    public void GetSpan_EdgeSection_MatchesOdrgReader()
    {
        var section = _fixture.Directory.FindSection(OdrgFormat.SectionEdgeTable);
        var edges = _fixture.Handle.GetSpan<CoreEdge>(
            (long)section.Offset,
            _fixture.EdgeCount);

        Assert.Equal(_fixture.EdgeCount, edges.Length);
        var expected = _fixture.Reference.Edges;
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].FromVertexId, edges[i].FromVertexId);
            Assert.Equal(expected[i].ToVertexId, edges[i].ToVertexId);
            Assert.Equal(expected[i].ShapeOffset, edges[i].ShapeOffset);
            Assert.Equal(expected[i].ShapePointCount, edges[i].ShapePointCount);
            Assert.Equal(expected[i].BakedProfileIndex, edges[i].BakedProfileIndex);
        }
    }

    [Fact]
    public void GetSpan_EdgeShapeSection_MatchesOdrgReader()
    {
        var section = _fixture.Directory.FindSection(OdrgFormat.SectionEdgeShapeBuffer);
        int totalShapePoints = checked((int)((long)section.Length / OdrgFormat.ShapePointSize));
        var shapeBuffer = _fixture.Handle.GetSpan<OdrgVertex>(
            (long)section.Offset,
            totalShapePoints);

        // OdrgReader のジャグ配列を結合した連続列と完全一致するはず
        var edges = _fixture.Reference.Edges;
        var expectedShapes = _fixture.Reference.EdgeShapes;
        for (int e = 0; e < edges.Length; e++)
        {
            int shapeStart = checked((int)((long)edges[e].ShapeOffset / OdrgFormat.ShapePointSize));
            int shapeLen = (int)edges[e].ShapePointCount;
            for (int j = 0; j < shapeLen; j++)
            {
                Assert.Equal(expectedShapes[e][j].Longitude, shapeBuffer[shapeStart + j].Lon);
                Assert.Equal(expectedShapes[e][j].Latitude, shapeBuffer[shapeStart + j].Lat);
            }
        }
    }

    [Fact]
    public void GetSpan_EdgeAabbSection_MatchesOdrgReader()
    {
        var section = _fixture.Directory.FindSection(OdrgFormat.SectionEdgeAabbTable);
        var aabbs = _fixture.Handle.GetSpan<OdrgBbox>(
            (long)section.Offset,
            _fixture.EdgeCount);

        Assert.Equal(_fixture.EdgeCount, aabbs.Length);
        var expected = _fixture.Reference.EdgeAabbs;
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].MinLon, aabbs[i].MinLon);
            Assert.Equal(expected[i].MinLat, aabbs[i].MinLat);
            Assert.Equal(expected[i].MaxLon, aabbs[i].MaxLon);
            Assert.Equal(expected[i].MaxLat, aabbs[i].MaxLat);
        }
    }

    [Fact]
    public void GetSpan_EdgeFlagSection_MatchesOdrgReader()
    {
        var section = _fixture.Directory.FindSection(OdrgFormat.SectionEdgeFlagTable);
        var flags = _fixture.Handle.GetSpan<ushort>(
            (long)section.Offset,
            _fixture.EdgeCount);

        Assert.Equal(_fixture.EdgeCount, flags.Length);
        var expected = _fixture.Reference.EdgeFlags;
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal((ushort)expected[i], flags[i]);
        }
    }

    [Fact]
    public void GetSpan_RTreeNodes_MatchesOdrgReader()
    {
        var section = _fixture.Directory.FindSection(OdrgFormat.SectionEdgeSpatialIndex);
        long nodesOff = (long)section.Offset + OdrgFormat.RTreeHeaderSize;
        int nodeCount = (int)_fixture.Reference.RTree.NodeCount;
        var nodes = _fixture.Handle.GetSpan<OdrgRTreeNode>(nodesOff, nodeCount);

        Assert.Equal(nodeCount, nodes.Length);
        var expected = _fixture.Reference.RTree.Nodes;
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Bounds.MinLon, nodes[i].Bbox.MinLon);
            Assert.Equal(expected[i].Bounds.MinLat, nodes[i].Bbox.MinLat);
            Assert.Equal(expected[i].Bounds.MaxLon, nodes[i].Bbox.MaxLon);
            Assert.Equal(expected[i].Bounds.MaxLat, nodes[i].Bbox.MaxLat);
            Assert.Equal(expected[i].FirstChildIndex, nodes[i].FirstChildIndex);
            Assert.Equal(expected[i].ChildCount, nodes[i].ChildCount);
            Assert.Equal(expected[i].Flags, nodes[i].Flags);
        }
    }

    [Fact]
    public void GetSpan_BakedProfileEntries_MatchesOdrgReader()
    {
        var section = _fixture.Directory.FindSection(OdrgFormat.SectionBakedProfileTable);
        int profileCount = _fixture.ProfileCount;
        int edgeCount = _fixture.EdgeCount;

        // name table (offset u32 + length u32) を読んで名前バッファ総バイト数を集計
        long nameTableOff = (long)section.Offset + OdrgFormat.BakedProfileTableHeaderSize;
        var nameTable = _fixture.Handle.GetRawSpan(
            nameTableOff,
            profileCount * OdrgFormat.BakedProfileNameTableEntrySize);
        long totalNameBufLen = 0;
        for (int p = 0; p < profileCount; p++)
        {
            uint nameLength = BinaryPrimitives.ReadUInt32LittleEndian(
                nameTable.Slice(p * OdrgFormat.BakedProfileNameTableEntrySize + 4, 4));
            totalNameBufLen += nameLength;
        }

        long entriesOff = nameTableOff
            + (long)profileCount * OdrgFormat.BakedProfileNameTableEntrySize
            + totalNameBufLen;
        int totalEntries = profileCount * edgeCount;
        var entries = _fixture.Handle.GetSpan<OdrgBakedProfileEntry>(entriesOff, totalEntries);

        Assert.Equal(totalEntries, entries.Length);
        for (int p = 0; p < profileCount; p++)
        {
            var expectedRow = _fixture.Reference.ProfileTable.EntriesByProfile[p];
            for (int e = 0; e < edgeCount; e++)
            {
                var expected = expectedRow[e];
                var actual = entries[p * edgeCount + e];
                Assert.Equal(expected.SpeedKmh, actual.SpeedKmh);
                Assert.Equal(expected.Flags, actual.Flags);
            }
        }
    }

    [Fact]
    public void GetSpan_AfterDispose_ThrowsObjectDisposedException()
    {
        var handle = OdrgMmfHandle.Open(TestPaths.TsushimaOdrg);
        handle.Dispose();

        Assert.Throws<ObjectDisposedException>(() => handle.GetSpan<byte>(0, 1));
    }
}
