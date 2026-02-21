namespace Models.Core.Communication.gRPC;

/// <summary>
/// Bearer トークンを動的に取得するためのプロバイダーです。
/// </summary>
public interface IBearerTokenProvider
{
    /// <summary>
    /// Bearer トークンを取得します。
    /// </summary>
    /// <param name="cancellationToken">キャンセル要求を通知するトークンです。</param>
    /// <returns>取得したトークン。取得できない場合は <see langword="null"/>。</returns>
    ValueTask<string?> GetTokenAsync(CancellationToken cancellationToken);
}
