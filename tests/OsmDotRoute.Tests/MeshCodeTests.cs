using OsmDotRoute.Mesh;
using Xunit;

namespace OsmDotRoute.Tests;

/// <summary>
/// 「メッシュコード変換」の検証テスト（REQ-RST-016〜018）。
/// 既知のメッシュ（東京駅 53394611 = 1km、その細分）の境界座標を JIS X0410 計算で検算する。
/// 11 桁は 1/8 細分（125m・象限方式、REQ-RST-016 仕様確定）。
/// </summary>
public class MeshCodeTests
{
    // 1 度の許容誤差（1 マイクロ度 ≒ 11cm）
    private const double Eps = 1e-6;

    // 第3次メッシュ寸法
    private const double Lat3StepDeg = 30.0 / 3600.0;
    private const double Lon3StepDeg = 45.0 / 3600.0;

    [Fact]
    public void Level_Returns_Mesh3rd_For_8Digit_Code()
    {
        Assert.Equal(MeshLevel.Mesh3rd, new MeshCode(53394611L).Level);
    }

    [Fact]
    public void Level_Returns_HalfMesh_For_9Digit_Code()
    {
        Assert.Equal(MeshLevel.HalfMesh, new MeshCode(533946111L).Level);
    }

    [Fact]
    public void Level_Returns_QuarterMesh_For_10Digit_Code()
    {
        Assert.Equal(MeshLevel.QuarterMesh, new MeshCode(5339461111L).Level);
    }

    [Fact]
    public void Level_Returns_EighthMesh_For_11Digit_Code()
    {
        Assert.Equal(MeshLevel.EighthMesh, new MeshCode(53394611111L).Level);
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(1L)]
    [InlineData(9_999_999L)]            // 7 桁
    [InlineData(100_000_000_000L)]      // 12 桁
    [InlineData(999_999_999_999L)]      // 12 桁
    public void Level_Throws_ForOutOfRangeDigits(long value)
    {
        var meshCode = new MeshCode(value);
        Assert.Throws<ArgumentOutOfRangeException>(() => meshCode.Level);
    }

    [Fact]
    public void ToBoundingBox_Mesh3rd_53394611_MatchesJisCalculation()
    {
        // 53394611: p1=53, u1=39, p2=4, u2=6, p3=1, u3=1
        // SW lat = 53/1.5 + 4*(5/60) + 1*(30/3600) = 35.3333... + 0.3333... + 0.00833... = 35.6750
        // SW lon = 139 + 6*(7.5/60) + 1*(45/3600) = 139 + 0.75 + 0.0125 = 139.7625
        // 寸法: 緯度 0.008333..., 経度 0.0125 度
        var aabb = MeshCodeConverter.ToBoundingBox(new MeshCode(53394611L));

        Assert.Equal(35.6750, aabb.SouthWest.Latitude, precision: 6);
        Assert.Equal(139.7625, aabb.SouthWest.Longitude, precision: 6);
        Assert.Equal(35.6750 + Lat3StepDeg, aabb.NorthEast.Latitude, precision: 6);
        Assert.Equal(139.7625 + Lon3StepDeg, aabb.NorthEast.Longitude, precision: 6);
    }

    [Fact]
    public void ToBoundingBox_Mesh3rd_ContainsTokyoStation()
    {
        // 東京駅は 53394611 内（緯度 35.6812, 経度 139.7671）に位置する
        var aabb = MeshCodeConverter.ToBoundingBox(new MeshCode(53394611L));
        Assert.InRange(35.6812, aabb.SouthWest.Latitude, aabb.NorthEast.Latitude);
        Assert.InRange(139.7671, aabb.SouthWest.Longitude, aabb.NorthEast.Longitude);
    }

    [Fact]
    public void ToBoundingBox_HalfMesh_SwQuadrant_PreservesParentSouthwest()
    {
        // 533946111 = 53394611 の 1/2 細分 sub=1（南西象限）
        // SW は親メッシュと同じ、サイズは半分
        var aabb = MeshCodeConverter.ToBoundingBox(new MeshCode(533946111L));
        Assert.Equal(35.6750, aabb.SouthWest.Latitude, precision: 6);
        Assert.Equal(139.7625, aabb.SouthWest.Longitude, precision: 6);
        Assert.Equal(35.6750 + Lat3StepDeg / 2, aabb.NorthEast.Latitude, precision: 6);
        Assert.Equal(139.7625 + Lon3StepDeg / 2, aabb.NorthEast.Longitude, precision: 6);
    }

