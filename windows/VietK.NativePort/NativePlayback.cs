using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using LibVLCSharp.WPF;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using VietK.Core;

namespace VietK.NativePort;

// Windows implementation of the original selected EvIjkPlayer operations.
// Actual decoding is provided by bundled libVLC; Android/vendor-only scoring,
// encryption, DSP and microphone paths have not been translated by this adapter.
public sealed class WindowsVideoDecoder : IOriginalVideoDecoder, IDisposable
{
    private readonly LibVLC library;
    private readonly Dispatcher dispatcher;
    private Media? media;
    private string? source;
    private int generation;
    private bool disposed, firstFrame, buffering;
    private float mediaVolume = 1;
    private int originalTrack = -1, accompanimentTrack = -1;
    public MediaPlayer Native { get; }
    public OriginalVideoPlayer? Original { get; set; }
    public Action<bool>? ConfirmedPause { get; set; }
    public Action? ConfirmedTrack { get; set; }
    public Action? Started { get; set; }
    public int OutputVolumeStep { get; private set; } = 15;
    public int Position => (int)Math.Clamp(Native.Time, 0, int.MaxValue);
    public int Duration => (int)Math.Clamp(Native.Length, 0, int.MaxValue);

    public WindowsVideoDecoder(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        var directory = Path.Combine(AppContext.BaseDirectory, "libvlc", "win-x64");
        if (!File.Exists(Path.Combine(directory, "libvlc.dll")))
            throw new FileNotFoundException("Bundled Windows decoder missing", directory);
        LibVLCSharp.Shared.Core.Initialize(directory);
        library = new LibVLC("--no-video-title-show", "--no-osd");
        Native = new MediaPlayer(library) { EnableKeyInput = false, EnableMouseInput = false };
        Native.Playing += (_, _) => Post(() => { Original?.OnStart(); ConfirmedPause?.Invoke(false); Started?.Invoke(); });
        Native.Paused += (_, _) => Post(() => ConfirmedPause?.Invoke(true));
        Native.EndReached += (_, _) => Post(() => Original?.OnComplete());
        Native.EncounteredError += (_, _) => Post(() => Original?.OnError(1)); // IEvBasePlayer.Error.UNKNOWN
        Native.Buffering += (_, args) => Post(() =>
        {
            if (args.Cache < 100 && !buffering) { buffering = true; Original?.OnBufferingStart(); }
            else if (args.Cache >= 100 && buffering) { buffering = false; Original?.OnBufferingEnd(); }
        });
        Native.TimeChanged += (_, args) =>
        {
            if (args.Time <= 0 || firstFrame) return;
            firstFrame = true;
            Post(() =>
            {
                uint width = 0, height = 0;
                var hasVideo = Native.Size(0, ref width, ref height) && width > 0 && height > 0;
                // Translate decoder progress/render readiness to the original
                // event boundary; native snapshots verify actual decoded pixels.
                Original?.OnInfo(hasVideo ? 10003 : 10004);
                Original?.OnAudioRenderingStart(hasVideo);
                if (hasVideo) Original?.OnVideoRenderingStart();
            });
        };
    }
    private void Post(Action action)
    {
        var current = generation;
        dispatcher.BeginInvoke(() => { if (!disposed && current == generation) action(); });
    }
    public void SetSource(string? value)
    {
        if (string.IsNullOrEmpty(value)) throw new ArgumentException("Empty original media source");
        generation++; firstFrame = false; buffering = false; source = value;
        media?.Dispose();
        media = new Media(library, new Uri(Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : value, UriKind.Absolute));
    }
    public void PrepareAsync()
    { if (media is null || !Native.Play(media)) throw new InvalidOperationException("Windows decoder rejected media"); }
    public void Start() => Native.SetPause(false);
    public void Pause() => Native.SetPause(true);
    public void Stop() { generation++; firstFrame = false; Native.Stop(); }
    public void Seek(int milliseconds) => Native.Time = milliseconds;
    public void SetTrackInfo(int original, int accompaniment)
    { originalTrack = original; accompanimentTrack = accompaniment; }
    public bool SwitchTrack(bool original)
    {
        var tracks = Native.AudioTrackDescription.Where(track => track.Id >= 0).ToArray();
        if (tracks.Length == 0) return false;
        var index = original ? originalTrack : accompanimentTrack;
        bool changed;
        if (tracks.Length >= 2)
        {
            if (index < 0 || index >= tracks.Length) return false;
            // libVLC requires stream IDs, not array positions. The original
            // helper likewise selects the matching audio entry in all streams.
            changed = Native.SetAudioTrack(tracks[index].Id);
        }
        else
        {
            var channel = index == 0 ? 1 : index == 1 ? 0 : index;
            // Original libijkplayer ffp_set_audio_channel @0x2d0d0:
            // 0 -> stereotools lr>ll, 1 -> lr>rr, 2 -> empty filter/stereo.
            changed = channel switch
            {
                0 => Native.SetChannel(AudioOutputChannel.Left),
                1 => Native.SetChannel(AudioOutputChannel.Right),
                2 => Native.SetChannel(AudioOutputChannel.Stereo),
                _ => false
            };
        }
        if (changed) Post(() => ConfirmedTrack?.Invoke());
        return changed;
    }
    public void SetVolume(float volume) { mediaVolume = volume; ApplyVolume(); }
    public void SetOutputVolumeStep(int step) { OutputVolumeStep = Math.Clamp(step, 0, 20); ApplyVolume(); }
    private void ApplyVolume() => Native.Volume = (int)Math.Clamp(mediaVolume * OutputVolumeStep / 20 * 100, 0, 200);
    public void Dispose()
    {
        if (disposed) return;
        disposed = true; generation++; Native.Stop(); Native.Dispose(); media?.Dispose(); library.Dispose();
    }
}

