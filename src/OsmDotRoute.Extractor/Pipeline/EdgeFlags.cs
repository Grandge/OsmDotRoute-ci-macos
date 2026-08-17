using System;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// .odrg 仕様書 §4.5 Edge Flag Table の 16-bit ビット割り当て。
/// </summary>
/// <remarks>
/// <para>
/// 12 属性 + Oneway 2 bit = 14 bit 使用、残り 2 bit 予約 (v0.2 確定)。
/// </para>
/// <para>
/// bit 11 (<see cref="IsSchoolZone"/>) は v0.2 で予約のみ。抽出ツール側は 0 固定出力。
/// </para>
/// </remarks>
[Flags]
internal enum EdgeFlags : ushort
{
    None = 0,
    IsBridge = 1 << 0,
    IsTunnel = 1 << 1,
    IsElevated = 1 << 2,
    IsRoundabout = 1 << 3,
    IsToll = 1 << 4,
    IsPrivateAccess = 1 << 5,
    IsServiceWay = 1 << 6,
    IsTrack = 1 << 7,
    IsLivingStreet = 1 << 8,
    IsPedestrianSeparated = 1 << 9,
    IsWinterClosed = 1 << 10,
    IsSchoolZone = 1 << 11,
    IsOnewayForward = 1 << 12,
    IsOnewayBackward = 1 << 13,
    // bit 14, 15: 予約
}
