using System.Security;

namespace VietK.Core;

public enum OriginalVideoState { Unknown, Idle, Init, Preparing, Prepared, Play, Pause, Stopped, Buffering, Complete, Errors, Uninitialized, Max }
public enum OriginalSingMode { Original, Accompaniment, Unknown }

// Windows adapters supply actual media operations. These are the original
// selected EvIjkPlayer contract's operations, not a simulated playback clock.
public interface IOriginalVideoDecoder
{
    void SetSource(string? source);
    void PrepareAsync();
    void Start();
    void Pause();
    void Stop();
    void Seek(int milliseconds);
    int Position { get; }
    int Duration { get; }
    void SetTrackInfo(int original, int accompaniment);
    bool SwitchTrack(bool original);
    void SetVolume(float volume);
}

// Hand translation of KmVideoPlayer's selected IJK path. Backend event methods
// are called by the native adapter on the UI thread, preserving callback order.
public sealed class OriginalVideoPlayer(IOriginalVideoDecoder decoder, Action<bool> blackView)
{
    private IOriginalVideoDecoder? player = decoder;
    public OriginalVideoState State { get; private set; } = OriginalVideoState.Idle;
    public OriginalSingMode SingMode { get; private set; } = OriginalSingMode.Original;
    public float Volume { get; private set; } = 1;
    public string? Source { get; private set; }
    public bool AudioMode { get; private set; }
    public event Action? Played, Completed, BufferingStarted, BufferingEnded;
    public event Action<int>? Failed;

    public int SetSource(string? source)
    {
        if (State is not (OriginalVideoState.Idle or OriginalVideoState.Stopped or OriginalVideoState.Errors or OriginalVideoState.Init)) return -1;
        Source = source;
        try { player!.SetSource(source); State = OriginalVideoState.Init; return 0; }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException or SecurityException) { return -1; }
    }
    public int Play()
    {
        if (State == OriginalVideoState.Init)
        {
            try { player!.PrepareAsync(); State = OriginalVideoState.Preparing; }
            catch (InvalidOperationException) { State = OriginalVideoState.Idle; return -1; }
        }
        else { player!.Start(); State = OriginalVideoState.Play; }
        return 0;
    }
    public int Pause()
    {
        if (State == OriginalVideoState.Play) { player!.Pause(); State = OriginalVideoState.Pause; }
        return 0;
    }
    public int Stop()
    {
        if (State is OriginalVideoState.Play or OriginalVideoState.Pause or OriginalVideoState.Buffering or
            OriginalVideoState.Prepared or OriginalVideoState.Preparing or OriginalVideoState.Idle or OriginalVideoState.Errors)
        { blackView(true); player!.Stop(); State = OriginalVideoState.Idle; }
        return 0;
    }
    public int Position => player?.Position ?? 0;
    public int Duration => State is OriginalVideoState.Play or OriginalVideoState.Pause or OriginalVideoState.Buffering ? player?.Duration ?? 0 : -1;
    public int Seek(int milliseconds)
    {
        if (milliseconds < 0 || milliseconds > player!.Duration) return -1;
        if (State is OriginalVideoState.Play or OriginalVideoState.Pause) player.Seek(milliseconds);
        return 0;
    }
    public bool SetSingMode(OriginalSingMode mode)
    {
        if (State is OriginalVideoState.Play or OriginalVideoState.Pause or OriginalVideoState.Buffering) SwitchTrack(mode);
        SingMode = mode; return true;
    }
    private void SwitchTrack(OriginalSingMode mode)
    {
        if (State is OriginalVideoState.Init or OriginalVideoState.Errors or OriginalVideoState.Idle or OriginalVideoState.Preparing) return;
        player?.SwitchTrack(mode == OriginalSingMode.Original);
    }
    public int SetTrackInfo(int original, int accompaniment)
    {
        // C# and Java integer remainder both preserve the dividend's sign.
        player?.SetTrackInfo(original % 2, accompaniment % 2); return 0;
    }
    public int SetVolume(float value)
    {
        if (Volume == value) return 0;
        Volume = value;
        if (State is OriginalVideoState.Play or OriginalVideoState.Pause or OriginalVideoState.Buffering) player!.SetVolume(value);
        return 0;
    }
    public void Mute() => player?.SetVolume(0);
    public void Unmute() => player?.SetVolume(Volume);
    public int Destroy() { if (player is not null) { Stop(); player = null; } return 0; }

    public void OnStart()
    {
        State = OriginalVideoState.Play;
        if (!AudioMode) SwitchTrack(SingMode);
        player!.SetVolume(Volume);
    }
    public void OnInfo(int what) { if (what is 10003 or 10004) blackView(false); }
    public void OnVideoRenderingStart() => Played?.Invoke();
    public void OnAudioRenderingStart(bool hasVideo)
    { if (!hasVideo) AudioMode = true; if (AudioMode) Played?.Invoke(); }
    public void OnComplete() { Completed?.Invoke(); State = OriginalVideoState.Idle; }
    public void OnError(int originalErrorOrdinal) { State = OriginalVideoState.Errors; Failed?.Invoke(originalErrorOrdinal); }
    public void OnBufferingStart() => BufferingStarted?.Invoke();
    public void OnBufferingEnd() => BufferingEnded?.Invoke();
}
