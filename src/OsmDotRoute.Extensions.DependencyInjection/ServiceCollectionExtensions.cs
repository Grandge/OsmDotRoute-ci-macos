using Microsoft.Extensions.DependencyInjection;

namespace OsmDotRoute.Extensions.DependencyInjection;

/// <summary>
/// <see cref="IServiceCollection"/> に OsmDotRoute 一式を登録するための拡張メソッド。
/// </summary>
/// <remarks>
/// Phase 3 ステップ 3C.3 (REQ-MAP-009) で Itinero RouterDb 経由から <c>.odrg</c> 直接ロードへ破壊変更。
/// 旧 <c>AddOsmDotRoute(string routerDbPath)</c> → 新 <c>AddOsmDotRoute(string odrgPath)</c>。
/// </remarks>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// <c>.odrg</c> ファイルパスを指定して OsmDotRoute サービス一式を Singleton で登録する。
    /// 登録される型: <see cref="RouterDb"/>、<see cref="Router"/>、<see cref="RestrictedAreaService"/>。
    /// </summary>
    /// <param name="services">対象のサービスコレクション</param>
    /// <param name="odrgPath">起動時に読み込む <c>.odrg</c> ファイルのパス</param>
    /// <returns>メソッドチェーン用 <paramref name="services"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> が <c>null</c></exception>
    /// <exception cref="ArgumentException"><paramref name="odrgPath"/> が <c>null</c> または空白</exception>
    public static IServiceCollection AddOsmDotRoute(this IServiceCollection services, string odrgPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(odrgPath);

        return AddOsmDotRoute(services, options => options.OdrgPath = odrgPath);
    }

    /// <summary>
    /// <see cref="OsmDotRouteOptions"/> 経由で詳細設定を行いながら OsmDotRoute サービス一式を Singleton で登録する。
    /// 登録される型: <see cref="RouterDb"/>、<see cref="Router"/>、<see cref="RestrictedAreaService"/>。
    /// </summary>
    /// <param name="services">対象のサービスコレクション</param>
    /// <param name="configure">オプション構築デリゲート</param>
    /// <returns>メソッドチェーン用 <paramref name="services"/></returns>
    /// <exception cref="ArgumentNullException"><paramref name="services"/> または <paramref name="configure"/> が <c>null</c></exception>
    /// <exception cref="InvalidOperationException"><see cref="OsmDotRouteOptions.OdrgPath"/> が未設定</exception>
    public static IServiceCollection AddOsmDotRoute(this IServiceCollection services, Action<OsmDotRouteOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var options = new OsmDotRouteOptions();
        configure(options);
        if (string.IsNullOrWhiteSpace(options.OdrgPath))
        {
            throw new InvalidOperationException($"{nameof(OsmDotRouteOptions)}.{nameof(OsmDotRouteOptions.OdrgPath)} を設定してください。");
        }

        services.AddSingleton(_ => RouterDb.LoadFromOdrg(options.OdrgPath!));
        services.AddSingleton<RestrictedAreaService>();
        services.AddSingleton(sp => new Router(sp.GetRequiredService<RouterDb>(), sp.GetRequiredService<RestrictedAreaService>()));

        return services;
    }
}
