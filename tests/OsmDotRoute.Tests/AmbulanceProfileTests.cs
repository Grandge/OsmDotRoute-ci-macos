using OsmDotRoute.Profiles;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 4「救急車 (ambulance) プロファイル」の検証テスト。
/// REQ-PRF-005: 緊急自動車（救急車相当）。一方通行逆走可（ignoreOneway）、emergency アクセスタグ評価、
/// 歩道(footway/path)も低速通行可（Q2）、小型寸法（4.0t / 2.6m / 2.0m）、難所耐性は car より高め。
/// </summary>
public class AmbulanceProfileTests
{
    // --- 同梱プロファイルのロード ---

    [Fact]
    public void Ambulance_LoadsFromEmbeddedResource()
    {
        var ambulance = VehicleProfile.Ambulance;
        Assert.Equal("ambulance", ambulance.Name);
    }

    [Fact]
    public void Ambulance_IsCached()
    {
        Assert.Same(VehicleProfile.Ambulance, VehicleProfile.Ambulance);
    }

    // --- 通行可エッジ（主要道路）---

    [Fact]
    public void Ambulance_Evaluate_Motorway_Allows()
    {
        // motorway raw 120 × speedMultiplier 0.75 = 90 km/h
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(Tags(("highway", "motorway")));

        Assert.True(eval.CanPass);
        Assert.Equal(90f, eval.SpeedKmh);
    }

    // --- 歩道通行可（Q2: car と異なり footway/path/pedestrian/cycleway を低速通行可）---

    [Fact]
    public void Ambulance_Evaluate_Footway_AllowsAtLowSpeed()
    {
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(Tags(("highway", "footway")));

        Assert.True(eval.CanPass, "救急車は緊急時に歩道を通行可（Q2）");
        Assert.True(eval.SpeedKmh <= 10f, $"歩道は低速通行（実値 {eval.SpeedKmh}）");
    }

    [Fact]
    public void Ambulance_Evaluate_Path_AllowsAtLowSpeed()
    {
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(Tags(("highway", "path")));

        Assert.True(eval.CanPass);
        Assert.True(eval.SpeedKmh <= 10f);
    }

    [Fact]
    public void Ambulance_Evaluate_Pedestrian_AllowsAtLowSpeed()
    {
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(Tags(("highway", "pedestrian")));

        Assert.True(eval.CanPass);
        Assert.True(eval.SpeedKmh <= 10f);
    }

    [Fact]
    public void Ambulance_Evaluate_FootwayAllowed_ContrastWithCar()
    {
        // car は footway 通行不可、ambulance は通行可（Q2 の差別化）
        var ambulanceEval = VehicleProfile.Ambulance.Evaluator.Evaluate(Tags(("highway", "footway")));
        var carEval = VehicleProfile.Car.Evaluator.Evaluate(Tags(("highway", "footway")));

        Assert.True(ambulanceEval.CanPass);
        Assert.False(carEval.CanPass);
    }

    // --- steps は物理的に通行不可 ---

    [Fact]
    public void Ambulance_Evaluate_Steps_Denies()
    {
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(Tags(("highway", "steps")));
        Assert.False(eval.CanPass);
    }

    // --- emergency アクセスタグ評価（末尾優先で access=no/private を上書き）---

    [Fact]
    public void Ambulance_Evaluate_EmergencyYes_OverridesAccessPrivate()
    {
        // access=private でも emergency=yes が末尾優先で許可（§1.1: emergency アクセスキー）
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(
            Tags(("highway", "service"), ("access", "private"), ("emergency", "yes")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void Ambulance_Evaluate_EmergencyDesignated_OverridesAccessNo()
    {
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(
            Tags(("highway", "service"), ("access", "no"), ("emergency", "designated")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void Ambulance_Evaluate_EmergencyNo_DeniesAllowedRoad()
    {
        // emergency=no は末尾優先で通行不可（緊急車も入れない明示）
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(
            Tags(("highway", "service"), ("emergency", "no")));
        Assert.False(eval.CanPass);
    }

    // --- vehicleLimits 評価（小型寸法 4.0t / 2.6m / 2.0m）---

    [Fact]
    public void Ambulance_Evaluate_Maxweight3t_Denies()
    {
        // 4.0t > 3t → 通行不可（物理制約は emergency でも遵守、§1.1）
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "3")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Ambulance_Evaluate_Maxweight5t_Allows()
    {
        // 4.0t < 5t → 通行可
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "5")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void Ambulance_Evaluate_EmergencyYesButMaxweightExceeded_StillDenies()
    {
        // emergency=yes でアクセス許可されても、vehicleLimits（物理制約）は上書き不可（§1.1）
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("emergency", "yes"), ("maxweight", "3")));
        Assert.False(eval.CanPass);
    }

    // --- oneway（ignoreOneway: true で逆走可 → 常に Bidirectional）---

    [Fact]
    public void Ambulance_Evaluate_OnewayYes_IgnoredReturnsBidirectional()
    {
        // 一方通行逆走可（§2.1）→ ignoreOneway: true
        var eval = VehicleProfile.Ambulance.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("oneway", "yes")));

        Assert.Equal(OnewayDirection.Bidirectional, eval.Oneway);
    }

    // --- 難所評価（car より耐性高め、landslide は canPass=false 維持）---

    [Fact]
    public void Ambulance_EvaluateDifficulty_Landslide_CanNotPass()
    {
        // 土砂崩れは緊急車でも物理的に通行不可（R2）
        var diff = VehicleProfile.Ambulance.Evaluator.EvaluateDifficulty(DifficultyTypes.Landslide);
        Assert.False(diff.CanPass);
    }

    [Fact]
    public void Ambulance_EvaluateDifficulty_Congestion_HigherThanCar()
    {
        // 緊急車はサイレンで渋滞をすり抜け → congestion 耐性が car より高い
        var ambulanceCong = VehicleProfile.Ambulance.Evaluator.EvaluateDifficulty(DifficultyTypes.Congestion);
        var carCong = VehicleProfile.Car.Evaluator.EvaluateDifficulty(DifficultyTypes.Congestion);

        Assert.True(ambulanceCong.CanPass);
        Assert.True(ambulanceCong.SpeedFactor > carCong.SpeedFactor,
            $"Ambulance congestion ({ambulanceCong.SpeedFactor}) should exceed Car ({carCong.SpeedFactor})");
    }

    [Fact]
    public void Ambulance_EvaluateDifficulty_AllBuiltInTypes_InRange()
    {
        foreach (var type in new[]
        {
            DifficultyTypes.Flooding, DifficultyTypes.Liquefaction,
            DifficultyTypes.Landslide, DifficultyTypes.Construction,
            DifficultyTypes.Obstacle, DifficultyTypes.Congestion,
            DifficultyTypes.Snow, DifficultyTypes.Ice,
        })
        {
            var diff = VehicleProfile.Ambulance.Evaluator.EvaluateDifficulty(type);
            Assert.InRange(diff.SpeedFactor, 0f, 1f);
        }
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
