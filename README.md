# GrpcCommunicationCore

`Models.Core.Communication.gRPC` 名前空間で提供する、軽量な gRPC 通信コアです。  
GUI でのステータス表示用途を想定し、機能を最小限に絞っています。

## 何ができるか

- 4種類の RPC 実行
  - Unary
  - Server Streaming
  - Client Streaming
  - Bidirectional Streaming
- 接続設定の一元化
  - `Host / Port / Endpoint / TLS / Deadline`
- 認証の切替
  - `None / BearerToken / ApiKey / MutualTls`
- `GrpcChannel` の再利用
- ZLogger によるテキストログ出力
- `ObservableCollection` へ反映する `GrpcStreamBindingAdapter<T>` の利用

## ログ方針

- `ILogger<T>` は DI から受け取ります
- `logger` が `null` の場合は `NullLogger<T>` へフォールバックします

## CPU/メモリチューニング方針

- 既定値は GUI ステータス表示向けの軽量設定です
  - `GrpcStreamBindingAdapter<T>`
    - `maxItemCount = 2000`
    - `uiBatchSize = 64`
    - `trimBatchSize = 256`
  - `GrpcConnectionOptions`
    - `KeepAlivePingDelay = Timeout.InfiniteTimeSpan`
    - `KeepAlivePingTimeout = Timeout.InfiniteTimeSpan`
- ストリーム受信で先頭削除が多い場合、内部でコレクション再構築に切り替えて CPU 使用率を抑えます
- 応答遅延より負荷低減を優先したい場合は `uiBatchSize` を増やしてください（例: `128`）

## 主要クラス

- `GrpcCommunicationOptions`
  - 接続/認証の設定
- `GrpcChannelProvider`
  - `GrpcChannel` と `CallInvoker` の保持
- `GrpcTransportCore`
  - 4種類のRPC実行
- `GrpcStreamBindingAdapter<T>`
  - UI バインディング補助

## DI 登録

```csharp
using Models.Core.Communication.gRPC;

services.AddGrpcCommunicationCore(options =>
{
    options.Connection = new GrpcConnectionOptions
    {
        Host = "10.0.0.25",
        Port = 50051,
        UseTls = true,
        DefaultDeadline = TimeSpan.FromSeconds(10)
    };

    options.Authentication = new GrpcAuthenticationOptions
    {
        Mode = GrpcAuthenticationMode.None
    };
});
```

Bearer トークンを動的取得したい場合は `IBearerTokenProvider` を追加登録します。

必要に応じて Keep-Alive ping を有効化する場合:

```csharp
services.AddGrpcCommunicationCore(options =>
{
    options.Connection = new GrpcConnectionOptions
    {
        Host = "10.0.0.25",
        Port = 50051,
        UseTls = true,
        KeepAlivePingDelay = TimeSpan.FromSeconds(30),
        KeepAlivePingTimeout = TimeSpan.FromSeconds(15)
    };
});
```

## `.proto` からの利用例（Server Streaming）

```csharp
public sealed class MonitorGateway
{
    private readonly GrpcChannelProvider _channel;
    private readonly GrpcTransportCore _transport;

    public MonitorGateway(GrpcChannelProvider channel, GrpcTransportCore transport)
    {
        _channel = channel;
        _transport = transport;
    }

    public IAsyncEnumerable<CpuUsageReply> StreamCpuUsageAsync(string machineId, CancellationToken ct)
    {
        var client = new MonitorService.MonitorServiceClient(_channel.CallInvoker);
        var request = new StreamCpuUsageRequest { MachineId = machineId };

        return _transport.ServerStreamingAsync(
            operationName: "MonitorService/StreamCpuUsage",
            callFactory: options => client.StreamCpuUsage(request, options),
            cancellationToken: ct);
    }
}
```

## `.proto` からの利用例（Unary: コマンド発行）

```csharp
public sealed class CommandGateway
{
    private readonly GrpcChannelProvider _channel;
    private readonly GrpcTransportCore _transport;

    public CommandGateway(GrpcChannelProvider channel, GrpcTransportCore transport)
    {
        _channel = channel;
        _transport = transport;
    }

    public Task<ExecuteCommandReply> ExecuteCommandAsync(string machineId, string command, CancellationToken ct)
    {
        var client = new MonitorService.MonitorServiceClient(_channel.CallInvoker);
        var request = new ExecuteCommandRequest
        {
            MachineId = machineId,
            Command = command
        };

        return _transport.UnaryAsync(
            operationName: "MonitorService/ExecuteCommand",
            callExecutor: options => client.ExecuteCommandAsync(request, options).ResponseAsync,
            cancellationToken: ct);
    }
}
```

## ViewModel でコマンド発行する例（Unary）

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

public partial class CommandViewModel : ObservableObject
{
    private readonly CommandGateway _gateway;

    [ObservableProperty]
    private string commandText = "restart-service";

    [ObservableProperty]
    private string resultText = "Not executed";

    public CommandViewModel(CommandGateway gateway)
    {
        _gateway = gateway;
    }

    [RelayCommand]
    private async Task SendCommandAsync()
    {
        try
        {
            var reply = await _gateway.ExecuteCommandAsync("PC-001", CommandText, CancellationToken.None);
            ResultText = $"Success: {reply.ResultMessage}";
        }
        catch (RpcException ex)
        {
            ResultText = $"Failed: {ex.StatusCode}";
        }
    }
}
```

## ViewModel で連続更新する例

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

public partial class CpuUsageViewModel : ObservableObject
{
    private readonly MonitorGateway _gateway;
    private readonly GrpcStreamBindingAdapter<CpuUsageReply> _adapter = new(
        maxItemCount: 1000,
        uiBatchSize: 64,
        trimBatchSize: 256);
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    private double currentCpuUsage;

    [ObservableProperty]
    private string statusText = "Idle";

    public CpuUsageViewModel(MonitorGateway gateway)
    {
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
    private async Task StartAsync()
    {
        if (_cts is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        StatusText = "Streaming...";

        try
        {
            await _adapter.BindAsync(_gateway.StreamCpuUsageAsync("PC-001", _cts.Token), cancellationToken: _cts.Token);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Stopped";
        }
        catch (RpcException ex)
        {
            StatusText = $"Error: {ex.StatusCode}";
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
        }
    }

    [RelayCommand]
    private void Stop()
    {
        _cts?.Cancel();
    }
}
```

## XAML バインディング例

```xml
<StackPanel Margin="16" Spacing="8">
    <TextBlock Text="{Binding CurrentCpuUsage, StringFormat=CPU: {0:F1}%}" />
    <ProgressBar Minimum="0" Maximum="100" Value="{Binding CurrentCpuUsage}" Height="16" />
    <TextBlock Text="{Binding StatusText}" />
    <Button Content="Start" Command="{Binding StartCommand}" />
    <Button Content="Stop" Command="{Binding StopCommand}" />
</StackPanel>
```

## 使用パッケージ

- `Grpc.Net.Client`
- `Grpc.Core.Api`
- `Microsoft.Extensions.DependencyInjection`
- `Microsoft.Extensions.Options`
- `Microsoft.Extensions.Logging.Abstractions`
- `ZLogger`
- `CommunityToolkit.Mvvm`
