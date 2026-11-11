using System;
using System.Globalization;

namespace OsmDotRoute.Extractor.Cli;

/// <summary>
/// WGS84 経緯度バウンディングボックス。<c>--bbox minLon,minLat,maxLon,maxLat</c> から構築する。
/// </summary>
/// <remarks>
/// .odrg 仕様書 v0.2 §5.3.1 で <c>--bbox</c> は lon/lat 直接指定のみと確定。Phase 2 では
/// メッシュコード・都道府県名プリセットは採用しない。
/// </remarks>
internal readonly record struct Bbox(double MinLon, double MinLat, double MaxLon, double MaxLat)
{
    /// <summary>"minLon,minLat,maxLon,maxLat" 形式の文字列をパースする。</summary>
    /// <exception cref="FormatException">フォーマット不正・経緯度範囲外・min &gt;= max の場合。</exception>
    public static Bbox Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        string[] parts = text.Split(',');
        if (parts.Length != 4)
            throw new FormatException(
                $"--bbox は 4 値カンマ区切り (minLon,minLat,maxLon,maxLat) が必要。受信: '{text}'");

        if (!TryParseDouble(parts[0], out double minLon) ||
            !TryParseDouble(parts[1], out double minLat) ||
            !TryParseDouble(parts[2], out double maxLon) ||
            !TryParseDouble(parts[3], out double maxLat))
        {
            throw new FormatException(
                $"--bbox の各値は浮動小数点として解釈できる必要がある。受信: '{text}'");
        }

        if (minLon < -180.0 || minLon > 180.0 || maxLon < -180.0 || maxLon > 180.0)
            throw new FormatException($"--bbox 経度は -180〜180 の範囲。受信: '{text}'");
        if (minLat < -90.0 || minLat > 90.0 || maxLat < -90.0 || maxLat > 90.0)
            throw new FormatException($"--bbox 緯度は -90〜90 の範囲。受信: '{text}'");
        if (minLon >= maxLon)
            throw new FormatException($"--bbox は minLon < maxLon が必要。受信: '{text}'");
        if (minLat >= maxLat)
            throw new FormatException($"--bbox は minLat < maxLat が必要。受信: '{text}'");

        return new Bbox(minLon, minLat, maxLon, maxLat);
    }

    private static bool TryParseDouble(string s, out double value) =>
        double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    public override string ToString() => string.Create(
        CultureInfo.InvariantCulture,
        $"{MinLon},{MinLat},{MaxLon},{MaxLat}");
}
