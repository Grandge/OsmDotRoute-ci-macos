using OsmDotRoute.Profiles;

namespace OsmDotRoute.Routing;

/// <summary>
/// エッジ重み計算器。プロファイル評価結果と動的制約からエッジの所要時間（秒）と方向別通行可否を算出する。
/// </summary>
/// <remarks>
/// <para>重み = 距離 (m) / (速度 (m/s) × 結合 speedFactor)。プロファイルで通行不可・速度 0・進入不可制約交差時は無限大。</para>
/// <para>
/// 方向解釈: エニュメレータの <c>From → To</c> 方向を「順方向」と呼ぶ。
/// <c>DataInverted=false</c> なら順方向 = OSM デジタイズ方向、
/// <c>DataInverted=true</c> なら順方向 = OSM デジタイズ方向の逆。
/// この変換を <see cref="CanTraverseInEnumeratorDirection"/> に閉じ込めている。
/// </para>
/// <para>
/// 制約評価（REQ-RST-013〜015, REQ-RST-030〜032）はエッジ全体（端点 + 中間シェイプ）を単位とし、
/// 同一エッジを部分通過する場合（ソース／ターゲットスナップ）でも同じ結合 speedFactor を適用する。
/// </para>
/// </remarks>
internal sealed class EdgeWeightCalculator
{
    private readonly IRoadGraph _graph;
    private readonly ProfileEvaluator _evaluator;
    private readonly RestrictedAreaService? _restrictions;

