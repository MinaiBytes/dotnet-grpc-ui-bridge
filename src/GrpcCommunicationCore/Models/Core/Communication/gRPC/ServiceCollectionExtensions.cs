using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Models.Core.Communication.gRPC;

/// <summary>
/// gRPC 通信コアの DI 登録拡張メソッドを提供します。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// gRPC 通信コア関連サービスを DI コンテナーへ登録します。
    /// </summary>
    /// <param name="services">サービスコレクションです。</param>
    /// <param name="configure">オプション設定アクションです。</param>
    /// <returns>登録後のサービスコレクションです。</returns>
    public static IServiceCollection AddGrpcCommunicationCore(
        this IServiceCollection services,
        Action<GrpcCommunicationOptions> configure)
    {
        if (services is null)
        {
            throw new ArgumentNullException(nameof(services));
        }
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        services
            .AddOptions<GrpcCommunicationOptions>()
            .Configure(configure)
            .Validate(ValidateOptions, "Invalid gRPC communication options.")
            .ValidateOnStart();

        services.TryAddSingleton<GrpcChannelProvider>();
        services.TryAddSingleton<GrpcTransportCore>();

        return services;
    }

    /// <summary>
    /// gRPC 通信オプションの妥当性を検証します。
    /// </summary>
    /// <param name="options">検証対象オプションです。</param>
    /// <returns>妥当な場合は <see langword="true"/>。</returns>
    private static bool ValidateOptions(GrpcCommunicationOptions options)
    {
        // Endpoint を直接指定しない場合は Host/Port で解決するため、最低限の検証を行います。
        var connection = options.Connection;
        if (connection.Endpoint is null)
        {
            if (string.IsNullOrWhiteSpace(connection.Host))
            {
                return false;
            }

            if (connection.Port is <= 0 or > 65535)
            {
                return false;
            }
        }

        if (options.Authentication.Mode == GrpcAuthenticationMode.ApiKey)
        {
            return !string.IsNullOrWhiteSpace(options.Authentication.ApiKey)
                && !string.IsNullOrWhiteSpace(options.Authentication.ApiKeyHeaderName);
        }

        if (options.Authentication.Mode == GrpcAuthenticationMode.MutualTls)
        {
            return !string.IsNullOrWhiteSpace(options.Authentication.ClientCertificatePath);
        }

        return true;
    }
}
