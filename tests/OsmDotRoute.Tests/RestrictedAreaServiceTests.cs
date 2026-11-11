using OsmDotRoute.Geometry;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 1 ステップ 8「制約管理基盤」の <see cref="RestrictedAreaService"/> 単体テスト。
/// 進入不可・難所エリアの登録／削除／タグ削除／全クリア／一覧、メッシュ一括登録、
/// AABB プリフィルタ（QueryCandidates）を検証する（REQ-RST-001〜015）。
/// </summary>
public class RestrictedAreaServiceTests
{
    /// <summary>(35,139)-(36,140) の単位正方形</summary>
    private static GeoPolygon UnitSquare()
    {
        var ring = new[]
        {
            new GeoCoordinate(35.0, 139.0),
            new GeoCoordinate(36.0, 139.0),
            new GeoCoordinate(36.0, 140.0),
            new GeoCoordinate(35.0, 140.0),
            new GeoCoordinate(35.0, 139.0),
        };
        return new GeoPolygon(ring);
    }

    [Fact]
    public void AddBlockArea_Polygon_Registers_And_Returns_BlockArea_In_List()
    {
        var service = new RestrictedAreaService();
        var id = service.AddBlockArea(UnitSquare(), tag: "disaster-1");

        var all = service.ListAll();
        Assert.Single(all);
        var area = Assert.IsType<BlockArea>(all[0]);
        Assert.Equal(id, area.Id);
        Assert.Equal("disaster-1", area.Tag);
        Assert.NotNull(area.Polygon);
        Assert.Null(area.MeshCodes);
    }

    [Fact]
    public void AddDifficultyArea_Polygon_Registers_With_DifficultyType()
    {
        var service = new RestrictedAreaService();
        var id = service.AddDifficultyArea(UnitSquare(), DifficultyTypes.Flooding);

        var area = Assert.IsType<DifficultyArea>(service.ListAll()[0]);
        Assert.Equal(id, area.Id);
        Assert.Equal(DifficultyTypes.Flooding, area.DifficultyType);
        Assert.NotNull(area.Polygon);
    }

    [Fact]
    public void AddBlockArea_Mesh_Registers_Single_Mesh()
    {
        var service = new RestrictedAreaService();
        var id = service.AddBlockArea(new MeshCode(53394611L));

        var area = Assert.IsType<BlockArea>(service.ListAll()[0]);
        Assert.Null(area.Polygon);
        Assert.NotNull(area.MeshCodes);
        Assert.Single(area.MeshCodes);
        Assert.Equal(53394611L, area.MeshCodes[0].Value);
        Assert.Equal(id, area.Id);
    }

    [Fact]
    public void AddBlockArea_MultipleMeshes_Registers_As_Single_Entry()
    {
        var service = new RestrictedAreaService();
        var meshes = new[]
        {
            new MeshCode(53394611L),
            new MeshCode(53394612L),
            new MeshCode(53394621L),
        };
        var id = service.AddBlockArea(meshes);

        var all = service.ListAll();
        Assert.Single(all);    // 1 ID = 1 RestrictedArea
        var area = Assert.IsType<BlockArea>(all[0]);
        Assert.Equal(id, area.Id);
        Assert.NotNull(area.MeshCodes);
        Assert.Equal(3, area.MeshCodes.Count);
    }

    [Fact]
    public void AddDifficultyArea_MultipleMeshes_With_DifficultyType()
    {
        var service = new RestrictedAreaService();
        var meshes = new[] { new MeshCode(53394611L), new MeshCode(53394612L) };
        var id = service.AddDifficultyArea(meshes, DifficultyTypes.Construction);

        var area = Assert.IsType<DifficultyArea>(service.ListAll()[0]);
        Assert.Equal(id, area.Id);
        Assert.Equal(DifficultyTypes.Construction, area.DifficultyType);
        Assert.Equal(2, area.MeshCodes!.Count);
    }

    [Fact]
    public void AddDifficultyArea_Rejects_Empty_Or_Null_DifficultyType()
    {
        var service = new RestrictedAreaService();
        Assert.Throws<ArgumentException>(() => service.AddDifficultyArea(UnitSquare(), ""));
        Assert.Throws<ArgumentException>(() => service.AddDifficultyArea(UnitSquare(), "   "));
        Assert.Throws<ArgumentException>(() => service.AddDifficultyArea(UnitSquare(), null!));
    }

