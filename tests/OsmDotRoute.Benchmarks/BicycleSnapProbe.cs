using OsmDotRoute.Benchmarks.Generators;
using OsmDotRoute.Geometry;

namespace OsmDotRoute.Benchmarks;

/// <summary>
/// 既存 route-pairs.json (Car スナップ成功 100 ペア) を Bicycle プロファイルで
/// Calculate し、成功 / Snap 失敗 / 経路発見失敗の件数を出力する診断ツール
/// （Phase 3 ステップ 3E.1、計画書 §4.1）。
/// </summary>
/// <remarks>
/// <para>
/// 3E.3 完了後の追加診断 (ステップ 3E 軽微逸脱、ユーザー指摘
/// 「日本では高速道路以外は自転車通行可、スナップ失敗 35% は異常」を受けて):
/// </para>
/// <list type="bullet">
///   <item>Car / Bicycle / Pedestrian の通行可能エッジ数を集計し、集合関係 (Bicycle ⊇ Car ?) を検証</item>
///   <item>Snap 失敗ペアの最近傍 Bicycle 通行可エッジまでの距離を実測し、500m 探索半径の妥当性を確認</item>
/// </list>
/// 結果は <c>phase3_benchmark_results.md §3.3 C2 Bicycle スナップ失敗率</c> に転記する。
/// ベンチマーク本体には組み込まれない (BenchmarkDotNet の対象外、コンソール出力のみ)。
/// </remarks>
internal static class BicycleSnapProbe
{
    public static void Run()
    {
        Console.WriteLine($"== Bicycle Snap Probe ==");
        Console.WriteLine($"津島市 .odrg: {BenchmarkAssets.TsushimaOdrgPath}");
        var (routerDb, graph) = BenchmarkAssets.LoadNativeRouterDb();
        try
        {
            var router = new Router(routerDb);
            var profile = VehicleProfile.Bicycle;
            var pairs = TestDataInitializer.LoadRoutePairs().Pairs;
            Console.WriteLine($"ペア数: {pairs.Count}");
            Console.WriteLine();

            // === 通行可能エッジ数の比較 ===
            int edgeCount = (int)routerDb.GetStatistics().EdgeCount;
            int carPassable = CountPassableEdges(graph, VehicleProfile.Car.Name, edgeCount);
            int bicyclePassable = CountPassableEdges(graph, VehicleProfile.Bicycle.Name, edgeCount);
            int pedestrianPassable = CountPassableEdges(graph, VehicleProfile.Pedestrian.Name, edgeCount);
            int truckPassable = CountPassableEdges(graph, VehicleProfile.Truck.Name, edgeCount);
            Console.WriteLine($"-- 通行可能エッジ数 (全 {edgeCount:N0} エッジ中) --");
            Console.WriteLine($"  Car:        {carPassable,6:N0} ({100.0 * carPassable / edgeCount:F1}%)");
            Console.WriteLine($"  Bicycle:    {bicyclePassable,6:N0} ({100.0 * bicyclePassable / edgeCount:F1}%)");
            Console.WriteLine($"  Pedestrian: {pedestrianPassable,6:N0} ({100.0 * pedestrianPassable / edgeCount:F1}%)");
            Console.WriteLine($"  Truck:      {truckPassable,6:N0} ({100.0 * truckPassable / edgeCount:F1}%)");
            Console.WriteLine();

            // === Car スナップ判定 (新 odrg で route-pairs.json が妥当かの仮説検証) ===
            Console.WriteLine("-- Car (route-pairs.json 生成時と同じ profile) スナップ結果 --");
            int carSuccess = 0, carFromFail = 0, carToFail = 0, carRouteFail = 0;
            foreach (var pair in pairs)
            {
                var fromSnap = router.SnapToRoad(VehicleProfile.Car, pair.From, 500f);
                if (fromSnap is null) { carFromFail++; continue; }
                var toSnap = router.SnapToRoad(VehicleProfile.Car, pair.To, 500f);
                if (toSnap is null) { carToFail++; continue; }
                var route = router.Calculate(VehicleProfile.Car, pair.From, pair.To);
                if (route is null) carRouteFail++; else carSuccess++;
            }
            Console.WriteLine($"  成功:             {carSuccess,3} / 100 ({carSuccess}%)");
            Console.WriteLine($"  From スナップ失敗: {carFromFail,3} / 100");
            Console.WriteLine($"  To スナップ失敗:   {carToFail,3} / 100");
            Console.WriteLine($"  経路発見失敗:     {carRouteFail,3} / 100");
            Console.WriteLine();

            // === Bicycle スナップ判定 ===
            const float snapDistanceM = 500f;
            var success = 0;
            var fromSnapFail = 0;
            var toSnapFail = 0;
            var routeFail = 0;
            var fromFailDistances = new List<double>();
            var toFailDistances = new List<double>();

            foreach (var pair in pairs)
            {
                var fromSnap = router.SnapToRoad(profile, pair.From, snapDistanceM);
                if (fromSnap is null)
                {
                    fromSnapFail++;
                    // 失敗時に最近傍 Bicycle 通行可エッジまでの距離を実測 (探索半径拡大)
                    var nearestDist = FindNearestPassableEdgeDistance(graph, pair.From, VehicleProfile.Bicycle.Name, edgeCount);
                    fromFailDistances.Add(nearestDist);
                    continue;
                }
                var toSnap = router.SnapToRoad(profile, pair.To, snapDistanceM);
                if (toSnap is null)
                {
                    toSnapFail++;
                    var nearestDist = FindNearestPassableEdgeDistance(graph, pair.To, VehicleProfile.Bicycle.Name, edgeCount);
                    toFailDistances.Add(nearestDist);
                    continue;
                }
                var route = router.Calculate(profile, pair.From, pair.To);
                if (route is null) routeFail++;
                else success++;
            }

            var total = pairs.Count;
            Console.WriteLine($"-- スナップ結果 (探索半径 {snapDistanceM:F0}m) --");
            Console.WriteLine($"  成功:             {success,3} / {total} ({100.0 * success / total:F1}%)");
            Console.WriteLine($"  From スナップ失敗: {fromSnapFail,3} / {total} ({100.0 * fromSnapFail / total:F1}%)");
            Console.WriteLine($"  To スナップ失敗:   {toSnapFail,3} / {total} ({100.0 * toSnapFail / total:F1}%)");
            Console.WriteLine($"  経路発見失敗:     {routeFail,3} / {total} ({100.0 * routeFail / total:F1}%)");
            Console.WriteLine();

            // === 失敗ペアの最近傍距離分布 ===
            if (fromFailDistances.Count > 0)
            {
                Console.WriteLine($"-- From スナップ失敗 {fromFailDistances.Count} 件の最近傍 Bicycle 通行可エッジ距離 --");
                PrintDistribution(fromFailDistances);
            }
            if (toFailDistances.Count > 0)
            {
                Console.WriteLine($"-- To スナップ失敗 {toFailDistances.Count} 件の最近傍 Bicycle 通行可エッジ距離 --");
                PrintDistribution(toFailDistances);
            }
        }
        finally
        {
            graph.Dispose();
        }
    }

