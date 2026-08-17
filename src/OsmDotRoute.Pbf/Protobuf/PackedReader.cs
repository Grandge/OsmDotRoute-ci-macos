using System;
using System.Collections.Generic;

namespace OsmDotRoute.Pbf.Protobuf;

/// <summary>
/// protobuf packed repeated フィールドのデコードヘルパー。
/// </summary>
/// <remarks>
/// packed encoding は length-delimited 1 件で全要素を含む。本クラスは中身を順次デコードする。
/// 各メソッドは入力が空なら <see cref="Array.Empty{T}"/> を返す（ゼロアロケート）。
/// </remarks>
internal static class PackedReader
{
    /// <summary>packed uint32 配列を読む。</summary>
    /// <remarks>値は int として返す（StringTable インデックス等の用途、実用上値域 0..2^31-1）。</remarks>
    public static int[] ReadPackedUint32(ReadOnlySpan<byte> packedBytes)
    {
        if (packedBytes.IsEmpty) return Array.Empty<int>();

        var reader = new ProtoReader(packedBytes);
        var list = new List<int>();
        while (reader.HasMore)
        {
            list.Add(checked((int)reader.ReadVarint32()));
        }
        return list.ToArray();
    }

    /// <summary>packed sint64 (zigzag) 配列を読む。</summary>
    /// <remarks>DenseNodes.id / lat / lon、Way.refs の delta-coded sint64 で使用。</remarks>
    public static long[] ReadPackedZigzag64(ReadOnlySpan<byte> packedBytes)
    {
        if (packedBytes.IsEmpty) return Array.Empty<long>();

        var reader = new ProtoReader(packedBytes);
        var list = new List<long>();
        while (reader.HasMore)
        {
            list.Add(reader.ReadZigzag64());
        }
        return list.ToArray();
    }
}
