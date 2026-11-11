using OsmDotRoute.Profiles;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 4「災害用車両 (disaster) プロファイル」の検証テスト。
/// REQ-PRF-006: 災害対策基本法の緊急通行車両（重機含む）相当。寸法は truck 同等（20t / 3.8m / 2.5m）、
/// 難所耐性を強化（flooding/liquefaction/construction/obstacle の speedFactor を truck/car より高め）、
/// landslide は canPass=false 維持（N5）、ignoreOneway=false（災害規制は上位レイヤー責務、R3）。
/// </summary>
public class DisasterProfileTests
{
    // --- 同梱プロファイルのロード ---

    [Fact]
    public void Disaster_LoadsFromEmbeddedResource()
    {
        var disaster = VehicleProfile.Disaster;
        Assert.Equal("disaster", disaster.Name);
    }

    [Fact]
    public void Disaster_IsCached()
    {
        Assert.Same(VehicleProfile.Disaster, VehicleProfile.Disaster);
    }

    // --- 通行可エッジ（truck 同等速度）---

    [Fact]
    public void Disaster_Evaluate_Motorway_Allows()
    {
        // motorway raw 90 × speedMultiplier 0.75 = 67.5 km/h
        var eval = VehicleProfile.Disaster.Evaluator.Evaluate(Tags(("highway", "motorway")));

        Assert.True(eval.CanPass);
        Assert.Equal(67.5f, eval.SpeedKmh);
    }

    // --- emergency アクセスタグ評価（緊急通行車両は緊急自動車を包含）---

    [Fact]
    public void Disaster_Evaluate_EmergencyYes_OverridesAccessPrivate()
    {
        var eval = VehicleProfile.Disaster.Evaluator.Evaluate(
            Tags(("highway", "service"), ("access", "private"), ("emergency", "yes")));
        Assert.True(eval.CanPass);
    }

    // --- vehicleLimits 評価（truck 同等 20t / 3.8m / 2.5m）---

    [Fact]
    public void Disaster_Evaluate_Maxweight8t_Denies()
    {
        // 20t > 8t → 通行不可（truck 同等寸法）
        var eval = VehicleProfile.Disaster.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "8")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Disaster_Evaluate_Maxweight25t_Allows()
    {
        var eval = VehicleProfile.Disaster.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "25")));
        Assert.True(eval.CanPass);
    }

    // --- oneway（ignoreOneway: false → 一方通行を尊重、R3）---

    [Fact]
    public void Disaster_Evaluate_OnewayYes_ReturnsForward()
    {
        // 災害規制区間の通行は上位レイヤー責務のため、プロファイル自体は一方通行を尊重（R3）
        var eval = VehicleProfile.Disaster.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("oneway", "yes")));

        Assert.Equal(OnewayDirection.Forward, eval.Oneway);
    }

    // --- 難所評価（耐性中心: truck/car より高い speedFactor）---

    [Fact]
    public void Disaster_EvaluateDifficulty_Landslide_CanNotPass()
    {
        // 土砂崩れは物理的に通行不可（N5）
        var diff = VehicleProfile.Disaster.Evaluator.EvaluateDifficulty(DifficultyTypes.Landslide);
        Assert.False(diff.CanPass);
    }

    [Fact]
    public void Disaster_EvaluateDifficulty_Flooding_HigherThanTruck()
    {
        // 災害用車両は難所耐性が高い → flooding speedFactor が truck より高い
        var disasterFlood = VehicleProfile.Disaster.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);
        var truckFlood = VehicleProfile.Truck.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);

        Assert.True(disasterFlood.CanPass);
        Assert.True(disasterFlood.SpeedFactor > truckFlood.SpeedFactor,
            $"Disaster flooding ({disasterFlood.SpeedFactor}) should exceed Truck ({truckFlood.SpeedFactor})");
    }

    [Fact]
    public void Disaster_EvaluateDifficulty_Obstacle_HigherThanCar()
    {
        // 障害物（瓦礫等）も car より高い耐性
        var disasterObs = VehicleProfile.Disaster.Evaluator.EvaluateDifficulty(DifficultyTypes.Obstacle);
        var carObs = VehicleProfile.Car.Evaluator.EvaluateDifficulty(DifficultyTypes.Obstacle);

        Assert.True(disasterObs.CanPass);
        Assert.True(disasterObs.SpeedFactor > carObs.SpeedFactor,
            $"Disaster obstacle ({disasterObs.SpeedFactor}) should exceed Car ({carObs.SpeedFactor})");
    }

    [Fact]
    public void Disaster_EvaluateDifficulty_AllBuiltInTypes_InRange()
    {
        foreach (var type in new[]
        {
            DifficultyTypes.Flooding, DifficultyTypes.Liquefaction,
            DifficultyTypes.Landslide, DifficultyTypes.Construction,
            DifficultyTypes.Obstacle, DifficultyTypes.Congestion,
            DifficultyTypes.Snow, DifficultyTypes.Ice,
        })
        {
            var diff = VehicleProfile.Disaster.Evaluator.EvaluateDifficulty(type);
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
