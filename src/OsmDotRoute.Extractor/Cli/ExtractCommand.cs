using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace OsmDotRoute.Extractor.Cli;

/// <summary>
/// <c>extract</c> サブコマンドの定義を組み立てる。
/// </summary>
/// <remarks>
/// 構造:
/// <code>
/// osmdotroute-extractor extract \
///   --input  &lt;file.osm.pbf&gt; \
///   --output &lt;file.odrg&gt; \
///   --bbox   minLon,minLat,maxLon,maxLat \
///  [--profiles car,pedestrian]
/// </code>
/// <para>
/// サブステップ 3.1 ではコマンドツリーを組み立てて handler から
/// <see cref="ExtractOptions"/> を引き渡す段階まで。
/// 抽出パイプライン本体は 3.2 以降で実装する。
/// </para>
/// </remarks>
internal static class ExtractCommand
{
    /// <summary>extract サブコマンドを構築する。</summary>
    /// <param name="onExtract">確定済みオプションで呼ばれるハンドラ。戻り値はプロセス終了コード。</param>
    public static Command Build(Func<ExtractOptions, int> onExtract)
    {
        ArgumentNullException.ThrowIfNull(onExtract);

        var inputOption = new Option<FileInfo>("--input", "-i")
        {
            Description = "入力 OSM PBF ファイル (.osm.pbf)。Japan-wide PBF を想定。",
            Required = true,
        };

        var outputOption = new Option<FileInfo>("--output", "-o")
        {
            Description = "出力 .odrg ファイルパス。",
            Required = true,
        };

        var bboxOption = new Option<Bbox>("--bbox")
        {
            Description = "抽出 bbox を WGS84 で指定: minLon,minLat,maxLon,maxLat。Phase 2 v0.2 仕様により必須。",
            Required = true,
            CustomParser = result =>
            {
                string text = result.Tokens.Single().Value;
                try
                {
                    return Bbox.Parse(text);
                }
                catch (FormatException ex)
                {
                    result.AddError(ex.Message);
                    return default;
                }
            },
        };

        var profilesOption = new Option<string[]>("--profiles", "-p")
        {
            Description = "bake するプロファイル一覧 (カンマ区切り)。例: car,pedestrian",
            DefaultValueFactory = _ => new[] { "car", "pedestrian" },
            AllowMultipleArgumentsPerToken = true,
            CustomParser = result =>
            {
                if (result.Tokens.Count == 0)
                    return new[] { "car", "pedestrian" };

                return result.Tokens
                    .SelectMany(t => t.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    .ToArray();
            },
        };

        var command = new Command("extract", "OSM PBF から .odrg バイナリグラフを抽出する。")
        {
            inputOption,
            outputOption,
            bboxOption,
            profilesOption,
        };

        command.SetAction(parseResult =>
        {
            var options = new ExtractOptions(
                Input: parseResult.GetValue(inputOption)!,
                Output: parseResult.GetValue(outputOption)!,
                Bbox: parseResult.GetValue(bboxOption),
                Profiles: parseResult.GetValue(profilesOption) ?? Array.Empty<string>());
            return onExtract(options);
        });

        return command;
    }
}
