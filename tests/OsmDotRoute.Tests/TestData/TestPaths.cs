namespace OsmDotRoute.Tests.TestData;

/// <summary>
/// テストで参照する外部データファイルのパス。
/// ファイルが存在しないテストはスキップ扱いとする（個人マシン固有のデータに依存しないため）。
/// </summary>
internal static class TestPaths
{
    /// <summary>
    /// 親プロジェクト「災害廃棄物処理シミュレーション」の default.routerdb。
    /// Phase 1 ステップ 3 のアダプター検証で利用。
    /// </summary>
    public const string ParentDefaultRouterDb =
        @"d:\workspace\災害廃棄物処理シミュレーション\App\DisasterWasteSim.Server\Data\Scenarios\default.routerdb";

    /// <summary>
    /// 親プロジェクト「災害廃棄物処理シミュレーション」の津島市抽出 OSM PBF（約 13 MB）。
    /// Phase 2 ステップ 2.10 の PbfReader 統合テストで利用。
    /// </summary>
    public const string TsushimaExtractPbf =
        @"d:\workspace\災害廃棄物処理シミュレーション\App\DisasterWasteSim.Server\Data\tsushima_extract.osm.pbf";

    /// <summary>
    /// リポジトリ同梱の津島市 <c>.odrg</c>（約 3.55 MB、commit <c>4a5a90a</c>）。
    /// Phase 3 ステップ 3A の `.odrg` ランタイム読込テストで参照真値として利用。
    /// </summary>
    /// <remarks>
    /// テスト実行時のカレントは <c>tests/OsmDotRoute.Tests/bin/{Config}/{TFM}/</c>。
    /// 5 階層上がリポジトリルート。
    /// </remarks>
    public static string TsushimaOdrg => Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "Data", "tsushima.odrg"));
}
