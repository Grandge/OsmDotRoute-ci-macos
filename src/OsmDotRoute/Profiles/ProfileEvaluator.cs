using System.Globalization;

namespace OsmDotRoute.Profiles;

/// <summary>
/// プロファイル JSON の評価器（REQ-PRF-001〜002, REQ-PRF-007〜014）。
/// OSM タグ集合からエッジ評価 (canPass, speed, oneway) を導出する。
/// 難所タイプから難所評価 (speedFactor, canPass) を導出する。
/// </summary>
internal sealed class ProfileEvaluator
{
    private readonly JsonProfileDefinition _def;
    private readonly JsonDifficultyRule _difficultyDefault;
    private readonly double _fallbackSpeedKmh;
    private readonly bool _fallbackAccessAllow;
    private readonly double _minKmh;
    private readonly double _maxKmh;
    private readonly string _maxspeedTagKey;
    private readonly bool _maxspeedDefaultMph;
    private readonly double _speedMultiplier;
    private readonly JsonVehicleLimits? _vehicleLimits;

    /// <summary>
    /// 難所タイプ → ルールの case-insensitive 照合用ルックアップ（v1.1.1、親プロFB 不具合修正、REQ-PRF-014 改訂）。
    /// JSON デシリアライズ既定の case-sensitive 比較では <c>"Flooding"</c> 等の表記揺れで
    /// サイレントに <c>difficultyDefault</c> へ落ちる問題があったため、Ordinal-IgnoreCase で正規化する。
    /// </summary>
    private readonly Dictionary<string, JsonDifficultyRule> _difficultyLookup;

