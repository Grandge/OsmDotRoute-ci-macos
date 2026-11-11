using System;
using System.IO;
using OsmDotRoute.Pbf.Osm;
using OsmDotRoute.Pbf.Protobuf;

namespace OsmDotRoute.Tests.Pbf.Osm;

public class OsmRelationParserTests
{
    [Fact]
    public void Parse_MinimalRelation_IdOnly()
    {
        byte[] bytes = BuildRelation(id: 99L);

        OsmRelation relation = OsmRelationParser.Parse(bytes);

        Assert.Equal(99L, relation.Id);
        Assert.Empty(relation.Members);
        Assert.Empty(relation.TagKeys);
        Assert.Empty(relation.TagValues);
    }

    [Fact]
    public void Parse_RelationWithMembers_DeltaDecoded()
    {
        // 3 メンバー: 絶対 memid = [100, 105, 110]、type = NODE/WAY/RELATION、role = 1/2/3
        byte[] bytes = BuildRelation(
            id: 1L,
            memberIds: new[] { 100L, 105L, 110L },
            memberTypes: new[] { OsmMemberType.Node, OsmMemberType.Way, OsmMemberType.Relation },
            roleIndices: new[] { 1, 2, 3 });

        OsmRelation relation = OsmRelationParser.Parse(bytes);

        Assert.Equal(3, relation.Members.Length);
        Assert.Equal(new OsmRelationMember(100L, OsmMemberType.Node, 1), relation.Members[0]);
        Assert.Equal(new OsmRelationMember(105L, OsmMemberType.Way, 2), relation.Members[1]);
        Assert.Equal(new OsmRelationMember(110L, OsmMemberType.Relation, 3), relation.Members[2]);
    }

    [Fact]
    public void Parse_AllMemberTypes_DecodedCorrectly()
    {
        byte[] bytes = BuildRelation(
            id: 1L,
            memberIds: new[] { 1L, 2L, 3L },
            memberTypes: new[] { OsmMemberType.Node, OsmMemberType.Way, OsmMemberType.Relation },
            roleIndices: new[] { 0, 0, 0 });

        OsmRelation relation = OsmRelationParser.Parse(bytes);

        Assert.Equal(OsmMemberType.Node, relation.Members[0].Type);
        Assert.Equal(OsmMemberType.Way, relation.Members[1].Type);
        Assert.Equal(OsmMemberType.Relation, relation.Members[2].Type);
    }

    [Fact]
    public void Parse_NegativeMemIdDeltas_AbsoluteValuesPreserved()
    {
        // 絶対値 [-50, -40, -10, 30] → delta = [-50, 10, 30, 40]
        byte[] bytes = BuildRelation(
            id: 1L,
            memberIds: new[] { -50L, -40L, -10L, 30L },
            memberTypes: new[] { OsmMemberType.Node, OsmMemberType.Node, OsmMemberType.Node, OsmMemberType.Node },
            roleIndices: new[] { 0, 0, 0, 0 });

        OsmRelation relation = OsmRelationParser.Parse(bytes);

        Assert.Equal(-50L, relation.Members[0].MemberId);
        Assert.Equal(-40L, relation.Members[1].MemberId);
        Assert.Equal(-10L, relation.Members[2].MemberId);
        Assert.Equal(30L, relation.Members[3].MemberId);
    }

    [Fact]
    public void Parse_RelationWithTags_TagsRead()
    {
        byte[] bytes = BuildRelation(
            id: 1L,
            tagKeys: new[] { 1u, 3u },
            tagVals: new[] { 2u, 4u });

        OsmRelation relation = OsmRelationParser.Parse(bytes);

        Assert.Equal(new[] { 1, 3 }, relation.TagKeys);
        Assert.Equal(new[] { 2, 4 }, relation.TagValues);
    }

    [Fact]
    public void Parse_FullRelation_AllFieldsRead()
    {
        byte[] bytes = BuildRelation(
            id: 42L,
            memberIds: new[] { 1000L, 1010L },
            memberTypes: new[] { OsmMemberType.Way, OsmMemberType.Node },
            roleIndices: new[] { 5, 6 },
            tagKeys: new[] { 10u },
            tagVals: new[] { 11u });

        OsmRelation relation = OsmRelationParser.Parse(bytes);

        Assert.Equal(42L, relation.Id);
        Assert.Equal(2, relation.Members.Length);
        Assert.Equal(new OsmRelationMember(1000L, OsmMemberType.Way, 5), relation.Members[0]);
        Assert.Equal(new OsmRelationMember(1010L, OsmMemberType.Node, 6), relation.Members[1]);
        Assert.Equal(new[] { 10 }, relation.TagKeys);
        Assert.Equal(new[] { 11 }, relation.TagValues);
    }

