using System;
using System.Globalization;
using OsmDotRoute.Pbf.Osm;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// エッジの OSM tag 集合から <see cref="EdgeFlags"/> を bake する。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 3.5。仕様書 §4.5 で確定した 14 bit (12 属性 + Oneway 2 bit) の
/// bake ルールを実装する。
/// </para>
/// <para>
/// 座標を一切使わないため、エッジ生成 (3.4) 直後・座標解決前のタイミングで bake 可能。
/// ホットパス: UTF-8 リテラル + <see cref="MemoryExtensions.SequenceEqual{T}(ReadOnlySpan{T}, ReadOnlySpan{T})"/>
/// で StringTable インデックスを文字列化せず処理する (way フィルタと同じ戦略)。
/// </para>
/// </remarks>
internal static class EdgeFlagsBaker
{
    // tag キー
    private static ReadOnlySpan<byte> KeyBridge => "bridge"u8;
    private static ReadOnlySpan<byte> KeyTunnel => "tunnel"u8;
    private static ReadOnlySpan<byte> KeyLayer => "layer"u8;
    private static ReadOnlySpan<byte> KeyJunction => "junction"u8;
    private static ReadOnlySpan<byte> KeyToll => "toll"u8;
    private static ReadOnlySpan<byte> KeyAccess => "access"u8;
    private static ReadOnlySpan<byte> KeyHighway => "highway"u8;
    private static ReadOnlySpan<byte> KeySidewalk => "sidewalk"u8;
    private static ReadOnlySpan<byte> KeySeasonal => "seasonal"u8;
    private static ReadOnlySpan<byte> KeyWinterRoad => "winter_road"u8;
    private static ReadOnlySpan<byte> KeyOneway => "oneway"u8;

    // tag 値
    private static ReadOnlySpan<byte> ValYes => "yes"u8;
    private static ReadOnlySpan<byte> ValNo => "no"u8;
    private static ReadOnlySpan<byte> ValBoth => "both"u8;
    private static ReadOnlySpan<byte> ValMinusOne => "-1"u8;
    private static ReadOnlySpan<byte> ValRoundabout => "roundabout"u8;
    private static ReadOnlySpan<byte> ValPrivate => "private"u8;
    private static ReadOnlySpan<byte> ValService => "service"u8;
    private static ReadOnlySpan<byte> ValTrack => "track"u8;
    private static ReadOnlySpan<byte> ValLivingStreet => "living_street"u8;
    private static ReadOnlySpan<byte> ValViaduct => "viaduct"u8;
    private static ReadOnlySpan<byte> ValWinter => "winter"u8;

    /// <summary><see cref="EdgeRecord"/> から <see cref="EdgeFlags"/> を bake する。</summary>
    public static EdgeFlags Bake(EdgeRecord edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return Bake(edge.TagKeys, edge.TagValues, edge.StringTable);
    }

    /// <summary>
    /// tag インデックス配列と <see cref="OsmStringTable"/> から bake する低レベル API。
    /// </summary>
    public static EdgeFlags Bake(int[] tagKeys, int[] tagValues, OsmStringTable stringTable)
    {
        ArgumentNullException.ThrowIfNull(tagKeys);
        ArgumentNullException.ThrowIfNull(tagValues);
        ArgumentNullException.ThrowIfNull(stringTable);

        EdgeFlags flags = EdgeFlags.None;
        bool hasJunctionRoundabout = false;
        bool hasExplicitOneway = false;

        for (int i = 0; i < tagKeys.Length; i++)
        {
            ReadOnlySpan<byte> key = stringTable.GetBytes(tagKeys[i]);
            ReadOnlySpan<byte> val = stringTable.GetBytes(tagValues[i]);

            if (key.SequenceEqual(KeyBridge))
            {
                // bridge=no 以外なら橋。viaduct は高架も立てる
                if (!val.SequenceEqual(ValNo))
                {
                    flags |= EdgeFlags.IsBridge;
                    if (val.SequenceEqual(ValViaduct))
                        flags |= EdgeFlags.IsElevated;
                }
            }
            else if (key.SequenceEqual(KeyTunnel))
            {
                if (!val.SequenceEqual(ValNo))
                    flags |= EdgeFlags.IsTunnel;
            }
            else if (key.SequenceEqual(KeyLayer))
            {
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int layer)
                    && layer >= 1)
                {
                    flags |= EdgeFlags.IsElevated;
                }
            }
            else if (key.SequenceEqual(KeyJunction))
            {
                if (val.SequenceEqual(ValRoundabout))
                {
                    flags |= EdgeFlags.IsRoundabout;
                    hasJunctionRoundabout = true;
                }
            }
            else if (key.SequenceEqual(KeyToll))
            {
                if (val.SequenceEqual(ValYes))
                    flags |= EdgeFlags.IsToll;
            }
            else if (key.SequenceEqual(KeyAccess))
            {
                if (val.SequenceEqual(ValPrivate))
                    flags |= EdgeFlags.IsPrivateAccess;
            }
            else if (key.SequenceEqual(KeyHighway))
            {
                if (val.SequenceEqual(ValService))
                    flags |= EdgeFlags.IsServiceWay;
                else if (val.SequenceEqual(ValTrack))
                    flags |= EdgeFlags.IsTrack;
                else if (val.SequenceEqual(ValLivingStreet))
                    flags |= EdgeFlags.IsLivingStreet;
            }
            else if (key.SequenceEqual(KeySidewalk))
            {
                if (val.SequenceEqual(ValYes) || val.SequenceEqual(ValBoth))
                    flags |= EdgeFlags.IsPedestrianSeparated;
            }
            else if (key.SequenceEqual(KeySeasonal))
            {
                if (val.SequenceEqual(ValWinter))
                    flags |= EdgeFlags.IsWinterClosed;
            }
            else if (key.SequenceEqual(KeyWinterRoad))
            {
                if (val.SequenceEqual(ValNo))
                    flags |= EdgeFlags.IsWinterClosed;
            }
            else if (key.SequenceEqual(KeyOneway))
            {
                hasExplicitOneway = true;
                if (val.SequenceEqual(ValYes))
                    flags |= EdgeFlags.IsOnewayForward;
                else if (val.SequenceEqual(ValMinusOne))
                    flags |= EdgeFlags.IsOnewayBackward;
                // oneway=no / false → どちらも立てない (双方向)
            }
        }

        // junction=roundabout の暗黙 oneway: 明示的 oneway 指定がなければ正方向通行とみなす
        if (hasJunctionRoundabout && !hasExplicitOneway)
            flags |= EdgeFlags.IsOnewayForward;

        // IsSchoolZone (bit 11) は v0.2 で予約のみ、抽出ツールは 0 固定 (仕様書 §4.5)
        return flags;
    }
}
