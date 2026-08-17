using System;
using System.Text;

namespace OsmDotRoute.Pbf.Osm;

/// <summary>
/// OSM PBF PrimitiveBlock 内の StringTable。
/// Node / DenseNodes / Way / Relation の tag キー・値は本テーブルへの整数インデックスで表される。
/// </summary>
/// <remarks>
/// <para>OSM PBF の慣習：インデックス 0 は通常空文字列（DenseNodes の tag リスト区切り用）。</para>
/// <para>本実装は <c>byte[][]</c> をそのまま保持する。各エントリは UTF-8 バイト列。</para>
/// </remarks>
internal sealed class OsmStringTable
{
    private readonly byte[][] _items;

    public OsmStringTable(byte[][] items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = items;
    }

    /// <summary>エントリ総数（インデックス 0 を含む）。</summary>
    public int Count => _items.Length;

    /// <summary>指定インデックスの UTF-8 バイト列を返す（コピーなし）。</summary>
    public ReadOnlySpan<byte> GetBytes(int index) => _items[index];

    /// <summary>指定インデックスの文字列を UTF-8 デコードして返す。</summary>
    public string GetString(int index) => Encoding.UTF8.GetString(_items[index]);
}
