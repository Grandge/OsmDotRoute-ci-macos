using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Geometry;
using OsmDotRoute.Internal.Odrg;
using OsmDotRoute.Native;
using OsmDotRoute.Profiles;
using OsmDotRoute.Tests.TestData;
using Xunit;

namespace OsmDotRoute.Tests.Native;

/// <summary>
/// Phase 3 ステップ 3A.6 — Native 系統 (<see cref="NativeRoadGraph"/> + <see cref="NativeRoadSnapper"/>) を
/// <see cref="OsmDotRoute.RouterDb"/> / <see cref="OsmDotRoute.Router"/> に組み込んだ共有 fixture（計画書 §4.6-A、Q6）。
/// </summary>
/// <remarks>
/// 通行可能エッジの探索および Router.Calculate が成功する短距離 / 中距離ペアを 1 度だけ事前計算し、
/// 16 件のテストで使い回す。IClassFixture で xUnit がクラスごとに 1 インスタンスのみ生成する保証を活用。
/// </remarks>
public sealed class NativeRouterDbFixture : IDisposable
{
    internal NativeRoadGraph Graph { get; }
    internal NativeRoadSnapper Snapper { get; }
    internal OdrgReadResult Truth { get; }
    internal OsmDotRoute.RouterDb RouterDb { get; }
    internal OsmDotRoute.Router Router { get; }
    internal VehicleProfile Car { get; }

    /// <summary>通行可能エッジの From 頂点座標（同一点テスト用）</summary>
    internal GeoCoordinate SamePoint { get; }

    /// <summary>~100m 程度離れた、Router.Calculate (双方向とも) が成功する頂点ペア</summary>
    internal (GeoCoordinate From, GeoCoordinate To) ShortPair { get; }

    /// <summary>~1km 程度離れた、Router.Calculate (双方向とも) が成功する頂点ペア</summary>
    internal (GeoCoordinate From, GeoCoordinate To) MediumPair { get; }

    public NativeRouterDbFixture()
    {
        if (!File.Exists(TestPaths.TsushimaOdrg))
        {
            Assert.Fail($"リポジトリ同梱の津島.odrg が見つかりません: {TestPaths.TsushimaOdrg}");
        }
        Graph = new NativeRoadGraph(TestPaths.TsushimaOdrg);
        Snapper = new NativeRoadSnapper(Graph);
        Truth = OdrgReader.Read(TestPaths.TsushimaOdrg);
        RouterDb = new OsmDotRoute.RouterDb(Graph, Snapper);
        Router = new OsmDotRoute.Router(RouterDb);
        Car = VehicleProfile.Car;

        SamePoint = FindFirstCarPassableFromVertex();
        ShortPair = FindWorkingPair(targetMeters: 100.0, toleranceRatio: 0.6, seed: 42);
        MediumPair = FindWorkingPair(targetMeters: 1000.0, toleranceRatio: 0.5, seed: 43);
    }

    public void Dispose() => Graph.Dispose();

    private GeoCoordinate FindFirstCarPassableFromVertex()
    {
        int carSlot = Array.IndexOf(Truth.ProfileTable.ProfileNames, Car.Name);
        if (carSlot < 0) throw new InvalidOperationException("Car プロファイルが BAKED_PROFILE に存在しない");
        var carEntries = Truth.ProfileTable.EntriesByProfile[carSlot];

        for (uint e = 0; e < Graph.EdgeCount; e++)
        {
            if (!carEntries[(int)e].CanPass) continue;
            var edge = Graph.ReadEdge(e);
            return Graph.GetVertex(edge.FromVertexId);
        }
        throw new InvalidOperationException("車両通行可能エッジが見つからない");
    }

    /// <summary>
    /// 直線距離が targetMeters ± toleranceRatio の範囲に収まり、
    /// 双方向 (from→to と to→from) で Router.Calculate が null を返さない頂点ペアをランダム試行で探す。
    /// </summary>
    private (GeoCoordinate From, GeoCoordinate To) FindWorkingPair(double targetMeters, double toleranceRatio, int seed)
    {
        int carSlot = Array.IndexOf(Truth.ProfileTable.ProfileNames, Car.Name);
        if (carSlot < 0) throw new InvalidOperationException("Car プロファイルが BAKED_PROFILE に存在しない");
        var carEntries = Truth.ProfileTable.EntriesByProfile[carSlot];

        var passable = new List<uint>();
        for (uint e = 0; e < Graph.EdgeCount; e++)
        {
            if (carEntries[(int)e].CanPass) passable.Add(e);
        }
        if (passable.Count < 2) throw new InvalidOperationException("車両通行可能エッジが 2 件未満");

        double minDist = targetMeters * (1.0 - toleranceRatio);
        double maxDist = targetMeters * (1.0 + toleranceRatio);

        var rng = new Random(seed);
        const int MaxTrials = 1000;
        for (int trial = 0; trial < MaxTrials; trial++)
        {
            var e1 = passable[rng.Next(passable.Count)];
            var e2 = passable[rng.Next(passable.Count)];
            if (e1 == e2) continue;

            var from = Graph.GetVertex(Graph.ReadEdge(e1).FromVertexId);
            var to = Graph.GetVertex(Graph.ReadEdge(e2).FromVertexId);
            var d = GeoMath.HaversineMeters(from, to);
            if (d < minDist || d > maxDist) continue;

            var forward = Router.Calculate(Car, from, to);
            if (forward is null) continue;
            var reverse = Router.Calculate(Car, to, from);
            if (reverse is null) continue;

            return (from, to);
        }
        throw new InvalidOperationException(
            $"Working pair (target={targetMeters}m, ±{toleranceRatio:P0}) が {MaxTrials} 試行で見つからない");
    }
}
