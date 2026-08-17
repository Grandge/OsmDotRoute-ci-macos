namespace OsmDotRoute.Restrictions;

/// <summary>
/// 動的制約とエッジ ID の交差判定結果を格納するキャッシュ（Phase 3 ステップ 3B.1、計画書 §4.1）。
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RestrictedAreaService"/> に <see cref="OsmDotRoute.Routing.IRoadGraph"/> が注入された
/// 時点で eager bake され、Dijkstra ホットパスは <see cref="IsBlocked"/> / <see cref="GetDifficultyAreas"/>
/// による <see cref="HashSet{T}"/> / <see cref="Dictionary{TKey, TValue}"/> 一発参照のみに圧縮される。
/// </para>
/// <para>
/// Block 制約はプロファイル非依存（絶対通行不可）のため <see cref="HashSet{T}"/> でフラグ集約。
/// Difficulty 制約はプロファイル依存（<c>EvaluateDifficulty</c> の戻り値が <see cref="OsmDotRoute.Profiles.ProfileEvaluator"/>
/// ごとに異なる）のため、エッジに該当する <see cref="DifficultyArea"/> リストのみ保持し、
/// <c>SpeedFactor</c> / <c>CanPass</c> はホットパスで都度計算する（計画書 §4.1.1 T1=A 確定）。
/// </para>
/// </remarks>
internal sealed class RestrictedAreaEdgeCache
{
    // Block: プロファイル非依存
    private readonly Dictionary<RestrictedAreaId, HashSet<uint>> _blockedByArea = new();
    private readonly HashSet<uint> _blockedEdges = new();

    // Difficulty: プロファイル依存のため、エッジ → 該当 DifficultyArea のリスト
    private readonly Dictionary<RestrictedAreaId, HashSet<uint>> _difficultyByArea = new();
    private readonly Dictionary<uint, List<DifficultyArea>> _difficultyAreasByEdge = new();

    /// <summary>指定エッジが何らかの Block 制約に該当するかを返す（ホットパス API、HashSet 1 発）。</summary>
    public bool IsBlocked(uint edgeId) => _blockedEdges.Contains(edgeId);

    /// <summary>指定エッジに該当する Difficulty 制約のリストを返す（該当なしは空配列）。</summary>
    /// <remarks>
    /// 戻り値の列挙中に呼出側がリストを変更してはならない。ホットパス用途のため alloc を発生させない。
    /// </remarks>
    public IReadOnlyList<DifficultyArea> GetDifficultyAreas(uint edgeId)
    {
        return _difficultyAreasByEdge.TryGetValue(edgeId, out var list)
            ? list
            : Array.Empty<DifficultyArea>();
    }

    /// <summary>Block 制約 ID に対し交差エッジ ID を追加する（bake 時に呼ぶ）。</summary>
    public void AddBlocked(RestrictedAreaId areaId, uint edgeId)
    {
        if (!_blockedByArea.TryGetValue(areaId, out var set))
        {
            set = new HashSet<uint>();
            _blockedByArea[areaId] = set;
        }
        set.Add(edgeId);
        _blockedEdges.Add(edgeId);
    }

    /// <summary>Difficulty 制約に対し交差エッジ ID を追加する（bake 時に呼ぶ）。</summary>
    public void AddDifficulty(RestrictedAreaId areaId, DifficultyArea area, uint edgeId)
    {
        ArgumentNullException.ThrowIfNull(area);
        if (!_difficultyByArea.TryGetValue(areaId, out var set))
        {
            set = new HashSet<uint>();
            _difficultyByArea[areaId] = set;
        }
        // 同一エリアの別 Shape で既に登録済みのエッジは二重に積まない（REQ-RST-014）。
        // speedFactor は「エリア単位で 1 回」効く（EvaluateConstraints の seenIds と同セマンティクス）。
        if (!set.Add(edgeId)) return;

        if (!_difficultyAreasByEdge.TryGetValue(edgeId, out var list))
        {
            list = new List<DifficultyArea>();
            _difficultyAreasByEdge[edgeId] = list;
        }
        list.Add(area);
    }

    /// <summary>
    /// 指定制約 ID に紐づくキャッシュエントリを削除する（<see cref="RestrictedAreaService.Remove"/> 等から呼ぶ）。
    /// 他の Block 制約にも該当するエッジは <see cref="_blockedEdges"/> から外さない（計画書 §4.1.1 T2=A）。
    /// Difficulty は List から該当 areaId の要素を <c>RemoveAll</c>、空になったエントリは削除（T3=A）。
    /// </summary>
    public void RemoveArea(RestrictedAreaId areaId)
    {
        // Block 側: 先に逆引きから削除 → 残り Block にも該当するか走査
        if (_blockedByArea.TryGetValue(areaId, out var blockEdges))
        {
            _blockedByArea.Remove(areaId);
            foreach (var edgeId in blockEdges)
            {
                if (!OtherBlockContains(edgeId))
                {
                    _blockedEdges.Remove(edgeId);
                }
            }
        }

        // Difficulty 側
        if (_difficultyByArea.TryGetValue(areaId, out var diffEdges))
        {
            _difficultyByArea.Remove(areaId);
            foreach (var edgeId in diffEdges)
            {
                if (_difficultyAreasByEdge.TryGetValue(edgeId, out var list))
                {
                    list.RemoveAll(a => a.Id.Equals(areaId));
                    if (list.Count == 0)
                    {
                        _difficultyAreasByEdge.Remove(edgeId);
                    }
                }
            }
        }
    }

    /// <summary>全エントリをクリアする（<see cref="RestrictedAreaService.ClearAll"/> 等から呼ぶ）。</summary>
    public void Clear()
    {
        _blockedByArea.Clear();
        _blockedEdges.Clear();
        _difficultyByArea.Clear();
        _difficultyAreasByEdge.Clear();
    }

    private bool OtherBlockContains(uint edgeId)
    {
        foreach (var set in _blockedByArea.Values)
        {
            if (set.Contains(edgeId)) return true;
        }
        return false;
    }
}