    [Fact]
    public void AddDifficultyArea_Accepts_UserDefined_DifficultyType()
    {
        var service = new RestrictedAreaService();
        var id = service.AddDifficultyArea(UnitSquare(), "snow_heavy");
        var area = Assert.IsType<DifficultyArea>(service.ListAll()[0]);
        Assert.Equal("snow_heavy", area.DifficultyType);
        Assert.Equal(id, area.Id);
    }

    [Fact]
    public void Remove_By_Id_Removes_Single_Entry()
    {
        var service = new RestrictedAreaService();
        var id1 = service.AddBlockArea(UnitSquare());
        var id2 = service.AddBlockArea(new MeshCode(53394611L));

        service.Remove(id1);

        var all = service.ListAll();
        Assert.Single(all);
        Assert.Equal(id2, all[0].Id);
    }

    [Fact]
    public void Remove_Unknown_Id_Is_NoOp()
    {
        var service = new RestrictedAreaService();
        service.AddBlockArea(UnitSquare());
        service.Remove(RestrictedAreaId.New());    // 例外を投げないこと
        Assert.Single(service.ListAll());
    }

    [Fact]
    public void RemoveByTag_Removes_All_Matching_Entries()
    {
        var service = new RestrictedAreaService();
        service.AddBlockArea(UnitSquare(), tag: "disaster-1");
        service.AddBlockArea(new MeshCode(53394611L), tag: "disaster-1");
        service.AddBlockArea(new MeshCode(53394612L), tag: "disaster-2");

        service.RemoveByTag("disaster-1");

        var all = service.ListAll();
        Assert.Single(all);
        Assert.Equal("disaster-2", all[0].Tag);
    }

    [Fact]
    public void ClearAll_Empties_Service()
    {
        var service = new RestrictedAreaService();
        service.AddBlockArea(UnitSquare());
        service.AddDifficultyArea(UnitSquare(), DifficultyTypes.Flooding);
        service.ClearAll();
        Assert.Empty(service.ListAll());
    }

    [Fact]
    public void QueryCandidates_Returns_Only_Intersecting_Areas()
    {
        var service = new RestrictedAreaService();
        var insideId = service.AddBlockArea(UnitSquare());                          // (35,139)-(36,140)
        service.AddBlockArea(new MeshCode(53394611L));                              // 東京駅付近 ≈ (35.675,139.7625)-(35.683,139.775)
        var farPolygon = new GeoPolygon(new[]
        {
            new GeoCoordinate(40.0, 141.0),
            new GeoCoordinate(41.0, 141.0),
            new GeoCoordinate(41.0, 142.0),
            new GeoCoordinate(40.0, 142.0),
            new GeoCoordinate(40.0, 141.0),
        });
        service.AddBlockArea(farPolygon);    // 完全に離れた領域

        // UnitSquare の南西だけを切り取り、東京駅メッシュ・farPolygon はクエリ外になる範囲
        var query = new Aabb(new GeoCoordinate(34.5, 138.5), new GeoCoordinate(35.5, 139.5));
        var hits = service.QueryCandidates(query).ToList();

        Assert.Single(hits);
        Assert.Equal(insideId, hits[0].Id);
    }

    [Fact]
    public void QueryCandidates_By_Segment_Returns_Areas_Whose_Aabb_Intersects()
    {
        var service = new RestrictedAreaService();
        var id = service.AddBlockArea(UnitSquare());
        service.AddBlockArea(new GeoPolygon(new[]
        {
            new GeoCoordinate(40.0, 141.0),
            new GeoCoordinate(41.0, 141.0),
            new GeoCoordinate(41.0, 142.0),
            new GeoCoordinate(40.0, 142.0),
            new GeoCoordinate(40.0, 141.0),
        }));

        var hits = service.QueryCandidates(
            new GeoCoordinate(35.5, 138.5),
            new GeoCoordinate(35.5, 140.5)).ToList();
        Assert.Single(hits);
        Assert.Equal(id, hits[0].Id);
    }

    [Fact]
    public void QueryCandidates_For_MultiMesh_Returns_Id_Once_Even_When_Multiple_Shapes_Hit()
    {
        var service = new RestrictedAreaService();
        // 隣接する 2 メッシュ（南北方向）を 1 ID で登録
        var meshes = new[] { new MeshCode(53394611L), new MeshCode(53394621L) };
        var id = service.AddBlockArea(meshes);

        // 両方の AABB に跨る大きなクエリ
        var query = new Aabb(
            new GeoCoordinate(35.0, 139.0),
            new GeoCoordinate(36.0, 140.0));
        var hits = service.QueryCandidates(query).ToList();

        // 同じ ID は 1 回だけ返ること
        Assert.Single(hits);
        Assert.Equal(id, hits[0].Id);
    }
}
