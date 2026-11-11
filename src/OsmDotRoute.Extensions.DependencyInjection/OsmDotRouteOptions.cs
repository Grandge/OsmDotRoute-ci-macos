namespace OsmDotRoute.Extensions.DependencyInjection;

/// <summary>
/// <see cref="ServiceCollectionExtensions.AddOsmDotRoute(Microsoft.Extensions.DependencyInjection.IServiceCollection, System.Action{OsmDotRouteOptions})"/>
/// に渡す設定オプション。
/// </summary>
/// <remarks>
/// Phase 3 ステップ 3C.3 (REQ-MAP-009) で <c>RouterDbPath</c> → <c>OdrgPath</c> に破壊変更。
/// </remarks>
public sealed class OsmDotRouteOptions
{
    /// <summary>
    /// 起動時に読み込む <c>.odrg</c> ファイルのパス。
    /// 必須（未指定で <c>AddOsmDotRoute</c> 実行時に例外）。
    /// </summary>
    public string? OdrgPath { get; set; }

    /// <summary>
    /// 既定の <see cref="VehicleProfile"/>。未指定時は <see cref="VehicleProfile.Car"/>。
    /// Phase 1 時点では参照用途のみ（DI コンテナには登録しないが、ユーザーが利便のために保持可）。
    /// </summary>
    public VehicleProfile? DefaultProfile { get; set; }
}
