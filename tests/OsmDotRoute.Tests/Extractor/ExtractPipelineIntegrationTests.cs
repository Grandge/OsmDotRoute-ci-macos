using System.Buffers.Binary;
using System.IO;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Tests.TestData;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.9 — <see cref="ExtractPipeline"/> + <see cref="OdrgWriter"/> 統合テスト
/// (津島市抽出 PBF を入力)。
/// </summary>
public sealed class ExtractPipelineIntegrationTests
{
    /// <summary>津島市を内包する bbox（PbfReader 統合テストと整合）。</summary>
    private static readonly Aabb TsushimaBbox = new(
        MinLon: 136.65, MinLat: 35.13,
        MaxLon: 136.80, MaxLat: 35.25);

    private static ExtractPipelineOptions DefaultOptions() => new(
        InputPbf: TestPaths.TsushimaExtractPbf,
        Bbox: TsushimaBbox,
        Profiles: new[] { VehicleProfile.Car, VehicleProfile.Pedestrian });

    // 親プロジェクト由来 PBF はリポジトリに含めない（CLAUDE.md）。CI など不在の環境では当該テストをスキップする。
    private static bool EnsureTestData() => File.Exists(TestPaths.TsushimaExtractPbf);

    [Fact]
    public void Run_TsushimaPbf_ProducesNonEmptyGraph()
    {
        if (!EnsureTestData()) return;

        var result = ExtractPipeline.Run(DefaultOptions());

        Assert.True(result.Vertices.Length > 1000,
            $"頂点数 {result.Vertices.Length} が予想より少ない (1000+ 期待)");
        Assert.True(result.Edges.Length > result.Vertices.Length,
            $"エッジ数 ({result.Edges.Length}) が頂点数 ({result.Vertices.Length}) より少ない");
        Assert.Equal(result.Edges.Length, result.EdgeAabbs.Length);
        Assert.Equal(result.Edges.Length, result.EdgeFlags.Length);
        Assert.Equal(result.Edges.Length, result.ProfileTable.EdgeCount);
        Assert.Equal(2, result.ProfileTable.ProfileCount);
    }

    [Fact]
    public void Run_TsushimaPbf_VertexCoordinatesInTsushimaArea()
    {
        if (!EnsureTestData()) return;
        var result = ExtractPipeline.Run(DefaultOptions());

        // 全頂点が津島市付近 (経度 136-137、緯度 35-36) にあるはず
        foreach (var v in result.Vertices)
        {
            Assert.InRange(v.Longitude, 136.0, 137.5);
            Assert.InRange(v.Latitude, 35.0, 35.5);
        }
    }

    [Fact]
    public void Run_TsushimaPbf_EdgeReferencesValidVertices()
    {
        if (!EnsureTestData()) return;
        var result = ExtractPipeline.Run(DefaultOptions());

        int vMax = result.Vertices.Length;
        foreach (var e in result.Edges)
        {
            Assert.InRange(e.FromVertexId, 0, vMax - 1);
            Assert.InRange(e.ToVertexId, 0, vMax - 1);
        }
    }

    [Fact]
    public void Run_TsushimaPbf_EdgePermutationIsBijection()
    {
        if (!EnsureTestData()) return;
        var result = ExtractPipeline.Run(DefaultOptions());

        var set = new HashSet<int>();
        foreach (int oldId in result.RTree.EdgePermutation)
        {
            Assert.True(set.Add(oldId), $"permutation で重複: {oldId}");
            Assert.InRange(oldId, 0, result.Edges.Length - 1);
        }
        Assert.Equal(result.Edges.Length, set.Count);
    }

    [Fact]
    public void Run_TsushimaPbf_RTreeRootBoundsCoverFileBbox()
    {
        if (!EnsureTestData()) return;
        var result = ExtractPipeline.Run(DefaultOptions());

        if (result.Edges.Length == 0) return;
        var root = result.RTree.Nodes[result.RTree.RootIndex];

        // root の Bounds は全エッジを内包するため fileBbox を包含するはず
        Assert.True(root.Bounds.MinLon <= result.FileBbox.MinLon + 1e-9);
        Assert.True(root.Bounds.MaxLon >= result.FileBbox.MaxLon - 1e-9);
        Assert.True(root.Bounds.MinLat <= result.FileBbox.MinLat + 1e-9);
        Assert.True(root.Bounds.MaxLat >= result.FileBbox.MaxLat - 1e-9);
    }

