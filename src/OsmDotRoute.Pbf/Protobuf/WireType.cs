namespace OsmDotRoute.Pbf.Protobuf;

/// <summary>
/// protobuf ワイヤ形式の wire_type 列挙。
/// 参考: https://protobuf.dev/programming-guides/encoding/
/// </summary>
internal enum WireType
{
    /// <summary>varint (int32/int64/uint32/uint64/sint32/sint64/bool/enum)</summary>
    Varint = 0,

    /// <summary>64-bit 固定長 (fixed64/sfixed64/double)</summary>
    Fixed64 = 1,

    /// <summary>length-delimited (string/bytes/embedded message/packed repeated)</summary>
    LengthDelimited = 2,

    /// <summary>start group (廃止、PBF では使われない)</summary>
    StartGroup = 3,

    /// <summary>end group (廃止、PBF では使われない)</summary>
    EndGroup = 4,

    /// <summary>32-bit 固定長 (fixed32/sfixed32/float)</summary>
    Fixed32 = 5,
}
