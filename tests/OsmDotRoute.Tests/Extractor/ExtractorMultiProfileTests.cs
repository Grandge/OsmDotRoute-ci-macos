using System;
using System.Collections.Generic;
using System.Text;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Pbf.Osm;
using Xunit;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// Phase 3 ステップ 3D.4「Extractor 4 プロファイル bake」の統合テスト。
/// car / pedestrian / bicycle / truck の 4 プロファイルで <see cref="BakedProfileTable"/> を構築し、
/// 各 (プロファイル, エッジ) セルの評価結果が個別プロファイル定義と整合することを検証する。
/// </summary>
public sealed class ExtractorMultiProfileTests
{
    private static EdgeRecord MakeEdgeWithTags(long osmWayId, params (string Key, string Value)[] tags)
    {
        var entries = new List<string> { "" };
        var keys = new int[tags.Length];
        var values = new int[tags.Length];
        for (int i = 0; i < tags.Length; i++)
        {
            keys[i] = entries.Count;
            entries.Add(tags[i].Key);
            values[i] = entries.Count;
            entries.Add(tags[i].Value);
        }
        var bytes = new byte[entries.Count][];
        for (int i = 0; i < entries.Count; i++)
            bytes[i] = Encoding.UTF8.GetBytes(entries[i]);
        var table = new OsmStringTable(bytes);

        return new EdgeRecord(
            OsmWayId: osmWayId,
            FromVertexId: 0,
            ToVertexId: 1,
            ShapeNodeRefs: Array.Empty<long>(),
            TagKeys: keys,
            TagValues: values,
            StringTable: table);
    }

    private static VehicleProfile[] FourProfiles() => new[]
    {
        VehicleProfile.Car,
        VehicleProfile.Pedestrian,
        VehicleProfile.Bicycle,
        VehicleProfile.Truck,
    };

    [Fact]
    public void Build_FourProfilesAndDiverseEdges_TableHasCorrectShape()
    {
        var edges = new[]
        {
            MakeEdgeWithTags(1, ("highway", "motorway")),
            MakeEdgeWithTags(2, ("highway", "cycleway")),
            MakeEdgeWithTags(3, ("highway", "footway")),
            MakeEdgeWithTags(4, ("highway", "primary")),
        };
        var table = ProfileBaker.Build(FourProfiles(), edges);

        Assert.Equal(4, table.ProfileCount);
        Assert.Equal(4, table.EdgeCount);
        Assert.Equal(
            new[] { "car", "pedestrian", "bicycle", "truck" },
            table.ProfileNames);
    }

    [Fact]
    public void Build_MotorwayEdge_AllowedByCarAndTruckButDeniedByPedestrianAndBicycle()
    {
        var edges = new[] { MakeEdgeWithTags(1, ("highway", "motorway")) };
        var table = ProfileBaker.Build(FourProfiles(), edges);

        Assert.True(table.Get(0, 0).CanPass);   // car
        Assert.False(table.Get(1, 0).CanPass);  // pedestrian
        Assert.False(table.Get(2, 0).CanPass);  // bicycle
        Assert.True(table.Get(3, 0).CanPass);   // truck
    }

    [Fact]
    public void Build_CyclewayEdge_AllowedByPedestrianAndBicycleButDeniedByCarAndTruck()
    {
        var edges = new[] { MakeEdgeWithTags(1, ("highway", "cycleway")) };
        var table = ProfileBaker.Build(FourProfiles(), edges);

        Assert.False(table.Get(0, 0).CanPass);  // car
        Assert.True(table.Get(1, 0).CanPass);   // pedestrian
        Assert.True(table.Get(2, 0).CanPass);   // bicycle
        Assert.False(table.Get(3, 0).CanPass);  // truck
    }

    [Fact]
    public void Build_PrimaryWithMaxweight8t_BlocksTruckOnlyViaVehicleLimits()
    {
        // primary は通常 car/pedestrian/bicycle/truck 全てで通行可だが、
        // maxweight=8 で Truck (vehicleLimits.maxWeightTon=20) のみ通行不可になる
        var edges = new[]
        {
            MakeEdgeWithTags(1, ("highway", "primary"), ("maxweight", "8")),
        };
        var table = ProfileBaker.Build(FourProfiles(), edges);

        Assert.True(table.Get(0, 0).CanPass);   // car (vehicleLimits なし)
        Assert.True(table.Get(1, 0).CanPass);   // pedestrian (vehicleLimits なし)
        Assert.True(table.Get(2, 0).CanPass);   // bicycle (vehicleLimits なし)
        Assert.False(table.Get(3, 0).CanPass);  // truck (vehicleLimits 20t > 8t)
    }

    [Fact]
    public void Build_LivingStreetEdge_TruckAllowsAtLowSpeedForNaturalAvoidance()
    {
        // living_street は Truck で speedKmh=5 低設定（Q2 確定: Dijkstra で自然回避）
        var edges = new[] { MakeEdgeWithTags(1, ("highway", "living_street")) };
        var table = ProfileBaker.Build(FourProfiles(), edges);

        var truckEntry = table.Get(3, 0);
        Assert.True(truckEntry.CanPass);
        Assert.True(truckEntry.SpeedKmh <= 10f,
            $"Truck living_street は自然回避のため低速 (実値 {truckEntry.SpeedKmh})");
    }

    [Fact]
    public void Build_PrimaryWithHgvNo_DeniesTruckOnly()
    {
        // hgv=no は Truck の accessTagKeys 末尾優先で拒否、他プロファイルは hgv を見ない
        var edges = new[] { MakeEdgeWithTags(1, ("highway", "primary"), ("hgv", "no")) };
        var table = ProfileBaker.Build(FourProfiles(), edges);

        Assert.True(table.Get(0, 0).CanPass);   // car (hgv を accessTagKeys に含まない)
        Assert.True(table.Get(1, 0).CanPass);   // pedestrian
        Assert.True(table.Get(2, 0).CanPass);   // bicycle
        Assert.False(table.Get(3, 0).CanPass);  // truck (hgv=no で拒否)
    }
}