    public EdgeWeightCalculator(IRoadGraph graph, ProfileEvaluator evaluator, RestrictedAreaService? restrictions = null)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentNullException.ThrowIfNull(evaluator);
        _graph = graph;
        _evaluator = evaluator;
        _restrictions = restrictions;
    }

    /// <summary>エニュメレータが指す現在エッジの評価結果（通行可否・速度・方向制限）を取得する（ホットパス用）。</summary>
    public EdgeEvaluation Evaluate(IRoadGraphEdgeEnumerator en)
        => _graph.EvaluateEdge(en, _evaluator);

    /// <summary>エッジ ID で取得した <see cref="RoadEdge"/> の評価結果を取得する（スナップエッジ評価用）。</summary>
    public EdgeEvaluation Evaluate(RoadEdge edge)
        => _graph.EvaluateEdge(edge, _evaluator);

    /// <summary>距離 (m) と速度 (km/h) から所要時間 (秒) を算出する。速度が 0 以下なら <see cref="double.PositiveInfinity"/>。</summary>
    public static double DurationSec(double distanceM, float speedKmh)
    {
        if (speedKmh <= 0f) return double.PositiveInfinity;
        var speedMps = speedKmh * 1000.0 / 3600.0;
        return distanceM / speedMps;
    }

    /// <summary>
    /// 評価結果と <see cref="IRoadGraphEdgeEnumerator.DataInverted"/> から、
    /// エニュメレータの <c>From → To</c> 方向に通行可能かを判定する。
    /// </summary>
    public static bool CanTraverseInEnumeratorDirection(EdgeEvaluation eval, bool dataInverted)
    {
        if (!eval.CanPass) return false;
        return eval.Oneway switch
        {
            // 両方向通行可
            OnewayDirection.Bidirectional => true,
            // OSM 順方向のみ通行可。enum 方向 = OSM 順 (DataInverted=false) のとき通行可
            OnewayDirection.Forward => !dataInverted,
            // OSM 逆方向のみ通行可。enum 方向 = OSM 逆 (DataInverted=true) のとき通行可
            OnewayDirection.Backward => dataInverted,
            _ => false,
        };
    }

    /// <summary>
    /// 制約評価込みでエッジ全体の所要時間 (秒) を返す。通行不可・進入不可制約交差時は <see cref="double.PositiveInfinity"/>。
    /// </summary>
    public double EvaluateEdgeDurationSec(IRoadGraphEdgeEnumerator en)
    {
        var eval = Evaluate(en);
        if (!CanTraverseInEnumeratorDirection(eval, en.DataInverted)) return double.PositiveInfinity;

        var baseDuration = DurationSec(en.DistanceM, eval.SpeedKmh);
        if (double.IsPositiveInfinity(baseDuration)) return baseDuration;

        var factor = EvaluateConstraintFactor(en.EdgeId, en.From, en.To, en.Shape);
        if (double.IsPositiveInfinity(factor)) return double.PositiveInfinity;
        return baseDuration / factor;
    }

    /// <summary>
    /// 指定エッジで部分距離 (m) を通過する所要時間 (秒) を制約込みで返す。
    /// ソース／ターゲットのスナップ部分通過用。制約評価はエッジ全体に対して 1 回行う（部分通過でも同じ係数）。
    /// </summary>
    /// <returns>部分通過所要時間。通行不可・進入不可なら <see cref="double.PositiveInfinity"/>。</returns>
    public double EvaluateEdgePartialDurationSec(RoadEdge edge, double partialDistanceM, EdgeEvaluation eval)
    {
        var baseDuration = DurationSec(partialDistanceM, eval.SpeedKmh);
        if (double.IsPositiveInfinity(baseDuration)) return baseDuration;

        var factor = EvaluateConstraintFactor(edge.EdgeId, edge.From, edge.To, edge.Shape);
        if (double.IsPositiveInfinity(factor)) return double.PositiveInfinity;
        return baseDuration / factor;
    }

    /// <summary>
    /// 制約サービスが未設定／登録 0 件の場合は 1.0、制約交差時は <see cref="double.PositiveInfinity"/>。
    /// それ以外は結合 speedFactor を返す。
    /// </summary>
    /// <remarks>
    /// <para>
    /// Phase 3 ステップ 3B.4: graph 注入済の場合は <see cref="OsmDotRoute.Restrictions.RestrictedAreaEdgeCache"/>
    /// 参照のみに圧縮（HashSet/Dictionary 各 1 発、計画書 §4.4.2）。<see cref="BuildFullShape"/> + <c>_index.Query</c> +
    /// <c>EdgeIntersectsAreaShapes</c> の毎エッジ alloc / 線形走査 / 二重ループはホットパスから除去される
    /// （Phase 1 §18.4 + §18.3 改善の本命）。
    /// </para>
    /// <para>
    /// graph 未注入時 (Service 単体テスト等) は Phase 1 動作にフォールバック。<see cref="BuildFullShape"/> も
    /// 未注入経路で引き続き使用されるため、3B.4 では削除せず温存 (3C で Itinero 撤去 + Router 必須化後に再評価)。
    /// </para>
    /// </remarks>
    private double EvaluateConstraintFactor(uint edgeId, uint from, uint to, IReadOnlyList<GeoCoordinate> middleShape)
    {
        if (_restrictions is null) return 1.0;

        // graph 注入済の場合はキャッシュ参照のみ (3B.4 ホットパス置換)
        if (_restrictions.IsGraphAttached)
        {
            var cache = _restrictions.Cache!;
            if (cache.IsBlocked(edgeId)) return double.PositiveInfinity;

            var areas = cache.GetDifficultyAreas(edgeId);
            if (areas.Count == 0) return 1.0;

            double combined = 1.0;
            foreach (var area in areas)
            {
                var ev = _evaluator.EvaluateDifficulty(area.DifficultyType);
                if (!ev.CanPass) return double.PositiveInfinity;
                combined *= ev.SpeedFactor;
                if (combined <= 0.0) return double.PositiveInfinity;
            }
            return combined;
        }

        // graph 未注入時は Phase 1 動作にフォールバック
        var shape = BuildFullShape(from, to, middleShape);
        return _restrictions.EvaluateConstraints(shape, _evaluator);
    }

    private IReadOnlyList<GeoCoordinate> BuildFullShape(uint from, uint to, IReadOnlyList<GeoCoordinate> middle)
    {
        var list = new List<GeoCoordinate>(middle.Count + 2)
        {
            _graph.GetVertex(from),
        };
        for (var i = 0; i < middle.Count; i++) list.Add(middle[i]);
        list.Add(_graph.GetVertex(to));
        return list;
    }
}
