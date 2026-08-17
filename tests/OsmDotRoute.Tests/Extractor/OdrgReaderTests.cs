using System;
using System.IO;
using System.Linq;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Tests.TestData;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 5.1 — <see cref="OdrgReader"/> の読込テスト。
/// </summary>
/// <remarks>
/// 主要な検証は <see cref="OdrgWriter"/> の出力を読み戻し、フィールドが一致することを確認する往復テスト。
/// </remarks>
public sealed class OdrgReaderTests
{
    private static readonly OsmStringTable EmptyStringTable = new(new[] { Array.Empty<byte>() });

    private static OdrgWriteInput MinimalInput()
    {
        var vertices = new[]
        {
            new GeoCoordinate(35.16, 136.70),
            new GeoCoordinate(35.20, 136.78),
        };
        var edges = new[]
        {
            new EdgeRecord(
                OsmWayId: 1,
                FromVertexId: 0,
                ToVertexId: 1,
                ShapeNodeRefs: Array.Empty<long>(),
                TagKeys: Array.Empty<int>(),
                TagValues: Array.Empty<int>(),
                StringTable: EmptyStringTable),
        };
        var aabbs = new[] { new Aabb(136.70, 35.16, 136.78, 35.20) };
        var flags = new[] { EdgeFlags.None };
        var rtree = new StrRTree(
            Nodes: new[]
            {
                RTreeNode.Create(new Aabb(136.70, 35.16, 136.78, 35.20),
                    firstChildIndex: 0, childCount: 1, isLeaf: true),
            },
            RootIndex: 0,
            BranchingFactor: 16,
            TreeHeight: 1,
            EdgePermutation: new[] { 0 });

        var table = new BakedProfileTable(
            new[] { "car" },
            new[] { new[] { BakedProfileEntry.Create(true, 50f, true, true) } });

        return new OdrgWriteInput(
            Vertices: vertices,
            Edges: edges,
            EdgeAabbs: aabbs,
            EdgeFlags: flags,
            RTree: rtree,
            ProfileTable: table,
            NodeCoordLookup: _ => default,
            Bbox: new Aabb(136.70, 35.16, 136.78, 35.20),
            MetadataJson: "{}");
    }

    private static OdrgReadResult RoundTrip(OdrgWriteInput input)
    {
        using var ms = new MemoryStream();
        OdrgWriter.Write(ms, input);
        ms.Position = 0;
        return OdrgReader.Read(ms);
    }

    [Fact]
    public void Roundtrip_MinimalInput_PreservesHeader()
    {
        var r = RoundTrip(MinimalInput());

        Assert.Equal(OdrgFormat.VersionMajor, r.Header.VersionMajor);
        Assert.Equal(OdrgFormat.VersionMinor, r.Header.VersionMinor);
        Assert.Equal(2UL, r.Header.VertexCount);
        Assert.Equal(1UL, r.Header.EdgeCount);
        Assert.Equal(1u, r.Header.ProfileCount);
        Assert.Equal(OdrgFormat.EdgeFlagBytes, r.Header.EdgeFlagBytes);
        Assert.Equal(136.70, r.Header.Bbox.MinLon);
        Assert.Equal(35.16, r.Header.Bbox.MinLat);
        Assert.Equal(136.78, r.Header.Bbox.MaxLon);
        Assert.Equal(35.20, r.Header.Bbox.MaxLat);
        Assert.Equal(256UL, r.Header.SectionTableOffset);
        Assert.Equal(9u, r.Header.SectionCount);
    }

