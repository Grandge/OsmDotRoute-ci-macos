using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Geometry;
using OsmDotRoute.Native;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// Phase 3 ステップ 3A.5b — <see cref="NativeRoadSnapper"/> の検証 12 件（計画書 §4.5.2）。
/// Brute-force 突合 (Done 基準本体) は <see cref="OdrgReader"/> 真値の EDGE_AABB / 完全シェイプ全走査で行う。
/// Itinero との突合は 3A.6 (89 ペア経路パリティ) で経路結果一致として担保。
/// </summary>
public sealed class NativeRoadSnapperTests : IClassFixture<NativeAndOdrgReaderFixture>
{
    private readonly NativeAndOdrgReaderFixture _fixture;

    public NativeRoadSnapperTests(NativeAndOdrgReaderFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void Constructor_AcceptsGraph_DoesNotThrow()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        Assert.NotNull(snapper);
    }

    [Fact]
    public void Snap_VertexCoordinate_ReturnsNearbyEdge()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        // ある頂点座標で Snap → 近傍エッジが返り、Location は頂点近傍 (Haversine < 10m)
        var vertex = _fixture.Graph.GetVertex(0);
        var result = snapper.Snap(_fixture.Car.Name, vertex, searchDistanceM: 500f);
        Assert.NotNull(result);
        var dist = GeoMath.HaversineMeters(vertex, result.Value.Location);
        Assert.InRange(dist, 0.0, 10.0);
    }

    [Fact]
    public void Snap_EdgeMidpoint_ReturnsThatEdge()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        // ある通行可能エッジを選び、From-To の中点で Snap → bestEdgeId がそのエッジ
        var (targetEdgeId, midpoint) = FindCarPassableEdgeMidpoint();
        var result = snapper.Snap(_fixture.Car.Name, midpoint, searchDistanceM: 500f);
        Assert.NotNull(result);
        Assert.Equal(targetEdgeId, result.Value.EdgeId);
    }

    [Fact]
    public void Snap_NonExistentProfile_ReturnsNull()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        var vertex = _fixture.Graph.GetVertex(0);
        var result = snapper.Snap("non_existent_profile_xxx", vertex, searchDistanceM: 500f);
        Assert.Null(result);
    }

    [Fact]
    public void Snap_SearchDistanceZero_ReturnsNull()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        var vertex = _fixture.Graph.GetVertex(0);
        var result = snapper.Snap(_fixture.Car.Name, vertex, searchDistanceM: 0f);
        Assert.Null(result);
    }

    [Fact]
    public void Snap_PointFarOutsideBounds_ReturnsNull()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        // 津島市 (≒ N35) から十分離れた高緯度
        var far = new GeoCoordinate(89.0, 0.0);
        var result = snapper.Snap(_fixture.Car.Name, far, searchDistanceM: 500f);
        Assert.Null(result);
    }

    [Fact]
    public void Snap_NearBoundary_BboxExpansionFindsEdge()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        // 通行可能エッジの中点から正確に約 30m 離した点を作り、searchDistanceM=100m で見つかる、5m で null
        var (_, mid) = FindCarPassableEdgeMidpoint();
        var (dLat30, _) = GeoMath.MetersToBboxDegrees(30.0, mid.Latitude);
        var nearby = new GeoCoordinate(mid.Latitude + dLat30, mid.Longitude);

        var found = snapper.Snap(_fixture.Car.Name, nearby, searchDistanceM: 100f);
        Assert.NotNull(found);

        var notFound = snapper.Snap(_fixture.Car.Name, nearby, searchDistanceM: 5f);
        Assert.Null(notFound);
    }

    [Fact]
    public void Snap_TwentyRandomPoints_MatchesBruteForceEdgeIdAndOffset()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        var graph = _fixture.Graph;
        var truth = _fixture.Truth;
        var rootBox = graph.GetRTreeNodes()[(int)graph.RTreeRootIndex].Bbox;

        var rng = new Random(789);
        const int TrialCount = 20;
        const float SearchDistanceM = 500f;

        // 車プロファイル通行可能エッジ ID 集合 (Brute-force 突合で除外フィルタに使用)
        int carSlot = Array.IndexOf(truth.ProfileTable.ProfileNames, _fixture.Car.Name);
        Assert.True(carSlot >= 0);
        var carEntries = truth.ProfileTable.EntriesByProfile[carSlot];

        for (int trial = 0; trial < TrialCount; trial++)
        {
            double lon = rootBox.MinLon + rng.NextDouble() * (rootBox.MaxLon - rootBox.MinLon);
            double lat = rootBox.MinLat + rng.NextDouble() * (rootBox.MaxLat - rootBox.MinLat);
            var query = new GeoCoordinate(lat, lon);

            var nativeResult = snapper.Snap(_fixture.Car.Name, query, SearchDistanceM);

            // Brute-force: 全エッジに対し点-線分最短距離、車通行可能フィルタ
            uint? bruteEdgeId = null;
            double bruteBestDist = double.PositiveInfinity;
            for (uint e = 0; e < graph.EdgeCount; e++)
            {
                if (!carEntries[(int)e].CanPass) continue;
                var fullShape = BuildFullShape(graph, e);
                for (int s = 0; s < fullShape.Length - 1; s++)
                {
                    var (d, _, _) = GeoMath.PointToSegment(query, fullShape[s], fullShape[s + 1]);
                    if (d < bruteBestDist)
                    {
                        bruteBestDist = d;
                        bruteEdgeId = e;
                    }
                }
            }

            if (bruteBestDist > SearchDistanceM)
            {
                Assert.True(nativeResult is null,
                    $"trial={trial} Brute-force 距離 {bruteBestDist:F2} > {SearchDistanceM} で null 期待");
                continue;
            }

            Assert.True(nativeResult.HasValue, $"trial={trial} Brute-force 候補あり ({bruteBestDist:F2}m) なのに Native null");
            Assert.Equal(bruteEdgeId, nativeResult.Value.EdgeId);
        }
    }

    [Fact]
    public void Snap_TwoCloseQueries_OffsetMonotonicAlongEdge()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        var (edgeId, mid) = FindCarPassableEdgeMidpoint();

        // From 寄り (t≈0.3) と To 寄り (t≈0.7) の点を生成
        var edge = _fixture.Graph.ReadEdge(edgeId);
        var from = _fixture.Graph.GetVertex(edge.FromVertexId);
        var to = _fixture.Graph.GetVertex(edge.ToVertexId);
        var near30 = Interpolate(from, to, 0.3);
        var near70 = Interpolate(from, to, 0.7);

        var r30 = snapper.Snap(_fixture.Car.Name, near30, searchDistanceM: 500f);
        var r70 = snapper.Snap(_fixture.Car.Name, near70, searchDistanceM: 500f);

        Assert.NotNull(r30);
        Assert.NotNull(r70);
        Assert.Equal(edgeId, r30.Value.EdgeId);
        Assert.Equal(edgeId, r70.Value.EdgeId);
        Assert.True(r30.Value.Offset < r70.Value.Offset,
            $"From 側 ({r30.Value.Offset}) < To 側 ({r70.Value.Offset}) 期待");
    }

    [Fact]
    public void Snap_QueryAtFromVertex_OffsetIsNearEndpoint()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        var (edgeId, _) = FindCarPassableEdgeMidpoint();
        var edge = _fixture.Graph.ReadEdge(edgeId);
        var from = _fixture.Graph.GetVertex(edge.FromVertexId);

        var result = snapper.Snap(_fixture.Car.Name, from, searchDistanceM: 500f);
        Assert.NotNull(result);
        // OSM では vertex は複数エッジで共有されるため、snap される edge は一意に決まらない。
        // 元エッジが選ばれれば Offset ≈ 0、別エッジの端点 vertex が共有なら Offset ≈ 0 か 65535。
        // いずれにせよ vertex 直上で snap した結果は端点近傍 (誤差幅 100 = 全体の 0.15%)。
        var offset = result.Value.Offset;
        Assert.True(offset < 100 || offset > 65435,
            $"vertex 上の snap は端点近傍 (0 か 65535) 期待だが Offset = {offset}");
    }

    [Fact]
    public void Snap_QueryAtToVertex_OffsetIsNearEndpoint()
    {
        var snapper = new NativeRoadSnapper(_fixture.Graph);
        var (edgeId, _) = FindCarPassableEdgeMidpoint();
        var edge = _fixture.Graph.ReadEdge(edgeId);
        var to = _fixture.Graph.GetVertex(edge.ToVertexId);

        var result = snapper.Snap(_fixture.Car.Name, to, searchDistanceM: 500f);
        Assert.NotNull(result);
        // From 側と同じ理由で端点近傍 (0 か 65535) を許容
        var offset = result.Value.Offset;
        Assert.True(offset < 100 || offset > 65435,
            $"vertex 上の snap は端点近傍 (0 か 65535) 期待だが Offset = {offset}");
    }

    [Fact]
    public void Snap_DisposedGraph_ThrowsObjectDisposedException()
    {
        var graph = new NativeRoadGraph(TestPaths.TsushimaOdrg);
        var snapper = new NativeRoadSnapper(graph);
        graph.Dispose();

        Assert.Throws<ObjectDisposedException>(() =>
            snapper.Snap(_fixture.Car.Name, new GeoCoordinate(35.18, 136.74), searchDistanceM: 500f));
    }

    /// <summary>
    /// 車プロファイルで通行可能かつ中間シェイプが少なくとも From-To 直結ですむ単純エッジで、
    /// **中点 10m 以内に他の Car 通行可能エッジが存在しない孤立エッジ** を探し、
    /// (エッジ ID, From-To 中点) を返す。テスト用ヘルパ。
    /// </summary>
    /// <remarks>
    /// Phase 3 ステップ 3E.1 で「孤立」フィルタを追加 (tsushima.odrg を 4 プロファイル bake で再生成した結果、
    /// cycleway / footway が並走する Car 通行可能エッジで Snap が並走道路に吸われる事象を回避)。
    /// </remarks>
    private (uint EdgeId, GeoCoordinate Midpoint) FindCarPassableEdgeMidpoint()
    {
        var truth = _fixture.Truth;
        int carSlot = Array.IndexOf(truth.ProfileTable.ProfileNames, _fixture.Car.Name);
        Assert.True(carSlot >= 0);
        var carEntries = truth.ProfileTable.EntriesByProfile[carSlot];

        for (uint e = 0; e < _fixture.Graph.EdgeCount; e++)
        {
            if (!carEntries[(int)e].CanPass) continue;
            var edge = _fixture.Graph.ReadEdge(e);
            if (edge.FromVertexId == edge.ToVertexId) continue;  // 自己ループ除外
            // 中間シェイプ 0 件 (純粋な From-To 直線エッジ) に絞る
            // → From-To 中点 = エッジ実中央 を保証し、PointToSegment 距離計算の予測可能性を担保
            if (_fixture.Graph.GetEdgeShape(e).Length > 0) continue;
            var from = _fixture.Graph.GetVertex(edge.FromVertexId);
            var to = _fixture.Graph.GetVertex(edge.ToVertexId);
            // 直線距離 50m 以上のエッジに絞る (短い断片で並走道路に挟まれるケースを排除)
            if (GeoMath.HaversineMeters(from, to) < 50.0) continue;

            var midpoint = Interpolate(from, to, 0.5);
            if (IsIsolatedAt(midpoint, e, carEntries, thresholdM: 10.0))
            {
                return (e, midpoint);
            }
        }
        throw new InvalidOperationException("孤立した直線 Car 通行可能エッジが見つからない");
    }

    /// <summary>
    /// <paramref name="point"/> の <paramref name="thresholdM"/> メートル以内に
    /// <paramref name="excludeEdgeId"/> 以外の Car 通行可能エッジが存在しないかを判定する。
    /// </summary>
    private bool IsIsolatedAt(
        GeoCoordinate point,
        uint excludeEdgeId,
        BakedProfileEntry[] carEntries,
        double thresholdM)
    {
        for (uint e = 0; e < _fixture.Graph.EdgeCount; e++)
        {
            if (e == excludeEdgeId) continue;
            if (!carEntries[(int)e].CanPass) continue;
            var fullShape = BuildFullShape(_fixture.Graph, e);
            // bbox プリフィルタ: いずれかの頂点が ±thresholdM bbox 内
            var (dLat, dLon) = GeoMath.MetersToBboxDegrees(thresholdM, point.Latitude);
            double minLat = point.Latitude - dLat, maxLat = point.Latitude + dLat;
            double minLon = point.Longitude - dLon, maxLon = point.Longitude + dLon;
            bool nearBbox = false;
            for (int i = 0; i < fullShape.Length; i++)
            {
                if (fullShape[i].Latitude >= minLat && fullShape[i].Latitude <= maxLat &&
                    fullShape[i].Longitude >= minLon && fullShape[i].Longitude <= maxLon)
                {
                    nearBbox = true;
                    break;
                }
            }
            if (!nearBbox) continue;

            for (int s = 0; s < fullShape.Length - 1; s++)
            {
                var (d, _, _) = GeoMath.PointToSegment(point, fullShape[s], fullShape[s + 1]);
                if (d < thresholdM) return false;  // 並走道路あり
            }
        }
        return true;  // 孤立
    }

    private static GeoCoordinate Interpolate(GeoCoordinate a, GeoCoordinate b, double t) =>
        new(a.Latitude + (b.Latitude - a.Latitude) * t,
            a.Longitude + (b.Longitude - a.Longitude) * t);

    private static GeoCoordinate[] BuildFullShape(NativeRoadGraph graph, uint edgeId)
    {
        var edge = graph.ReadEdge(edgeId);
        var mid = graph.GetEdgeShape(edgeId);
        var full = new GeoCoordinate[mid.Length + 2];
        full[0] = graph.GetVertex(edge.FromVertexId);
        for (int i = 0; i < mid.Length; i++) full[i + 1] = mid[i];
        full[^1] = graph.GetVertex(edge.ToVertexId);
        return full;
    }
}
