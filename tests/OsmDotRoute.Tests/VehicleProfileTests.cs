using OsmDotRoute.Profiles;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 1 ステップ 5a「JSON プロファイル基盤」の検証テスト。
/// 同梱 car.json / pedestrian.json のロード、ProfileEvaluator の評価ロジック、
/// 難所評価、ユーザー JSON ロード、不正 JSON 検証を網羅する。
/// </summary>
public class VehicleProfileTests
{
    // --- 同梱プロファイルのロード ---

    [Fact]
    public void Car_LoadsFromEmbeddedResource()
    {
        var car = VehicleProfile.Car;
        Assert.Equal("car", car.Name);
    }

    [Fact]
    public void Pedestrian_LoadsFromEmbeddedResource()
    {
        var ped = VehicleProfile.Pedestrian;
        Assert.Equal("pedestrian", ped.Name);
    }

    [Fact]
    public void Car_AndPedestrian_AreCached()
    {
        // 同じインスタンスが返ること（Lazy<T> による単一インスタンス）
        Assert.Same(VehicleProfile.Car, VehicleProfile.Car);
        Assert.Same(VehicleProfile.Pedestrian, VehicleProfile.Pedestrian);
    }

    // --- Car プロファイル: 評価ロジック ---

    [Fact]
    public void Car_Evaluate_Motorway_AllowsAt90Kmh()
    {
        // motorway raw 120 km/h × speedMultiplier 0.75 = 90 km/h
        var eval = VehicleProfile.Car.Evaluator.Evaluate(Tags(("highway", "motorway")));

        Assert.True(eval.CanPass);
        Assert.Equal(90f, eval.SpeedKmh);
    }

    [Fact]
    public void Car_Evaluate_Residential_ClampedTo30Kmh()
    {
        // residential raw 50 × 0.75 = 37.5、minKmh=30 のままなので 37.5 が返る
        var eval = VehicleProfile.Car.Evaluator.Evaluate(Tags(("highway", "residential")));

        Assert.True(eval.CanPass);
        Assert.Equal(37.5f, eval.SpeedKmh);
    }

