using System.Collections.Generic;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の HeaderBlock 解析結果。
/// </summary>
/// <param name="BoundingBox">ファイル全体のバウンディングボックス (任意)。</param>
/// <param name="RequiredFeatures">読込側が必ずサポートしなければならない機能タグ (例: "OsmSchema-V0.6", "DenseNodes")。</param>
/// <param name="OptionalFeatures">任意機能タグ (情報のみ、未サポートでも読込続行可)。</param>
/// <param name="WritingProgram">PBF を生成したプログラム名 (任意、診断用)。</param>
/// <param name="Source">データソース (任意、診断用)。</param>
/// <remarks>
/// replication 関連フィールド (field 32-34) は Phase 2 では使用しないためスキップする。
/// </remarks>
internal sealed record OsmHeader(
    OsmBoundingBox? BoundingBox,
    IReadOnlyList<string> RequiredFeatures,
    IReadOnlyList<string> OptionalFeatures,
    string? WritingProgram,
    string? Source);
