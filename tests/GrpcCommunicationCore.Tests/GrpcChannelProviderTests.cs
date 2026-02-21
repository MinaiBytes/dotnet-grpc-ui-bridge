using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http;
using System.Reflection;

namespace Models.Core.Communication.gRPC.Tests;

public class GrpcChannelProviderTests
{
    [Fact]
    public void Constructor_WithNullLogger_CreatesChannelAndInvoker()
    {
        var options = CreateOptions(useTls: false);
        using var provider = new GrpcChannelProvider(Options.Create(options), logger: null);

        Assert.Equal("http://127.0.0.1:50051/", provider.Endpoint.ToString());
        Assert.NotNull(provider.CallInvoker);
    }

    [Fact]
    public void Constructor_WithTlsAndInsecureServerCert_SetsValidationCallback()
    {
        var options = CreateOptions(useTls: true, allowInsecureServerCertificate: true);
        using var provider = new GrpcChannelProvider(Options.Create(options), logger: null);

        var httpHandler = GetPrivateField<SocketsHttpHandler>(provider, "_httpHandler");

        Assert.NotNull(httpHandler.SslOptions);
        Assert.NotNull(httpHandler.SslOptions.RemoteCertificateValidationCallback);
    }

    [Fact]
    public void Constructor_MutualTlsWithoutCertificate_ThrowsInvalidOperationException()
    {
        var options = new GrpcCommunicationOptions
        {
            Connection = new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 50051,
                UseTls = true
            },
            Authentication = new GrpcAuthenticationOptions
            {
                Mode = GrpcAuthenticationMode.MutualTls,
                ClientCertificatePath = null
            }
        };

        Assert.Throws<InvalidOperationException>(() => new GrpcChannelProvider(Options.Create(options), logger: null));
    }

    [Fact]
    public void Constructor_MutualTlsWithCertificate_LoadsClientCertificate()
    {
        var password = "p@ss";
        var certPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pfx");
        try
        {
            using (var rsa = RSA.Create(2048))
            {
                var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var cert = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));
                var pfx = cert.Export(X509ContentType.Pfx, password);
                File.WriteAllBytes(certPath, pfx);
            }

            var options = new GrpcCommunicationOptions
            {
                Connection = new GrpcConnectionOptions
                {
                    Host = "127.0.0.1",
                    Port = 50051,
                    UseTls = true
                },
                Authentication = new GrpcAuthenticationOptions
                {
                    Mode = GrpcAuthenticationMode.MutualTls,
                    ClientCertificatePath = certPath,
                    ClientCertificatePassword = password
                }
            };

            using var provider = new GrpcChannelProvider(Options.Create(options), logger: null);
            var httpHandler = GetPrivateField<SocketsHttpHandler>(provider, "_httpHandler");

            Assert.NotNull(httpHandler.SslOptions.ClientCertificates);
            Assert.True(httpHandler.SslOptions.ClientCertificates.Count > 0);
        }
        finally
        {
            if (File.Exists(certPath))
            {
                File.Delete(certPath);
            }
        }
    }

    [Fact]
    public void Dispose_CanBeCalled()
    {
        var options = CreateOptions(useTls: false);
        var provider = new GrpcChannelProvider(Options.Create(options), logger: null);

        provider.Dispose();
    }

    private static GrpcCommunicationOptions CreateOptions(bool useTls, bool allowInsecureServerCertificate = false)
    {
        return new GrpcCommunicationOptions
        {
            Connection = new GrpcConnectionOptions
            {
                Host = "127.0.0.1",
                Port = 50051,
                UseTls = useTls
            },
            Authentication = new GrpcAuthenticationOptions
            {
                Mode = GrpcAuthenticationMode.None,
                AllowInsecureServerCertificate = allowInsecureServerCertificate
            }
        };
    }

    private static T GetPrivateField<T>(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (T)field!.GetValue(instance)!;
    }
}