    /// <summary>
    /// プロファイル定義から評価器を構築する。検証は <see cref="ValidateAndCompile"/> で行う。
    /// </summary>
    /// <exception cref="InvalidProfileException">プロファイル定義が不正</exception>
    public ProfileEvaluator(JsonProfileDefinition def)
    {
        ArgumentNullException.ThrowIfNull(def);
        ValidateAndCompile(def);

        _def = def;
        _difficultyDefault = def.DifficultyDefault!;
        _fallbackSpeedKmh = def.Fallback!.SpeedKmh;
        _fallbackAccessAllow = NormalizeAccessLiteral(def.Fallback.Access);
        _minKmh = def.SpeedBounds!.MinKmh;
        _maxKmh = def.SpeedBounds.MaxKmh;
        _maxspeedTagKey = def.MaxspeedTagKey ?? "maxspeed";
        _maxspeedDefaultMph = string.Equals(def.MaxspeedUnitDefault, "mph", StringComparison.OrdinalIgnoreCase);
        _speedMultiplier = def.SpeedMultiplier ?? 1.0;
        _vehicleLimits = def.VehicleLimits;

        _difficultyLookup = def.Difficulty is { } diff
            ? new Dictionary<string, JsonDifficultyRule>(diff, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, JsonDifficultyRule>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// プロファイル JSON の <c>name</c> フィールド。<c>NativeRoadGraph</c> が
    /// <c>.odrg</c> の BAKED_PROFILE スロット解決に使う（Phase 3 ステップ 3A.3b）。
    /// </summary>
    public string Name => _def.Name
        ?? throw new InvalidOperationException(
            "ProfileEvaluator: JSON プロファイルに name フィールドがありません。");

    /// <summary>
    /// OSM タグ集合からエッジ評価を導出する。
    /// 評価順: highway 別ルール（access/speed 基本値） → アクセスタグ上書き → maxspeed タグ → 速度クランプ → oneway。
    /// </summary>
    public EdgeEvaluation Evaluate(IReadOnlyDictionary<string, string> osmTags)
    {
        ArgumentNullException.ThrowIfNull(osmTags);

        // 1. highway 値を取得し、対応ルール参照。無ければ fallback。
        // 「highway 別ルールが access:"no" を明示している場合」は hard-deny とし、
        // アクセスタグでの上書きを許可しない（Itinero car.lua / pedestrian.lua のセマンティクスに合わせる）。
        // 例: car プロファイルにとって highway=footway は motor_vehicle=yes 等の上書きを許さない。
        bool accessAllow;
        bool isHardDeny;
        double speedKmh;

        if (osmTags.TryGetValue("highway", out var highwayValue)
            && _def.Highway!.TryGetValue(highwayValue, out var highwayRule))
        {
            var hwyAccessRaw = highwayRule.Access;
            if (string.Equals(hwyAccessRaw, "no", StringComparison.OrdinalIgnoreCase))
            {
                accessAllow = false;
                isHardDeny = true;
            }
            else
            {
                accessAllow = true; // null または "yes"
                isHardDeny = false;
            }
            speedKmh = highwayRule.SpeedKmh ?? _fallbackSpeedKmh;
        }
        else
        {
            accessAllow = _fallbackAccessAllow;
            isHardDeny = false; // 未知 highway は access タグでの上書き可
            speedKmh = _fallbackSpeedKmh;
        }

        // 2. アクセスタグで上書き（hard-deny 時は適用しない）
        if (!isHardDeny && _def.AccessTagKeys is { } accessKeys)
        {
            foreach (var key in accessKeys)
            {
                if (osmTags.TryGetValue(key, out var accessValue)
                    && _def.AccessValueMap!.TryGetValue(accessValue, out var decision))
                {
                    accessAllow = string.Equals(decision, "allow", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        // 2.5. vehicleLimits 評価（Phase 3 ステップ 3D.2、Truck 用 maxweight/maxheight/maxwidth）。
        // accessTagKeys 後に評価することで「access=destination → 許可」を物理制限で再度ブロックできる
        // ＝物理制限は法令タグ上書きより優先（hard-deny 等価セマンティクス）。
        if (accessAllow && _vehicleLimits is not null && ExceedsVehicleLimit(osmTags))
        {
            accessAllow = false;
        }

        // 3. 通行不可なら早期 return
        if (!accessAllow)
        {
            return new EdgeEvaluation(false, 0f, OnewayDirection.Bidirectional);
        }

        // 4. maxspeed タグでさらに上書き
        if (osmTags.TryGetValue(_maxspeedTagKey, out var maxspeedRaw)
            && TryParseMaxspeed(maxspeedRaw, _maxspeedDefaultMph, out var parsedKmh))
        {
            speedKmh = parsedKmh;
        }

        // 5. speedMultiplier を適用（Itinero Fastest 相当: 0.75）
        speedKmh *= _speedMultiplier;

        // 6. 速度クランプ
        if (speedKmh < _minKmh) speedKmh = _minKmh;
        if (speedKmh > _maxKmh) speedKmh = _maxKmh;

        // 7. oneway
        var oneway = _def.IgnoreOneway
            ? OnewayDirection.Bidirectional
            : ParseOneway(osmTags);

        return new EdgeEvaluation(true, (float)speedKmh, oneway);
    }

    /// <summary>
    /// 難所タイプ評価。プロファイルに該当タイプ定義があればそれを、無ければ <c>difficultyDefault</c> を返す（REQ-PRF-014）。
    /// </summary>
    /// <remarks>
    /// 照合は **case-insensitive**（Ordinal-IgnoreCase、v1.1.1〜）。
    /// 例: 正準キー <c>"flooding"</c> を持つプロファイルに対し、<c>"Flooding"</c> / <c>"FLOODING"</c> 等の表記揺れでも一致する。
    /// v1.1.0 までは case-sensitive で、表記不一致がサイレントに <c>difficultyDefault</c> へ落ちる不具合があった（親プロFB 起因）。
    /// </remarks>
    public DifficultyEvaluation EvaluateDifficulty(string difficultyType)
    {
        if (string.IsNullOrWhiteSpace(difficultyType))
        {
            return new DifficultyEvaluation((float)_difficultyDefault.SpeedFactor, _difficultyDefault.CanPass);
        }

        if (_difficultyLookup.TryGetValue(difficultyType, out var rule))
        {
            return new DifficultyEvaluation((float)rule.SpeedFactor, rule.CanPass);
        }

        return new DifficultyEvaluation((float)_difficultyDefault.SpeedFactor, _difficultyDefault.CanPass);
    }

    /// <summary>
    /// このプロファイルが <c>difficulty</c> セクションで定義する難所タイプキー集合（v1.1.1、観測性 API）。
    /// </summary>
    /// <remarks>
    /// 正準キーは小文字（<see cref="OsmDotRoute.DifficultyTypes"/> 参照）だが、本コレクションは JSON 定義のままの表記で返す。
    /// 利用者は登録予定の難所タイプキーがこのコレクションに含まれるかを起動時等で確認することで、
    /// タイプミス由来のサイレント・フォールバックを早期検知できる。照合自体は <see cref="EvaluateDifficulty"/> で
    /// case-insensitive に行われるため、本コレクションも <see cref="HasDifficulty"/> 経由での照合を推奨する。
    /// </remarks>
    public IReadOnlyCollection<string> KnownDifficultyTypes => _difficultyLookup.Keys;

    /// <summary>
    /// 指定した難所タイプがこのプロファイルの <c>difficulty</c> セクションで明示的に定義されているかを返す（v1.1.1、観測性 API）。
    /// </summary>
    /// <remarks>
    /// 照合は case-insensitive。<c>false</c> のとき、その難所タイプを <see cref="EvaluateDifficulty"/> に渡すと
    /// <c>difficultyDefault</c>（既定 <c>speedFactor=1.0, canPass=true</c>）にフォールバックする（REQ-PRF-014）。
    /// 利用者が「速度低下が見込める難所タイプか」を事前確認したい場合のフックとして提供する。
    /// </remarks>
    /// <param name="difficultyType">難所タイプ文字列。<c>null</c> / 空 / 空白のみは <c>false</c> を返す</param>
    public bool HasDifficulty(string difficultyType)
        => !string.IsNullOrWhiteSpace(difficultyType) && _difficultyLookup.ContainsKey(difficultyType);

    private static OnewayDirection ParseOneway(IReadOnlyDictionary<string, string> osmTags)
    {
        if (!osmTags.TryGetValue("oneway", out var v))
        {
            return OnewayDirection.Bidirectional;
        }

        return v switch
        {
            "yes" or "true" or "1" => OnewayDirection.Forward,
            "-1" or "reverse" => OnewayDirection.Backward,
            _ => OnewayDirection.Bidirectional,
        };
    }

    /// <summary>
    /// vehicleLimits 評価。OSM タグ maxweight (t) / maxheight (m) / maxwidth (m) のいずれかが
    /// プロファイル制限を下回るなら true（=このエッジは通行不可）。
    /// 未対応単位（kg, ft, in 等）のタグ値は安全側として制限発火させない（=false）。
    /// </summary>
    private bool ExceedsVehicleLimit(IReadOnlyDictionary<string, string> osmTags)
    {
        if (_vehicleLimits!.MaxWeightTon is { } weightLimit
            && osmTags.TryGetValue("maxweight", out var weightRaw)
            && TryParseLimitValue(weightRaw, "t", out var weight)
            && weight < weightLimit)
        {
            return true;
        }

        if (_vehicleLimits.MaxHeightMeter is { } heightLimit
            && osmTags.TryGetValue("maxheight", out var heightRaw)
            && TryParseLimitValue(heightRaw, "m", out var height)
            && height < heightLimit)
        {
            return true;
        }

        if (_vehicleLimits.MaxWidthMeter is { } widthLimit
            && osmTags.TryGetValue("maxwidth", out var widthRaw)
            && TryParseLimitValue(widthRaw, "m", out var width)
            && width < widthLimit)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// OSM タグの数値制限値をパース。例: "8" / "8 t" / "3.5 m" / "8t" → 数値部のみ返す。
    /// 単位サフィックスが既定値（"t" or "m"）と一致する場合のみ受け入れ、不一致（"kg" / "ft" 等）は
    /// 安全側として false を返す（=制限発火しない）。
    /// </summary>
    private static bool TryParseLimitValue(string raw, string defaultUnit, out double value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        // 空白で数値部と単位部を分離（"8 t" / "3.5 m"）。空白なし（"8t" / "3.5m" / "8"）はループで判定。
        ReadOnlySpan<char> span = raw.AsSpan().Trim();
        int numLen = 0;
        while (numLen < span.Length && (char.IsDigit(span[numLen]) || span[numLen] == '.'))
        {
            numLen++;
        }
        if (numLen == 0) return false;

        if (!double.TryParse(span[..numLen], NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return false;
        }

        // 残り部分が単位サフィックス。空白除去してから比較。
        var unitPart = span[numLen..].Trim();
        if (unitPart.IsEmpty)
        {
            return true; // 単位省略 = 既定単位
        }

        if (unitPart.Equals(defaultUnit, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // 想定外単位（kg / ft / in / lb 等）は未対応として制限発火させない
        value = 0;
        return false;
    }

    private static bool TryParseMaxspeed(string raw, bool defaultMph, out double speedKmh)
    {
        speedKmh = 0;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        // 例: "50", "50 mph", "30 kmh", "walk", "signals"
        var parts = raw.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        if (!double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return false; // "walk" / "signals" 等は対応せず、highway デフォルトを使う
        }

        bool isMph;
        if (parts.Length == 2)
        {
            isMph = parts[1].Equals("mph", StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            isMph = defaultMph;
        }

        speedKmh = isMph ? value * 1.609344 : value;
        return true;
    }

    private static bool NormalizeAccessLiteral(string? access)
    {
        // null / "yes" → 許可、"no" → 拒否
        if (string.IsNullOrWhiteSpace(access)) return true;
        return access.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    private static void ValidateAndCompile(JsonProfileDefinition def)
    {
        if (string.IsNullOrWhiteSpace(def.Name))
        {
            throw new InvalidProfileException("プロファイル定義に 'name' が必要です。");
        }

        if (def.Highway is null || def.Highway.Count == 0)
        {
            throw new InvalidProfileException($"プロファイル '{def.Name}' に 'highway' ルールが必要です。");
        }

        if (def.AccessValueMap is null)
        {
            throw new InvalidProfileException($"プロファイル '{def.Name}' に 'accessValueMap' が必要です。");
        }

        if (def.Fallback is null)
        {
            throw new InvalidProfileException($"プロファイル '{def.Name}' に 'fallback' が必要です。");
        }

        if (def.SpeedBounds is null)
        {
            throw new InvalidProfileException($"プロファイル '{def.Name}' に 'speedBounds' が必要です。");
        }

        if (def.SpeedBounds.MinKmh < 0 || def.SpeedBounds.MaxKmh <= def.SpeedBounds.MinKmh)
        {
            throw new InvalidProfileException(
                $"プロファイル '{def.Name}' の 'speedBounds' が不正です (min={def.SpeedBounds.MinKmh}, max={def.SpeedBounds.MaxKmh})。");
        }

        if (def.DifficultyDefault is null)
        {
            throw new InvalidProfileException($"プロファイル '{def.Name}' に 'difficultyDefault' が必要です。");
        }

        if (def.DifficultyDefault.SpeedFactor < 0 || def.DifficultyDefault.SpeedFactor > 1)
        {
            throw new InvalidProfileException(
                $"プロファイル '{def.Name}' の 'difficultyDefault.speedFactor' は 0.0〜1.0 の範囲が必要です。");
        }

        if (def.Difficulty is { } diff)
        {
            // 範囲チェック
            foreach (var (key, rule) in diff)
            {
                if (rule.SpeedFactor < 0 || rule.SpeedFactor > 1)
                {
                    throw new InvalidProfileException(
                        $"プロファイル '{def.Name}' の 'difficulty[{key}].speedFactor' は 0.0〜1.0 の範囲が必要です（実値: {rule.SpeedFactor}）。");
                }
            }

            // case-only 重複キー検出（v1.1.1、照合 case-insensitive 化に伴う一意性担保）
            var caseInsensitiveSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in diff.Keys)
            {
                if (!caseInsensitiveSeen.Add(key))
                {
                    throw new InvalidProfileException(
                        $"プロファイル '{def.Name}' の 'difficulty' に case 違いで重複するキーがあります（'{key}'）。"
                        + "難所タイプ照合は case-insensitive のため、正準小文字キーに統一してください。");
                }
            }
        }

        if (def.VehicleLimits is { } limits)
        {
            if (limits.MaxWeightTon is < 0)
            {
                throw new InvalidProfileException(
                    $"プロファイル '{def.Name}' の 'vehicleLimits.maxWeightTon' は 0 以上が必要です（実値: {limits.MaxWeightTon}）。");
            }
            if (limits.MaxHeightMeter is < 0)
            {
                throw new InvalidProfileException(
                    $"プロファイル '{def.Name}' の 'vehicleLimits.maxHeightMeter' は 0 以上が必要です（実値: {limits.MaxHeightMeter}）。");
            }
            if (limits.MaxWidthMeter is < 0)
            {
                throw new InvalidProfileException(
                    $"プロファイル '{def.Name}' の 'vehicleLimits.maxWidthMeter' は 0 以上が必要です（実値: {limits.MaxWidthMeter}）。");
            }
        }
    }
}