    [Fact]
    public void ToBoundingBox_HalfMesh_NeQuadrant_StartsAtMidpoint()
    {
        // 533946114 = sub=4（北東象限）
        var aabb = MeshCodeConverter.ToBoundingBox(new MeshCode(533946114L));
        Assert.Equal(35.6750 + Lat3StepDeg / 2, aabb.SouthWest.Latitude, precision: 6);
        Assert.Equal(139.7625 + Lon3StepDeg / 2, aabb.SouthWest.Longitude, precision: 6);
        Assert.Equal(35.6750 + Lat3StepDeg, aabb.NorthEast.Latitude, precision: 6);
        Assert.Equal(139.7625 + Lon3StepDeg, aabb.NorthEast.Longitude, precision: 6);
    }

    [Fact]
    public void ToBoundingBox_QuarterMesh_DeepestSw_PreservesParentSouthwest()
    {
        // 5339461111 = 53394611 / 1/2 sub=1 / 1/4 sub=1（最南西）
        var aabb = MeshCodeConverter.ToBoundingBox(new MeshCode(5339461111L));
        Assert.Equal(35.6750, aabb.SouthWest.Latitude, precision: 6);
        Assert.Equal(139.7625, aabb.SouthWest.Longitude, precision: 6);
        Assert.Equal(35.6750 + Lat3StepDeg / 4, aabb.NorthEast.Latitude, precision: 6);
        Assert.Equal(139.7625 + Lon3StepDeg / 4, aabb.NorthEast.Longitude, precision: 6);
    }

    [Fact]
    public void ToBoundingBox_QuarterMesh_DeepestNe_LandsInParentNeCorner()
    {
        // 5339461144 = 1/2 sub=4（NE）→ 1/4 sub=4（NE of NE）
        var aabb = MeshCodeConverter.ToBoundingBox(new MeshCode(5339461144L));
        // SW = 親 NE - 1 step (= 親 SW + 3/4 step)
        Assert.Equal(35.6750 + Lat3StepDeg * 0.75, aabb.SouthWest.Latitude, precision: 6);
        Assert.Equal(139.7625 + Lon3StepDeg * 0.75, aabb.SouthWest.Longitude, precision: 6);
        Assert.Equal(35.6750 + Lat3StepDeg, aabb.NorthEast.Latitude, precision: 6);
        Assert.Equal(139.7625 + Lon3StepDeg, aabb.NorthEast.Longitude, precision: 6);
    }

    [Fact]
    public void ToBoundingBox_SizeMatchesExpectedMetersPerLevel()
    {
        // 緯度 1 度 ≈ 111.32 km。寸法のメートル換算で 1km / 500m / 250m に近いことを確認。
        var mesh1km = MeshCodeConverter.ToBoundingBox(new MeshCode(53394611L));
        var mesh500m = MeshCodeConverter.ToBoundingBox(new MeshCode(533946111L));
        var mesh250m = MeshCodeConverter.ToBoundingBox(new MeshCode(5339461111L));

        var lat1km = (mesh1km.NorthEast.Latitude - mesh1km.SouthWest.Latitude) * 111320;
        var lat500m = (mesh500m.NorthEast.Latitude - mesh500m.SouthWest.Latitude) * 111320;
        var lat250m = (mesh250m.NorthEast.Latitude - mesh250m.SouthWest.Latitude) * 111320;

        Assert.InRange(lat1km, 900, 1100);    // 約 927m（30秒 × 111.32km/度）
        Assert.InRange(lat500m, 450, 550);
        Assert.InRange(lat250m, 220, 270);
    }

    [Fact]
    public void ToBoundingBox_HalfMesh_InvalidSubdivisionDigit_Throws()
    {
        // 9 桁目が 0 や 5 など範囲外
        Assert.Throws<ArgumentException>(() => MeshCodeConverter.ToBoundingBox(new MeshCode(533946110L)));
        Assert.Throws<ArgumentException>(() => MeshCodeConverter.ToBoundingBox(new MeshCode(533946115L)));
    }

    [Fact]
    public void ToBoundingBox_QuarterMesh_InvalidSubdivisionDigit_Throws()
    {
        // 10 桁目が範囲外
        Assert.Throws<ArgumentException>(() => MeshCodeConverter.ToBoundingBox(new MeshCode(5339461110L)));
        Assert.Throws<ArgumentException>(() => MeshCodeConverter.ToBoundingBox(new MeshCode(5339461115L)));
    }

