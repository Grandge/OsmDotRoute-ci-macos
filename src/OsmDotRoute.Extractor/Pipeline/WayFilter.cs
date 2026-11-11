using System;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// OSM Way を「道路ネットワーク抽出対象か」で判定するフィルタ。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.2。計画書 §5.6-17 で確定：
/// <c>highway=*</c> を全部取り込み、<c>access=no</c> / <c>area=yes</c> で除外。
/// プロファイル判定は bake 時 (3.6) に実施し、ここでは広めに通す。
/// </para>
/// <para>
/// Phase 3 で Bicycle / Truck プロファイル等を追加する際に
/// 元の way 集合が不足しないようフィルタは緩めに維持する。
/// </para>
/// <para>
/// ホットパス: Japan-wide PBF では数千万 way を流すため、StringTable から UTF-8 バイト列を
/// <see cref="ReadOnlySpan{T}"/> で取得し SequenceEqual で比較。
/// 文字列デコード・<see cref="System.Collections.Generic.Dictionary{TKey,TValue}"/> は使わない。
/// </para>
/// </remarks>
internal static class WayFilter
{
    private static ReadOnlySpan<byte> HighwayKey => "highway"u8;
    private static ReadOnlySpan<byte> AccessKey => "access"u8;
    private static ReadOnlySpan<byte> AreaKey => "area"u8;
    private static ReadOnlySpan<byte> NoValue => "no"u8;
    private static ReadOnlySpan<byte> YesValue => "yes"u8;

    /// <summary>
    /// この way を道路ネットワーク抽出対象として採用するかを判定する。
    /// </summary>
    /// <param name="way">PBF から読み出した Way。</param>
    /// <param name="stringTable">同 PBF Block の StringTable。</param>
    /// <returns>
    /// 採用なら true。基準:
    /// <list type="bullet">
    ///   <item><c>highway</c> タグを持つ（値は問わない）</item>
    ///   <item>かつ <c>access=no</c> を持たない</item>
    ///   <item>かつ <c>area=yes</c> を持たない</item>
    /// </list>
    /// </returns>
    public static bool IsRoadWay(OsmWay way, OsmStringTable stringTable)
    {
        ArgumentNullException.ThrowIfNull(way);
        ArgumentNullException.ThrowIfNull(stringTable);

        bool hasHighway = false;
        int[] keys = way.TagKeys;
        int[] values = way.TagValues;

        for (int i = 0; i < keys.Length; i++)
        {
            ReadOnlySpan<byte> key = stringTable.GetBytes(keys[i]);

            if (key.SequenceEqual(HighwayKey))
            {
                hasHighway = true;
                continue;
            }

            if (key.SequenceEqual(AccessKey))
            {
                ReadOnlySpan<byte> value = stringTable.GetBytes(values[i]);
                if (value.SequenceEqual(NoValue))
                    return false;
                continue;
            }

            if (key.SequenceEqual(AreaKey))
            {
                ReadOnlySpan<byte> value = stringTable.GetBytes(values[i]);
                if (value.SequenceEqual(YesValue))
                    return false;
            }
        }

        return hasHighway;
    }
}
