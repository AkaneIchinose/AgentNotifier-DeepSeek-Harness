using System.IO;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;

namespace AgentNotifier.Audio;

/// <summary>
/// MP3 播放器：WPF MediaPlayer（MediaFoundation，支持 mp3/wma/wav），
/// 运行在独立 STA 线程 + Dispatcher 泵上（MediaPlayer 需要 DispatcherObject 线程）。
/// </summary>
public sealed class Mp3Player : IDisposable
{
    private readonly Thread _thread;
    private Dispatcher _dispatcher = null!;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private MediaPlayer? _current;
    private CancellationTokenSource? _cts;

    public Mp3Player()
    {
        _thread = new Thread(ThreadMain);
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.IsBackground = true;
        _thread.Name = "AgentNotifier.Mp3";
        _thread.Start();
        _ready.Task.Wait(5000);
    }

    private void ThreadMain()
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        _ready.TrySetResult();
        Dispatcher.Run();
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _dispatcher.BeginInvoke(new Action(() =>
            {
                try { _current?.Stop(); } catch { }
                _current = null;
            }));
        }
        catch { }
    }

    /// <summary>播放文件（mp3/wma/wav），音量 0-100，循环 repeats 次，间隔 intervalSec 秒；失败静默</summary>
    public async Task PlayAsync(string filePath, int volume, int repeats, double intervalSec)
    {
        try
        {
            _cts?.Cancel();
            var cts = new CancellationTokenSource();
            _cts = cts;
            var count = Math.Clamp(repeats, 1, 10);
            var intervalMs = (int)(Math.Clamp(intervalSec, 0.2, 10) * 1000);
            for (int r = 0; r < count && !cts.IsCancellationRequested; r++)
            {
                await PlayOnceAsync(filePath, volume, cts.Token);
                if (r < count - 1 && !cts.IsCancellationRequested)
                {
                    try { await Task.Delay(intervalMs, cts.Token); } catch { }
                }
            }
        }
        catch { }
    }

    private Task PlayOnceAsync(string filePath, int volume, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        void Run()
        {
            try
            {
                var mp = new MediaPlayer { Volume = Math.Clamp(volume, 0, 100) / 100f };
                _current = mp;
                mp.MediaEnded += (_, _) => { try { mp.Close(); } catch { } tcs.TrySetResult(); };
                mp.MediaFailed += (_, e) => { try { mp.Close(); } catch { } tcs.TrySetResult(); };
                mp.Open(new Uri(filePath));
                mp.Play();
            }
            catch { tcs.TrySetResult(); }
        }
        try { _dispatcher.BeginInvoke(new Action(Run)); }
        catch { tcs.TrySetResult(); }
        try { return tcs.Task.WaitAsync(TimeSpan.FromSeconds(60), ct); }
        catch { return Task.CompletedTask; }
    }

    public void Dispose()
    {
        try
        {
            _cts?.Cancel();
            _dispatcher.InvokeShutdown();
        }
        catch { }
    }
}
