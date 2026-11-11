using System;
using System.IO;
using OsmDotRoute;

namespace OsmDotRoute.Extractor;

/// <summary>
/// CLI <c>--profiles</c> 引数の各要素を <see cref="VehicleProfile"/> へ解決する（Phase 4）。
/// 組込みプロファイル名（car / pedestrian / bicycle / truck / ambulance / fire_engine / disaster）に加え、
/// 既存の JSON ファイルパスを指定するとユーザー定義プロファイルとして読み込む（REQ-PRF-009 を bake 経路に拡張）。
/// </summary>
internal static class ProfileResolver
{
    /// <summary>組込みプロファイル名の一覧（エラーメッセージ用）。</summary>
    public static readonly string[] BuiltInNames =
        { "car", "pedestrian", "bicycle", "truck", "ambulance", "fire_engine", "disaster" };

    /// <summary>
    /// 名前またはファイルパスを <see cref="VehicleProfile"/> に解決する。
    /// </summary>
    /// <param name="nameOrPath">組込みプロファイル名、または JSON ファイルパス</param>
    /// <exception cref="ArgumentException">名前が未対応かつファイルが存在しない、または外部 JSON の読込に失敗</exception>
    public static VehicleProfile Resolve(string nameOrPath)
    {
        if (string.IsNullOrWhiteSpace(nameOrPath))
        {
            throw new ArgumentException("プロファイル名またはパスを指定してください。", nameof(nameOrPath));
        }

        switch (nameOrPath.ToLowerInvariant())
        {
            case "car": return VehicleProfile.Car;
            case "pedestrian": return VehicleProfile.Pedestrian;
            case "bicycle": return VehicleProfile.Bicycle;
            case "truck": return VehicleProfile.Truck;
            case "ambulance": return VehicleProfile.Ambulance;
            case "fire_engine": return VehicleProfile.FireEngine;
            case "disaster": return VehicleProfile.Disaster;
        }

        // 組込み名でなければ外部 JSON プロファイルファイルとして扱う。
        if (File.Exists(nameOrPath))
        {
            try
            {
                return VehicleProfile.LoadFromJsonFile(nameOrPath);
            }
            catch (InvalidProfileException ex)
            {
                throw new ArgumentException(
                    $"外部プロファイル '{nameOrPath}' の読込に失敗しました: {ex.Message}", ex);
            }
        }

        throw new ArgumentException(
            $"未対応プロファイル: '{nameOrPath}'。組込み名（{string.Join(" / ", BuiltInNames)}）" +
            "または既存の JSON ファイルパスを指定してください。");
    }
}
