namespace OsmDotRoute.Geometry;

/// <summary>
/// 緯度経度の外接矩形（AABB）。南西端と北東端で表現する。
/// </summary>
internal readonly record struct GeoBounds(GeoCoordinate SouthWest, GeoCoordinate NorthEast);
