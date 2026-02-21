# GrpcCommunicationCore

`Models.Core.Communication.gRPC` 名前空間で提供する、WPF/MVVM 向けの軽量 gRPC 通信コアです。  
役割は「通信の共通化」に限定し、`.proto` 由来のクライアントは 1 つ上の層で扱います。

## 何ができるか

- 4種類の RPC を統一 API で実行
  - `UnaryAsync`
  - `ServerStreamingAsync`
  - `ClientStreamingAsync`
  - `DuplexStreamingAsync`
- 接続設定の一元化
  - `Host / Port / Endpoint / TLS / Deadline`
- 認証モードの切り替え
  - `None / BearerToken / ApiKey / MutualTls`
- 画面単位セッションの生成と破棄
  - `GrpcCommunicationSessionFactory`
- `ObservableCollection` バインディング補助
  - `GrpcStreamBindingAdapter<T>`
- ZLogger によるテキストログ出力

## 設計の考え方（要点）

- 通信コアは薄く保つ
  - gRPC 本体は `Grpc.Net.Client`、DI は `Microsoft.Extensions.*` を利用
- 画面ライフサイクルと接続ライフサイクルを一致させる
  - 画面 `Loaded` でセッション作成、`Unloaded` で `Dispose`
- 低負荷寄りの既定値
  - KeepAlive 無効、既定デッドライン無効、`EnableMultipleHttp2Connections = false`
- `ILogger<T>` が `null` の場合は `NullLogger<T>` にフォールバック

## 使用パッケージ

- `Grpc.Net.Client`
- `Grpc.Core.Api`
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Logging.Abstractions`
- `ZLogger`
- `CommunityToolkit.Mvvm`

## クイックスタート（推奨構成）

### 1. DI 登録

```csharp
using Models.Core.Communication.gRPC;

services.AddGrpcCommunicationCore(options =>
{
    options.Connection = new GrpcConnectionOptions
    {
        Host = "10.0.0.25",
        Port = 50051,
        UseTls = true,
        DefaultDeadline = TimeSpan.FromSeconds(10), // 必要なら設定
        EnableMultipleHttp2Connections = false
    };

    options.Authentication = new GrpcAuthenticationOptions
    {
        Mode = GrpcAuthenticationMode.None
    };
});
```

Bearer トークンを動的取得する場合のみ、`IBearerTokenProvider` を別途 DI 登録します。

### 2. `.proto` 由来クライアントを使う Gateway を用意

```csharp
using Models.Core.Communication.gRPC;

public sealed class MonitorGateway
{
    public IAsyncEnumerable<CpuUsageReply> StreamCpuUsageAsync(
        GrpcCommunicationSession session,
        string machineId,
        CancellationToken ct)
    {
        var client = new MonitorService.MonitorServiceClient(session.CallInvoker);
        var request = new StreamCpuUsageRequest { MachineId = machineId };

        return session.Transport.ServerStreamingAsync(
            operationName: "MonitorService/StreamCpuUsage",
            callFactory: options => client.StreamCpuUsage(request, options),
            cancellationToken: ct);
    }

    public Task<ExecuteCommandReply> ExecuteCommandAsync(
        GrpcCommunicationSession session,
        string machineId,
        string command,
        CancellationToken ct)
    {
        var client = new MonitorService.MonitorServiceClient(session.CallInvoker);
        var request = new ExecuteCommandRequest { MachineId = machineId, Command = command };

        return session.Transport.UnaryAsync(
            operationName: "MonitorService/ExecuteCommand",
            callExecutor: options => client.ExecuteCommandAsync(request, options).ResponseAsync,
            cancellationToken: ct);
    }
}
```

### 3. ViewModel（画面表示中だけ接続）

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Grpc.Core;
using Models.Core.Communication.gRPC;
using System.Collections.ObjectModel;

public partial class MonitorViewModel : ObservableObject, IDisposable
{
    private readonly GrpcCommunicationSessionFactory _sessionFactory;
    private readonly MonitorGateway _gateway;
    private readonly GrpcStreamBindingAdapter<CpuUsageReply> _adapter = new(maxItemCount: 500, uiBatchSize: 64, trimBatchSize: 128);

    private GrpcCommunicationSession? _session;
    private CancellationTokenSource? _streamCts;

    [ObservableProperty]
    private double currentCpuUsage;

    [ObservableProperty]
    private string statusText = "Idle";

    [ObservableProperty]
    private string commandText = "restart-service";

    [ObservableProperty]
    private string commandResult = "Not executed";

    public MonitorViewModel(GrpcCommunicationSessionFactory sessionFactory, MonitorGateway gateway)
    {
        _sessionFactory = sessionFactory;
        _gateway = gateway;

        _adapter.Items.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is { Count: > 0 } && e.NewItems[e.NewItems.Count - 1] is CpuUsageReply latest)
            {
                CurrentCpuUsage = latest.UsagePercent;
            }
        };
    }

    public ReadOnlyObservableCollection<CpuUsageReply> Samples => _adapter.Items;

    [RelayCommand]
    private async Task OnLoadedAsync()
    {
        if (_session is not null)
        {
            return;
        }

        _session = _sessionFactory.CreateSession();
        _streamCts = new CancellationTokenSource();
        StatusText = "Streaming...";

        try
        {
            await _adapter.BindAsync(
                _gateway.StreamCpuUsageAsync(_session, "PC-001", _streamCts.Token),
                cancellationToken: _streamCts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped";
        }
        catch (RpcException ex)
        {
            StatusText = $"Error: {ex.StatusCode}";
        }
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        if (_session is null)
        {
            return;
        }

        try
        {
            var reply = await _gateway.ExecuteCommandAsync(_session, "PC-001", CommandText, CancellationToken.None);
            CommandResult = $"Success: {reply.ResultMessage}";
        }
        catch (RpcException ex)
        {
            CommandResult = $"Failed: {ex.StatusCode}";
        }
    }

    [RelayCommand]
    private void OnUnloaded()
    {
        _streamCts?.Cancel();
        _streamCts?.Dispose();
        _streamCts = null;
        _session?.Dispose();
        _session = null;
        StatusText = "Idle";
    }

    public void Dispose() => OnUnloaded();
}
```

### 4. XAML バインディング例

```xml
<StackPanel Margin="16">
    <TextBlock Text="{Binding CurrentCpuUsage, StringFormat=CPU: {0:F1}%}" />
    <ProgressBar Minimum="0" Maximum="100" Value="{Binding CurrentCpuUsage}" Height="16" />
    <TextBlock Text="{Binding StatusText}" />

    <TextBox Text="{Binding CommandText, UpdateSourceTrigger=PropertyChanged}" />
    <TextBlock Text="{Binding CommandResult}" />

    <Button Content="Start" Command="{Binding OnLoadedCommand}" />
    <Button Content="Send Command" Command="{Binding SendCommandCommand}" />
    <Button Content="Stop" Command="{Binding OnUnloadedCommand}" />
</StackPanel>
```

## 他の RPC を使うとき

```csharp
// Client Streaming
await session.Transport.ClientStreamingAsync(
    operationName: "Service/UploadMetrics",
    callFactory: options => client.UploadMetrics(options),
    requestStream: requestStream,
    cancellationToken: ct);

// Bidirectional Streaming
await foreach (var reply in session.Transport.DuplexStreamingAsync(
    operationName: "Service/WatchAndControl",
    callFactory: options => client.WatchAndControl(options),
    requestStream: requestStream,
    cancellationToken: ct))
{
    // 受信処理
}
```