    [Fact]
    public void ToBoundingBox_SecondaryMeshDigit_OutOfRange_Throws()
    {
        // 第2次メッシュ番号 (5桁目 p2, 6桁目 u2) は 0〜7。8 や 9 を含むと不正。
        // 例: 5339_85_47 → p2=8 (NG)
        Assert.Throws<ArgumentException>(() => MeshCodeConverter.ToBoundingBox(new MeshCode(53398547L)));
        // 5339_48_47 → u2=8 (NG)
        Assert.Throws<ArgumentException>(() => MeshCodeConverter.ToBoundingBox(new MeshCode(53394847L)));
    }

    [Fact]
    public void ToBoundingBox_8DigitCode_53394547_MatchesSpecExample()
    {
        // 要件定義 REQ-RST-016 の例 53394547 の境界
        // p1=53, u1=39, p2=4, u2=5, p3=4, u3=7
        // SW lat = 53/1.5 + 4*(5/60) + 4*(30/3600) = 35.3333 + 0.3333 + 0.0333 = 35.7000
        // SW lon = 139 + 5*(7.5/60) + 7*(45/3600) = 139 + 0.625 + 0.0875 = 139.7125
        var aabb = MeshCodeConverter.ToBoundingBox(new MeshCode(53394547L));
        Assert.Equal(35.7000, aabb.SouthWest.Latitude, precision: 6);
        Assert.Equal(139.7125, aabb.SouthWest.Longitude, precision: 6);
        Assert.Equal(35.7000 + Lat3StepDeg, aabb.NorthEast.Latitude, precision: 6);
        Assert.Equal(139.7125 + Lon3StepDeg, aabb.NorthEast.Longitude, precision: 6);
    }

    [Fact]
    public void ToBounds_PublicApi_MatchesInternalConverter()
    {
        var mesh = new MeshCode(53394611L);
        var bounds = mesh.ToBounds();
        var aabb = MeshCodeConverter.ToBoundingBox(mesh);
        Assert.Equal(aabb.SouthWest, bounds.SouthWest);
        Assert.Equal(aabb.NorthEast, bounds.NorthEast);
    }

    [Fact]
    public void EnumerateInBounds_Mesh3rd_SingleCell_YieldsThatCell()
    {
        // 53394611 の AABB そのものを範囲に渡すと、そのメッシュ 1 個だけ列挙される
        var target = new MeshCode(53394611L);
        var bounds = target.ToBounds();
        var codes = MeshCode.EnumerateInBounds(bounds, MeshLevel.Mesh3rd).ToArray();
        Assert.Single(codes);
        Assert.Equal(target.Value, codes[0].Value);
    }

    [Fact]
    public void EnumerateInBounds_Mesh3rd_TwoByTwoArea_Yields4Cells()
    {
        // 53394611 と東/北/北東の隣接 3 メッシュ計 4 個を含む範囲
        var sw = new MeshCode(53394611L).ToBounds().SouthWest;
        var bounds = new MapBounds(
            sw,
            new GeoCoordinate(sw.Latitude + Lat3StepDeg * 1.9, sw.Longitude + Lon3StepDeg * 1.9));
        var codes = MeshCode.EnumerateInBounds(bounds, MeshLevel.Mesh3rd).ToArray();
        Assert.Equal(4, codes.Length);
    }

    [Fact]
    public void EnumerateInBounds_HalfMesh_Of_1km_Yields4SubCells()
    {
        // 1km メッシュ 1 個分の範囲を 1/2 細分で列挙すると 4 個
        var bounds = new MeshCode(53394611L).ToBounds();
        var codes = MeshCode.EnumerateInBounds(bounds, MeshLevel.HalfMesh).ToArray();
        Assert.Equal(4, codes.Length);
        var values = codes.Select(c => c.Value).ToHashSet();
        Assert.Contains(533946111L, values);
        Assert.Contains(533946112L, values);
        Assert.Contains(533946113L, values);
        Assert.Contains(533946114L, values);
    }

    [Fact]
    public void EnumerateInBounds_QuarterMesh_Of_1km_Yields16SubCells()
    {
        var bounds = new MeshCode(53394611L).ToBounds();
        var codes = MeshCode.EnumerateInBounds(bounds, MeshLevel.QuarterMesh).ToArray();
        Assert.Equal(16, codes.Length);
    }

