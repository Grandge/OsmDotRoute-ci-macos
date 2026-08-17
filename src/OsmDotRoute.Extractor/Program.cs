using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using OsmDotRoute;
using OsmDotRoute.Extractor;
using OsmDotRoute.Extractor.Cli;
using OsmDotRoute.Extractor.Pipeline;

Console.OutputEncoding = System.Text.Encoding.UTF8;
Console.InputEncoding = System.Text.Encoding.UTF8;

var rootCommand = new RootCommand(
    "OsmDotRoute Extractor — OSM PBF から .odrg バイナリグラフを抽出する CLI。" +
    " 詳細: Documents/phase2_graph_format_spec.md v0.2 を参照。")
{
    ExtractCommand.Build(Run),
    PatchBboxCommand.Build(RunPatchBbox),
};

return rootCommand.Parse(args).Invoke();

static int Run(ExtractOptions options)
{
    Console.WriteLine($"input    : {options.Input.FullName}");
    Console.WriteLine($"output   : {options.Output.FullName}");
    Console.WriteLine($"bbox     : {options.Bbox}");
    Console.WriteLine($"profiles : {string.Join(",", options.Profiles)}");
    Console.WriteLine();

    if (!options.Input.Exists)
    {
        Console.Error.WriteLine($"入力 PBF が見つかりません: {options.Input.FullName}");
        return 2;
    }

    VehicleProfile[] profiles;
    try
    {
        profiles = options.Profiles.Select(ProfileResolver.Resolve).ToArray();
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 2;
    }

    // プロファイル名の一意性チェック（R5）。.odrg の BAKED_PROFILE スロットは name で解決されるため重複不可。
    var duplicateName = profiles
        .GroupBy(p => p.Name)
        .FirstOrDefault(g => g.Count() > 1);
    if (duplicateName is not null)
    {
        Console.Error.WriteLine(
            $"プロファイル名が重複しています: '{duplicateName.Key}'。各プロファイルの name は一意である必要があります。");
        return 2;
    }

    var bbox = new Aabb(
        MinLon: options.Bbox.MinLon,
        MinLat: options.Bbox.MinLat,
        MaxLon: options.Bbox.MaxLon,
        MaxLat: options.Bbox.MaxLat);

    var pipelineOpts = new ExtractPipelineOptions(
        InputPbf: options.Input.FullName,
        Bbox: bbox,
        Profiles: profiles);

    var sw = Stopwatch.StartNew();
    Console.WriteLine("抽出開始...");
    ExtractPipelineResult result;
    try
    {
        result = ExtractPipeline.Run(pipelineOpts);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"抽出失敗: {ex.Message}");
        return 3;
    }
    sw.Stop();

    Console.WriteLine($"抽出完了: 頂点 {result.Vertices.Length:N0} 件 / エッジ {result.Edges.Length:N0} 件 ({sw.Elapsed.TotalSeconds:F1} 秒)");

    // Metadata JSON 構築
    string metadataJson = JsonSerializer.Serialize(new
    {
        createdAt = DateTimeOffset.UtcNow.ToString("o"),
        createdBy = "OsmDotRoute.Extractor",
        sourcePbf = options.Input.Name,
        // 外部 JSON プロファイル指定時はファイルパスではなく実際のプロファイル名（JSON の name）を記録する。
        // .odrg の BAKED_PROFILE 名テーブルと一致させ、ランタイムのスロット解決を保証する。
        profiles = profiles.Select(p => p.Name).ToArray(),
        bbox = new
        {
            minLon = bbox.MinLon,
            minLat = bbox.MinLat,
            maxLon = bbox.MaxLon,
            maxLat = bbox.MaxLat,
        },
        vertexCount = result.Vertices.Length,
        edgeCount = result.Edges.Length,
        rtreeBranchingFactor = result.RTree.BranchingFactor,
    });

    var writeInput = new OdrgWriteInput(
        Vertices: result.Vertices,
        Edges: result.Edges,
        EdgeAabbs: result.EdgeAabbs,
        EdgeFlags: result.EdgeFlags,
        RTree: result.RTree,
        ProfileTable: result.ProfileTable,
        NodeCoordLookup: result.NodeCoordLookup,
        Bbox: result.FileBbox,
        MetadataJson: metadataJson,
        RequestedBbox: result.RequestedBbox);

    Console.WriteLine("書出開始...");
    sw.Restart();
    using (var fs = options.Output.Open(FileMode.Create, FileAccess.Write, FileShare.None))
    {
        OdrgWriter.Write(fs, writeInput);
    }
    sw.Stop();
    long fileSize = new FileInfo(options.Output.FullName).Length;
    Console.WriteLine($"書出完了: {fileSize:N0} byte ({sw.Elapsed.TotalSeconds:F1} 秒)");
    Console.WriteLine($"出力ファイル: {options.Output.FullName}");

    return 0;
}

