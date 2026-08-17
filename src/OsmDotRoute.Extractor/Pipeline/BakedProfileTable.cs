using System;
using System.Collections.Generic;

namespace OsmDotRoute.Extractor.Pipeline;

/// <summary>
/// .odrg 仕様書 §4.7 Baked Profile Table。プロファイル × エッジ の bake 結果を保持する。
/// </summary>
/// <remarks>
/// <para>
/// 内部レイアウトはプロファイル major（仕様書 §4.7.3「プロファイル単位でブロック化、各エッジ ID 順」）。
/// 3.8 の .odrg 書出はこのレイアウトをそのままバイナリにダンプする。
/// </para>
/// <para>
/// <c>bakedProfileIndex == edgeId</c> （仕様書 §4.7.5 v0.2 確定）のため、本テーブルは
/// edgeId 順にエッジ数と同じ長さの配列をプロファイル数ぶん持つ。
/// </para>
/// </remarks>
internal sealed class BakedProfileTable
{
    private readonly BakedProfileEntry[][] _byProfile;
    private readonly string[] _profileNames;

    internal BakedProfileTable(string[] profileNames, BakedProfileEntry[][] byProfile)
    {
        ArgumentNullException.ThrowIfNull(profileNames);
        ArgumentNullException.ThrowIfNull(byProfile);
        if (profileNames.Length != byProfile.Length)
            throw new ArgumentException(
                $"profileNames.Length ({profileNames.Length}) != byProfile.Length ({byProfile.Length})");

        _profileNames = profileNames;
        _byProfile = byProfile;
    }

    /// <summary>プロファイル数。</summary>
    public int ProfileCount => _profileNames.Length;

    /// <summary>エッジ数（プロファイル間で同じ）。</summary>
    public int EdgeCount => _byProfile.Length > 0 ? _byProfile[0].Length : 0;

    /// <summary>プロファイル名（採番順）。</summary>
    public IReadOnlyList<string> ProfileNames => _profileNames;

    /// <summary>指定プロファイル ID の全エッジ bake 結果を返す（zero-copy）。</summary>
    public ReadOnlySpan<BakedProfileEntry> GetProfileEntries(int profileIndex) =>
        _byProfile[profileIndex];

    /// <summary>指定 (profileIndex, edgeId) の bake 結果を返す。</summary>
    public BakedProfileEntry Get(int profileIndex, int edgeId) =>
        _byProfile[profileIndex][edgeId];
}