    [Fact]
    public void EnumerateInBounds_EnumeratedCells_AreInsideTheirComputedBounds()
    {
        // 列挙された各メッシュ ID を ToBounds() で逆算すると、元の bounds と交差すること
        var bounds = new MapBounds(
            new GeoCoordinate(35.65, 139.74),
            new GeoCoordinate(35.70, 139.80));
        var codes = MeshCode.EnumerateInBounds(bounds, MeshLevel.Mesh3rd).ToArray();
        Assert.NotEmpty(codes);
        foreach (var c in codes)
        {
            var cellBounds = c.ToBounds();
            // 中心が範囲内にあれば交差している
            var midLat = (cellBounds.SouthWest.Latitude + cellBounds.NorthEast.Latitude) / 2;
            var midLon = (cellBounds.SouthWest.Longitude + cellBounds.NorthEast.Longitude) / 2;
            Assert.True(midLat >= bounds.MinLatitude - Lat3StepDeg && midLat <= bounds.MaxLatitude + Lat3StepDeg);
            Assert.True(midLon >= bounds.MinLongitude - Lon3StepDeg && midLon <= bounds.MaxLongitude + Lon3StepDeg);
        }
    }

    [Fact]
    public void ToBoundingBox_AllFourHalfQuadrants_TileWithoutGap()
    {
        // sub=1〜4 の 4 つの 1/2 細分が 親メッシュを隙間なく分割（タイル化）することを確認
        var parent = MeshCodeConverter.ToBoundingBox(new MeshCode(53394611L));
        var sw = MeshCodeConverter.ToBoundingBox(new MeshCode(533946111L));
        var se = MeshCodeConverter.ToBoundingBox(new MeshCode(533946112L));
        var nw = MeshCodeConverter.ToBoundingBox(new MeshCode(533946113L));
        var ne = MeshCodeConverter.ToBoundingBox(new MeshCode(533946114L));

        // 全 4 サブメッシュの SW 集合に親の SW が含まれ、全 NE 集合に親の NE が含まれる
        Assert.Equal(parent.SouthWest.Latitude, sw.SouthWest.Latitude, precision: 6);
        Assert.Equal(parent.SouthWest.Longitude, sw.SouthWest.Longitude, precision: 6);
        Assert.Equal(parent.NorthEast.Latitude, ne.NorthEast.Latitude, precision: 6);
        Assert.Equal(parent.NorthEast.Longitude, ne.NorthEast.Longitude, precision: 6);

        // 南西と南東は緯度同じ、南東 SW.Lon = 南西 NE.Lon
        Assert.Equal(sw.SouthWest.Latitude, se.SouthWest.Latitude, precision: 6);
        Assert.Equal(sw.NorthEast.Longitude, se.SouthWest.Longitude, precision: 6);
        // 北西は南西の真北
        Assert.Equal(sw.NorthEast.Latitude, nw.SouthWest.Latitude, precision: 6);
        Assert.Equal(sw.SouthWest.Longitude, nw.SouthWest.Longitude, precision: 6);
    }

    // ---- 1/8 細分メッシュ（11 桁、125m、REQ-RST-016 仕様確定） ----

    [Fact]
    public void ToBoundingBox_EighthMesh_SwQuadrant_SharesParentQuarterSouthwest()
    {
        // 53394611111 = 1/4 細分 5339461111 の南西象限（sub=1）。SW は親と厳密一致（境界共有）
        var parent = MeshCodeConverter.ToBoundingBox(new MeshCode(5339461111L));
        var eighth = MeshCodeConverter.ToBoundingBox(new MeshCode(53394611111L));

        Assert.Equal(parent.SouthWest.Latitude, eighth.SouthWest.Latitude, precision: 12);
        Assert.Equal(parent.SouthWest.Longitude, eighth.SouthWest.Longitude, precision: 12);
        // 寸法は親の半分（緯度 3.75 秒、経度 5.625 秒）
        Assert.Equal(Lat3StepDeg / 8, eighth.NorthEast.Latitude - eighth.SouthWest.Latitude, precision: 12);
        Assert.Equal(Lon3StepDeg / 8, eighth.NorthEast.Longitude - eighth.SouthWest.Longitude, precision: 12);
    }

    [Fact]
    public void ToBoundingBox_EighthMesh_NeQuadrant_SharesParentQuarterNortheast()
    {
        // 53394611114 = 1/4 細分 5339461111 の北東象限（sub=4）。NE は親と厳密一致（境界共有）
        var parent = MeshCodeConverter.ToBoundingBox(new MeshCode(5339461111L));
        var eighth = MeshCodeConverter.ToBoundingBox(new MeshCode(53394611114L));

        Assert.Equal(parent.NorthEast.Latitude, eighth.NorthEast.Latitude, precision: 12);
        Assert.Equal(parent.NorthEast.Longitude, eighth.NorthEast.Longitude, precision: 12);
    }

