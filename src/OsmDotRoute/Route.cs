namespace OsmDotRoute;

/// <summary>
/// 経路計算結果（REQ-FMT-001〜003、REQ-FMT-006）。
/// 総距離・総所要時間・経路形状・Shape 点別累積所要時間を保持する。
/// </summary>
/// <remarks>
/// Phase 3 ステップ 3C.1（Phase 2 §5.5-8 確定）で <see cref="Shape"/> 型を
/// <c>IReadOnlyList&lt;GeoCoordinate&gt;</c> から <see cref="ReadOnlyMemory{T}"/> に
/// 破壊変更した。反復には <c>route.Shape.Span</c>、要素アクセスには <c>route.Shape.Span[i]</c>、
/// 要素数取得には <c>route.Shape.Length</c> を用いる。
/// Phase 4 親プロFB 追補（REQ-FMT-006、Ver. 1.1.0）で <see cref="CumulativeDurationsSec"/> を追加。
/// </remarks>
public sealed class Route
{
    /// <summary>
    /// 経路を作成する（Phase 1 互換コンストラクタ。累積所要時間は <see cref="Shape"/> の端点線形補間で生成）。
    /// </summary>
    /// <param name="totalDistanceM">総距離（メートル）</param>
    /// <param name="totalDurationSec">総所要時間（秒）</param>
    /// <param name="shape">経路形状（緯度経度頂点列、起点から終点まで）</param>
    public Route(double totalDistanceM, double totalDurationSec, ReadOnlyMemory<GeoCoordinate> shape)
        : this(totalDistanceM, totalDurationSec, shape, BuildLinearCumulative(totalDurationSec, shape.Length))
    {
    }

    /// <summary>
    /// 経路を作成する（REQ-FMT-006、Ver. 1.1.0 追加）。
    /// </summary>
    /// <param name="totalDistanceM">総距離（メートル）</param>
    /// <param name="totalDurationSec">総所要時間（秒）</param>
    /// <param name="shape">経路形状（緯度経度頂点列、起点から終点まで）</param>
    /// <param name="cumulativeDurationsSec">
    /// Shape 各点における起点からの累積所要秒。<see cref="Shape"/> と 1:1 で整列し、
    /// <c>[0] == 0</c>、<c>[^1] == <paramref name="totalDurationSec"/></c>、単調非減少であることを呼び出し側が保証する。
    /// </param>
    public Route(
        double totalDistanceM,
        double totalDurationSec,
        ReadOnlyMemory<GeoCoordinate> shape,
        ReadOnlyMemory<double> cumulativeDurationsSec)
    {
        if (cumulativeDurationsSec.Length != shape.Length)
        {
            throw new ArgumentException(
                $"CumulativeDurationsSec.Length ({cumulativeDurationsSec.Length}) must equal Shape.Length ({shape.Length})",
                nameof(cumulativeDurationsSec));
        }
        TotalDistanceM = totalDistanceM;
        TotalDurationSec = totalDurationSec;
        Shape = shape;
        CumulativeDurationsSec = cumulativeDurationsSec;
    }

    /// <summary>総距離（メートル）</summary>
    public double TotalDistanceM { get; }

    /// <summary>総所要時間（秒）</summary>
    public double TotalDurationSec { get; }

    /// <summary>
    /// 経路形状（起点から終点までの緯度経度頂点列、シェイプ込み）。
    /// 反復には <c>Shape.Span</c>、要素数取得には <c>Shape.Length</c> を用いる（Phase 2 §5.5-8、Phase 3 3C.1 破壊変更）。
    /// </summary>
    public ReadOnlyMemory<GeoCoordinate> Shape { get; }

    /// <summary>
    /// Shape 各点における起点からの累積所要秒（REQ-FMT-006、Ver. 1.1.0）。<see cref="Shape"/> と 1:1 で整列する。
    /// <c>CumulativeDurationsSec.Length == Shape.Length</c>、<c>[0] == 0</c>、<c>[^1] == TotalDurationSec</c>（誤差なく一致）、単調非減少。
    /// 区間 i（<c>Shape[i] → Shape[i+1]</c>）の所要時間 = <c>CumulativeDurationsSec.Span[i+1] - CumulativeDurationsSec.Span[i]</c>。
    /// 移動困難エリア（<c>AddDifficultyArea</c>）の速度低下が区間所要時間に反映される（エッジ単位の SpeedFactor 由来）。
    /// </summary>
    /// <remarks>
    /// **デバッグ tips**: 難所エリアを登録したにもかかわらず該当区間で累積秒の傾きが変わらない（= 速度低下が見えない）場合、
    /// 渡した <c>difficultyType</c> 文字列がプロファイル側で未定義のため <c>difficultyDefault</c>（既定 <c>speedFactor=1.0</c>）に
    /// サイレント・フォールバックしている可能性が高い（REQ-PRF-014）。<see cref="VehicleProfile.HasDifficulty"/> や
    /// <see cref="VehicleProfile.KnownDifficultyTypes"/> で事前確認できる。
    /// </remarks>
    public ReadOnlyMemory<double> CumulativeDurationsSec { get; }

    /// <summary>
    /// 互換コンストラクタ用フォールバック: Shape の端点間で <paramref name="totalDurationSec"/> を線形補間した
    /// 暫定累積秒列を返す。区間別速度差は反映されないため、正確な区間所要時間が必要な利用者は
    /// 4 引数コンストラクタ経由で値を渡すこと（<see cref="OsmDotRoute.Routing.RouteBuilder"/> はこちらを使う）。
    /// </summary>
    private static ReadOnlyMemory<double> BuildLinearCumulative(double totalDurationSec, int shapeLength)
    {
        if (shapeLength <= 0) return ReadOnlyMemory<double>.Empty;
        var arr = new double[shapeLength];
        if (shapeLength == 1)
        {
            arr[0] = 0.0;
            return arr;
        }
        double step = totalDurationSec / (shapeLength - 1);
        for (int i = 0; i < shapeLength - 1; i++) arr[i] = step * i;
        arr[shapeLength - 1] = totalDurationSec;
        return arr;
    }
}