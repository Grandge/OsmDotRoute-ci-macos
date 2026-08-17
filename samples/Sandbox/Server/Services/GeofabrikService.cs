namespace Sandbox.Server.Services;

public sealed class GeofabrikService
{
    private readonly HttpClient _http;

    public GeofabrikService(HttpClient http)
    {
        _http = http;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("OsmDotRoute.Sandbox/1.0");
    }

    public static readonly RegionInfo[] Regions =
    [
        new("hokkaido", "北海道", "asia/japan/hokkaido", "北海道"),
        new("tohoku", "東北", "asia/japan/tohoku", "青森・岩手・宮城・秋田・山形・福島"),
        new("kanto", "関東", "asia/japan/kanto", "茨城・栃木・群馬・埼玉・千葉・東京・神奈川"),
        new("chubu", "中部", "asia/japan/chubu", "新潟・富山・石川・福井・山梨・長野・岐阜・静岡・愛知"),
        new("kansai", "近畿", "asia/japan/kansai", "三重・滋賀・京都・大阪・兵庫・奈良・和歌山"),
        new("chugoku", "中国", "asia/japan/chugoku", "鳥取・島根・岡山・広島・山口"),
        new("shikoku", "四国", "asia/japan/shikoku", "徳島・香川・愛媛・高知"),
        new("kyushu", "九州", "asia/japan/kyushu", "福岡・佐賀・長崎・熊本・大分・宮崎・鹿児島・沖縄"),
        new("japan", "日本全国", "asia/japan", "約 1.8 GB"),
    ];

    public static RegionInfo? FindRegion(string key) =>
        Array.Find(Regions, r => r.Key == key);

    public string GetDownloadUrl(RegionInfo region) =>
        $"https://download.geofabrik.de/{region.GeofabrikPath}-latest.osm.pbf";

    public async Task<HttpResponseMessage> SendDownloadRequestAsync(string url, CancellationToken ct)
    {
        var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }
}

public sealed record RegionInfo(string Key, string DisplayName, string GeofabrikPath, string Description);