    [Fact]
    public void Parse_InfoFieldSkipped()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 7UL);
        var info = new MemoryStream();
        WriteTag(info, 1, WireType.Varint);
        WriteVarint(info, 5);
        byte[] infoBytes = info.ToArray();
        WriteTag(ms, 4, WireType.LengthDelimited);
        WriteVarint(ms, (ulong)infoBytes.Length);
        ms.Write(infoBytes);

        OsmRelation relation = OsmRelationParser.Parse(ms.ToArray());

        Assert.Equal(7L, relation.Id);
    }

    [Fact]
    public void Parse_UnknownFieldSkipped()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);
        WriteTag(ms, 99, WireType.Varint);
        WriteVarint(ms, 12345UL);

        OsmRelation relation = OsmRelationParser.Parse(ms.ToArray());

        Assert.Equal(1L, relation.Id);
    }

    [Fact]
    public void Parse_EmptyMembers_EmptyArray()
    {
        // member 配列を全て length=0 で出す
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);
        WriteTag(ms, 8, WireType.LengthDelimited);
        WriteVarint(ms, 0);
        WriteTag(ms, 9, WireType.LengthDelimited);
        WriteVarint(ms, 0);
        WriteTag(ms, 10, WireType.LengthDelimited);
        WriteVarint(ms, 0);

        OsmRelation relation = OsmRelationParser.Parse(ms.ToArray());

        Assert.Empty(relation.Members);
    }

    [Fact]
    public void Parse_TurnRestrictionRelation_ParsedAsTagsAndMembers()
    {
        // type=restriction relation の典型: 3 members (from way, via node, to way)
        // 仕様完全性のために形式上解析できることを確認 (Phase 2 では使わない)
        byte[] bytes = BuildRelation(
            id: 12345L,
            memberIds: new[] { 100L, 200L, 300L },
            memberTypes: new[] { OsmMemberType.Way, OsmMemberType.Node, OsmMemberType.Way },
            roleIndices: new[] { 10 /* "from" */, 11 /* "via" */, 12 /* "to" */ },
            tagKeys: new[] { 1u /* "type" */, 2u /* "restriction" */ },
            tagVals: new[] { 3u /* "restriction" */, 4u /* "no_left_turn" */ });

        OsmRelation relation = OsmRelationParser.Parse(bytes);

        Assert.Equal(12345L, relation.Id);
        Assert.Equal(3, relation.Members.Length);
        Assert.Equal(OsmMemberType.Way, relation.Members[0].Type);
        Assert.Equal(OsmMemberType.Node, relation.Members[1].Type);
        Assert.Equal(OsmMemberType.Way, relation.Members[2].Type);
        Assert.Equal(2, relation.TagKeys.Length);
    }

    [Fact]
    public void Parse_MissingId_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 8, WireType.LengthDelimited);
        WriteVarint(ms, 0);

        Assert.Throws<InvalidDataException>(() => OsmRelationParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_KeysVsValsLengthMismatch_Throws()
    {
        byte[] bytes = BuildRelation(
            id: 1L,
            tagKeys: new[] { 1u, 2u, 3u },
            tagVals: new[] { 1u, 2u });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            OsmRelationParser.Parse(bytes));
        Assert.Contains("keys=3", ex.Message);
    }

    [Fact]
    public void Parse_MemberArraysLengthMismatch_Throws()
    {
        // memids = 3 個、types = 2 個 → 不一致
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);

        WriteTag(ms, 8, WireType.LengthDelimited);
        byte[] rolesPacked = PackUint32(new uint[] { 0, 0, 0 });
        WriteVarint(ms, (ulong)rolesPacked.Length);
        ms.Write(rolesPacked);

        WriteTag(ms, 9, WireType.LengthDelimited);
        byte[] memidsPacked = PackZigzag(new long[] { 1L, 1L, 1L });
        WriteVarint(ms, (ulong)memidsPacked.Length);
        ms.Write(memidsPacked);

        WriteTag(ms, 10, WireType.LengthDelimited);
        byte[] typesPacked = PackUint32(new uint[] { 0, 1 });
        WriteVarint(ms, (ulong)typesPacked.Length);
        ms.Write(typesPacked);

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            OsmRelationParser.Parse(ms.ToArray()));
        Assert.Contains("member arrays length mismatch", ex.Message);
    }

    [Fact]
    public void Parse_UnknownMemberType_Throws()
    {
        // types = [3] は未定義 (0/1/2 のみ valid)
        byte[] bytes = BuildRelationRawTypes(
            id: 1L,
            memberIds: new[] { 1L },
            memberTypesRaw: new uint[] { 3 },
            roleIndices: new[] { 0 });

        InvalidDataException ex = Assert.Throws<InvalidDataException>(() =>
            OsmRelationParser.Parse(bytes));
        Assert.Contains("member type 3", ex.Message);
    }

    [Fact]
    public void Parse_IdWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.LengthDelimited);
        WriteVarint(ms, 1);
        ms.WriteByte(0);

        Assert.Throws<InvalidDataException>(() => OsmRelationParser.Parse(ms.ToArray()));
    }

    [Fact]
    public void Parse_MemidsWrongWireType_Throws()
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, 1UL);
        WriteTag(ms, 9, WireType.Varint); // packed のはず
        WriteVarint(ms, 100UL);

        Assert.Throws<InvalidDataException>(() => OsmRelationParser.Parse(ms.ToArray()));
    }

    // --- 補助メソッド --------------------------------------------------------

    private static byte[] BuildRelation(
        long id,
        long[]? memberIds = null,
        OsmMemberType[]? memberTypes = null,
        int[]? roleIndices = null,
        uint[]? tagKeys = null,
        uint[]? tagVals = null)
    {
        uint[]? typesRaw = memberTypes is null ? null :
            Array.ConvertAll(memberTypes, t => (uint)(int)t);
        return BuildRelationRawTypes(id, memberIds, typesRaw, roleIndices, tagKeys, tagVals);
    }

    private static byte[] BuildRelationRawTypes(
        long id,
        long[]? memberIds = null,
        uint[]? memberTypesRaw = null,
        int[]? roleIndices = null,
        uint[]? tagKeys = null,
        uint[]? tagVals = null)
    {
        using var ms = new MemoryStream();
        WriteTag(ms, 1, WireType.Varint);
        WriteVarint(ms, unchecked((ulong)id));

        if (tagKeys is not null && tagKeys.Length > 0)
        {
            WriteTag(ms, 2, WireType.LengthDelimited);
            byte[] kp = PackUint32(tagKeys);
            WriteVarint(ms, (ulong)kp.Length);
            ms.Write(kp);
        }

        if (tagVals is not null && tagVals.Length > 0)
        {
            WriteTag(ms, 3, WireType.LengthDelimited);
            byte[] vp = PackUint32(tagVals);
            WriteVarint(ms, (ulong)vp.Length);
            ms.Write(vp);
        }

        if (roleIndices is not null && roleIndices.Length > 0)
        {
            WriteTag(ms, 8, WireType.LengthDelimited);
            uint[] rolesUint = Array.ConvertAll(roleIndices, i => (uint)i);
            byte[] rp = PackUint32(rolesUint);
            WriteVarint(ms, (ulong)rp.Length);
            ms.Write(rp);
        }

        if (memberIds is not null && memberIds.Length > 0)
        {
            WriteTag(ms, 9, WireType.LengthDelimited);
            byte[] mp = PackZigzag(ToDeltas(memberIds));
            WriteVarint(ms, (ulong)mp.Length);
            ms.Write(mp);
        }

        if (memberTypesRaw is not null && memberTypesRaw.Length > 0)
        {
            WriteTag(ms, 10, WireType.LengthDelimited);
            byte[] tp = PackUint32(memberTypesRaw);
            WriteVarint(ms, (ulong)tp.Length);
            ms.Write(tp);
        }

        return ms.ToArray();
    }

    private static long[] ToDeltas(long[] absoluteValues)
    {
        var deltas = new long[absoluteValues.Length];
        long prev = 0;
        for (int i = 0; i < absoluteValues.Length; i++)
        {
            deltas[i] = absoluteValues[i] - prev;
            prev = absoluteValues[i];
        }
        return deltas;
    }

    private static byte[] PackZigzag(long[] values)
    {
        using var ms = new MemoryStream();
        foreach (long v in values) WriteVarint(ms, ZigZagEncode(v));
        return ms.ToArray();
    }

    private static byte[] PackUint32(uint[] values)
    {
        using var ms = new MemoryStream();
        foreach (uint v in values) WriteVarint(ms, v);
        return ms.ToArray();
    }

    private static void WriteTag(Stream output, int fieldNumber, WireType wireType)
    {
        ulong tag = ((ulong)(uint)fieldNumber << 3) | (uint)wireType;
        WriteVarint(output, tag);
    }

    private static void WriteVarint(Stream output, ulong value)
    {
        while (value >= 0x80)
        {
            output.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        output.WriteByte((byte)value);
    }

    private static ulong ZigZagEncode(long value)
    {
        return (ulong)((value << 1) ^ (value >> 63));
    }
}