    [Fact]
    public void Roundtrip_MinimalInput_PreservesSectionTable()
    {
        var r = RoundTrip(MinimalInput());

        Assert.Equal(9, r.SectionTable.Length);
        Assert.Equal(OdrgFormat.SectionVertexTable, r.SectionTable[0].Kind);
        Assert.Equal(OdrgFormat.SectionEdgeTable, r.SectionTable[1].Kind);
        Assert.Equal(OdrgFormat.SectionEdgeShapeBuffer, r.SectionTable[2].Kind);
        Assert.Equal(OdrgFormat.SectionEdgeAabbTable, r.SectionTable[3].Kind);
        Assert.Equal(OdrgFormat.SectionEdgeFlagTable, r.SectionTable[4].Kind);
        Assert.Equal(OdrgFormat.SectionEdgeSpatialIndex, r.SectionTable[5].Kind);
        Assert.Equal(OdrgFormat.SectionBakedProfileTable, r.SectionTable[6].Kind);
        Assert.Equal(OdrgFormat.SectionTurnRestrictionTable, r.SectionTable[7].Kind);
        Assert.Equal(OdrgFormat.SectionMetadata, r.SectionTable[8].Kind);
    }

    [Fact]
    public void Roundtrip_MinimalInput_PreservesVertices()
    {
        var r = RoundTrip(MinimalInput());

        Assert.Equal(2, r.Vertices.Length);
        Assert.Equal(35.16, r.Vertices[0].Latitude);
        Assert.Equal(136.70, r.Vertices[0].Longitude);
        Assert.Equal(35.20, r.Vertices[1].Latitude);
        Assert.Equal(136.78, r.Vertices[1].Longitude);
    }

    [Fact]
    public void Roundtrip_MinimalInput_PreservesEdges()
    {
        var r = RoundTrip(MinimalInput());

        Assert.Single(r.Edges);
        Assert.Equal(0u, r.Edges[0].FromVertexId);
        Assert.Equal(1u, r.Edges[0].ToVertexId);
        Assert.Equal(0UL, r.Edges[0].ShapeOffset);
        Assert.Equal(0u, r.Edges[0].ShapePointCount);
        Assert.Equal(0u, r.Edges[0].BakedProfileIndex);
    }

    [Fact]
    public void Roundtrip_MinimalInput_PreservesEdgeAabbs()
    {
        var r = RoundTrip(MinimalInput());

        Assert.Single(r.EdgeAabbs);
        Assert.Equal(new Aabb(136.70, 35.16, 136.78, 35.20), r.EdgeAabbs[0]);
    }

    [Fact]
    public void Roundtrip_VariousEdgeFlags_PreservesAllBits()
    {
        var input = MinimalInput() with
        {
            EdgeFlags = new[]
            {
                EdgeFlags.IsBridge | EdgeFlags.IsTunnel | EdgeFlags.IsRoundabout
                    | EdgeFlags.IsToll | EdgeFlags.IsOnewayForward | EdgeFlags.IsOnewayBackward,
            },
        };
        var r = RoundTrip(input);
        Assert.Equal(input.EdgeFlags[0], r.EdgeFlags[0]);
    }

    [Fact]
    public void Roundtrip_MinimalInput_PreservesRTree()
    {
        var r = RoundTrip(MinimalInput());

        Assert.Equal(1u, r.RTree.NodeCount);
        Assert.Equal(0u, r.RTree.RootIndex);
        Assert.Equal(16u, r.RTree.BranchingFactor);
        Assert.Equal(1u, r.RTree.TreeHeight);
        Assert.Single(r.RTree.Nodes);

        var node = r.RTree.Nodes[0];
        Assert.True(node.IsLeaf);
        Assert.Equal(0u, node.FirstChildIndex);
        Assert.Equal(1u, node.ChildCount);
        Assert.Equal(new Aabb(136.70, 35.16, 136.78, 35.20), node.Bounds);
    }

    [Fact]
    public void Roundtrip_MinimalInput_PreservesBakedProfile()
    {
        var r = RoundTrip(MinimalInput());

        Assert.Equal(1, r.ProfileTable.ProfileCount);
        Assert.Equal(1, r.ProfileTable.EdgeCount);
        Assert.Equal((uint)OdrgFormat.BakedProfileEntrySize, r.ProfileTable.EntrySize);
        Assert.Equal("car", r.ProfileTable.ProfileNames[0]);

        var entry = r.ProfileTable.EntriesByProfile[0][0];
        Assert.True(entry.CanPass);
        Assert.True(entry.Forward);
        Assert.True(entry.Backward);
        Assert.Equal(50f, entry.SpeedKmh);
    }

