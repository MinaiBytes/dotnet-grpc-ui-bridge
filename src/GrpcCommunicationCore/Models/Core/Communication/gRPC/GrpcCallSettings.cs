using Grpc.Core;

namespace Models.Core.Communication.gRPC;

/// <summary>
/// 各 RPC 呼び出しに適用する実行設定です。
/// </summary>
public sealed class GrpcCallSettings
{
    /// <summary>
    /// 呼び出し時に追加するヘッダーです。
    /// </summary>
    public Metadata? Headers { get; init; }

    /// <summary>
    /// 絶対時刻で指定するデッドライン (UTC) です。
    /// </summary>
    public DateTime? DeadlineUtc { get; init; }

    /// <summary>
    /// 現在時刻からの相対デッドラインです。
    /// </summary>
    public TimeSpan? DeadlineFromNow { get; init; }

    /// <summary>
    /// 呼び出し単位のキャンセレーショントークンです。
    /// </summary>
    public CancellationToken CancellationToken { get; init; }
}
