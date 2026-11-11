using OsmDotRoute.Mesh;
using OsmDotRoute.Restrictions;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// Phase 3 ステップ 3B.1 — <see cref="RestrictedAreaEdgeCache"/> の単体検証 7 件（計画書 §4.1.3）。
/// </summary>
/// <remarks>
/// T1=A (Difficulty 都度評価) / T2=A (Block 重複は OtherContains 走査) / T3=A (Difficulty 重複は List から RemoveAll) を検証する。
/// </remarks>
public class RestrictedAreaEdgeCacheTests
{
    [Fact]
    public void Empty_IsBlocked_ReturnsFalse_GetDifficultyAreas_Empty()
    {
        var cache = new RestrictedAreaEdgeCache();
        Assert.False(cache.IsBlocked(0));
        Assert.False(cache.IsBlocked(999));
        Assert.Empty(cache.GetDifficultyAreas(0));
    }

    [Fact]
    public void AddBlocked_ThenIsBlocked_ReturnsTrue()
    {
        var cache = new RestrictedAreaEdgeCache();
        var id1 = RestrictedAreaId.New();
        cache.AddBlocked(id1, 100);
        cache.AddBlocked(id1, 200);

        Assert.True(cache.IsBlocked(100));
        Assert.True(cache.IsBlocked(200));
        Assert.False(cache.IsBlocked(300));
    }

    [Fact]
    public void AddDifficulty_ThenGetDifficultyAreas_ContainsArea()
    {
        var cache = new RestrictedAreaEdgeCache();
        var id1 = RestrictedAreaId.New();
        var area = new DifficultyArea(id1, new MeshCode(53394611L), "surface_damage");
        cache.AddDifficulty(id1, area, 100);

        var areas = cache.GetDifficultyAreas(100);
        Assert.Single(areas);
        Assert.Same(area, areas[0]);
        Assert.Empty(cache.GetDifficultyAreas(200));
    }

    [Fact]
    public void MultipleDifficultyAreas_SameEdge_AllReturned()
    {
        var cache = new RestrictedAreaEdgeCache();
        var id1 = RestrictedAreaId.New();
        var id2 = RestrictedAreaId.New();
        var area1 = new DifficultyArea(id1, new MeshCode(53394611L), "surface_damage");
        var area2 = new DifficultyArea(id2, new MeshCode(53394612L), "inundation");

        cache.AddDifficulty(id1, area1, 100);
        cache.AddDifficulty(id2, area2, 100);

        var areas = cache.GetDifficultyAreas(100);
        Assert.Equal(2, areas.Count);
        Assert.Contains(area1, areas);
        Assert.Contains(area2, areas);
    }

    [Fact]
    public void AddDifficulty_SameAreaSameEdgeTwice_StoredOnce()
    {
        // 親プロFB 回帰: 1 エリアが複数 Shape を持つと bake が同一 (areaId, edgeId) を N 回呼ぶ。
        // speedFactor はエリア単位で 1 回だけ効くべきなので、List に積むのは初回のみ。
        var cache = new RestrictedAreaEdgeCache();
        var id1 = RestrictedAreaId.New();
        var area = new DifficultyArea(id1, new MeshCode(53394611L), "surface_damage");

        cache.AddDifficulty(id1, area, 100);
        cache.AddDifficulty(id1, area, 100);
        cache.AddDifficulty(id1, area, 100);

        Assert.Single(cache.GetDifficultyAreas(100));
    }

    [Fact]
    public void RemoveArea_AfterDuplicateAddDifficulty_FullyRemoved()
    {
        var cache = new RestrictedAreaEdgeCache();
        var id1 = RestrictedAreaId.New();
        var area = new DifficultyArea(id1, new MeshCode(53394611L), "surface_damage");

        cache.AddDifficulty(id1, area, 100);
        cache.AddDifficulty(id1, area, 100);

        cache.RemoveArea(id1);
        Assert.Empty(cache.GetDifficultyAreas(100));
    }

    [Fact]
    public void RemoveArea_BlockedNotInOtherArea_RemovedFromBlockedEdges()
    {
        var cache = new RestrictedAreaEdgeCache();
        var id1 = RestrictedAreaId.New();
        cache.AddBlocked(id1, 100);
        Assert.True(cache.IsBlocked(100));

        cache.RemoveArea(id1);
        Assert.False(cache.IsBlocked(100));
    }

    [Fact]
    public void RemoveArea_BlockedAlsoInOtherArea_KeptInBlockedEdges()
    {
        var cache = new RestrictedAreaEdgeCache();
        var id1 = RestrictedAreaId.New();
        var id2 = RestrictedAreaId.New();
        // 同一エッジ 100 が 2 つの Block 制約に該当
        cache.AddBlocked(id1, 100);
        cache.AddBlocked(id2, 100);

        // id1 を削除しても、id2 にまだ該当するのでブロック維持 (T2=A OtherContains 走査)
        cache.RemoveArea(id1);
        Assert.True(cache.IsBlocked(100));

        // id2 も削除してはじめてブロック解除
        cache.RemoveArea(id2);
        Assert.False(cache.IsBlocked(100));
    }

    [Fact]
    public void RemoveArea_DifficultyArea_RemovedFromList()
    {
        var cache = new RestrictedAreaEdgeCache();
        var id1 = RestrictedAreaId.New();
        var id2 = RestrictedAreaId.New();
        var area1 = new DifficultyArea(id1, new MeshCode(53394611L), "surface_damage");
        var area2 = new DifficultyArea(id2, new MeshCode(53394612L), "inundation");

        cache.AddDifficulty(id1, area1, 100);
        cache.AddDifficulty(id2, area2, 100);

        // id1 削除で area1 のみ List から消える (T3=A RemoveAll)
        cache.RemoveArea(id1);
        var areas = cache.GetDifficultyAreas(100);
        Assert.Single(areas);
        Assert.Same(area2, areas[0]);

        // id2 削除で List も完全に空になり、Dictionary エントリも削除される (空配列が返る)
        cache.RemoveArea(id2);
        Assert.Empty(cache.GetDifficultyAreas(100));
    }
}
