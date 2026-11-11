namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// .odrg 仕様書 §4.7.3 Baked Profile Table の 1 エントリ（8 byte）。
/// </summary>
/// <param name="SpeedKmh">通行速度 km/h（<see cref="CanPass"/> が false なら 0）。</param>
/// <param name="Flags">bit 0 = CanPass、bit 1 = Forward、bit 2 = Backward、bit 3-7 = 予約。</param>
/// <remarks>
/// .odrg 書出（3.8）で 4-byte float + 1-byte flags + 3-byte reserved = 8 byte レイアウトに展開する。
/// 本構造体自体は寿命管理用の論理表現で、wire format との packing は <c>OdrgWriter</c> 側で行う。
/// </remarks>
internal readonly record struct BakedProfileEntry(float SpeedKmh, byte Flags)
{
    private const byte CanPassBit = 1 << 0;
    private const byte ForwardBit = 1 << 1;
    private const byte BackwardBit = 1 << 2;

    /// <summary>このプロファイルでこのエッジを通行可能か。</summary>
    public bool CanPass => (Flags & CanPassBit) != 0;

    /// <summary>OSM デジタイズ方向（from → to）に通行可能か。</summary>
    public bool Forward => (Flags & ForwardBit) != 0;

    /// <summary>OSM デジタイズ方向の逆（to → from）に通行可能か。</summary>
    public bool Backward => (Flags & BackwardBit) != 0;

    /// <summary>論理値からエントリを構築する。</summary>
    public static BakedProfileEntry Create(bool canPass, float speedKmh, bool forward, bool backward)
    {
        byte flags = 0;
        if (canPass) flags |= CanPassBit;
        if (forward) flags |= ForwardBit;
        if (backward) flags |= BackwardBit;
        return new BakedProfileEntry(canPass ? speedKmh : 0f, flags);
    }
}
