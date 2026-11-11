namespace OsmDotRoute;

/// <summary>
/// JIS X0410 地域メッシュの階層。<see cref="MeshCode.Level"/> が桁数から自動判定する。
/// 8〜11 桁の 4 階層に対応（11 桁は 1/8 細分・象限方式、REQ-RST-016 で仕様確定）。
/// </summary>
public enum MeshLevel
{
    /// <summary>第3次メッシュ（約 1km 四方、8 桁、例 53394611）</summary>
    Mesh3rd,

    /// <summary>1/2 細分メッシュ（約 500m 四方、9 桁、例 533946111）</summary>
    HalfMesh,

    /// <summary>1/4 細分メッシュ（約 250m 四方、10 桁、例 5339461111）</summary>
    QuarterMesh,

    /// <summary>1/8 細分メッシュ（約 125m 四方、11 桁、例 53394611111）</summary>
    EighthMesh,
}