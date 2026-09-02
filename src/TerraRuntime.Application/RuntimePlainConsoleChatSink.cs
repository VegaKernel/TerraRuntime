using System.Threading.Channels;
using TerraRuntime.Operations;

namespace TerraRuntime;

/// <summary>
/// Non-blocking projection of public chat into the plain terminal. Network/chat producers only perform
/// a bounded TryWrite; terminal I/O is isolated on one background worker and never participates in
/// authoritative relay success.
/// </summary>
internal sealed class RuntimePlainConsoleChatSink : IAsyncDisposable
{
    private const int QueueCapacity = 256;

    private readonly Func<bool> isPlainConsoleActive;
    private readonly TextWriter writer;
    private readonly Channel<RuntimeChatEntry> channel;
    private readonly CancellationTokenSource stop = new();
    private readonly Task drainTask;
    private int accepting = 1;

    public RuntimePlainConsoleChatSink(Func<bool> isPlainConsoleActive, TextWriter writer)
    {
        this.isPlainConsoleActive = isPlainConsoleActive ?? throw new ArgumentNullException(nameof(isPlainConsoleActive));
        this.writer = writer ?? throw new ArgumentNullException(nameof(writer));
        channel = Channel.CreateBounded<RuntimeChatEntry>(
            new BoundedChannelOptions(QueueCapacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        drainTask = Task.Run(DrainAsync);
    }

    public void TryPublish(RuntimeChatEntry entry)
    {
        if (Volatile.Read(ref accepting) == 0 || !SafeIsActive())
            return;

        channel.Writer.TryWrite(entry);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref accepting, 0) == 0)
            return;

        channel.Writer.TryComplete();
        try
        {
            await drainTask.WaitAsync(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            stop.Dispose();
        }
        catch (TimeoutException)
        {
            // A broken or indefinitely blocked stdout must not extend server shutdown. The worker is
            // background-only, so cancellation is best-effort and process exit remains authoritative.
            stop.Cancel();
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
            stop.Dispose();
        }
    }

    private async Task DrainAsync()
    {
        try
        {
            await foreach (RuntimeChatEntry entry in channel.Reader.ReadAllAsync(stop.Token).ConfigureAwait(false))
            {
                if (!SafeIsActive())
                    continue;

                try
                {
                    await writer.WriteLineAsync($"[chat] #{entry.PlayerSlot}: {entry.Text}").ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is IOException or ObjectDisposedException)
                {
                    // Console output is observability only. Keep draining/dropping rather than affecting chat.
                }
            }
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested)
        {
        }
    }

    private bool SafeIsActive()
    {
        try
        {
            return isPlainConsoleActive();
        }
        catch (Exception)
        {
            return false;
        }
    }
}
