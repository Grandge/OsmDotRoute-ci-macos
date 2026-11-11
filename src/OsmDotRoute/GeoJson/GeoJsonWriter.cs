using System.Text;
using System.Text.Json;
using OsmDotRoute.Routing;

namespace OsmDotRoute.GeoJson;

/// <summary>
/// GeoJSON 出力ユーティリティ。Phase 1 では道路ネットワーク全体の出力（REQ-RTE-004）のみ対応する。
/// 経路の GeoJSON 出力（REQ-RTE-005）はステップ 11 で追加予定。
/// </summary>
internal static class GeoJsonWriter
{
    /// <summary>
    /// 道路グラフのエッジを GeoJSON <c>FeatureCollection</c>（<c>LineString</c> 列）として書き出す。
    /// </summary>
    /// <param name="graph">道路グラフ。</param>
    /// <param name="bounds">
    /// フィルタ用 bbox（省略可）。指定した場合、いずれかの座標が bbox 内に含まれるエッジのみを出力する。
    /// way 拡張で FileBbox が広がる場合に RequestedBbox を渡すことで表示範囲を抑制できる。
    /// </param>
    /// <remarks>
    /// <para>各 <c>Feature</c> は単一エッジで構成され、座標列は <c>[lon, lat]</c> 順（GeoJSON 標準, RFC 7946）。</para>
    /// <para>同一エッジは複数頂点から enum されるため、<see cref="HashSet{T}"/> で edge ID を管理し重複排除する。</para>
    /// <para><c>properties</c> は空オブジェクト（親プロジェクト <c>MapService.GetRoadNetworkGeoJson</c> と同じスキーマ）。
    /// 将来 highway タグ等を含める場合は overload を追加する。</para>
    /// </remarks>
    public static string WriteRoadNetwork(
        IRoadGraph graph,
        (GeoCoordinate SouthWest, GeoCoordinate NorthEast)? bounds = null)
    {
        ArgumentNullException.ThrowIfNull(graph);

        using var ms = new MemoryStream();
        using (var writer = new Utf8JsonWriter(ms))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "FeatureCollection");
            writer.WriteStartArray("features");

            var processed = new HashSet<uint>();
            var vertexCount = graph.VertexCount;
            for (uint v = 0; v < vertexCount; v++)
            {
                var en = graph.GetEdgeEnumerator(v);
                while (en.MoveNext())
                {
                    if (!processed.Add(en.EdgeId)) continue;

                    var from = graph.GetVertex(v);
                    var to = graph.GetVertex(en.To);

                    if (bounds.HasValue && !EdgeIntersectsBounds(from, en.Shape, to, bounds.Value))
                        continue;

                    WriteFeature(writer, from, en.Shape, to);
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private static bool EdgeIntersectsBounds(
        GeoCoordinate from,
        IReadOnlyList<GeoCoordinate> shape,
        GeoCoordinate to,
        (GeoCoordinate SouthWest, GeoCoordinate NorthEast) bounds)
    {
        static bool InBounds(GeoCoordinate c, (GeoCoordinate SouthWest, GeoCoordinate NorthEast) b) =>
            c.Latitude >= b.SouthWest.Latitude && c.Latitude <= b.NorthEast.Latitude &&
            c.Longitude >= b.SouthWest.Longitude && c.Longitude <= b.NorthEast.Longitude;

        if (InBounds(from, bounds) || InBounds(to, bounds)) return true;
        for (int i = 0; i < shape.Count; i++)
            if (InBounds(shape[i], bounds)) return true;
        return false;
    }

    private static void WriteFeature(
        Utf8JsonWriter writer,
        GeoCoordinate from,
        IReadOnlyList<GeoCoordinate> shape,
        GeoCoordinate to)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");

        writer.WriteStartObject("geometry");
        writer.WriteString("type", "LineString");
        writer.WriteStartArray("coordinates");
        WriteCoordinate(writer, from);
        for (int i = 0; i < shape.Count; i++)
        {
            WriteCoordinate(writer, shape[i]);
        }
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
