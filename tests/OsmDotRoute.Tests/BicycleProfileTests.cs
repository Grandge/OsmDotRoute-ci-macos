using OsmDotRoute.Profiles;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 3 ステップ 3D.1「Bicycle プロファイル」の検証テスト。
/// REQ-PRF-003: cycleway/path 優先、motorway/trunk 通行不可、平均 15 km/h。
/// </summary>
public class BicycleProfileTests
{
    // --- 同梱プロファイルのロード ---

    [Fact]
    public void Bicycle_LoadsFromEmbeddedResource()
    {
        var bicycle = VehicleProfile.Bicycle;
        Assert.Equal("bicycle", bicycle.Name);
    }

    [Fact]
    public void Bicycle_IsCached()
    {
        Assert.Same(VehicleProfile.Bicycle, VehicleProfile.Bicycle);
    }

    // --- 通行可エッジ ---

    [Fact]
    public void Bicycle_Evaluate_Cycleway_AllowsAt15Kmh()
    {
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(Tags(("highway", "cycleway")));

        Assert.True(eval.CanPass);
        Assert.Equal(15f, eval.SpeedKmh);
    }

    [Fact]
    public void Bicycle_Evaluate_Path_AllowsAt15Kmh()
    {
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(Tags(("highway", "path")));

        Assert.True(eval.CanPass);
        Assert.Equal(15f, eval.SpeedKmh);
    }

    [Fact]
    public void Bicycle_Evaluate_Primary_AllowsAt15Kmh()
    {
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(Tags(("highway", "primary")));

        Assert.True(eval.CanPass);
        Assert.Equal(15f, eval.SpeedKmh);
    }

    [Fact]
    public void Bicycle_Evaluate_Footway_AllowsAt5Kmh()
    {
        // 歩行者と同居する footway は徐行 5 km/h（speedBounds.minKmh と一致）
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(Tags(("highway", "footway")));

        Assert.True(eval.CanPass);
        Assert.Equal(5f, eval.SpeedKmh);
    }

    // --- 通行不可エッジ（REQ-PRF-003 highway=motorway/trunk は通行不可）---

    [Fact]
    public void Bicycle_Evaluate_Motorway_Denies()
    {
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(Tags(("highway", "motorway")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Bicycle_Evaluate_Trunk_Denies()
    {
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(Tags(("highway", "trunk")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Bicycle_Evaluate_MotorwayLink_Denies()
    {
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(Tags(("highway", "motorway_link")));
        Assert.False(eval.CanPass);
    }

    // --- アクセスタグ上書き ---

    [Fact]
    public void Bicycle_Evaluate_BicycleNo_OverridesPrimaryAllow()
    {
        // primary はデフォルト許可だが bicycle=no で拒否
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("bicycle", "no")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Bicycle_Evaluate_BicycleYes_DoesNotOverrideMotorwayHardDeny()
    {
        // motorway は highway 別 access=no（hard-deny）なので bicycle=yes でも上書き不可
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(
            Tags(("highway", "motorway"), ("bicycle", "yes")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Bicycle_Evaluate_AccessNo_OverridesPrimaryAllow()
    {
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("access", "no")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Bicycle_Evaluate_UnknownHighway_UsesFallbackDeny()
    {
        // bicycle.json: fallback = { speedKmh: 5, access: "no" }
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(Tags(("highway", "unknown_xyz")));
        Assert.False(eval.CanPass);
    }

    // --- oneway 評価（ignoreOneway: false 確認）---

    [Fact]
    public void Bicycle_Evaluate_OnewayYes_ReturnsForward()
    {
        var eval = VehicleProfile.Bicycle.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("oneway", "yes")));

        Assert.Equal(OnewayDirection.Forward, eval.Oneway);
    }

    // --- 難所評価 ---

    [Fact]
    public void Bicycle_EvaluateDifficulty_Landslide_CanNotPass()
    {
        var diff = VehicleProfile.Bicycle.Evaluator.EvaluateDifficulty(DifficultyTypes.Landslide);
        Assert.False(diff.CanPass);
    }

    [Fact]
    public void Bicycle_EvaluateDifficulty_Flooding_LowSpeedFactor()
    {
        // 自転車は浸水で押し歩き想定（car/pedestrian より低めの speedFactor）
        var diff = VehicleProfile.Bicycle.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);
        Assert.True(diff.CanPass);
        Assert.InRange(diff.SpeedFactor, 0f, 0.5f);
    }

    [Fact]
    public void Bicycle_EvaluateDifficulty_AllBuiltInTypes_ResolveInRange()
    {
        foreach (var type in new[]
        {
            DifficultyTypes.Flooding, DifficultyTypes.Liquefaction,
            DifficultyTypes.Landslide, DifficultyTypes.Construction,
            DifficultyTypes.Obstacle, DifficultyTypes.Congestion,
            DifficultyTypes.Snow, DifficultyTypes.Ice,
        })
        {
            var diff = VehicleProfile.Bicycle.Evaluator.EvaluateDifficulty(type);
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
