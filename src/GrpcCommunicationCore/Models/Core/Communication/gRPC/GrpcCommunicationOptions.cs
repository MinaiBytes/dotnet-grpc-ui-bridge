namespace Models.Core.Communication.gRPC;

/// <summary>
/// gRPC 通信コア全体の設定をまとめたオプションです。
/// </summary>
public sealed class GrpcCommunicationOptions
{
    /// <summary>
    /// 接続設定です。
    /// </summary>
    public GrpcConnectionOptions Connection { get; init; } = new();

    /// <summary>
    /// 認証設定です。
    /// </summary>
    public GrpcAuthenticationOptions Authentication { get; init; } = new();
}