    [Fact]
    public void Car_Evaluate_Footway_Denies()
    {
        var eval = VehicleProfile.Car.Evaluator.Evaluate(Tags(("highway", "footway")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Car_Evaluate_AccessNo_OverridesHighwayAllow()
    {
        // motorway はデフォルト許可だが access=no で拒否
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "motorway"), ("access", "no")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Car_Evaluate_MotorVehicleYes_OverridesAccessNo()
    {
        // access=no を motor_vehicle=yes で上書き（アクセスタグ優先度: motor_vehicle > vehicle > access）
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("access", "no"), ("motor_vehicle", "yes")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void Car_Evaluate_MaxspeedTagOverridesHighwayDefault()
    {
        // maxspeed 80 × 0.75 = 60 km/h
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "motorway"), ("maxspeed", "80")));

        Assert.True(eval.CanPass);
        Assert.Equal(60f, eval.SpeedKmh);
    }

    [Fact]
    public void Car_Evaluate_MaxspeedMph_ConvertsToKmh()
    {
        // 50 mph × 1.609344 ≈ 80.47 km/h × 0.75 ≈ 60.35 km/h
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxspeed", "50 mph")));

        Assert.True(eval.CanPass);
        Assert.InRange(eval.SpeedKmh, 60f, 61f);
    }

    [Fact]
    public void Car_Evaluate_MaxspeedInvalidString_FallsBackToHighwayDefault()
    {
        // primary raw 90 × 0.75 = 67.5
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxspeed", "walk")));

        Assert.True(eval.CanPass);
        Assert.Equal(67.5f, eval.SpeedKmh);
    }

    [Fact]
    public void Car_Evaluate_MaxspeedClampedToBounds()
    {
        // maxspeed 300 × 0.75 = 225、speedBounds.maxKmh=200 でクランプ
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "motorway"), ("maxspeed", "300")));

        Assert.Equal(200f, eval.SpeedKmh);
    }

    [Fact]
    public void Car_Evaluate_OnewayYes_ReturnsForward()
    {
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("oneway", "yes")));

        Assert.Equal(OnewayDirection.Forward, eval.Oneway);
    }

    [Fact]
    public void Car_Evaluate_OnewayMinus1_ReturnsBackward()
    {
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("oneway", "-1")));

        Assert.Equal(OnewayDirection.Backward, eval.Oneway);
    }

    [Fact]
    public void Car_Evaluate_UnknownHighway_UsesFallback()
    {
        // car.json: fallback = { speedKmh: 30, access: "no" }
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "unknown_type")));

        // fallback access=no → 通行不可
        Assert.False(eval.CanPass);
    }

    // --- Pedestrian プロファイル ---

    [Fact]
    public void Pedestrian_Evaluate_Footway_AllowsAt4Kmh()
    {
        // Itinero pedestrian と整合: footway = 4 km/h (multiplier 1.0)
        var eval = VehicleProfile.Pedestrian.Evaluator.Evaluate(Tags(("highway", "footway")));

        Assert.True(eval.CanPass);
        Assert.Equal(4f, eval.SpeedKmh);
    }

    [Fact]
    public void Pedestrian_Evaluate_Motorway_Denies()
    {
        var eval = VehicleProfile.Pedestrian.Evaluator.Evaluate(Tags(("highway", "motorway")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Pedestrian_Evaluate_IgnoresOneway()
    {
        // pedestrian.json: ignoreOneway = true
        var eval = VehicleProfile.Pedestrian.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("oneway", "yes")));

        Assert.Equal(OnewayDirection.Bidirectional, eval.Oneway);
    }

    // --- 難所評価 ---

    [Fact]
    public void Car_EvaluateDifficulty_Flooding_Returns03AndCanPass()
    {
        var diff = VehicleProfile.Car.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);
        Assert.True(diff.CanPass);
        Assert.Equal(0.3f, diff.SpeedFactor);
    }

    [Fact]
    public void Car_EvaluateDifficulty_Landslide_CanNotPass()
    {
        var diff = VehicleProfile.Car.Evaluator.EvaluateDifficulty(DifficultyTypes.Landslide);
        Assert.False(diff.CanPass);
    }

    [Fact]
    public void Pedestrian_EvaluateDifficulty_Flooding_Returns01()
    {
        var diff = VehicleProfile.Pedestrian.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);
        Assert.True(diff.CanPass);
        Assert.Equal(0.1f, diff.SpeedFactor);
    }

    [Fact]
    public void EvaluateDifficulty_UnknownType_ReturnsDefault()
    {
        // 未定義タイプ → difficultyDefault (1.0, true) が適用される
        var diff = VehicleProfile.Car.Evaluator.EvaluateDifficulty("unknown_meteor_strike");
        Assert.True(diff.CanPass);
        Assert.Equal(1.0f, diff.SpeedFactor);
    }

    [Fact]
    public void EvaluateDifficulty_NullOrEmpty_ReturnsDefault()
    {
        var diff1 = VehicleProfile.Car.Evaluator.EvaluateDifficulty("");
        Assert.True(diff1.CanPass);
        Assert.Equal(1.0f, diff1.SpeedFactor);
    }

    [Fact]
    public void EvaluateDifficulty_AllBuiltInTypes_ResolveSuccessfully()
    {
        var car = VehicleProfile.Car;
        foreach (var type in new[]
        {
            DifficultyTypes.Flooding, DifficultyTypes.Liquefaction,
            DifficultyTypes.Landslide, DifficultyTypes.Construction,
            DifficultyTypes.Obstacle, DifficultyTypes.Congestion,
            DifficultyTypes.Snow, DifficultyTypes.Ice,
        })
        {
            var diff = car.Evaluator.EvaluateDifficulty(type);
            Assert.InRange(diff.SpeedFactor, 0f, 1f);
        }
    }

    // --- ユーザー JSON ロード ---

    [Fact]
    public void LoadFromJsonString_MinimalValidProfile_LoadsSuccessfully()
    {
        const string json = """
        {
          "name": "mybike",
          "highway": { "cycleway": { "speedKmh": 20, "access": "yes" } },
          "accessValueMap": { "yes": "allow", "no": "deny" },
          "fallback": { "speedKmh": 10, "access": "no" },
          "speedBounds": { "minKmh": 1, "maxKmh": 30 },
          "difficultyDefault": { "speedFactor": 1.0, "canPass": true }
        }
        """;

        var profile = VehicleProfile.LoadFromJsonString(json);

        Assert.Equal("mybike", profile.Name);
        var eval = profile.Evaluator.Evaluate(Tags(("highway", "cycleway")));
        Assert.True(eval.CanPass);
        Assert.Equal(20f, eval.SpeedKmh);
    }

    [Fact]
    public void LoadFromJsonString_UserDefinedDifficulty_IsEvaluable()
    {
        const string json = """
        {
          "name": "snowmobile",
          "highway": { "track": { "speedKmh": 40, "access": "yes" } },
          "accessValueMap": { "yes": "allow", "no": "deny" },
          "fallback": { "speedKmh": 10, "access": "no" },
          "speedBounds": { "minKmh": 1, "maxKmh": 60 },
          "difficulty": {
            "snow": { "speedFactor": 1.0, "canPass": true },
            "tsunami": { "speedFactor": 0.0, "canPass": false }
          },
          "difficultyDefault": { "speedFactor": 1.0, "canPass": true }
        }
        """;

        var profile = VehicleProfile.LoadFromJsonString(json);

        // ユーザー定義難所タイプ "tsunami"
        var diff = profile.Evaluator.EvaluateDifficulty("tsunami");
        Assert.False(diff.CanPass);
    }

    [Fact]
    public void LoadFromJsonString_InvalidJson_ThrowsInvalidProfileException()
    {
        const string brokenJson = "{ \"name\": \"x\", invalid";
        Assert.Throws<InvalidProfileException>(() => VehicleProfile.LoadFromJsonString(brokenJson));
    }

    [Fact]
    public void LoadFromJsonString_MissingName_ThrowsInvalidProfileException()
    {
        const string json = """
        {
          "highway": { "x": { "speedKmh": 1, "access": "yes" } },
          "accessValueMap": { "yes": "allow" },
          "fallback": { "speedKmh": 10, "access": "no" },
          "speedBounds": { "minKmh": 1, "maxKmh": 30 },
          "difficultyDefault": { "speedFactor": 1.0, "canPass": true }
        }
        """;

        var ex = Assert.Throws<InvalidProfileException>(() => VehicleProfile.LoadFromJsonString(json));
        Assert.Contains("name", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromJsonString_DifficultySpeedFactorOutOfRange_ThrowsInvalidProfileException()
    {
        const string json = """
        {
          "name": "test",
          "highway": { "x": { "speedKmh": 1, "access": "yes" } },
          "accessValueMap": { "yes": "allow" },
          "fallback": { "speedKmh": 10, "access": "no" },
          "speedBounds": { "minKmh": 1, "maxKmh": 30 },
          "difficulty": { "weird": { "speedFactor": 1.5, "canPass": true } },
          "difficultyDefault": { "speedFactor": 1.0, "canPass": true }
        }
        """;

        var ex = Assert.Throws<InvalidProfileException>(() => VehicleProfile.LoadFromJsonString(json));
        Assert.Contains("speedFactor", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LoadFromJsonFile_NonExistent_ThrowsFileNotFound()
    {
        var fakePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        Assert.Throws<FileNotFoundException>(() => VehicleProfile.LoadFromJsonFile(fakePath));
    }

    [Fact]
    public void LoadFromJsonString_NullOrEmpty_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() => VehicleProfile.LoadFromJsonString(""));
        Assert.Throws<ArgumentException>(() => VehicleProfile.LoadFromJsonString(null!));
    }

    [Fact]
    public void LoadFromJsonStream_Null_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => VehicleProfile.LoadFromJsonStream(null!));
    }

    private static Dictionary<string, string> Tags(params (string Key, string Value)[] entries)
    {
        var dict = new Dictionary<string, string>(entries.Length, StringComparer.Ordinal);
        foreach (var (k, v) in entries)
        {
            dict[k] = v;
        }
        return dict;
    }
}
