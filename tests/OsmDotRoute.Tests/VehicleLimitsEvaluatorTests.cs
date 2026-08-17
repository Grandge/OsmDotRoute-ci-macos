using OsmDotRoute.Profiles;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 3 ステップ 3D.2「ProfileEvaluator vehicleLimits 拡張」の検証テスト。
/// REQ-PRF-004 Truck の maxweight/maxheight/maxwidth 数値評価セマンティクスを網羅。
/// 計画書 §3.1 / §4.2.2 / §4.2.3 採用設計。
/// </summary>
public class VehicleLimitsEvaluatorTests
{
    /// <summary>テスト用最小プロファイル + 任意の vehicleLimits 設定。</summary>
    private static VehicleProfile MakeProfile(string vehicleLimitsJson)
    {
        string json = $$"""
        {
          "name": "test_truck",
          "accessTagKeys": ["access", "vehicle", "hgv"],
          "highway": {
            "primary": { "speedKmh": 80, "access": "yes" }
          },
          "accessValueMap": {
            "yes": "allow",
            "permissive": "allow",
            "destination": "allow",
            "no": "deny",
            "private": "deny"
          },
          "fallback": { "speedKmh": 30, "access": "no" },
          "speedBounds": { "minKmh": 20, "maxKmh": 90 },
          "vehicleLimits": {{vehicleLimitsJson}},
          "difficultyDefault": { "speedFactor": 1.0, "canPass": true }
        }
        """;
        return VehicleProfile.LoadFromJsonString(json);
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

    // --- 回帰確認: vehicleLimits 未定義時は既存挙動と完全一致 ---

    [Fact]
    public void Car_VehicleLimitsUndefined_NoEffectOnExistingTags()
    {
        // car.json は vehicleLimits 未定義。maxweight タグがあっても評価に影響しない
        var eval = VehicleProfile.Car.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "3")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void Pedestrian_VehicleLimitsUndefined_NoEffectOnExistingTags()
    {
        var eval = VehicleProfile.Pedestrian.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "3"), ("maxheight", "2")));
        Assert.True(eval.CanPass);
    }

    // --- maxWeightTon 制限 ---

    [Fact]
    public void MaxWeightTon20_MaxweightTag8_Denies()
    {
        var profile = MakeProfile("{ \"maxWeightTon\": 20.0 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "8")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void MaxWeightTon20_MaxweightTag25_Allows()
    {
        var profile = MakeProfile("{ \"maxWeightTon\": 20.0 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "25")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void MaxWeightTon20_MaxweightWithUnitT_Denies()
    {
        // "8 t" (空白あり) → 単位省略時と同様、t をデフォルト単位として解釈
        var profile = MakeProfile("{ \"maxWeightTon\": 20.0 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "8 t")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void MaxWeightTon20_MaxweightNoSpaceUnit_Denies()
    {
        // "8t" (空白なし) もパース成功
        var profile = MakeProfile("{ \"maxWeightTon\": 20.0 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "8t")));
        Assert.False(eval.CanPass);
    }

    // --- maxHeightMeter 制限 ---

    [Fact]
    public void MaxHeightMeter38_MaxheightTag35_Denies()
    {
        var profile = MakeProfile("{ \"maxHeightMeter\": 3.8 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxheight", "3.5")));
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void MaxHeightMeter38_MaxheightTagWithUnitM_Allows()
    {
        // "4.0 m" 単位付き、制限内
        var profile = MakeProfile("{ \"maxHeightMeter\": 3.8 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxheight", "4.0 m")));
        Assert.True(eval.CanPass);
    }

    // --- maxWidthMeter 制限 ---

    [Fact]
    public void MaxWidthMeter25_MaxwidthTag20_Denies()
    {
        var profile = MakeProfile("{ \"maxWidthMeter\": 2.5 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxwidth", "2.0")));
        Assert.False(eval.CanPass);
    }

    // --- 未対応単位は安全側として制限発火しない (T1 リスク対応) ---

    [Fact]
    public void MaxWeightTon20_MaxweightInKg_DoesNotTriggerLimit()
    {
        // "8000 kg" は OSM 表記としては 8 t 相当だが、本実装では kg 単位を未対応として
        // 制限発火させない（安全側 = 通行可）
        var profile = MakeProfile("{ \"maxWeightTon\": 20.0 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "8000 kg")));
        Assert.True(eval.CanPass);
    }

    [Fact]
    public void MaxWeightTon20_MaxweightInvalidString_DoesNotTriggerLimit()
    {
        // "signals" は数値部なし → パース失敗 → 制限発火しない
        var profile = MakeProfile("{ \"maxWeightTon\": 20.0 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "signals")));
        Assert.True(eval.CanPass);
    }

    // --- hard-deny セマンティクス: access=destination で物理制限を上書きできない ---

    [Fact]
    public void VehicleLimitsExceeded_AccessDestination_StillDenies()
    {
        // access=destination は accessValueMap で "allow" だが、
        // vehicleLimits は accessTagKeys 後に評価されるため上書き不可（hard-deny 等価）
        var profile = MakeProfile("{ \"maxWeightTon\": 20.0 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "8"), ("access", "destination")));
        Assert.False(eval.CanPass);
    }

    // --- 複数制限の組合せ ---

    [Fact]
    public void MultipleLimits_HeightExceededOnly_Denies()
    {
        var profile = MakeProfile(
            "{ \"maxWeightTon\": 20.0, \"maxHeightMeter\": 3.8, \"maxWidthMeter\": 2.5 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "25"), ("maxheight", "3.0"), ("maxwidth", "3.0")));
        // maxheight 3.0 < 3.8 で拒否
        Assert.False(eval.CanPass);
    }

    [Fact]
    public void MultipleLimits_AllWithinBounds_Allows()
    {
        var profile = MakeProfile(
            "{ \"maxWeightTon\": 20.0, \"maxHeightMeter\": 3.8, \"maxWidthMeter\": 2.5 }");
        var eval = profile.Evaluator.Evaluate(
            Tags(("highway", "primary"), ("maxweight", "25"), ("maxheight", "4.5"), ("maxwidth", "3.0")));
        Assert.True(eval.CanPass);
    }

    // --- バリデーション ---

    [Fact]
    public void NegativeMaxWeightTon_ThrowsInvalidProfileException()
    {
        var ex = Assert.Throws<InvalidProfileException>(() =>
            MakeProfile("{ \"maxWeightTon\": -1.0 }"));
        Assert.Contains("maxWeightTon", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NegativeMaxHeightMeter_ThrowsInvalidProfileException()
    {
        var ex = Assert.Throws<InvalidProfileException>(() =>
            MakeProfile("{ \"maxHeightMeter\": -0.5 }"));
        Assert.Contains("maxHeightMeter", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
