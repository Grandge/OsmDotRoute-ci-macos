using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Native;
using OsmDotRoute.Profiles;
using OsmDotRoute.Routing;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// 津島市 `.odrg` を <see cref="NativeRoadGraph"/> と <see cref="OdrgReader"/> でロードする共有 fixture。
/// </summary>
public sealed class NativeAndOdrgReaderFixture : IDisposable
{
    internal NativeRoadGraph Graph { get; }
    internal OdrgReadResult Truth { get; }
    internal VehicleProfile Car { get; }
    internal VehicleProfile Pedestrian { get; }

    public NativeAndOdrgReaderFixture()
    {
        if (!File.Exists(TestPaths.TsushimaOdrg))
        {
            Assert.Fail($"リポジトリ同梱の津島.odrg が見つかりません: {TestPaths.TsushimaOdrg}");
        }
        Graph = new NativeRoadGraph(TestPaths.TsushimaOdrg);
        Truth = OdrgReader.Read(TestPaths.TsushimaOdrg);
        Car = VehicleProfile.Car;
        Pedestrian = VehicleProfile.Pedestrian;
    }

    public void Dispose() => Graph.Dispose();
}

/// <summary>
/// Phase 3 ステップ 3A.3f — <see cref="NativeRoadGraph"/> と <see cref="OdrgReader"/> 真値の突合
/// による自己整合性パリティテスト 9 件。Itinero RouterDb との突合は 3A.6 (89 ペア経路パリティ) で担保。
/// </summary>
public sealed class NativeRoadGraphParityTests : IClassFixture<NativeAndOdrgReaderFixture>
{
    private readonly NativeAndOdrgReaderFixture _fixture;

    public NativeRoadGraphParityTests(NativeAndOdrgReaderFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void GetVertex_Sample100_MatchesOdrgReaderCoordinates()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;

        var vertexCount = graph.VertexCount;
        uint step = Math.Max(1u, vertexCount / 100);
        int sampled = 0;

        for (uint v = 0; v < vertexCount && sampled < 100; v += step)
        {
            var actual = graph.GetVertex(v);
            var expected = truth.Vertices[v];
            Assert.Equal(expected.Latitude, actual.Latitude);
            Assert.Equal(expected.Longitude, actual.Longitude);
            sampled++;
        }
        Assert.True(sampled > 0, "頂点サンプルが取れること");
    }

    [Fact]
    public void GetEdgeEnumerator_Sample100Edges_FromToAndDataInvertedConsistentWithOdrgReader()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;

        int edgeCount = truth.Edges.Length;
        int step = Math.Max(1, edgeCount / 100);
        int sampled = 0;

