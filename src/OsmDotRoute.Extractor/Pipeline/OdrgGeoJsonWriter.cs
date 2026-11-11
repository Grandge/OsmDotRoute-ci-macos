using System.IO;
using System.Text;
using System.Text.Json;
using OsmDotRoute;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// <see cref="OdrgReadResult"/> 全エッジを GeoJSON <c>FeatureCollection</c>（<c>LineString</c> 列）として書き出す。
/// </summary>
/// <remarks>
/// <para>
/// Phase 2 ステップ 5.4。MapVerifier の <c>.odrg</c> オーバーレイ表示用。
/// </para>
/// <para>
/// 既存 <c>OsmDotRoute.GeoJson.GeoJsonWriter</c> と同じスキーマ:
/// 各 Feature の geometry は LineString、座標は <c>[lon, lat]</c> 順 (RFC 7946)、properties は空。
/// </para>
/// </remarks>
internal static class OdrgGeoJsonWriter
{
    /// <summary>
    /// <paramref name="result"/> の全エッジを GeoJSON FeatureCollection 文字列として返す。
    /// </summary>
    public static string WriteRoadNetwork(OdrgReadResult result)
    {
        System.ArgumentNullException.ThrowIfNull(result);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "FeatureCollection");
            writer.WriteStartArray("features");

            for (int i = 0; i < result.Edges.Length; i++)
            {
                var edge = result.Edges[i];
                if (edge.FromVertexId >= result.Vertices.Length || edge.ToVertexId >= result.Vertices.Length)
                    continue;
                WriteFeature(
                    writer,
                    result.Vertices[edge.FromVertexId],
                    result.EdgeShapes[i],
                    result.Vertices[edge.ToVertexId]);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static void WriteFeature(
        Utf8JsonWriter writer,
        GeoCoordinate from,
        GeoCoordinate[] shape,
        GeoCoordinate to)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");

        writer.WriteStartObject("geometry");
        writer.WriteString("type", "LineString");
        writer.WriteStartArray("coordinates");
        WriteCoordinate(writer, from);
        for (int i = 0; i < shape.Length; i++)
            WriteCoordinate(writer, shape[i]);
        WriteCoordinate(writer, to);
        writer.WriteEndArray();
        writer.WriteEndObject();

        writer.WriteStartObject("properties");
        writer.WriteEndObject();

        writer.WriteEndObject();
    }

    private static void WriteCoordinate(Utf8JsonWriter writer, GeoCoordinate c)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(c.Longitude);
        writer.WriteNumberValue(c.Latitude);
        writer.WriteEndArray();
    }
}
