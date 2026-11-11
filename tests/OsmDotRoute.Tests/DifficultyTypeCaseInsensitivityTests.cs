using OsmDotRoute.Profiles;
using OsmDotRoute.Tests.Native;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// v1.1.1 — 難所タイプ照合の case-insensitive 化（親プロFB 不具合修正、REQ-PRF-014 改訂）の回帰固定テスト。
/// 親プロジェクトが <c>"Flooding"</c>（PascalCase）を渡し、サイレントに <c>difficultyDefault</c> へ落ちて
/// 速度低下が一切効かなかった不具合の再発防止を狙う。
/// </summary>
public class DifficultyTypeCaseInsensitivityTests : IClassFixture<NativeRouterDbFixture>
{
    private readonly NativeRouterDbFixture _fixture;

    public DifficultyTypeCaseInsensitivityTests(NativeRouterDbFixture fixture)
    {
        _fixture = fixture;
    }

    // ---- 単体テスト（ProfileEvaluator） ----

    [Theory]
    [InlineData("Flooding")]
    [InlineData("FLOODING")]
    [InlineData("fLoOdInG")]
    [InlineData("flooding")]
    public void EvaluateDifficulty_AnyCase_MatchesLowercaseProfileEntry(string variant)
    {
        // car.json の "flooding" エントリ（speedFactor=0.3）が case を問わず一致する
        var diff = VehicleProfile.Car.Evaluator.EvaluateDifficulty(variant);
        Assert.True(diff.CanPass);
        Assert.Equal(0.3f, diff.SpeedFactor, precision: 3);
    }

    [Fact]
    public void EvaluateDifficulty_TrulyUnknownType_StillFallsToDefault()
    {
        // case-insensitive 化後も、未定義タイプは従来通り difficultyDefault に落ちる（REQ-PRF-014 不変）
        var diff = VehicleProfile.Car.Evaluator.EvaluateDifficulty("meteor_strike");
        Assert.True(diff.CanPass);
        Assert.Equal(1.0f, diff.SpeedFactor, precision: 3);
    }

    // ---- 公開 API: VehicleProfile.HasDifficulty / KnownDifficultyTypes ----

    [Fact]
    public void HasDifficulty_CanonicalLowercase_ReturnsTrue()
    {
        Assert.True(VehicleProfile.Car.HasDifficulty(DifficultyTypes.Flooding));
    }

    [Theory]
    [InlineData("Flooding")]
    [InlineData("FLOODING")]
    [InlineData("fLoOdInG")]
    public void HasDifficulty_AnyCase_ReturnsTrue(string variant)
    {
        Assert.True(VehicleProfile.Car.HasDifficulty(variant));
    }

