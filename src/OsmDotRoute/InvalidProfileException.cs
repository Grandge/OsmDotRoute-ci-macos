namespace OsmDotRoute;

/// <summary>
/// プロファイル JSON の検証に失敗したことを示す例外（REQ-PRF-007〜010）。
/// 不正なスキーマ、必須フィールドの欠落、範囲外の値などで発生する。
/// </summary>
public sealed class InvalidProfileException : Exception
{
    /// <summary>メッセージを指定して例外を作成する。</summary>
    /// <param name="message">例外メッセージ</param>
    public InvalidProfileException(string message) : base(message)
    {
    }

    /// <summary>メッセージと内部例外を指定して例外を作成する。</summary>
    /// <param name="message">例外メッセージ</param>
    /// <param name="innerException">原因となった内部例外</param>
    public InvalidProfileException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