    [Fact]
    public void ToBoundingBox_EighthMesh_SizeIsApprox125Meters()
    {
        var aabb = MeshCodeConverter.ToBoundingBox(new MeshCode(53394611111L));
        var latMeters = (aabb.NorthEast.Latitude - aabb.SouthWest.Latitude) * 111320;
        Assert.InRange(latMeters, 110, 130);    // 約 115.7m（3.75秒 × 111.32km/度）
    }

    [Fact]
    public void ToBoundingBox_EighthMesh_InvalidSubdivisionDigit_Throws()
    {
        // 11 桁目が 0 や 5 など範囲外（既存細分桁と同じ ArgumentException）
        Assert.Throws<ArgumentException>(() => MeshCodeConverter.ToBoundingBox(new MeshCode(53394611110L)));
        Assert.Throws<ArgumentException>(() => MeshCodeConverter.ToBoundingBox(new MeshCode(53394611115L)));
    }

    [Fact]
    public void ToBounds_PublicApi_EighthMesh_MatchesInternalConverter()
    {
        var mesh = new MeshCode(53394611111L);
        var bounds = mesh.ToBounds();
        var aabb = MeshCodeConverter.ToBoundingBox(mesh);
        Assert.Equal(aabb.SouthWest, bounds.SouthWest);
        Assert.Equal(aabb.NorthEast, bounds.NorthEast);
    }

    [Fact]
    public void EnumerateInBounds_EighthMesh_Of_1km_Yields64SubCells()
    {
        // 第3次メッシュ 1 個の範囲は 1/8 細分 64 個（= 1/4 細分 16 個の縦横 2 倍）
        var bounds = new MeshCode(53394611L).ToBounds();
        var codes = MeshCode.EnumerateInBounds(bounds, MeshLevel.EighthMesh).ToArray();
        Assert.Equal(64, codes.Length);
        Assert.Equal(64, codes.Select(c => c.Value).Distinct().Count());
    }

    [Fact]
    public void ToBoundingBox_All64EighthMeshes_TileParentWithoutGapOrOverlap()
    {
        // 同一 3次メッシュ内の 64 個の 1/8 メッシュが、8×8 格子の全位置をちょうど 1 回ずつ占める
        var parent = MeshCodeConverter.ToBoundingBox(new MeshCode(53394611L));
        var latStep = Lat3StepDeg / 8;
        var lonStep = Lon3StepDeg / 8;

        var codes = MeshCode.EnumerateInBounds(
            new MeshCode(53394611L).ToBounds(), MeshLevel.EighthMesh).ToArray();
        Assert.Equal(64, codes.Length);

        var occupied = new HashSet<(int Row, int Col)>();
        foreach (var code in codes)
        {
            var cell = MeshCodeConverter.ToBoundingBox(code);
            // セル寸法が均一
            Assert.Equal(latStep, cell.NorthEast.Latitude - cell.SouthWest.Latitude, precision: 12);
            Assert.Equal(lonStep, cell.NorthEast.Longitude - cell.SouthWest.Longitude, precision: 12);
            // SW が親メッシュ内の 8×8 格子点に一致
            var row = (int)Math.Round((cell.SouthWest.Latitude - parent.SouthWest.Latitude) / latStep);
            var col = (int)Math.Round((cell.SouthWest.Longitude - parent.SouthWest.Longitude) / lonStep);
            Assert.InRange(row, 0, 7);
            Assert.InRange(col, 0, 7);
            Assert.Equal(cell.SouthWest.Latitude, parent.SouthWest.Latitude + row * latStep, precision: 12);
            Assert.Equal(cell.SouthWest.Longitude, parent.SouthWest.Longitude + col * lonStep, precision: 12);
            Assert.True(occupied.Add((row, col)), $"格子位置 ({row},{col}) が重複（code={code}）");
        }
        Assert.Equal(64, occupied.Count);
    }

    [Fact]
    public void EnumerateInBounds_AllFourLevels_CountsAreConsistent()
    {
        // 同一 bounds（1km メッシュ 1 個）で 1 / 4 / 16 / 64 個
        var bounds = new MeshCode(53394611L).ToBounds();
        Assert.Single(MeshCode.EnumerateInBounds(bounds, MeshLevel.Mesh3rd));
        Assert.Equal(4, MeshCode.EnumerateInBounds(bounds, MeshLevel.HalfMesh).Count());
        Assert.Equal(16, MeshCode.EnumerateInBounds(bounds, MeshLevel.QuarterMesh).Count());
        Assert.Equal(64, MeshCode.EnumerateInBounds(bounds, MeshLevel.EighthMesh).Count());
    }
}
