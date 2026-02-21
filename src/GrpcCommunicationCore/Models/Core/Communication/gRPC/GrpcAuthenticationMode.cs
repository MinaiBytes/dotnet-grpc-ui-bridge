namespace Models.Core.Communication.gRPC;

/// <summary>
/// gRPC 呼び出し時の認証方式を表します。
/// </summary>
public enum GrpcAuthenticationMode
{
    /// <summary>
    /// 認証を使用しません。
    /// </summary>
    None = 0,

    /// <summary>
    /// Bearer トークン認証を使用します。
    /// </summary>
    BearerToken = 1,

    /// <summary>
    /// API キー認証を使用します。
    /// </summary>
    ApiKey = 2,

    /// <summary>
    /// 相互 TLS 認証を使用します。
    /// </summary>
    MutualTls = 3
}
