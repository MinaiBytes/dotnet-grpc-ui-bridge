namespace Models.Core.Communication.gRPC;

/// <summary>
/// gRPC 認証に関する設定を表します。
/// </summary>
public sealed class GrpcAuthenticationOptions
{
    /// <summary>
    /// 使用する認証方式です。
    /// </summary>
    public GrpcAuthenticationMode Mode { get; init; } = GrpcAuthenticationMode.None;

    /// <summary>
    /// 固定 Bearer トークンです。
    /// </summary>
    public string? BearerToken { get; init; }

    /// <summary>
    /// API キー認証で使用するヘッダー名です。
    /// </summary>
    public string ApiKeyHeaderName { get; init; } = "x-api-key";

    /// <summary>
    /// API キー認証で使用するキー値です。
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// mTLS で使用するクライアント証明書のパスです。
    /// </summary>
    public string? ClientCertificatePath { get; init; }

    /// <summary>
    /// クライアント証明書のパスワードです。
    /// </summary>
    public string? ClientCertificatePassword { get; init; }

    /// <summary>
    /// サーバー証明書検証を緩和するかどうかを表します。
    /// </summary>
    public bool AllowInsecureServerCertificate { get; init; }
}
