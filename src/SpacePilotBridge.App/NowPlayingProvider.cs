using Windows.Media.Control;

namespace SpacePilotBridge.App;

/// <summary>
/// Lightweight Windows media-session reader. Spotify publishes its title/artist
/// through the system media transport controls, so no Spotify login/API token is required.
/// </summary>
internal sealed class NowPlayingProvider : IDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private Task? _loop;
    private volatile string _line = "SPOTIFY -";
    private volatile bool _available;

    public string Line => _line;
    public bool Available => _available;

    public NowPlayingProvider() => _loop = Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken token)
    {
        try
        {
            _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            while (!token.IsCancellationRequested)
            {
                try { await RefreshAsync(); } catch { _available = false; }
                await Task.Delay(1500, token);
            }
        }
        catch (OperationCanceledException) { }
        catch { _available = false; }
    }

    private async Task RefreshAsync()
    {
        if (_manager is null) return;
        GlobalSystemMediaTransportControlsSession? session = _manager.GetSessions()
            .FirstOrDefault(s => (s.SourceAppUserModelId ?? string.Empty).Contains("spotify", StringComparison.OrdinalIgnoreCase));
        if (session is null)
        {
            _line = "SPOTIFY -"; _available = false; return;
        }

        GlobalSystemMediaTransportControlsSessionMediaProperties props = await session.TryGetMediaPropertiesAsync();
        string title = (props.Title ?? string.Empty).Trim();
        string artist = (props.Artist ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(artist))
        {
            _line = "SPOTIFY -"; _available = false; return;
        }
        _line = string.IsNullOrWhiteSpace(artist) ? title : $"{artist} - {title}";
        _available = true;
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(250); } catch { }
        _cts.Dispose();
    }
}
