using OsmDotRoute.Native;

namespace OsmDotRoute.Benchmarks;

/// <summary>
/// ベンチマーク全体で共有する <c>.odrg</c> テストデータのパス・ロード手順
/// （Phase 3 ステップ 3C.4 で Itinero 完全撤去、Native 系統のみに統一）。
/// </summary>
internal static class BenchmarkAssets
{
    /// <summary>ベンチマーク用 TestData ディレクトリ（カレント基準）。</summary>
    public static string TestDataDirectory =>
        Path.Combine(AppContext.BaseDirectory, "TestData");

    /// <summary>route-pairs.json のフルパス。</summary>
    public static string RoutePairsPath => Path.Combine(TestDataDirectory, "route-pairs.json");

    /// <summary>restrictions-mixed-100.json のフルパス。</summary>
    public static string MixedRestrictionsPath => Path.Combine(TestDataDirectory, "restrictions-mixed-100.json");

    /// <summary>restrictions-block-100.json のフルパス。</summary>
    public static string BlockRestrictionsPath => Path.Combine(TestDataDirectory, "restrictions-block-100.json");

    /// <summary>
    /// 津島市 <c>.odrg</c>（リポジトリ同梱、3.55MB）のパス。
    /// </summary>
    /// <remarks>
    /// BenchmarkDotNet は子プロセスでベンチを実行し <c>AppContext.BaseDirectory</c> が中間ディレクトリに
    /// 変わるため、相対パスは利用不可。
    /// </remarks>
    public const string TsushimaOdrgPath =
        @"d:\workspace\DotRoute\samples\Data\tsushima.odrg";

    /// <summary>
    /// Native 系統 (<see cref="NativeRoadGraph"/> + <see cref="NativeRoadSnapper"/>) で
    /// 津島市 <c>.odrg</c> をロードし、<see cref="OsmDotRoute.RouterDb"/> として返す
    /// （Phase 3 ステップ 3B.5、計画書 §4.5-B T16=A）。
    /// </summary>
    /// <returns>(<see cref="OsmDotRoute.RouterDb"/>, <see cref="NativeRoadGraph"/>) のタプル。
    /// graph は <see cref="IDisposable"/> なので呼出側で Dispose 必須。</returns>
    public static (OsmDotRoute.RouterDb RouterDb, NativeRoadGraph Graph) LoadNativeRouterDb()
    {
        if (!File.Exists(TsushimaOdrgPath))
        {
            throw new FileNotFoundException(
                $"ベンチマーク用 .odrg が見つかりません: {TsushimaOdrgPath}\n" +
                "リポジトリ同梱の samples/Data/tsushima.odrg が必要です（commit 4a5a90a 以降）。",
                TsushimaOdrgPath);
        }
        var graph = new NativeRoadGraph(TsushimaOdrgPath);
        var snapper = new NativeRoadSnapper(graph);
        var routerDb = new OsmDotRoute.RouterDb(graph, snapper);
        return (routerDb, graph);
    }
}
