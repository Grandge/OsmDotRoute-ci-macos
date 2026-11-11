using System;
using System.IO;
using OsmDotRoute;
using OsmDotRoute.Extractor;
using Xunit;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// Phase 4「Extractor CLI 外部プロファイル対応」の検証テスト。
/// <see cref="ProfileResolver.Resolve"/> が組込み名と外部 JSON ファイルパスの両方を
/// <see cref="VehicleProfile"/> へ解決できることを検証する（REQ-PRF-009 を bake 経路に拡張）。
/// </summary>
public sealed class ProfileResolverTests
{
    // --- 組込みプロファイル名の解決 ---

    [Theory]
    [InlineData("car")]
    [InlineData("pedestrian")]
    [InlineData("bicycle")]
    [InlineData("truck")]
    [InlineData("ambulance")]
    [InlineData("fire_engine")]
    [InlineData("disaster")]
    public void Resolve_BuiltInName_ReturnsProfileWithSameName(string name)
    {
        var profile = ProfileResolver.Resolve(name);
        Assert.Equal(name, profile.Name);
    }

    [Fact]
    public void Resolve_BuiltInName_IsCaseInsensitive()
    {
        var profile = ProfileResolver.Resolve("AMBULANCE");
        Assert.Equal("ambulance", profile.Name);
    }

    // --- 外部 JSON ファイルの解決 ---

    [Fact]
    public void Resolve_ExternalJsonFile_LoadsUserProfile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"odr_test_profile_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, MinimalProfileJson("my_custom"));
        try
        {
            var profile = ProfileResolver.Resolve(path);
            Assert.Equal("my_custom", profile.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // --- エラー系 ---

    [Fact]
    public void Resolve_UnknownNameAndNoFile_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => ProfileResolver.Resolve("nonexistent_xyz"));
        Assert.Contains("未対応プロファイル", ex.Message);
    }

    [Fact]
    public void Resolve_InvalidExternalJson_ThrowsArgumentException()
    {
        var path = Path.Combine(Path.GetTempPath(), $"odr_test_bad_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is not valid json");
        try
        {
            var ex = Assert.Throws<ArgumentException>(() => ProfileResolver.Resolve(path));
            Assert.Contains("読込に失敗", ex.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Resolve_NullOrEmpty_Throws()
    {
        Assert.Throws<ArgumentException>(() => ProfileResolver.Resolve(""));
        Assert.Throws<ArgumentException>(() => ProfileResolver.Resolve("   "));
    }

    /// <summary>ProfileEvaluator の必須フィールドを満たす最小プロファイル JSON。</summary>
    private static string MinimalProfileJson(string name) => $$"""
    {
      "name": "{{name}}",
      "highway": { "primary": { "speedKmh": 60, "access": "yes" } },
      "accessValueMap": { "yes": "allow", "no": "deny" },
      "fallback": { "speedKmh": 30, "access": "no" },
      "speedBounds": { "minKmh": 5, "maxKmh": 120 },
      "difficultyDefault": { "speedFactor": 1.0, "canPass": true }
    }
    """;
}
