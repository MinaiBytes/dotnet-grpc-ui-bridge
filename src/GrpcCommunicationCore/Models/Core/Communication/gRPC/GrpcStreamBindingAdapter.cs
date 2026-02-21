using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Models.Core.Communication.gRPC;

/// <summary>
/// gRPC ストリームを <see cref="ObservableCollection{T}"/> に反映し、
/// MVVM のデータバインディングで扱えるようにするアダプターです。
/// </summary>
/// <typeparam name="T">要素型です。</typeparam>
public class GrpcStreamBindingAdapter<T> : ObservableObject, IDisposable
{
    // 実データ格納用の可変コレクションです。公開は ReadOnly で行います。
    private readonly ObservableCollection<T> _items = [];

    // UI スレッドへ戻すための同期コンテキストです。
    private readonly SynchronizationContext? _syncContext;

    // メモリ肥大化を避けるための保持上限です。
    private readonly int _maxItemCount;

    // バインド処理中の状態フラグです。
    private bool _isRunning;

    // 最後に発生したエラーを UI 表示しやすい形で保持します。
    private string? _lastError;

    /// <summary>
    /// <see cref="GrpcStreamBindingAdapter{T}"/> の新しいインスタンスを初期化します。
    /// </summary>
    /// <param name="maxItemCount">保持する最大要素数です。</param>
    /// <param name="syncContext">UI スレッドの同期コンテキストです。</param>
    public GrpcStreamBindingAdapter(int maxItemCount = 5000, SynchronizationContext? syncContext = null)
    {
        if (maxItemCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxItemCount));
        }

        _maxItemCount = maxItemCount;
        _syncContext = syncContext ?? SynchronizationContext.Current;
        Items = new ReadOnlyObservableCollection<T>(_items);
    }

    /// <summary>
    /// バインド対象の読み取り専用コレクションです。
    /// </summary>
    public ReadOnlyObservableCollection<T> Items { get; }

    /// <summary>
    /// ストリーム反映処理中かどうかを表します。
    /// </summary>
    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    /// <summary>
    /// 最後に発生したエラーメッセージです。
    /// </summary>
    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    /// <summary>
    /// ストリームを購読して <see cref="Items"/> に反映します。
    /// </summary>
    public async Task BindAsync(
        IAsyncEnumerable<T> stream,
        bool clearBeforeBind = true,
        CancellationToken cancellationToken = default)
    {
        if (clearBeforeBind)
        {
            await InvokeOnUiThreadAsync(_items.Clear, cancellationToken).ConfigureAwait(false);
        }

        LastError = null;
        IsRunning = true;

        try
        {
            await foreach (var item in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                await InvokeOnUiThreadAsync(() =>
                {
                    // 長期稼働時に要素が増え続けないよう、先頭から古い要素を捨てます。
                    if (_items.Count >= _maxItemCount)
                    {
                        _items.RemoveAt(0);
                    }

                    _items.Add(item);
                }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            throw;
        }
        finally
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// 保持している要素をすべてクリアします。
    /// </summary>
    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        return InvokeOnUiThreadAsync(_items.Clear, cancellationToken);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }

    /// <summary>
    /// 必要に応じて UI スレッドへディスパッチして処理を実行します。
    /// </summary>
    /// <param name="action">実行する処理です。</param>
    /// <param name="cancellationToken">キャンセルトークンです。</param>
    /// <returns>実行完了を待機するタスクです。</returns>
    private Task InvokeOnUiThreadAsync(Action action, CancellationToken cancellationToken)
    {
        if (_syncContext is null || ReferenceEquals(SynchronizationContext.Current, _syncContext))
        {
            action();
            return Task.CompletedTask;
        }

        // UI スレッド反映完了を待てるよう TaskCompletionSource を使います。
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationTokenRegistration? registration = null;
        if (cancellationToken.CanBeCanceled)
        {
            registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));
        }

        _syncContext.Post(static state =>
        {
            var (work, completion, tokenRegistration) = ((Action, TaskCompletionSource, CancellationTokenRegistration?))state!;
            try
            {
                work();
                completion.TrySetResult();
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
            finally
            {
                tokenRegistration?.Dispose();
            }
        }, (action, tcs, registration));

        return tcs.Task;
    }
}
