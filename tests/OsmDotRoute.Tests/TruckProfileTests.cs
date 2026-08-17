using OsmDotRoute.Profiles;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 3 ステップ 3D.3「Truck (10t) プロファイル」の検証テスト。
/// REQ-PRF-004: 日本道路法ベース（最大積載量 10t、車両総重量 20t / 全高 3.8m / 全幅 2.5m）、
/// hgv/maxweight/maxheight/maxwidth 評価、living_street は speedKmh 低設定で自然回避。
/// </summary>
public class TruckProfileTests
{
    // --- 同梱プロファイルのロード ---

    [Fact]
    public void Truck_LoadsFromEmbeddedResource()
    {
        var truck = VehicleProfile.Truck;
        Assert.Equal("truck", truck.Name);
    }

    [Fact]
    public void Truck_IsCached()
    {
        Assert.Same(VehicleProfile.Truck, VehicleProfile.Truck);
    }

    // --- 通行可エッジ（主要道路）---

    [Fact]
    public void Truck_Evaluate_Motorway_Allows()
    {
        // motorway raw 90 km/h × speedMultiplier 0.75 = 67.5 km/h
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "motorway")));

        Assert.True(eval.CanPass);
        Assert.Equal(67.5f, eval.SpeedKmh);
    }

    [Fact]
    public void Truck_Evaluate_Primary_Allows()
    {
        // primary raw 80 × 0.75 = 60 km/h
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "primary")));

        Assert.True(eval.CanPass);
        Assert.Equal(60f, eval.SpeedKmh);
    }

    // --- Truck 回避エッジ（speedKmh 低設定で Dijkstra コスト経由で自然回避）---

    [Fact]
    public void Truck_Evaluate_LivingStreet_AllowsAtLowSpeed()
    {
        // raw 5 × 0.75 = 3.75 → clamp(min=5) → 5 km/h（speedBounds.minKmh=5）
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "living_street")));

        Assert.True(eval.CanPass);
        Assert.True(eval.SpeedKmh <= 10f, $"living_street は低速で回避されるべき (実値 {eval.SpeedKmh})");
    }

    [Fact]
    public void Truck_Evaluate_Track_AllowsAtLowSpeed()
    {
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "track")));

        Assert.True(eval.CanPass);
        Assert.True(eval.SpeedKmh <= 10f);
    }

    // --- 物理通行不可エッジ ---

    [Fact]
    public void Truck_Evaluate_Footway_Denies()
    {
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "footway")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Truck_Evaluate_Cycleway_Denies()
    {
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "cycleway")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Truck_Evaluate_Path_Denies()
    {
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "path")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Truck_Evaluate_Steps_Denies()
    {
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(Tags(("highway", "steps")));
        Assert.False(eval.CanPass);
    }

    // --- hgv アクセスタグ評価（末尾優先）---

    [Fact]
    public void Truck_Evaluate_HgvNo_DeniesPrimary()
    {
        // primary はデフォルト許可だが hgv=no で拒否（accessTagKeys 末尾優先）
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("hgv", "no")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Truck_Evaluate_HgvYes_OverridesAccessNo()
    {
        // hgv=yes が末尾優先で access=no を上書き
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("access", "no"), ("hgv", "yes")));
        Assert.True(eval.CanPass);
    }

    // --- vehicleLimits 評価（物理寸法 20t / 3.8m / 2.5m）---

    [Fact]
    public void Truck_Evaluate_MaxweightTag8t_Denies()
    {
        // 20t > 8t → 通行不可
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "8")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Truck_Evaluate_MaxweightTag25t_Allows()
    {
        // 20t < 25t → 通行可
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "25")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void Truck_Evaluate_MaxheightTag30m_Denies()
    {
        // 3.8m > 3.0m → 通行不可
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxheight", "3.0")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Truck_Evaluate_MaxwidthTag20m_Denies()
    {
        // 2.5m > 2.0m → 通行不可
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxwidth", "2.0")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void Truck_Evaluate_HgvYesAndMaxweightExceeded_StillDenies()
    {
        // hgv=yes で access タグは許可されても、vehicleLimits は hard-deny で上書き不可
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("hgv", "yes"), ("maxweight", "8")));
        Assert.False(eval.CanPass);
    }

    // --- 難所評価（Truck は car より浸水・液状化に厳しめ）---

    [Fact]
    public void Truck_EvaluateDifficulty_Landslide_CanNotPass()
    {
        var diff = VehicleProfile.Truck.Evaluator.EvaluateDifficulty(DifficultyTypes.Landslide);
        Assert.False(diff.CanPass);
    }

    [Fact]
    public void Truck_EvaluateDifficulty_Flooding_LowerThanCar()
    {
        // Truck は浸水で car より厳しめ（large vehicle は冠水道路リスク大）
        var truckFlood = VehicleProfile.Truck.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);
        var carFlood = VehicleProfile.Car.Evaluator.EvaluateDifficulty(DifficultyTypes.Flooding);

        Assert.True(truckFlood.CanPass);
        Assert.True(truckFlood.SpeedFactor < carFlood.SpeedFactor,
            $"Truck flooding ({truckFlood.SpeedFactor}) should be lower than Car ({carFlood.SpeedFactor})");
    }

    [Fact]
    public void Truck_EvaluateDifficulty_AllBuiltInTypes_InRange()
    {
        foreach (var type in new[]
        {
            DifficultyTypes.Flooding, DifficultyTypes.Liquefaction,
            DifficultyTypes.Landslide, DifficultyTypes.Construction,
            DifficultyTypes.Obstacle, DifficultyTypes.Congestion,
            DifficultyTypes.Snow, DifficultyTypes.Ice,
        })
        {
            var diff = VehicleProfile.Truck.Evaluator.EvaluateDifficulty(type);
            Assert.InRange(diff.SpeedFactor, 0f, 1f);
        }
    }

    // --- oneway ---

    [Fact]
    public void Truck_Evaluate_OnewayYes_ReturnsForward()
    {
        // ignoreOneway: false 確認
        var eval = VehicleProfile.Truck.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("oneway", "yes")));

        Assert.Equal(OnewayDirection.Forward, eval.Oneway);
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
