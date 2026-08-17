using System.Collections.Generic;
using System.CommandLine;
using System.Linq;
using OsmDotRoute.Extractor.Cli;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.1 — <see cref="ExtractCommand.Build"/> が組み立てる CLI ツリーの
/// パース挙動を検証する。実際の抽出処理は 3.2 以降で実装する。
/// </summary>
public sealed class ExtractCommandTests
{
    private static RootCommand BuildRoot(out List<ExtractOptions> captured)
    {
        var captures = new List<ExtractOptions>();
        captured = captures;
        var root = new RootCommand("test root")
        {
            ExtractCommand.Build(opts =>
            {
                captures.Add(opts);
                return 0;
            }),
        };
        return root;
    }

    [Fact]
    public void Parse_AllRequiredOptions_InvokesHandlerWithExpectedValues()
    {
        var root = BuildRoot(out var captured);
        string[] args =
        {
            "extract",
            "--input", "tsushima.osm.pbf",
            "--output", "tsushima.odrg",
            "--bbox", "136.70,35.16,136.78,35.20",
        };

        int exit = root.Parse(args).Invoke();

        Assert.Equal(0, exit);
        var opts = Assert.Single(captured);
        Assert.EndsWith("tsushima.osm.pbf", opts.Input.FullName);
        Assert.EndsWith("tsushima.odrg", opts.Output.FullName);
        Assert.Equal(136.70, opts.Bbox.MinLon);
        Assert.Equal(35.20, opts.Bbox.MaxLat);
        Assert.Equal(new[] { "car", "pedestrian" }, opts.Profiles);
    }

    [Fact]
    public void Parse_CustomProfiles_OverridesDefault()
    {
        var root = BuildRoot(out var captured);
        string[] args =
        {
            "extract",
            "--input", "x.pbf",
            "--output", "x.odrg",
            "--bbox", "136,35,137,36",
            "--profiles", "car,truck,bicycle",
        };

        Assert.Equal(0, root.Parse(args).Invoke());
        Assert.Equal(new[] { "car", "truck", "bicycle" }, captured.Single().Profiles);
    }

    [Fact]
    public void Parse_MissingInput_ReportsError()
    {
        var root = BuildRoot(out var captured);
        string[] args =
        {
            "extract",
            "--output", "x.odrg",
            "--bbox", "136,35,137,36",
        };

        var result = root.Parse(args);

        Assert.NotEmpty(result.Errors);
        Assert.Empty(captured);
    }

    [Fact]
    public void Parse_MissingBbox_ReportsError()
    {
        var root = BuildRoot(out var captured);
        string[] args =
        {
            "extract",
            "--input", "x.pbf",
            "--output", "x.odrg",
        };

        var result = root.Parse(args);

        Assert.NotEmpty(result.Errors);
        Assert.Empty(captured);
    }

    [Fact]
    public void Parse_InvalidBboxFormat_ReportsError()
    {
        var root = BuildRoot(out var captured);
        string[] args =
        {
            "extract",
            "--input", "x.pbf",
            "--output", "x.odrg",
            "--bbox", "not-a-bbox",
        };

        var result = root.Parse(args);

        Assert.NotEmpty(result.Errors);
        Assert.Empty(captured);
    }
}
