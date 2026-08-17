using System;
using System.Collections.Generic;
using System.Text;
using OsmDotRoute;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Profiles;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// Phase 1 の <see cref="ProfileEvaluator"/> を流用し、エッジ列に対して
/// プロファイル別の <see cref="BakedProfileEntry"/> を bake する。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.6。仕様書 §4.7 Baked Profile Table を構築する。
/// </para>
/// <para>
/// <see cref="ProfileEvaluator"/> は <c>IReadOnlyDictionary&lt;string, string&gt;</c> を要求するため、
/// エッジ毎に tag インデックス → string 辞書 を 1 回展開し、複数プロファイルで再利用する。
/// 1 way が複数エッジに分割された場合は同じ tag 辞書を構築するコストを払うが、
/// 抽出は単発処理なので許容（ホットパスではない）。
/// </para>
/// </remarks>
internal static class ProfileBaker
{
    /// <summary>
    /// プロファイル列とエッジ列から <see cref="BakedProfileTable"/> を構築する。
    /// </summary>
    /// <param name="profiles">bake 対象プロファイル（順序が表のプロファイル ID に対応）。</param>
    /// <param name="edges">edgeId 順のエッジ列（<c>bakedProfileIndex == edgeId</c> 想定）。</param>
    public static BakedProfileTable Build(
        IReadOnlyList<VehicleProfile> profiles,
        IReadOnlyList<EdgeRecord> edges)
    {
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(edges);

        var names = new string[profiles.Count];
        var byProfile = new BakedProfileEntry[profiles.Count][];
        for (int p = 0; p < profiles.Count; p++)
        {
            names[p] = profiles[p].Name;
            byProfile[p] = new BakedProfileEntry[edges.Count];
        }

        // エッジ毎に tag 辞書を 1 回展開し、全プロファイルで使い回す
        for (int e = 0; e < edges.Count; e++)
        {
            var edge = edges[e];
            IReadOnlyDictionary<string, string> tagDict = BuildTagDictionary(edge.TagKeys, edge.TagValues, edge.StringTable);

            for (int p = 0; p < profiles.Count; p++)
            {
                EdgeEvaluation eval = profiles[p].Evaluator.Evaluate(tagDict);
                byProfile[p][e] = ToEntry(eval);
            }
        }

        return new BakedProfileTable(names, byProfile);
    }

    /// <summary>単一エッジ × 単一プロファイルの bake（テスト・診断用）。</summary>
    public static BakedProfileEntry Bake(EdgeRecord edge, VehicleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(edge);
        ArgumentNullException.ThrowIfNull(profile);
        var tagDict = BuildTagDictionary(edge.TagKeys, edge.TagValues, edge.StringTable);
        return ToEntry(profile.Evaluator.Evaluate(tagDict));
    }

    private static Dictionary<string, string> BuildTagDictionary(int[] keys, int[] values, OsmStringTable st)
    {
        var dict = new Dictionary<string, string>(keys.Length, StringComparer.Ordinal);
        for (int i = 0; i < keys.Length; i++)
        {
            string key = Encoding.UTF8.GetString(st.GetBytes(keys[i]));
            string value = Encoding.UTF8.GetString(st.GetBytes(values[i]));
            // 同じキーが複数回出る場合は後勝ち（Phase 1 ProfileEvaluator の挙動に合わせる）
            dict[key] = value;
        }
        return dict;
    }

    private static BakedProfileEntry ToEntry(EdgeEvaluation eval)
    {
        // OnewayDirection → (forward, backward) bit
        bool forward, backward;
        switch (eval.Oneway)
        {
            case OnewayDirection.Forward:
                forward = true; backward = false;
                break;
            case OnewayDirection.Backward:
                forward = false; backward = true;
                break;
            default:  // Bidirectional
                forward = true; backward = true;
                break;
        }

        // canPass=false なら方向ビットも意味を持たないので一律 false にしておく
        if (!eval.CanPass)
        {
            forward = false;
            backward = false;
        }

        return BakedProfileEntry.Create(eval.CanPass, eval.SpeedKmh, forward, backward);
    }
}