    [Fact]
    public void HasDifficulty_UnknownType_ReturnsFalse()
    {
        Assert.False(VehicleProfile.Car.HasDifficulty("meteor_strike"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void HasDifficulty_NullOrWhitespace_ReturnsFalse(string? input)
    {
        Assert.False(VehicleProfile.Car.HasDifficulty(input!));
    }

    [Fact]
    public void KnownDifficultyTypes_Car_ContainsAllBuiltinKeys()
    {
        var known = VehicleProfile.Car.KnownDifficultyTypes;
        // car.json 定義の組込み 8 種が全て含まれる
        Assert.Contains(DifficultyTypes.Flooding, known);
        Assert.Contains(DifficultyTypes.Liquefaction, known);
        Assert.Contains(DifficultyTypes.Landslide, known);
        Assert.Contains(DifficultyTypes.Construction, known);
        Assert.Contains(DifficultyTypes.Obstacle, known);
        Assert.Contains(DifficultyTypes.Congestion, known);
        Assert.Contains(DifficultyTypes.Snow, known);
        Assert.Contains(DifficultyTypes.Ice, known);
        Assert.Equal(8, known.Count);
    }

    // ---- 重複キー検出（case-only collision） ----

    [Fact]
    public void LoadFromJsonString_CaseOnlyDuplicateKeys_ThrowsInvalidProfileException()
    {
        // 同一プロファイル内に "flooding" と "Flooding" を共存させると、
        // case-insensitive 照合の一意性が崩れるため即時拒否
        var json = MinimalProfileJson(extraDifficulty: """
                    "flooding": { "speedFactor": 0.3, "canPass": true },
                    "Flooding": { "speedFactor": 0.5, "canPass": true },
            """);

        var ex = Assert.Throws<InvalidProfileException>(() => VehicleProfile.LoadFromJsonString(json));
        Assert.Contains("case", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ---- end-to-end: PascalCase でも経路計算が減速反映する ----

    [Fact]
    public void Calculate_DifficultyArea_PascalCase_SlowsDownSameAsLowercase()
    {
        // 親プロFB 起源の現実シナリオ: "Flooding"（PascalCase）を AddDifficultyArea に渡しても
        // 経路所要が lowercase 時と同等に増えること（v1.1.0 では同等にならずバグだった）
        var (from, to) = _fixture.MediumPair;
        var routerNoConstraints = new Router(_fixture.RouterDb);
        var baseline = routerNoConstraints.Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(baseline);

        var polygon = MakePolygonCoveringShape(baseline!.Shape, marginDeg: 0.01);

        // (A) 正準小文字キー
        var rsLower = new RestrictedAreaService();
        rsLower.AddDifficultyArea(polygon, DifficultyTypes.Flooding);
        var routeLower = new Router(_fixture.RouterDb, rsLower).Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(routeLower);

        // (B) PascalCase キー（親プロFB 原典のケース）
        var rsPascal = new RestrictedAreaService();
        rsPascal.AddDifficultyArea(polygon, "Flooding");
        var routePascal = new Router(_fixture.RouterDb, rsPascal).Calculate(VehicleProfile.Car, from, to);
        Assert.NotNull(routePascal);

        // 両者の所要時間が同一（同じ speedFactor=0.3 が適用されたから）
        Assert.Equal(routeLower!.TotalDurationSec, routePascal!.TotalDurationSec, precision: 3);
        // ベースラインより明確に増えている（= 速度低下が効いている、サイレント・フォールバックではない）
        Assert.True(routePascal.TotalDurationSec > baseline.TotalDurationSec * 3.0,
            $"PascalCase 指定時に速度低下が反映されていない: baseline={baseline.TotalDurationSec:F1}s, pascalCase={routePascal.TotalDurationSec:F1}s");
    }

    // ---- ヘルパ ----

    /// <summary>シェイプ全体を margin 度だけ広げた外接矩形ポリゴンを作る。</summary>
    private static GeoPolygon MakePolygonCoveringShape(ReadOnlyMemory<GeoCoordinate> shape, double marginDeg)
    {
        var span = shape.Span;
        double minLat = double.MaxValue, maxLat = double.MinValue;
        double minLon = double.MaxValue, maxLon = double.MinValue;
        for (int i = 0; i < span.Length; i++)
        {
            var c = span[i];
            if (c.Latitude < minLat) minLat = c.Latitude;
            if (c.Latitude > maxLat) maxLat = c.Latitude;
            if (c.Longitude < minLon) minLon = c.Longitude;
            if (c.Longitude > maxLon) maxLon = c.Longitude;
        }
        minLat -= marginDeg; maxLat += marginDeg;
        minLon -= marginDeg; maxLon += marginDeg;
        return new GeoPolygon(new[]
        {
            new GeoCoordinate(minLat, minLon),
            new GeoCoordinate(minLat, maxLon),
            new GeoCoordinate(maxLat, maxLon),
            new GeoCoordinate(maxLat, minLon),
            new GeoCoordinate(minLat, minLon),
        });
    }

    /// <summary>テスト用の最小プロファイル JSON 生成。<paramref name="extraDifficulty"/> は difficulty オブジェクト内の追加エントリ（末尾カンマ付）。</summary>
    private static string MinimalProfileJson(string extraDifficulty)
    {
        return $$"""
        {
          "name": "test_caseonly_dup",
          "vehicleType": "motor_vehicle",
          "accessTagKeys": ["access"],
          "highway": { "primary": { "speedKmh": 60, "access": "yes" } },
          "accessValueMap": { "yes": "allow", "no": "deny" },
          "fallback": { "speedKmh": 30, "access": "no" },
          "speedBounds": { "minKmh": 5, "maxKmh": 200 },
          "difficulty": {
        {{extraDifficulty}}
                    "_pad": { "speedFactor": 1.0, "canPass": true }
          },
          "difficultyDefault": { "speedFactor": 1.0, "canPass": true }
        }
        """;
    }
}