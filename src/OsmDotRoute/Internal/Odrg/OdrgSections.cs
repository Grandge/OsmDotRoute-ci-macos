using System.Runtime.InteropServices;

namespace OsmDotRoute.Internal.Odrg;

/// <summary>
/// VERTEX セクション要素（仕様書 §4.1、16 byte 固定）。
/// </summary>
/// <remarks>
/// 経度→緯度の順（ファイル形式と完全一致、<c>MemoryMarshal.Cast</c> による直 Span 化の受け皿）。
/// EDGE_SHAPE セクションも同レイアウト 16 byte の lon/lat 列のため、本型を流用する。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct OdrgVertex(double Lon, double Lat);

/// <summary>
/// EDGE セクション要素（仕様書 §4.3、24 byte 固定）。
/// </summary>
/// <remarks>
/// Phase 2 <c>OsmDotRoute.Extractor.Pipeline.OdrgEdge</c> と論理同一だが Core 独立定義
/// （DRY 一時違反、3C で統一予定）。フィールド順・サイズはファイル形式と一致。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct OdrgEdge(
    uint FromVertexId,
    uint ToVertexId,
    ulong ShapeOffset,
    uint ShapePointCount,
    uint BakedProfileIndex);

/// <summary>
/// SPATIAL_INDEX セクションのノード要素（仕様書 §4.6、56 byte 固定）。
/// </summary>
/// <remarks>
/// レイアウト: <see cref="OdrgBbox"/> 32 byte + FirstChildIndex u32 + ChildCount u32 + Flags u32 + reserved 12 byte。
/// Flags ビット 0 = リーフ判定（仕様書 §4.6 で予約）。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct OdrgRTreeNode(
    OdrgBbox Bbox,
    uint FirstChildIndex,
    uint ChildCount,
    uint Flags,
    uint Reserved0,
    uint Reserved1,
    uint Reserved2);

/// <summary>
/// BAKED_PROFILE セクションのエントリ要素（仕様書 §4.7、8 byte 固定）。
/// </summary>
/// <remarks>
/// レイアウト: SpeedKmh f32 + Flags u8 + reserved 3 byte。
/// プロファイル major（<c>entries[profile * edgeCount + edge]</c>）で BAKED_PROFILE セクションに格納される。
/// </remarks>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
internal readonly record struct OdrgBakedProfileEntry(
    float SpeedKmh,
    byte Flags,
    byte Reserved0,
    byte Reserved1,
    byte Reserved2);