    private static int CountPassableEdges(OsmDotRoute.Native.NativeRoadGraph graph, string profileName, int edgeCount)
    {
        if (!graph.HasProfile(profileName)) return 0;
        int count = 0;
        for (uint e = 0; e < edgeCount; e++)
        {
            if (graph.CanPass(e, profileName)) count++;
        }
        return count;
    }

    /// <summary>
    /// 与えられた座標に対し、指定プロファイルで通行可能な全エッジを brute-force 走査し、
    /// 最近傍エッジまでの距離 (メートル) を返す。
    /// </summary>
    private static double FindNearestPassableEdgeDistance(
        OsmDotRoute.Native.NativeRoadGraph graph,
        GeoCoordinate point,
        string profileName,
        int edgeCount)
    {
        double bestDist = double.PositiveInfinity;
        for (uint e = 0; e < edgeCount; e++)
        {
            if (!graph.CanPass(e, profileName)) continue;
            var edge = graph.ReadEdge(e);
            var from = graph.GetVertex(edge.FromVertexId);
            var to = graph.GetVertex(edge.ToVertexId);
            var midShape = graph.GetEdgeShape(e);

            // フルシェイプ (From + 中間 + To) の各セグメントへの最短距離を計算
            var prev = from;
            for (int i = 0; i < midShape.Length; i++)
            {
                var next = midShape[i];
                var (d, _, _) = GeoMath.PointToSegment(point, prev, next);
                if (d < bestDist) bestDist = d;
                prev = next;
            }
            var (dEnd, _, _) = GeoMath.PointToSegment(point, prev, to);
            if (dEnd < bestDist) bestDist = dEnd;
        }
        return bestDist;
    }

    private static void PrintDistribution(List<double> distances)
    {
        distances.Sort();
        var min = distances[0];
        var max = distances[^1];
        var median = distances[distances.Count / 2];
        var mean = distances.Average();
        Console.WriteLine($"  Min:    {min,8:F1} m");
        Console.WriteLine($"  Median: {median,8:F1} m");
        Console.WriteLine($"  Mean:   {mean,8:F1} m");
        Console.WriteLine($"  Max:    {max,8:F1} m");
        Console.WriteLine($"  全件 (m): {string.Join(", ", distances.Select(d => $"{d:F0}"))}");
    }
}