    [Fact]
    public void Run_TsushimaPbf_NarrowerBbox_ProducesSmallerGraph()
    {
        if (!EnsureTestData()) return;

        var fullResult = ExtractPipeline.Run(DefaultOptions());

        // 1/4 程度の狭い bbox で抽出
        var narrowOpts = DefaultOptions() with
        {
            Bbox = new Aabb(MinLon: 136.71, MinLat: 35.17, MaxLon: 136.74, MaxLat: 35.19),
        };
        var narrowResult = ExtractPipeline.Run(narrowOpts);

        Assert.True(narrowResult.Vertices.Length < fullResult.Vertices.Length,
            $"狭い bbox の頂点数 {narrowResult.Vertices.Length} が広い bbox の {fullResult.Vertices.Length} 以上");
        Assert.True(narrowResult.Edges.Length < fullResult.Edges.Length);
        Assert.True(narrowResult.Vertices.Length > 0);
    }

    [Fact]
    public void Run_TsushimaPbf_BakedProfilesAreReasonable()
    {
        if (!EnsureTestData()) return;
        var result = ExtractPipeline.Run(DefaultOptions());

        ReadOnlySpan<BakedProfileEntry> carEntries = result.ProfileTable.GetProfileEntries(0);
        ReadOnlySpan<BakedProfileEntry> pedEntries = result.ProfileTable.GetProfileEntries(1);

        int carPassable = 0, pedPassable = 0;
        for (int i = 0; i < carEntries.Length; i++)
        {
            if (carEntries[i].CanPass) carPassable++;
            if (pedEntries[i].CanPass) pedPassable++;
        }

        // 大半のエッジは car または pedestrian のどちらかで通行可能
        Assert.True(carPassable > 0, "車で通れるエッジが 0");
        Assert.True(pedPassable > 0, "歩行者で通れるエッジが 0");
    }

    [Fact]
    public void RunAndWrite_TsushimaPbf_OdrgFileIsValid()
    {
        if (!EnsureTestData()) return;
        var result = ExtractPipeline.Run(DefaultOptions());

        var writeInput = new OdrgWriteInput(
            Vertices: result.Vertices,
            Edges: result.Edges,
            EdgeAabbs: result.EdgeAabbs,
            EdgeFlags: result.EdgeFlags,
            RTree: result.RTree,
            ProfileTable: result.ProfileTable,
            NodeCoordLookup: result.NodeCoordLookup,
            Bbox: result.FileBbox,
            MetadataJson: "{\"test\":true}");

        using var ms = new MemoryStream();
        OdrgWriter.Write(ms, writeInput);
        byte[] bytes = ms.ToArray();

        // マジック
        Assert.Equal((byte)'O', bytes[0]);
        Assert.Equal((byte)'D', bytes[1]);
        Assert.Equal((byte)'R', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);

        // 頂点数とエッジ数がヘッダーに正しく反映
        ulong headerVertexCount = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16, 8));
        ulong headerEdgeCount = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(24, 8));
        Assert.Equal((ulong)result.Vertices.Length, headerVertexCount);
        Assert.Equal((ulong)result.Edges.Length, headerEdgeCount);

        // 9 セクション
        Assert.Equal(9u, BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(80, 4)));

        // 最終セクション (Metadata) の終端 = ファイル末尾
        long lastOff = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 8 + 8, 8));
        long lastLen = (long)BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(256 + 24 * 8 + 16, 8));
        Assert.Equal(bytes.Length, lastOff + lastLen);

        // 出力ファイルサイズが「100K vertices × 16B = 1.6MB 以上」「100MB 未満」の範囲
        Assert.InRange(bytes.Length, 100_000, 100_000_000);
    }

    [Fact]
    public void Run_MissingPbfFile_Throws()
    {
        var opts = DefaultOptions() with { InputPbf = "nonexistent.pbf" };
        Assert.Throws<FileNotFoundException>(() => ExtractPipeline.Run(opts));
    }

    [Fact]
    public void Run_EmptyProfiles_Throws()
    {
        if (!EnsureTestData()) return;
        var opts = DefaultOptions() with { Profiles = Array.Empty<VehicleProfile>() };
        Assert.Throws<ArgumentException>(() => ExtractPipeline.Run(opts));
    }

    [Fact]
    public void Run_NullOpts_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => ExtractPipeline.Run(null!));
    }
}
