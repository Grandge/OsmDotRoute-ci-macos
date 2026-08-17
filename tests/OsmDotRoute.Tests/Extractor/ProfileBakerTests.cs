using System;
using System.Collections.Generic;
using System.Text;
using OsmDotRoute;
using OsmDotRoute.Extractor.Pipeline;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Tests.Extractor;

/// <summary>
/// サブステップ 3.6 — <see cref="ProfileBaker"/> + <see cref="BakedProfileTable"/> の bake テスト。
/// </summary>
public sealed class ProfileBakerTests
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

    [Fact]
    public void Bake_ResidentialRoadWithCarProfile_CanPassWithSpeed()
    {
        var edge = MakeEdgeWithTags(1, ("highway", "residential"));
        var entry = ProfileBaker.Bake(edge, VehicleProfile.Car);

        Assert.True(entry.CanPass);
        Assert.True(entry.Forward);
        Assert.True(entry.Backward);
        Assert.True(entry.SpeedKmh > 0);
    }

    [Fact]
    public void Bake_MotorwayWithPedestrianProfile_CannotPass()
    {
        var edge = MakeEdgeWithTags(1, ("highway", "motorway"));
        var entry = ProfileBaker.Bake(edge, VehicleProfile.Pedestrian);

        Assert.False(entry.CanPass);
        Assert.False(entry.Forward);
        Assert.False(entry.Backward);
        Assert.Equal(0f, entry.SpeedKmh);
    }

    [Fact]
    public void Bake_OnewayResidential_ForwardOnly()
    {
        var edge = MakeEdgeWithTags(1, ("highway", "residential"), ("oneway", "yes"));
        var entry = ProfileBaker.Bake(edge, VehicleProfile.Car);

        Assert.True(entry.CanPass);
        Assert.True(entry.Forward);
        Assert.False(entry.Backward);
    }

    [Fact]
    public void Bake_OnewayReverseResidential_BackwardOnly()
    {
        var edge = MakeEdgeWithTags(1, ("highway", "residential"), ("oneway", "-1"));
        var entry = ProfileBaker.Bake(edge, VehicleProfile.Car);

        Assert.True(entry.CanPass);
        Assert.False(entry.Forward);
        Assert.True(entry.Backward);
    }

    [Fact]
    public void Build_TwoProfilesTwoEdges_TableHasCorrectDimensions()
    {
        var edges = new[]
        {
            MakeEdgeWithTags(1, ("highway", "residential")),
            MakeEdgeWithTags(2, ("highway", "footway")),
        };
        var profiles = new[] { VehicleProfile.Car, VehicleProfile.Pedestrian };

        var table = ProfileBaker.Build(profiles, edges);

        Assert.Equal(2, table.ProfileCount);
        Assert.Equal(2, table.EdgeCount);
        Assert.Equal(new[] { "car", "pedestrian" }, table.ProfileNames);
    }

    [Fact]
    public void Build_TwoProfilesTwoEdges_PerCellResultsMakeSense()
    {
        var edges = new[]
        {
            MakeEdgeWithTags(1, ("highway", "residential")),
            MakeEdgeWithTags(2, ("highway", "footway")),
        };
        var profiles = new[] { VehicleProfile.Car, VehicleProfile.Pedestrian };

        var table = ProfileBaker.Build(profiles, edges);

        // car × residential = OK
        Assert.True(table.Get(0, 0).CanPass);
        // car × footway = NG (歩道は車は通れない)
        Assert.False(table.Get(0, 1).CanPass);
        // pedestrian × residential = OK
        Assert.True(table.Get(1, 0).CanPass);
        // pedestrian × footway = OK
        Assert.True(table.Get(1, 1).CanPass);
    }

    [Fact]
    public void Build_GetProfileEntries_ReturnsAllEntriesForProfile()
    {
        var edges = new[]
        {
            MakeEdgeWithTags(1, ("highway", "residential")),
            MakeEdgeWithTags(2, ("highway", "service")),
            MakeEdgeWithTags(3, ("highway", "footway")),
        };
        var table = ProfileBaker.Build(new[] { VehicleProfile.Car }, edges);

        ReadOnlySpan<BakedProfileEntry> carEntries = table.GetProfileEntries(0);
        Assert.Equal(3, carEntries.Length);
        Assert.True(carEntries[0].CanPass);
        Assert.True(carEntries[1].CanPass);
        Assert.False(carEntries[2].CanPass);
    }

    [Fact]
    public void Build_NullArgs_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            ProfileBaker.Build(null!, Array.Empty<EdgeRecord>()));
        Assert.Throws<ArgumentNullException>(() =>
            ProfileBaker.Build(new[] { VehicleProfile.Car }, null!));
    }

    [Fact]
    public void Bake_NullArgs_Throws()
    {
        var edge = MakeEdgeWithTags(1, ("highway", "residential"));
        Assert.Throws<ArgumentNullException>(() => ProfileBaker.Bake(null!, VehicleProfile.Car));
        Assert.Throws<ArgumentNullException>(() => ProfileBaker.Bake(edge, null!));
    }

    [Fact]
    public void Build_EmptyEdges_TableHasZeroEdges()
    {
        var table = ProfileBaker.Build(new[] { VehicleProfile.Car }, Array.Empty<EdgeRecord>());
        Assert.Equal(1, table.ProfileCount);
        Assert.Equal(0, table.EdgeCount);
        Assert.Equal(0, table.GetProfileEntries(0).Length);
    }

    [Fact]
    public void BakedProfileTable_MismatchProfileCount_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            new BakedProfileTable(
                new[] { "car", "ped" },
                new BakedProfileEntry[][] { Array.Empty<BakedProfileEntry>() }));  // names=2, byProfile=1
    }
}