    [Fact]
    public void Roundtrip_CustomMetadata_PreservesUtf8Content()
    {
        var input = MinimalInput() with { MetadataJson = "{\"hello\":\"こんにちは\"}" };
        var r = RoundTrip(input);
        Assert.Equal("{\"hello\":\"こんにちは\"}", r.MetadataJson);
    }

    [Fact]
    public void Roundtrip_EmptyMetadata_PreservedAsEmptyString()
    {
        var input = MinimalInput() with { MetadataJson = string.Empty };
        var r = RoundTrip(input);
        Assert.Equal(string.Empty, r.MetadataJson);
    }

    [Fact]
    public void Roundtrip_EdgeWithShape_PreservesCoordinatesInOrder()
    {
        var input = MinimalInput() with
        {
            Edges = new[]
            {
                new EdgeRecord(
                    OsmWayId: 1,
                    FromVertexId: 0,
                    ToVertexId: 1,
                    ShapeNodeRefs: new long[] { 100, 200, 300 },
                    TagKeys: Array.Empty<int>(),
                    TagValues: Array.Empty<int>(),
                    StringTable: EmptyStringTable),
            },
            NodeCoordLookup = id => id switch
            {
                100 => new GeoCoordinate(35.17, 136.72),
                200 => new GeoCoordinate(35.18, 136.74),
                300 => new GeoCoordinate(35.19, 136.76),
                _ => default,
            },
        };
        var r = RoundTrip(input);

        Assert.Equal(3u, r.Edges[0].ShapePointCount);
        var shape = r.EdgeShapes[0];
        Assert.Equal(3, shape.Length);
        Assert.Equal(new GeoCoordinate(35.17, 136.72), shape[0]);
        Assert.Equal(new GeoCoordinate(35.18, 136.74), shape[1]);
        Assert.Equal(new GeoCoordinate(35.19, 136.76), shape[2]);
    }

    [Fact]
    public void Roundtrip_MultipleEdges_PreservesShapesPerEdge()
    {
        // 2 つのエッジを構築（1 つ目は shape 2 点、2 つ目は shape 1 点）
        var vertices = new[]
        {
            new GeoCoordinate(35.10, 136.60),
            new GeoCoordinate(35.20, 136.70),
            new GeoCoordinate(35.30, 136.80),
        };
        var edges = new[]
        {
            new EdgeRecord(1, 0, 1, new long[] { 10, 11 }, Array.Empty<int>(), Array.Empty<int>(), EmptyStringTable),
            new EdgeRecord(2, 1, 2, new long[] { 20 }, Array.Empty<int>(), Array.Empty<int>(), EmptyStringTable),
        };
        var aabbs = new[]
        {
            new Aabb(136.60, 35.10, 136.70, 35.20),
            new Aabb(136.70, 35.20, 136.80, 35.30),
        };
        var rtree = new StrRTree(
            Nodes: new[]
            {
                RTreeNode.Create(new Aabb(136.60, 35.10, 136.80, 35.30),
                    firstChildIndex: 0, childCount: 2, isLeaf: true),
            },
            RootIndex: 0,
            BranchingFactor: 16,
            TreeHeight: 1,
            EdgePermutation: new[] { 0, 1 });

        var table = new BakedProfileTable(
            new[] { "car" },
            new[]
            {
                new[]
                {
                    BakedProfileEntry.Create(true, 50f, true, true),
                    BakedProfileEntry.Create(true, 30f, true, false),
                },
            });

        var input = new OdrgWriteInput(
            Vertices: vertices,
            Edges: edges,
            EdgeAabbs: aabbs,
            EdgeFlags: new[] { EdgeFlags.None, EdgeFlags.IsOnewayForward },
            RTree: rtree,
            ProfileTable: table,
            NodeCoordLookup: id => id switch
            {
                10 => new GeoCoordinate(35.12, 136.62),
                11 => new GeoCoordinate(35.14, 136.64),
                20 => new GeoCoordinate(35.25, 136.75),
                _ => default,
            },
            Bbox: new Aabb(136.60, 35.10, 136.80, 35.30),
            MetadataJson: "{}");

        var r = RoundTrip(input);

        Assert.Equal(2, r.Edges.Length);
        Assert.Equal(2u, r.Edges[0].ShapePointCount);
        Assert.Equal(1u, r.Edges[1].ShapePointCount);

        Assert.Equal(new GeoCoordinate(35.12, 136.62), r.EdgeShapes[0][0]);
        Assert.Equal(new GeoCoordinate(35.14, 136.64), r.EdgeShapes[0][1]);
        Assert.Equal(new GeoCoordinate(35.25, 136.75), r.EdgeShapes[1][0]);

        Assert.Equal(EdgeFlags.None, r.EdgeFlags[0]);
        Assert.Equal(EdgeFlags.IsOnewayForward, r.EdgeFlags[1]);

        Assert.Equal(50f, r.ProfileTable.EntriesByProfile[0][0].SpeedKmh);
        Assert.Equal(30f, r.ProfileTable.EntriesByProfile[0][1].SpeedKmh);
        Assert.True(r.ProfileTable.EntriesByProfile[0][1].Forward);
        Assert.False(r.ProfileTable.EntriesByProfile[0][1].Backward);
    }

