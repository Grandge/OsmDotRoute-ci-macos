using System;
using System.Buffers.Binary;
using System.IO;
using OsmDotRoute.Internal.Odrg;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// 既存 <c>.odrg</c> のヘッダーのみを書き換えて RequestedBbox（要求 bbox）を後付けする
/// （v0.2 → v0.3 マイグレーション用）。グラフ本体（セクション）は一切変更しない。
/// </summary>
/// <remarks>
/// ヘッダーレイアウト（仕様書 §2）:
/// <list type="bullet">
///   <item>offset 10-11: VersionMinor を 1 に書換</item>
///   <item>offset 88-119: RequestedBbox（double × 4 = MinLon, MinLat, MaxLon, MaxLat）</item>
/// </list>
/// これらはいずれも v0.2 で予約領域 (reservedB) だったため、本体オフセットは不変。
/// </remarks>
internal static class OdrgHeaderPatcher
{
    /// <summary>
    /// <paramref name="odrgPath"/> のヘッダーに <paramref name="requestedBbox"/> を書き込み、
    /// VersionMinor を 1 に bump する。
    /// </summary>
    /// <exception cref="InvalidDataException">マジック不一致 / ファイルが小さすぎる場合。</exception>
    public static void Patch(string odrgPath, Aabb requestedBbox)
    {
        ArgumentNullException.ThrowIfNull(odrgPath);

        using var fs = new FileStream(odrgPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        if (fs.Length < OdrgFormat.HeaderSize)
            throw new InvalidDataException(
                $"File too small: {fs.Length} bytes < {OdrgFormat.HeaderSize} byte header ({odrgPath})");

        Span<byte> magic = stackalloc byte[8];
        fs.ReadExactly(magic);
        var expected = OdrgFormat.MagicBytes;
        for (int i = 0; i < expected.Length; i++)
        {
            if (magic[i] != expected[i])
                throw new InvalidDataException($"Invalid magic bytes (not an .odrg file): {odrgPath}");
        }

        // VersionMinor (offset 10-11) を 1 に書換
        Span<byte> minorBuf = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(minorBuf, OdrgFormat.VersionMinorRequestedBbox);
        fs.Seek(10, SeekOrigin.Begin);
        fs.Write(minorBuf);

        // RequestedBbox (offset 88-119) を書込
        Span<byte> bboxBuf = stackalloc byte[32];
        BinaryPrimitives.WriteDoubleLittleEndian(bboxBuf.Slice(0, 8), requestedBbox.MinLon);
        BinaryPrimitives.WriteDoubleLittleEndian(bboxBuf.Slice(8, 8), requestedBbox.MinLat);
        BinaryPrimitives.WriteDoubleLittleEndian(bboxBuf.Slice(16, 8), requestedBbox.MaxLon);
        BinaryPrimitives.WriteDoubleLittleEndian(bboxBuf.Slice(24, 8), requestedBbox.MaxLat);
        fs.Seek(88, SeekOrigin.Begin);
        fs.Write(bboxBuf);

        fs.Flush();
    }
}
