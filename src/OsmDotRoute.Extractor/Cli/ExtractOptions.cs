using System.IO;

namespace OsmDotRoute.Extractor.Cli;

/// <summary>
/// <c>extract</c> サブコマンドが受け取る確定済みパラメータ。
/// </summary>
/// <remarks>
/// CLI 層 (<see cref="ExtractCommand"/>) でパース・検証を済ませた後、ステップ 3.2 以降の
/// 抽出パイプラインに渡す DTO。サブステップ 3.1 では値を受け取って <see cref="System.Console"/>
/// にエコーするのみで、実際の抽出処理は 3.2 以降で実装する。
/// </remarks>
internal sealed record ExtractOptions(
    FileInfo Input,
    FileInfo Output,
    Bbox Bbox,
    IReadOnlyList<string> Profiles);
