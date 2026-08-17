namespace OsmDotRoute.Internal.Odrg;

/// <summary>
/// `.odrg` バイナリ形式の検証失敗を表す例外（Phase 3 ステップ 3A.1）。
/// </summary>
/// <remarks>
/// マジック不一致 / バージョン非対応 / セクションテーブル不整合 / オフセット越境 等、
/// <see cref="OdrgSectionDirectory"/> 解析時の構造的破損を一律に通知する。
/// </remarks>
internal sealed class OdrgFormatException : Exception
{
    public OdrgFormatException(string message) : base(message)
    {
    }

    public OdrgFormatException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