static int RunPatchBbox(PatchBboxOptions options)
{
    if (options.Dir is not null)
    {
        return RunPatchBboxDirectory(options.Dir);
    }

    if (options.Input is null)
    {
        Console.Error.WriteLine("--input または --dir のいずれかを指定してください。");
        return 2;
    }
    if (!options.Input.Exists)
    {
        Console.Error.WriteLine($".odrg が見つかりません: {options.Input.FullName}");
        return 2;
    }

    Bbox? bbox = options.Bbox;
    if (bbox is null && options.MapJson is not null)
    {
        if (!options.MapJson.Exists)
        {
            Console.Error.WriteLine($".map.json が見つかりません: {options.MapJson.FullName}");
            return 2;
        }
        bbox = MapJson.TryReadBbox(options.MapJson.FullName);
        if (bbox is null)
        {
            Console.Error.WriteLine($".map.json から bbox を読めませんでした: {options.MapJson.FullName}");
            return 2;
        }
    }

    if (bbox is null)
    {
        Console.Error.WriteLine("--bbox または --map-json のいずれかを指定してください。");
        return 2;
    }

    var aabb = ToAabb(bbox.Value);
    OdrgHeaderPatcher.Patch(options.Input.FullName, aabb);
    Console.WriteLine($"patched: {options.Input.FullName}  bbox={bbox.Value}");
    return 0;
}

static int RunPatchBboxDirectory(DirectoryInfo dir)
{
    if (!dir.Exists)
    {
        Console.Error.WriteLine($"ディレクトリが見つかりません: {dir.FullName}");
        return 2;
    }

    var odrgFiles = dir.GetFiles("*.odrg", SearchOption.AllDirectories);
    int patched = 0, skipped = 0;
    foreach (var odrg in odrgFiles)
    {
        // 同名 .map.json（"foo.odrg" → "foo.map.json"）
        var baseName = odrg.FullName.Substring(0, odrg.FullName.Length - ".odrg".Length);
        var mapJsonPath = baseName + ".map.json";

        if (!File.Exists(mapJsonPath))
        {
            Console.WriteLine($"skip (no .map.json): {odrg.Name}");
            skipped++;
            continue;
        }

        var bbox = MapJson.TryReadBbox(mapJsonPath);
        if (bbox is null)
        {
            Console.WriteLine($"skip (bad .map.json): {odrg.Name}");
            skipped++;
            continue;
        }

        try
        {
            OdrgHeaderPatcher.Patch(odrg.FullName, ToAabb(bbox.Value));
            Console.WriteLine($"patched: {odrg.Name}  bbox={bbox.Value}");
            patched++;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"error: {odrg.Name}  {ex.Message}");
            skipped++;
        }
    }

    Console.WriteLine($"完了: patched={patched} skipped={skipped} (total {odrgFiles.Length})");
    return 0;
}

static Aabb ToAabb(Bbox b) => new(b.MinLon, b.MinLat, b.MaxLon, b.MaxLat);
