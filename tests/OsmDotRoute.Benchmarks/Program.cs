using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Running;
using OsmDotRoute;
using OsmDotRoute.Benchmarks;
using OsmDotRoute.Benchmarks.Generators;

// 起動時引数:
//   --generate-data         route-pairs.json と restrictions-*.geojson を生成して終了
//   --bicycle-snap-probe    既存 route-pairs.json (Car スナップ済 100 ペア) を
//                           Bicycle プロファイルで Calculate し成功 / 失敗件数を出力
//                           (Phase 3 ステップ 3E.1 診断、C2 ベンチの解釈に使用)
//   その他は BenchmarkDotNet にそのまま渡す（--filter, --list, --job 等）
if (args.Length > 0 && args[0] == "--generate-data")
{
    var generated = TestDataInitializer.GenerateAll();
    Console.WriteLine($"TestData 生成完了: {generated.RoutePairsPath}, {generated.MixedGeoJsonPath}, {generated.BlockGeoJsonPath}");
    return;
}

if (args.Length > 0 && args[0] == "--bicycle-snap-probe")
{
    BicycleSnapProbe.Run();
    return;
}

if (args.Length > 0 && args[0] == "--prefecture-bench")
{
    PrefectureBench.Run(args.Skip(1).ToArray());
    return;
}

var config = ManualConfig.CreateMinimumViable()
    .AddDiagnoser(MemoryDiagnoser.Default)
    .WithOptions(ConfigOptions.JoinSummary | ConfigOptions.DisableLogFile);

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);

internal sealed partial class Program;