    [Fact]
    public void Roundtrip_MultipleProfiles_PreservesNamesAndEntriesInOrder()
    {
        var input = MinimalInput() with
        {
            ProfileTable = new BakedProfileTable(
                new[] { "car", "pedestrian", "bicycle" },
                new[]
                {
                    new[] { BakedProfileEntry.Create(true, 60f, true, false) },
                    new[] { BakedProfileEntry.Create(true, 5f, true, true) },
                    new[] { BakedProfileEntry.Create(false, 0f, false, false) },
                }),
        };

        var r = RoundTrip(input);

        Assert.Equal(3, r.ProfileTable.ProfileCount);
        Assert.Equal("car", r.ProfileTable.ProfileNames[0]);
        Assert.Equal("pedestrian", r.ProfileTable.ProfileNames[1]);
        Assert.Equal("bicycle", r.ProfileTable.ProfileNames[2]);

        Assert.Equal(60f, r.ProfileTable.EntriesByProfile[0][0].SpeedKmh);
        Assert.Equal(5f, r.ProfileTable.EntriesByProfile[1][0].SpeedKmh);
        Assert.False(r.ProfileTable.EntriesByProfile[2][0].CanPass);
    }

    [Fact]
    public void Roundtrip_UnicodeProfileName_PreservesUtf8()
    {
        var input = MinimalInput() with
        {
            ProfileTable = new BakedProfileTable(
                new[] { "自動車" },
                new[] { new[] { BakedProfileEntry.Create(true, 50f, true, true) } }),
        };

        var r = RoundTrip(input);
        Assert.Equal("自動車", r.ProfileTable.ProfileNames[0]);
    }

    [Fact]
    public void Roundtrip_TurnRestrictionSection_IsEmptyByteArray()
    {
        var r = RoundTrip(MinimalInput());
        Assert.Empty(r.TurnRestrictionRaw);
    }

