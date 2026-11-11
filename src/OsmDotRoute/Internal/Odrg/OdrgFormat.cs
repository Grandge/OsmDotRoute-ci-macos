namespace OsmDotRoute.Internal.Odrg;

/// <summary>
/// .odrg ファイル形式の定数。仕様書 §1〜§4 と対応。
/// </summary>
internal static class OdrgFormat
{
    public const int HeaderSize = 256;
    public const int SectionTableEntrySize = 24;
    public const int VertexSize = 16;
    public const int EdgeSize = 24;
    public const int ShapePointSize = 16;
    public const int EdgeAabbSize = 32;
    public const int RTreeNodeSize = 56;
    public const int RTreeHeaderSize = 16;
    public const int BakedProfileEntrySize = 8;
    public const int BakedProfileTableHeaderSize = 8;
    public const int BakedProfileNameTableEntrySize = 8;

    public const ushort VersionMajor = 1;
    public const ushort VersionMinor = 1;

    /// <summary>VersionMinor=1 以降で要求 bbox（オフセット 88-119）が定義される。</summary>
    public const ushort VersionMinorRequestedBbox = 1;

    public const uint EdgeFlagBytes = 2;  // ushort EdgeFlags

    public const ushort SectionVertexTable = 0x0001;
    public const ushort SectionEdgeTable = 0x0002;
    public const ushort SectionEdgeShapeBuffer = 0x0003;
    public const ushort SectionEdgeAabbTable = 0x0004;
    public const ushort SectionEdgeFlagTable = 0x0005;
    public const ushort SectionEdgeSpatialIndex = 0x0006;
    public const ushort SectionBakedProfileTable = 0x0007;
    public const ushort SectionTurnRestrictionTable = 0x0008;
    public const ushort SectionMetadata = 0x0009;

    public static ReadOnlySpan<byte> MagicBytes => "ODRG\0\0\0\0"u8;
}
