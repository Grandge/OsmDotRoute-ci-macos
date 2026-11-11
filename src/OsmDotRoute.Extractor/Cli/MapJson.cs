using System;
using System.IO;
using System.Text.Json;

namespace OsmDotRoute.Extractor.Cli;

/// <summary>
/// 親プロジェクト（災害廃棄物処理シミュレーション）の <c>.map.json</c> から bbox を読み取る。
/// </summary>
/// <remarks>
/// フォーマット（北西・南東の 2 隅で矩形を定義）:
/// <code>
/// { "NorthWestLat": 35.21, "NorthWestLon": 136.69, "SouthEastLat": 35.11, "SouthEastLon": 136.81 }
/// </code>
/// bbox への変換: MinLon=NorthWestLon, MinLat=SouthEastLat, MaxLon=SouthEastLon, MaxLat=NorthWestLat。
/// </remarks>
internal static class MapJson
{
    /// <summary><c>.map.json</c> を読んで bbox を返す。読めない場合は <c>null</c>。</summary>
    public static Bbox? TryReadBbox(string mapJsonPath)
    {
        try
        {
            var json = File.ReadAllText(mapJsonPath);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("NorthWestLat", out var nwLat) ||
                !root.TryGetProperty("NorthWestLon", out var nwLon) ||
                !root.TryGetProperty("SouthEastLat", out var seLat) ||
                !root.TryGetProperty("SouthEastLon", out var seLon))
            {
                return null;
            }

            double minLon = nwLon.GetDouble();
            double maxLat = nwLat.GetDouble();
            double maxLon = seLon.GetDouble();
            double minLat = seLat.GetDouble();

            if (minLon >= maxLon || minLat >= maxLat) return null;
            return new Bbox(minLon, minLat, maxLon, maxLat);
        }
        catch (Exception ex) when (ex is IOException or JsonException or FormatException)
        {
            return null;
        }
    }
}