    [Fact]
    public void Roundtrip_RTreeWithInnerNode_PreservesLeafFlag()
    {
        // ルート (内部) + 子 (葉) 2 つの 3 ノード R-tree
        var input = MinimalInput() with
        {
            Vertices = new[]
            {
                new GeoCoordinate(35.10, 136.60),
                new GeoCoordinate(35.20, 136.70),
                new GeoCoordinate(35.30, 136.80),
            },
            Edges = new[]
            {
                new EdgeRecord(1, 0, 1, Array.Empty<long>(), Array.Empty<int>(), Array.Empty<int>(), EmptyStringTable),
                new EdgeRecord(2, 1, 2, Array.Empty<long>(), Array.Empty<int>(), Array.Empty<int>(), EmptyStringTable),
            },
            EdgeAabbs = new[]
            {
                new Aabb(136.60, 35.10, 136.70, 35.20),
                new Aabb(136.70, 35.20, 136.80, 35.30),
            },
            EdgeFlags = new[] { EdgeFlags.None, EdgeFlags.None },
            RTree = new StrRTree(
                Nodes: new[]
                {
                    RTreeNode.Create(new Aabb(136.60, 35.10, 136.70, 35.20),
                        firstChildIndex: 0, childCount: 1, isLeaf: true),
                    RTreeNode.Create(new Aabb(136.70, 35.20, 136.80, 35.30),
                        firstChildIndex: 1, childCount: 1, isLeaf: true),
                    RTreeNode.Create(new Aabb(136.60, 35.10, 136.80, 35.30),
                        firstChildIndex: 0, childCount: 2, isLeaf: false),
                },
                RootIndex: 2,
                BranchingFactor: 16,
                TreeHeight: 2,
                EdgePermutation: new[] { 0, 1 }),
            ProfileTable = new BakedProfileTable(
                new[] { "car" },
                new[]
                {
                    new[]
                    {
                        BakedProfileEntry.Create(true, 50f, true, true),
                        BakedProfileEntry.Create(true, 50f, true, true),
                    },
                }),
        };

        var r = RoundTrip(input);

        Assert.Equal(3u, r.RTree.NodeCount);
        Assert.Equal(2u, r.RTree.RootIndex);
        Assert.True(r.RTree.Nodes[0].IsLeaf);
        Assert.True(r.RTree.Nodes[1].IsLeaf);
        Assert.False(r.RTree.Nodes[2].IsLeaf);
        Assert.Equal(0u, r.RTree.Nodes[2].FirstChildIndex);
        Assert.Equal(2u, r.RTree.Nodes[2].ChildCount);
    }

    [Fact]
    public void Read_BadMagic_Throws()
    {
        using var ms = new MemoryStream();
        OdrgWriter.Write(ms, MinimalInput());
        byte[] bytes = ms.ToArray();
        bytes[0] = (byte)'X';  // 先頭 1 byte を破壊
        Assert.Throws<InvalidDataException>(() => OdrgReader.Parse(bytes));
    }

    [Fact]
    public void Read_UnsupportedMajorVersion_Throws()
    {
        using var ms = new MemoryStream();
        OdrgWriter.Write(ms, MinimalInput());
        byte[] bytes = ms.ToArray();
        // VersionMajor (offset 8) を 99 に書き換え
        bytes[8] = 99;
        bytes[9] = 0;
        Assert.Throws<InvalidDataException>(() => OdrgReader.Parse(bytes));
    }

    [Fact]
    public void Read_TruncatedHeader_Throws()
    {
        byte[] bytes = new byte[100];  // header size 256 未満
        Assert.Throws<InvalidDataException>(() => OdrgReader.Parse(bytes));
    }

