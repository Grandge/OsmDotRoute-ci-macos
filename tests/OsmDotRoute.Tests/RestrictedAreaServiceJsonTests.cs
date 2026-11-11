using System.Text.Json;
using OsmDotRoute.Geometry;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// <see cref="RestrictedAreaService"/> の JSON 永続化 API
/// （<c>ToJsonString</c> / <c>SaveToJson*</c> / <c>AddFromJson*</c>）の単体テスト。
/// 保存形式は Sandbox デモと互換（<c>{ format, version, items }</c>）。往復・相互運用・検証を確認する。
/// </summary>
public class RestrictedAreaServiceJsonTests
{
    private static GeoPolygon TriPolygon()
        => new(new[]
        {
            new GeoCoordinate(35.0, 139.0),
            new GeoCoordinate(36.0, 139.0),
            new GeoCoordinate(36.0, 140.0),
        });

    private static RestrictedAreaService BuildSample()
    {
        var service = new RestrictedAreaService();
        service.AddBlockArea(TriPolygon(), tag: "incident-1");
        service.AddDifficultyArea(new MeshCode(53394611), DifficultyTypes.Flooding, tag: "typhoon-15");
        service.AddBlockArea(new[] { new MeshCode(53394601), new MeshCode(53394602) }, tag: "block-mesh");
        return service;
    }

    [Fact]
    public void ToJsonString_Then_AddFromJsonString_RoundTrips_All_Areas()
    {
        var source = BuildSample();
        var json = source.ToJsonString();

        var loaded = new RestrictedAreaService();
        var ids = loaded.AddFromJsonString(json);

        Assert.Equal(3, ids.Length);
        var all = loaded.ListAll();
        Assert.Equal(3, all.Count);

        var block = Assert.IsType<BlockArea>(Assert.Single(all, a => a is BlockArea b && b.Polygon is not null));
        Assert.Equal("incident-1", block.Tag);
        Assert.Equal(3, block.Polygon!.OuterBoundary.Count);

        var difficulty = Assert.IsType<DifficultyArea>(Assert.Single(all, a => a is DifficultyArea));
        Assert.Equal(DifficultyTypes.Flooding, difficulty.DifficultyType);
        Assert.Equal(53394611, difficulty.MeshCodes!.Single().Value);

        var blockMesh = Assert.IsType<BlockArea>(Assert.Single(all, a => a is BlockArea b && b.MeshCodes is not null));
        Assert.Equal(new long[] { 53394601, 53394602 }, blockMesh.MeshCodes!.Select(m => m.Value));
    }

    [Fact]
    public void Json_Contains_Sandbox_Compatible_Envelope()
    {
        var json = BuildSample().ToJsonString();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("osmdotroute-restrictions", root.GetProperty("format").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(3, root.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public void AddFromJson_Tolerates_Sandbox_Items_With_Id_Field()
    {
        // Sandbox の保存形式は item に id を含むが、読込時は無視して再採番する。
        var json = """
        {
          "format": "osmdotroute-restrictions",
          "version": 1,
          "items": [
            { "id": "11111111-1111-1111-1111-111111111111", "kind": "difficulty",
              "difficultyType": "flooding", "shapeType": "mesh", "meshCodes": [53394611], "tag": "a" }
          ]
        }
        """;

        var service = new RestrictedAreaService();
        var ids = service.AddFromJsonString(json);

        Assert.Single(ids);
        Assert.NotEqual("11111111-1111-1111-1111-111111111111", ids[0].Value.ToString());
        var area = Assert.IsType<DifficultyArea>(service.ListAll().Single());
        Assert.Equal("flooding", area.DifficultyType);
    }

    [Fact]
    public void AddFromJson_Appends_To_Existing_Restrictions()
    {
        var service = BuildSample();
        var json = new RestrictedAreaService()
            .Also(s => s.AddBlockArea(new MeshCode(53394600)))
            .ToJsonString();

        service.AddFromJsonString(json);

        Assert.Equal(4, service.ListAll().Count);
    }

    [Fact]
    public void SaveToJsonFile_Then_AddFromJsonFile_RoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"odr-restrictions-{Guid.NewGuid():N}.json");
        try
        {
            BuildSample().SaveToJsonFile(path);
            Assert.True(File.Exists(path));

            var loaded = new RestrictedAreaService();
            loaded.AddFromJsonFile(path);
            Assert.Equal(3, loaded.ListAll().Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void AddFromJson_Rejects_Wrong_Format()
    {
        var json = """{ "format": "something-else", "version": 1, "items": [] }""";
        Assert.Throws<FormatException>(() => new RestrictedAreaService().AddFromJsonString(json));
    }

    [Fact]
    public void AddFromJson_Rejects_Unsupported_Version()
    {
        var json = """{ "format": "osmdotroute-restrictions", "version": 99, "items": [] }""";
        Assert.Throws<FormatException>(() => new RestrictedAreaService().AddFromJsonString(json));
    }

    [Fact]
    public void AddFromJson_Rejects_Difficulty_Without_Type()
    {
        var json = """
        {
          "format": "osmdotroute-restrictions", "version": 1,
          "items": [ { "kind": "difficulty", "shapeType": "mesh", "meshCodes": [53394611] } ]
        }
        """;
        Assert.Throws<FormatException>(() => new RestrictedAreaService().AddFromJsonString(json));
    }

    [Fact]
    public void AddFromJson_Rejects_Polygon_With_Too_Few_Vertices()
    {
        var json = """
        {
          "format": "osmdotroute-restrictions", "version": 1,
          "items": [ { "kind": "block", "shapeType": "polygon",
            "outerBoundary": [ { "latitude": 35, "longitude": 139 }, { "latitude": 36, "longitude": 139 } ] } ]
        }
        """;
        Assert.Throws<FormatException>(() => new RestrictedAreaService().AddFromJsonString(json));
    }

    [Fact]
    public void AddFromJson_Throws_On_Malformed_Json()
    {
        Assert.Throws<JsonException>(() => new RestrictedAreaService().AddFromJsonString("{ not json"));
    }

    [Fact]
    public void Empty_Service_Saves_Empty_Items_And_Loads_Nothing()
    {
        var json = new RestrictedAreaService().ToJsonString();
        var loaded = new RestrictedAreaService();
        var ids = loaded.AddFromJsonString(json);
        Assert.Empty(ids);
        Assert.Empty(loaded.ListAll());
    }
}

internal static class TestFluentExtensions
{
    public static T Also<T>(this T self, Action<T> action)
    {
        action(self);
        return self;
    }
}
