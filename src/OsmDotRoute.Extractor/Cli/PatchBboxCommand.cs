using System;
using System.CommandLine;
using System.IO;
using System.Linq;

namespace OsmDotRoute.Extractor.Cli;

/// <summary>
/// <c>patch-bbox</c> サブコマンド: 既存 <c>.odrg</c> のヘッダーに要求 bbox（RequestedBbox）を後付けする
/// （v0.2 → v0.3 マイグレーション）。グラフ本体は再生成しない。
/// </summary>
/// <remarks>
/// 構造:
/// <code>
/// // 単一ファイル + 明示 bbox
/// osmdotroute-extractor patch-bbox --input file.odrg --bbox minLon,minLat,maxLon,maxLat
/// // 単一ファイル + .map.json から bbox 読込
/// osmdotroute-extractor patch-bbox --input file.odrg --map-json file.map.json
/// // ディレクトリ一括（同名 .map.json を自動ペアリング）
/// osmdotroute-extractor patch-bbox --dir D:\シナリオ
/// </code>
/// </remarks>
internal static class PatchBboxCommand
{
    public static Command Build(Func<PatchBboxOptions, int> onPatch)
    {
        ArgumentNullException.ThrowIfNull(onPatch);

        var inputOption = new Option<FileInfo?>("--input", "-i")
        {
            Description = "対象 .odrg ファイル（単一ファイルモード）。",
        };

        var bboxOption = new Option<Bbox?>("--bbox")
        {
            Description = "要求 bbox を WGS84 で指定: minLon,minLat,maxLon,maxLat。--input と併用。",
            CustomParser = result =>
            {
                if (result.Tokens.Count == 0) return null;
                try { return Bbox.Parse(result.Tokens.Single().Value); }
                catch (FormatException ex) { result.AddError(ex.Message); return null; }
            },
        };

        var mapJsonOption = new Option<FileInfo?>("--map-json")
        {
            Description = "bbox 取得元の .map.json（NorthWest/SouthEast 形式）。--input と併用。",
        };

        var dirOption = new Option<DirectoryInfo?>("--dir", "-d")
        {
            Description = "ディレクトリ一括モード。配下の *.odrg を走査し、同名 .map.json から bbox を読んで patch。",
        };

        var command = new Command("patch-bbox",
            "既存 .odrg のヘッダーに要求 bbox を後付けする (v0.2 → v0.3 マイグレーション)。")
        {
            inputOption,
            bboxOption,
            mapJsonOption,
            dirOption,
        };

        command.SetAction(parseResult =>
        {
            var options = new PatchBboxOptions(
                Input: parseResult.GetValue(inputOption),
                Bbox: parseResult.GetValue(bboxOption),
                MapJson: parseResult.GetValue(mapJsonOption),
                Dir: parseResult.GetValue(dirOption));
            return onPatch(options);
        });

        return command;
    }
}

internal sealed record PatchBboxOptions(
    FileInfo? Input,
    Bbox? Bbox,
    FileInfo? MapJson,
    DirectoryInfo? Dir);
