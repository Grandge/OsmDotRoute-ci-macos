using OsmDotRoute.Profiles;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 4「消防車 (fire_engine) プロファイル」の検証テスト。
/// REQ-PRF-005: 緊急自動車（消防車相当）。一方通行逆走可（ignoreOneway）、emergency/hgv アクセスタグ評価、
/// 歩道(footway/path)も徐行通行可（Q2）、大型寸法（8.0t / 2.9m / 2.1m）、難所耐性は ambulance より控えめ。
/// </summary>
public class FireEngineProfileTests
{
    // --- 同梱プロファイルのロード ---

    [Fact]
    public void FireEngine_LoadsFromEmbeddedResource()
    {
        var fire = VehicleProfile.FireEngine;
        Assert.Equal("fire_engine", fire.Name);
    }

    [Fact]
    public void FireEngine_IsCached()
    {
        Assert.Same(VehicleProfile.FireEngine, VehicleProfile.FireEngine);
    }

    // --- 通行可エッジ（主要道路、truck 同等速度）---

    [Fact]
    public void FireEngine_Evaluate_Motorway_Allows()
    {
        // motorway raw 90 × speedMultiplier 0.75 = 67.5 km/h
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(Tags(("highway", "motorway")));

        Assert.True(eval.CanPass);
        Assert.Equal(67.5f, eval.SpeedKmh);
    }

    // --- 歩道徐行通行可（Q2: truck と異なり footway/path を通行可）---

    [Fact]
    public void FireEngine_Evaluate_Footway_AllowsAtLowSpeed()
    {
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(Tags(("highway", "footway")));

        Assert.True(eval.CanPass, "消防車は緊急時に歩道を徐行通行可（Q2）");
        Assert.True(eval.SpeedKmh <= 5f, $"歩道は徐行（実値 {eval.SpeedKmh}）");
    }

    [Fact]
    public void FireEngine_Evaluate_FootwayAllowed_ContrastWithTruck()
    {
        // truck は footway 通行不可、fire_engine は通行可（Q2 の差別化）
        var fireEval = VehicleProfile.FireEngine.Evaluator.Evaluate(Tags(("highway", "footway")));
        var truckEval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "footway")));

        Assert.True(fireEval.CanPass);
        Assert.False(truckEval.CanPass);
    }

    [Fact]
    public void FireEngine_Evaluate_Steps_Denies()
    {
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(Tags(("highway", "steps")));
        Assert.False(eval.CanPass);
    }

    // --- emergency / hgv アクセスタグ評価 ---

    [Fact]
    public void FireEngine_Evaluate_EmergencyYes_OverridesAccessPrivate()
    {
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "service"), ("access", "private"), ("emergency", "yes")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void FireEngine_Evaluate_EmergencyNo_Denies()
    {
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "service"), ("emergency", "no")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void FireEngine_Evaluate_HgvNo_DeniesPrimary()
    {
        // 消防車は大型のため hgv キーも評価（truck 同様）
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("hgv", "no")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void FireEngine_Evaluate_EmergencyYes_OverridesHgvNo()
    {
        // accessTagKeys 順 [..., "hgv", "emergency"] で emergency が末尾優先
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("hgv", "no"), ("emergency", "yes")));
        Assert.True(eval.CanPass);
    }

    // --- vehicleLimits 評価（大型寸法 8.0t / 2.9m / 2.1m）---

    [Fact]
    public void FireEngine_Evaluate_Maxweight5t_Denies()
    {
        // 8.0t > 5t → 通行不可
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "5")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void FireEngine_Evaluate_Maxweight10t_Allows()
    {
        // 8.0t < 10t → 通行可
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "10")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void FireEngine_Evaluate_Maxheight28_Denies()
    {
        // 2.9m > 2.8m → 通行不可
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxheight", "2.8")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void FireEngine_Evaluate_AmbulancePassesButFireEngineDenied_HeavierVehicle()
    {
        // maxweight=5: ambulance(4t)は通行可、fire_engine(8t)は通行不可 → 寸法差の検証
        var ambEval = VehicleProfile.Ambulance.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "5")));
        var fireEval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "5")));

        Assert.True(ambEval.CanPass);
        Assert.False(fireEval.CanPass);
    }

    [Fact]
    public void FireEngine_Evaluate_EmergencyYesButMaxweightExceeded_StillDenies()
    {
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("emergency", "yes"), ("maxweight", "5")));
        Assert.False(eval.CanPass);
    }

    // --- oneway（ignoreOneway: true）---

    [Fact]
    public void FireEngine_Evaluate_OnewayYes_IgnoredReturnsBidirectional()
    {
        var eval = VehicleProfile.FireEngine.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("oneway", "yes")));

        Assert.Equal(OnewayDirection.Bidirectional, eval.Oneway);
    }

    // --- 難所評価 ---

    [Fact]
    public void FireEngine_EvaluateDifficulty_Landslide_CanNotPass()
    {
        var diff = VehicleProfile.FireEngine.Evaluator.EvaluateDifficulty(DifficultyTypes.Landslide);
        Assert.False(diff.CanPass);
    }

    [Fact]
    public void FireEngine_EvaluateDifficulty_Flooding_LowerThanAmbulance()
    {
        // 大型消防車は冠水に弱い → flooding 耐性は ambulance より低い
        var fireFlood = VehicleProfile.FireEngine.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);
        var ambFlood = VehicleProfile.Ambulance.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);

        Assert.True(fireFlood.CanPass);
        Assert.True(fireFlood.SpeedFactor < ambFlood.SpeedFactor,
            $"FireEngine flooding ({fireFlood.SpeedFactor}) should be lower than Ambulance ({ambFlood.SpeedFactor})");
    }

    [Fact]
    public void FireEngine_EvaluateDifficulty_AllBuiltInTypes_InRange()
    {
        foreach (var type in new[]
        {
            DifficultyTypes.Flooding, DifficultyTypes.Liquefaction,
            DifficultyTypes.Landslide, DifficultyTypes.Construction,
            DifficultyTypes.Obstacle, DifficultyTypes.Congestion,
            DifficultyTypes.Snow, DifficultyTypes.Ice,
        })
        {
            var diff = VehicleProfile.FireEngine.Evaluator.EvaluateDifficulty(type);
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