        for (int e = 0; e < edgeCount && sampled < 100; e += step)
        {
            var expected = truth.Edges[e];
            var startVertex = expected.FromVertexId;

            // 起点頂点から列挙し、対象 edgeId を発見できることを確認
            var en = graph.GetEdgeEnumerator(startVertex);
            bool found = false;
            while (en.MoveNext())
            {
                if (en.EdgeId == (uint)e)
                {
                    Assert.False(en.DataInverted, "From 側から列挙したとき DataInverted=false");
                    Assert.Equal(expected.FromVertexId, en.From);
                    Assert.Equal(expected.ToVertexId, en.To);
                    found = true;
                    break;
                }
            }
            Assert.True(found, $"edgeId={e} を起点頂点 {startVertex} から列挙できなかった");

            // 逆側 (ToVertexId 側) から列挙して反転エントリも検証
            if (expected.ToVertexId != expected.FromVertexId)
            {
                var enRev = graph.GetEdgeEnumerator(expected.ToVertexId);
                bool foundRev = false;
                while (enRev.MoveNext())
                {
                    if (enRev.EdgeId == (uint)e)
                    {
                        Assert.True(enRev.DataInverted, "To 側から列挙したとき DataInverted=true");
                        Assert.Equal(expected.ToVertexId, enRev.From);
                        Assert.Equal(expected.FromVertexId, enRev.To);
                        foundRev = true;
                        break;
                    }
                }
                Assert.True(foundRev, $"edgeId={e} を反転側頂点 {expected.ToVertexId} から列挙できなかった");
            }

            sampled++;
        }
        Assert.True(sampled > 0);
    }

    [Fact]
    public void GetEdge_Sample50_ShapeMatchesOdrgReader()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;

        int edgeCount = truth.Edges.Length;
        int step = Math.Max(1, edgeCount / 50);
        int sampled = 0;

        for (int e = 0; e < edgeCount && sampled < 50; e += step)
        {
            var roadEdge = graph.GetEdge((uint)e);
            var expectedShape = truth.EdgeShapes[e];

            Assert.Equal(expectedShape.Length, roadEdge.Shape.Count);
            for (int i = 0; i < expectedShape.Length; i++)
            {
                Assert.Equal(expectedShape[i].Latitude, roadEdge.Shape[i].Latitude);
                Assert.Equal(expectedShape[i].Longitude, roadEdge.Shape[i].Longitude);
            }
            sampled++;
        }
        Assert.True(sampled > 0);
    }

    [Fact]
    public void GetEdgeShape_Span_ContainsSameElementsAsGetEdgeList()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;

        int edgeCount = truth.Edges.Length;
        int step = Math.Max(1, edgeCount / 50);
        int sampled = 0;

        for (int e = 0; e < edgeCount && sampled < 50; e += step)
        {
            uint edgeId = (uint)e;
            var listShape = graph.GetEdge(edgeId).Shape;
            var spanShape = graph.GetEdgeShape(edgeId);

            Assert.Equal(listShape.Count, spanShape.Length);
            for (int i = 0; i < listShape.Count; i++)
            {
                Assert.Equal(listShape[i], spanShape[i]);
            }
            sampled++;
        }
        Assert.True(sampled > 0);
    }

    [Fact]
    public void GetEdgeShape_CalledTwice_ReturnsSameCachedArray()
    {
        var graph = _fixture.Graph;
        // 中間点を持つエッジを 1 本見つける (シェイプ 0 のエッジだとキャッシュ動作確認にならない)
        uint targetEdgeId = uint.MaxValue;
        var truth = _fixture.Truth;
        for (int e = 0; e < truth.Edges.Length; e++)
        {
            if (truth.EdgeShapes[e].Length > 0)
            {
                targetEdgeId = (uint)e;
                break;
            }
        }
        Assert.NotEqual(uint.MaxValue, targetEdgeId);

        var first = graph.GetOrBuildShape(targetEdgeId);
        var second = graph.GetOrBuildShape(targetEdgeId);
        Assert.Same(first, second);
    }

    [Fact]
    public void EvaluateEdge_Enumerator_Sample50TimesCarPedestrian_MatchesBakedProfileEntry()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;
        var profiles = new[] { _fixture.Car, _fixture.Pedestrian };

        int edgeCount = truth.Edges.Length;
        int step = Math.Max(1, edgeCount / 50);

        foreach (var profile in profiles)
        {
            var slotIndex = Array.IndexOf(truth.ProfileTable.ProfileNames, profile.Name);
            Assert.True(slotIndex >= 0, $"プロファイル '{profile.Name}' が .odrg に存在すること");
            var entries = truth.ProfileTable.EntriesByProfile[slotIndex];

            int sampled = 0;
            for (int e = 0; e < edgeCount && sampled < 50; e += step)
            {
                uint edgeId = (uint)e;
                var en = graph.GetEdgeEnumerator(truth.Edges[e].FromVertexId);
                bool found = false;
                while (en.MoveNext())
                {
                    if (en.EdgeId == edgeId)
                    {
                        var actual = graph.EvaluateEdge(en, profile.Evaluator);
                        var expectedEntry = entries[e];
                        AssertEvaluationMatchesEntry(profile.Name, edgeId, expectedEntry, actual);
                        found = true;
                        break;
                    }
                }
                Assert.True(found, $"profile={profile.Name} edgeId={edgeId} を起点から列挙できなかった");
                sampled++;
            }
            Assert.True(sampled > 0);
        }
    }

    [Fact]
    public void EvaluateEdge_RoadEdge_Sample50TimesCarPedestrian_MatchesBakedProfileEntry()
    {
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;
        var profiles = new[] { _fixture.Car, _fixture.Pedestrian };

        int edgeCount = truth.Edges.Length;
        int step = Math.Max(1, edgeCount / 50);

        foreach (var profile in profiles)
        {
            var slotIndex = Array.IndexOf(truth.ProfileTable.ProfileNames, profile.Name);
            Assert.True(slotIndex >= 0);
            var entries = truth.ProfileTable.EntriesByProfile[slotIndex];

            int sampled = 0;
            for (int e = 0; e < edgeCount && sampled < 50; e += step)
            {
                uint edgeId = (uint)e;
                var roadEdge = graph.GetEdge(edgeId);
                var actual = graph.EvaluateEdge(roadEdge, profile.Evaluator);
                var expectedEntry = entries[e];
                AssertEvaluationMatchesEntry(profile.Name, edgeId, expectedEntry, actual);
                sampled++;
            }
            Assert.True(sampled > 0);
        }
    }

    [Fact]
    public void Constructor_NonExistentPath_ThrowsFileNotFoundException()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".odrg");
        Assert.False(File.Exists(fakePath), "テスト前提: パスは存在しない");
        Assert.Throws<FileNotFoundException>(() => new NativeRoadGraph(fakePath));
    }

    [Fact]
    public void Constructor_InvalidMagicBytes_ThrowsOdrgFormatException()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".odrg");
        try
        {
            // tsushima.odrg のコピーを作り、マジック先頭バイトを 0xFF で潰す
            File.Copy(TestPaths.TsushimaOdrg, tempPath);
            using (var fs = File.OpenWrite(tempPath))
            {
                fs.WriteByte(0xFF);
            }
            Assert.Throws<OdrgFormatException>(() => new NativeRoadGraph(tempPath));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    private static void AssertEvaluationMatchesEntry(
        string profileName, uint edgeId, BakedProfileEntry expected, EdgeEvaluation actual)
    {
        Assert.Equal(expected.CanPass, actual.CanPass);
        if (!expected.CanPass)
        {
            Assert.Equal(0f, actual.SpeedKmh);
            return;
        }
        Assert.Equal(expected.SpeedKmh, actual.SpeedKmh);

        OnewayDirection expectedOneway = (expected.Forward, expected.Backward) switch
        {
            (true, true) => OnewayDirection.Bidirectional,
            (true, false) => OnewayDirection.Forward,
            (false, true) => OnewayDirection.Backward,
            _ => OnewayDirection.Bidirectional,
        };
        Assert.Equal(expectedOneway, actual.Oneway);
    }
}
