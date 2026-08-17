namespace OsmDotRoute.Pbf.Protobuf;

/// <summary>
/// protobuf フィールドのタグ (field_number + wire_type)。
/// バッファ末尾で読み出すと <see cref="IsEnd"/> が true となる。
/// </summary>
internal readonly record struct ProtoTag(int FieldNumber, WireType WireType)
{
    /// <summary>バッファ末尾を示すセンチネル値。</summary>
    public bool IsEnd => FieldNumber == 0;
}
