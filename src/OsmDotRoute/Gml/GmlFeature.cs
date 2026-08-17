namespace OsmDotRoute.Gml;

/// <summary>
/// KSJ GML のフィーチャ 1 件（形状＋属性）。<see cref="GmlParser.ParseFeaturesString"/> /
/// <see cref="GmlParser.ParseFeaturesStream"/> が返す（REQ-RST-041）。
/// </summary>
/// <param name="Polygon">フィーチャの形状（外周＋Hole）</param>
/// <param name="Attributes">
/// フィーチャ要素直下の単純な子要素から抽出した属性。key = 要素ローカル名（名前空間 prefix なし、例 "A51_001"）、
/// value = テキスト内容（型解釈・コードリスト解決は利用側責務）。
/// 子要素を持つ複合要素・xlink 参照要素（形状参照等）・空要素は含まれない。
/// 同名要素が複数あれば後勝ち。属性が 1 つも無いフィーチャでは空の Dictionary。
/// </param>
public sealed record GmlFeature(
    GeoPolygon Polygon,
    IReadOnlyDictionary<string, string> Attributes);