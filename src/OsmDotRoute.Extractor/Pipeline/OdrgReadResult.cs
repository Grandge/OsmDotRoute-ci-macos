using System.Collections.Generic;
using OsmDotRoute;
using OsmDotRoute.Internal.Odrg;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// .odrg ファイルヘッダーの論理表現（仕様書 §1）。
/// </summary>
internal readonly record struct OdrgHeader(
    ushort VersionMajor,
    ushort VersionMinor,
    uint Flags,
    ulong VertexCount,
    ulong EdgeCount,
    Aabb Bbox,
    uint ProfileCount,
    uint EdgeFlagBytes,
    ulong SectionTableOffset,
    uint SectionCount,
    Aabb RequestedBbox)
{
    /// <summary>VersionMinor=1 以降で <see cref="RequestedBbox"/> が明示的に書き込まれる。</summary>
    public bool HasRequestedBbox => VersionMinor >= OdrgFormat.VersionMinorRequestedBbox;
}

/// <summary>
/// セクションテーブルエントリ（仕様書 §2）。
/// </summary>
internal readonly record struct OdrgSectionTableEntry(
    ushort Kind,
    uint Flags,
    ulong Offset,
    ulong Length);

/// <summary>
/// 読込時のエッジ表現（仕様書 §4.3 Edge Table 1 行）。
/// </summary>
/// <remarks>
/// 書出側の <see cref="EdgeRecord"/> は OSM タグ情報を保持するが、
/// 本構造体は <c>.odrg</c> に実際に書かれた数値のみを保持する。
/// </remarks>
internal readonly record struct OdrgEdge(
    uint FromVertexId,
    uint ToVertexId,
    ulong ShapeOffset,
    uint ShapePointCount,
    uint BakedProfileIndex);

/// <summary>
/// 読込時の R-tree 表現（仕様書 §4.6）。
/// </summary>
internal sealed record OdrgRTreeRead(
    uint NodeCount,
    uint RootIndex,
    uint BranchingFactor,
    uint TreeHeight,
    RTreeNode[] Nodes);

/// <summary>
/// 読込時の bake プロファイル表（仕様書 §4.7）。
/// </summary>
/// <param name="ProfileNames">プロファイル名（採番順）。</param>
/// <param name="EntrySize">バイト数（書出時は <see cref="OdrgFormat.BakedProfileEntrySize"/> = 8 固定）。</param>
/// <param name="EntriesByProfile">
/// プロファイル major レイアウト。<c>EntriesByProfile[profileIndex][edgeId]</c>。
/// </param>
internal sealed record OdrgBakedProfileRead(
    string[] ProfileNames,
    uint EntrySize,
    BakedProfileEntry[][] EntriesByProfile)
{
    public int ProfileCount => ProfileNames.Length;
    public int EdgeCount => EntriesByProfile.Length > 0 ? EntriesByProfile[0].Length : 0;
}

/// <summary>
/// <see cref="OdrgReader.Read(System.IO.Stream)"/> の戻り値。`.odrg` 全セクションを eager parse で展開した結果を保持する。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 5.1 で導入。Phase 2 検証専用（MapVerifier オーバーレイ・整合テスト・RouterDb 突合）。
/// Phase 3 で実装予定の <c>NativeRoadGraph</c>（MMF + <c>ReadOnlySpan</c>）とは別系統。
/// </para>
/// <para>
/// <see cref="EdgeShapes"/> はエッジ ID 順のジャグ配列。空シェイプエッジは長さ 0 配列。
/// </para>
/// </remarks>
internal sealed record OdrgReadResult(
    OdrgHeader Header,
    OdrgSectionTableEntry[] SectionTable,
    GeoCoordinate[] Vertices,
    OdrgEdge[] Edges,
    GeoCoordinate[][] EdgeShapes,
    Aabb[] EdgeAabbs,
    EdgeFlags[] EdgeFlags,
    OdrgRTreeRead RTree,
    OdrgBakedProfileRead ProfileTable,
    byte[] TurnRestrictionRaw,
    string MetadataJson)
{
    /// <summary>セクション kind から対応エントリを取得する。</summary>
    public OdrgSectionTableEntry GetSection(ushort kind)
    {
        for (int i = 0; i < SectionTable.Length; i++)
            if (SectionTable[i].Kind == kind) return SectionTable[i];
        throw new KeyNotFoundException($"Section kind 0x{kind:X4} not found");
    }
}
