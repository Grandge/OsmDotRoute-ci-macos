namespace OsmDotRoute.Benchmarks.Generators;

/// <summary>制約ポリゴン 1 件。</summary>
internal sealed record RestrictionEntry(
    string Type,                       // "block" or "difficulty"
    string? DifficultyType,            // type=="difficulty" のときのみ
    List<List<double>> OuterBoundary); // [[lat, lon], ...] 閉ループ

/// <summary>restrictions-*.json のルートオブジェクト。</summary>
internal sealed record RestrictionsFile(
    int Seed,
    int Count,
    string Pattern,                    // "mixed" or "block-only"
    List<RestrictionEntry> Areas);