public sealed class TelevisionWindow : Window
{
    private bool allowClose;
    private readonly Border black;
    public bool BlackVisible => black.Visibility == Visibility.Visible;
    public TelevisionWindow(MediaPlayer player)
    {
        Title = "VietK — TV output"; Width = 960; Height = 540; Background = Brushes.Black;
        // activity_osd: full video surface, black cover above it, then loading,
        // playback hint and grading containers. Missing OSD layers stay absent.
        black = new Border { Background = Brushes.Black };
        Content = new VideoView { MediaPlayer = player, Content = black };
    }
    public void SetBlack(bool visible) => black.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
    public void ClosePermanently() { allowClose = true; Close(); }
    public void Detach() => ((VideoView)Content).MediaPlayer = null;
}

public sealed class NativePlayback : IDisposable
{
    private readonly BottomBar bottom;
    private readonly string stateFile;
    public WindowsVideoDecoder Decoder { get; }
    public OriginalVideoPlayer Player { get; }
    public TelevisionWindow Television { get; }
    public event Action? NextRequested;
    public SongMedia? CurrentMedia { get; private set; }
    public NativePlayback(BottomBar bottom, string stateDirectory)
    {
        this.bottom = bottom;
        stateFile = Path.Combine(stateDirectory, "playback-state.json");
        Decoder = new WindowsVideoDecoder(Dispatcher.CurrentDispatcher);
        Television = new TelevisionWindow(Decoder.Native);
        Player = new OriginalVideoPlayer(Decoder, Television.SetBlack);
        Decoder.Original = Player;
        Decoder.ConfirmedPause = paused => bottom.SetConfirmedPlaybackState(paused, Player.SingMode == OriginalSingMode.Original);
        Decoder.ConfirmedTrack = () => bottom.SetConfirmedPlaybackState(Player.State == OriginalVideoState.Pause,
            Player.SingMode == OriginalSingMode.Original);
        Player.Completed += () => Dispatcher.CurrentDispatcher.BeginInvoke(() => NextRequested?.Invoke());
        if (File.Exists(stateFile)) Decoder.SetOutputVolumeStep(JsonSerializer.Deserialize<PlaybackPreferences>(File.ReadAllText(stateFile))!.Volume);
    }
    public void ShowTelevision(Window panel)
    { Television.Show(); }
    public bool PlayMedia(string path, SongMedia? metadata = null)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || (uri.IsFile && !File.Exists(uri.LocalPath))) return false;
        Player.Stop(); CurrentMedia = metadata;
        Player.SetTrackInfo(metadata?.OriginalTrack ?? 0, metadata?.AccompanyTrack ?? 1);
        // KmPlayerCtrlImpl.getMediaVolume; configured HDD scale defaults to 1.
        var gain=(metadata?.DefaultVolume??100)/100f;
        Player.SetVolume(gain <= 0 ? 0.8f : gain);
        return Player.SetSource(path) == 0 && Player.Play() == 0;
    }
    public void Command(string command)
    {
        switch (command)
        {
            case "play_imv": case "pause_imv":
                if (Player.State == OriginalVideoState.Play) Player.Pause();
                else if (Player.State == OriginalVideoState.Pause) Player.Play();
                break;
            case "ori_imv": case "accp_imv":
                if (CurrentMedia is { OriginalTrack: 0, AccompanyTrack: 5 } or { OriginalTrack: 5, AccompanyTrack: 0 }) break;
                Player.SetSingMode(Player.SingMode == OriginalSingMode.Original ? OriginalSingMode.Accompaniment : OriginalSingMode.Original);
                break;
            case "replay_imv":
                if ((Player.State is OriginalVideoState.Play or OriginalVideoState.Pause) && Player.Source is { } path) PlayMedia(path, CurrentMedia);
                break;
            case "cut_song_imv": Player.Stop(); NextRequested?.Invoke(); break;
            case "volinc": case "voldec":
                Decoder.SetOutputVolumeStep(Decoder.OutputVolumeStep + (command == "volinc" ? 1 : -1));
                Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
                File.WriteAllText(stateFile, JsonSerializer.Serialize(new PlaybackPreferences(Decoder.OutputVolumeStep)));
                break;
        }
    }
    public void Dispose() { Television.Detach(); Television.ClosePermanently(); Decoder.Dispose(); }
    private sealed record PlaybackPreferences(int Volume);
}