    [Fact]
    public void Read_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OdrgReader.Read((string)null!));
        Assert.Throws<ArgumentNullException>(() => OdrgReader.Read((Stream)null!));
        Assert.Throws<ArgumentNullException>(() => OdrgReader.Parse(null!));
    }

    [Fact]
    public void Read_TsushimaPbfRoundtrip_VertexAndEdgeCountsMatch()
    {
        if (!File.Exists(TestPaths.TsushimaExtractPbf))
            return;  // 親プロジェクト由来 PBF はリポジトリに含めない（CLAUDE.md）。不在の環境ではスキップ。

        var opts = new ExtractPipelineOptions(
            InputPbf: TestPaths.TsushimaExtractPbf,
            Bbox: new Aabb(136.65, 35.13, 136.80, 35.25),
            Profiles: new[] { VehicleProfile.Car, VehicleProfile.Pedestrian });
        var result = ExtractPipeline.Run(opts);

        var writeInput = new OdrgWriteInput(
            Vertices: result.Vertices,
            Edges: result.Edges,
            EdgeAabbs: result.EdgeAabbs,
            EdgeFlags: result.EdgeFlags,
            RTree: result.RTree,
            ProfileTable: result.ProfileTable,
            NodeCoordLookup: result.NodeCoordLookup,
            Bbox: result.FileBbox,
            MetadataJson: "{\"src\":\"tsushima\"}");

        using var ms = new MemoryStream();
        OdrgWriter.Write(ms, writeInput);
        ms.Position = 0;
        var r = OdrgReader.Read(ms);

        Assert.Equal((ulong)result.Vertices.Length, r.Header.VertexCount);
        Assert.Equal((ulong)result.Edges.Length, r.Header.EdgeCount);
        Assert.Equal(result.Vertices.Length, r.Vertices.Length);
        Assert.Equal(result.Edges.Length, r.Edges.Length);
        Assert.Equal(result.Edges.Length, r.EdgeAabbs.Length);
        Assert.Equal(result.Edges.Length, r.EdgeFlags.Length);
        Assert.Equal(2, r.ProfileTable.ProfileCount);
        Assert.Equal(result.Edges.Length, r.ProfileTable.EdgeCount);
        Assert.Equal("{\"src\":\"tsushima\"}", r.MetadataJson);
    }

    [Fact]
    public void Read_TsushimaPbfRoundtrip_FirstEdgeShapeMatchesNodeCoord()
    {
        if (!File.Exists(TestPaths.TsushimaExtractPbf))
            return;  // 親プロジェクト由来 PBF はリポジトリに含めない（CLAUDE.md）。不在の環境ではスキップ。

        var opts = new ExtractPipelineOptions(
            InputPbf: TestPaths.TsushimaExtractPbf,
            Bbox: new Aabb(136.65, 35.13, 136.80, 35.25),
            Profiles: new[] { VehicleProfile.Car });
        var result = ExtractPipeline.Run(opts);

        var writeInput = new OdrgWriteInput(
            Vertices: result.Vertices,
            Edges: result.Edges,
            EdgeAabbs: result.EdgeAabbs,
            EdgeFlags: result.EdgeFlags,
            RTree: result.RTree,
            ProfileTable: result.ProfileTable,
            NodeCoordLookup: result.NodeCoordLookup,
            Bbox: result.FileBbox,
            MetadataJson: "{}");

        using var ms = new MemoryStream();
        OdrgWriter.Write(ms, writeInput);
        ms.Position = 0;
        var r = OdrgReader.Read(ms);

        // shape を持つ最初のエッジを探し、読み戻し後の座標が writer 入力時 NodeCoordLookup と一致することを確認
        int firstShapedEdge = -1;
        for (int i = 0; i < result.Edges.Length; i++)
        {
            if (result.Edges[i].ShapeNodeRefs.Length > 0) { firstShapedEdge = i; break; }
        }
        if (firstShapedEdge < 0) return;  // 全エッジが直線なら検証不要

        var expectedShape = result.Edges[firstShapedEdge].ShapeNodeRefs
            .Select(id => result.NodeCoordLookup(id))
            .ToArray();
        var actualShape = r.EdgeShapes[firstShapedEdge];

        Assert.Equal(expectedShape.Length, actualShape.Length);
        for (int j = 0; j < expectedShape.Length; j++)
        {
            Assert.Equal(expectedShape[j].Latitude, actualShape[j].Latitude);
            Assert.Equal(expectedShape[j].Longitude, actualShape[j].Longitude);
        }

        // 全エッジの AABB / Flags 一致
        for (int i = 0; i < result.Edges.Length; i++)
        {
            Assert.Equal(result.EdgeAabbs[i], r.EdgeAabbs[i]);
            Assert.Equal(result.EdgeFlags[i], r.EdgeFlags[i]);
        }
    }
}
