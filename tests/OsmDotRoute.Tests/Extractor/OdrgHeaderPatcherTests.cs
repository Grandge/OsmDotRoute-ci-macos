using System;
using System.IO;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// <see cref="OdrgHeaderPatcher"/> — 既存 .odrg ヘッダーへの RequestedBbox 後付けテスト。
/// </summary>
public sealed class OdrgHeaderPatcherTests
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
            new EdgeRecord(1, 0, 1, Array.Empty<long>(),
                Array.Empty<int>(), Array.Empty<int>(), EmptyStringTable),
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
            Vertices: vertices, Edges: edges, EdgeAabbs: aabbs, EdgeFlags: flags,
            RTree: rtree, ProfileTable: table, NodeCoordLookup: _ => default,
            Bbox: new Aabb(136.70, 35.16, 136.78, 35.20), MetadataJson: "{}");
    }

    [Fact]
    public void Patch_ExistingOdrg_WritesRequestedBboxReadableByReader()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            using (var fs = File.Create(tmp))
                OdrgWriter.Write(fs, MinimalInput());

            // patch 前: RequestedBbox は未指定 (ゼロ)
            var before = OdrgReader.Read(tmp);
            Assert.Equal(0.0, before.Header.RequestedBbox.MinLon);

            // patch
            var requested = new Aabb(136.50, 35.10, 137.00, 35.30);
            OdrgHeaderPatcher.Patch(tmp, requested);

            // patch 後: RequestedBbox が読める + 本体セクションは無傷
            var after = OdrgReader.Read(tmp);
            Assert.True(after.Header.HasRequestedBbox);
            Assert.Equal(136.50, after.Header.RequestedBbox.MinLon);
            Assert.Equal(35.10, after.Header.RequestedBbox.MinLat);
            Assert.Equal(137.00, after.Header.RequestedBbox.MaxLon);
            Assert.Equal(35.30, after.Header.RequestedBbox.MaxLat);

            // 本体不変の確認
            Assert.Equal(before.Vertices.Length, after.Vertices.Length);
            Assert.Equal(before.Edges.Length, after.Edges.Length);
            Assert.Equal(before.ProfileTable.ProfileNames, after.ProfileTable.ProfileNames);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void Patch_NonOdrgFile_Throws()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, new byte[300]);  // マジック不一致
            Assert.Throws<InvalidDataException>(() =>
                OdrgHeaderPatcher.Patch(tmp, new Aabb(136.5, 35.1, 137.0, 35.3)));
        }
        finally
        {
            File.Delete(tmp);
        }
    }
}
