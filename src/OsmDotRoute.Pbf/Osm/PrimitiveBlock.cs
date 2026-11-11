using System;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF の PrimitiveBlock エンベロープ（StringTable + 座標スケール情報）。
/// </summary>
/// <remarks>
/// <para>
/// PrimitiveGroup（Node / Way / Relation 等の実データ）は本クラスでは保持せず、ステップ 2.6 以降の
/// 専用イテレータでブロックバイト列から逐次取り出す予定。
/// </para>
/// <para>
/// 座標変換（PBF 仕様）：
/// <code>
/// lon = 1e-9 * (LonOffset + Granularity * encodedLon)
/// lat = 1e-9 * (LatOffset + Granularity * encodedLat)
/// </code>
/// Granularity / Offset はナノ度（10^-9 度）単位。
/// </para>
/// </remarks>
internal sealed class PrimitiveBlock
{
    /// <summary>nanodegrees → degrees の変換係数。</summary>
    private const double NanodegreeToDegree = 1e-9;

    /// <summary>本ブロックの文字列テーブル。Way / Node の tag はインデックスで本テーブルを参照する。</summary>
    public OsmStringTable StringTable { get; }

    /// <summary>座標エンコーディング粒度（nanodegree 単位、PBF default = 100）。</summary>
    public int Granularity { get; }

    /// <summary>緯度オフセット（nanodegree、PBF default = 0、負値可）。</summary>
    public long LatOffset { get; }

    /// <summary>経度オフセット（nanodegree、PBF default = 0、負値可）。</summary>
    public long LonOffset { get; }

    /// <summary>日付エンコーディング粒度（ミリ秒、PBF default = 1000）。Phase 2 では未使用。</summary>
    public int DateGranularity { get; }

    public PrimitiveBlock(
        OsmStringTable stringTable,
        int granularity,
        long latOffset,
        long lonOffset,
        int dateGranularity)
    {
        ArgumentNullException.ThrowIfNull(stringTable);
        StringTable = stringTable;
        Granularity = granularity;
        LatOffset = latOffset;
        LonOffset = lonOffset;
        DateGranularity = dateGranularity;
    }

    /// <summary>PBF encoded 経度（int64）を度単位の double に変換。</summary>
    public double ToLon(long encodedLon)
        => NanodegreeToDegree * (LonOffset + (long)Granularity * encodedLon);

    /// <summary>PBF encoded 緯度（int64）を度単位の double に変換。</summary>
    public double ToLat(long encodedLat)
        => NanodegreeToDegree * (LatOffset + (long)Granularity * encodedLat);
}
