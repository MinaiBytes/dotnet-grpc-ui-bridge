namespace Models.Core.Communication.gRPC;

/// <summary>
/// gRPC 接続に関する設定を表します。
/// </summary>
public sealed class GrpcConnectionOptions
{
    /// <summary>
    /// 接続先ホスト名または IP アドレスです。
    /// </summary>
    public string Host { get; init; } = "127.0.0.1";

    /// <summary>
    /// 接続先ポート番号です。
    /// </summary>
    public int Port { get; init; } = 50051;

    /// <summary>
    /// TLS を使用するかどうかを表します。
    /// </summary>
    public bool UseTls { get; init; } = true;

    /// <summary>
    /// 接続先 URI を直接指定する場合に使用します。
    /// </summary>
    public Uri? Endpoint { get; init; }

    /// <summary>
    /// 呼び出しに適用する既定のデッドラインです。
    /// </summary>
    public TimeSpan DefaultDeadline { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 接続確立時のタイムアウトです。
    /// </summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Keep-Alive Ping 送信間隔です。
    /// </summary>
    public TimeSpan KeepAlivePingDelay { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Keep-Alive Ping 応答待ち時間です。
    /// </summary>
    public TimeSpan KeepAlivePingTimeout { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 接続プール内のアイドル接続破棄までの時間です。
    /// </summary>
    public TimeSpan PooledConnectionIdleTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 受信メッセージサイズ上限を指定します。
    /// </summary>
    public int? MaxReceiveMessageSize { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// 送信メッセージサイズ上限を指定します。
    /// </summary>
    public int? MaxSendMessageSize { get; init; } = 4 * 1024 * 1024;

    /// <summary>
    /// 設定値から接続先 URI を解決します。
    /// </summary>
    /// <returns>解決された接続先 URI。</returns>
    public Uri ResolveEndpoint()
    {
        if (Endpoint is not null)
        {
            return Endpoint;
        }

        var scheme = UseTls ? "https" : "http";
        return new Uri($"{scheme}://{Host}:{Port}");
    }
}
