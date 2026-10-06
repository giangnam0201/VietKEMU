using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
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
    public int LastAudioTrackCount { get; private set; }
    public bool LastTrackSwitchSucceeded { get; private set; }
    public bool PreserveStereo { get; set; }
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
        Native.Playing += (_, _) => Post(() => { if(firstFrame)ConfirmedPause?.Invoke(false); });
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
                // libVLC's Playing event precedes creation of its audio output.
                // Raise the original start only when decoding/output is ready;
                // its channel and gain calls then operate on a real output.
                Original?.OnStart();ConfirmedPause?.Invoke(false);Started?.Invoke();
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
        if(PreserveStereo)
        {
            LastAudioTrackCount=Native.AudioTrackDescription.Count(track=>track.Id>=0);
            LastTrackSwitchSucceeded=Native.SetChannel(AudioOutputChannel.Stereo);
            if(LastTrackSwitchSucceeded)Post(()=>ConfirmedTrack?.Invoke());
            return LastTrackSwitchSucceeded;
        }
        var tracks = Native.AudioTrackDescription.Where(track => track.Id >= 0).ToArray();
        LastAudioTrackCount=tracks.Length;LastTrackSwitchSucceeded=false;
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
        LastTrackSwitchSucceeded=changed;
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
    public TelevisionOverlay Overlay { get; }
    public bool BlackVisible => black.Visibility == Visibility.Visible;
    public TelevisionWindow(MediaPlayer player)
    {
        Title = "VietK — TV output"; Width = 960; Height = 540; Background = Brushes.Black;
        // activity_osd: full video surface, black cover above it, then loading,
        // playback hint and grading containers. Missing OSD layers stay absent.
        black = new Border { Background = Brushes.Black };
        Overlay=new TelevisionOverlay(Path.Combine(AppContext.BaseDirectory,"Original"));
        var layers=new Grid();layers.Children.Add(black);
        layers.Children.Add(new Viewbox { Child=Overlay.Canvas,Stretch=Stretch.Uniform });
        Content = new VideoView { MediaPlayer = player, Content = layers };
    }
    public void SetBlack(bool visible) => black.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!allowClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }
    public void ClosePermanently() { allowClose = true; Close(); }
    public void Detach() => ((VideoView)Content).MediaPlayer = null;
    public BitmapSource CompositePreview(BitmapSource? video)
    {
        Overlay.Canvas.Measure(new Size(1280,720));Overlay.Canvas.Arrange(new Rect(0,0,1280,720));
        // Preserve the full transparent TV coordinate space. A default
        // VisualBrush crops to occupied content and expands the logo to fill
        // the preview when it is the only visible overlay.
        var overlayImage=new RenderTargetBitmap(1280,720,96,96,PixelFormats.Pbgra32);
        overlayImage.Render(Overlay.Canvas);
        var drawing=new DrawingVisual();using(var context=drawing.RenderOpen())
        {
            var bounds=new Rect(0,0,640,360);context.DrawRectangle(Brushes.Black,null,bounds);
            if(!BlackVisible && video is not null)context.DrawImage(video,bounds);
            context.DrawImage(overlayImage,bounds);
        }
        var result=new RenderTargetBitmap(640,360,96,96,PixelFormats.Pbgra32);result.Render(drawing);result.Freeze();return result;
    }
}

public sealed class NativePlayback : IDisposable
{
    private readonly BottomBar bottom;
    private readonly string stateFile;
    private readonly string previewDirectory;
    private readonly DispatcherTimer previewTimer;
    private string? pendingPreview;
    private DateTime pendingPreviewAt;
    private bool playingIdle;
    private int previewSequence;
    private BitmapSource? previewVideo;
    public BitmapSource? PreviewFrame { get; private set; }
    public int DecodedPreviewFrames { get; private set; }
    public event Action<BitmapSource>? PreviewFrameChanged;
    public WindowsVideoDecoder Decoder { get; }
    public OriginalVideoPlayer Player { get; }
    public TelevisionWindow Television { get; }
    public event Action? NextRequested;
    public event Action? LocalMediaRequested;
    public Func<string,bool>? CommandOverride { get; set; }
    public SongMedia? CurrentMedia { get; private set; }
    public NativePlayback(BottomBar bottom, string stateDirectory)
    {
        this.bottom = bottom;
        stateFile = Path.Combine(stateDirectory, "playback-state.json");
        Decoder = new WindowsVideoDecoder(Dispatcher.CurrentDispatcher);
        Television = new TelevisionWindow(Decoder.Native);
        previewDirectory=Path.Combine(stateDirectory,"preview");Directory.CreateDirectory(previewDirectory);
        previewTimer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(500) };
        previewTimer.Tick+=(_,_)=>RefreshPreview();previewTimer.Start();
        Player = new OriginalVideoPlayer(Decoder, Television.SetBlack);
        Decoder.Original = Player;
        Decoder.ConfirmedPause = paused => { bottom.SetConfirmedPlaybackState(paused, Player.SingMode == OriginalSingMode.Original);
            Television.Overlay.SetPaused(paused); };
        Decoder.ConfirmedTrack = () => bottom.SetConfirmedPlaybackState(Player.State == OriginalVideoState.Pause,
            Player.SingMode == OriginalSingMode.Original);
        Player.Completed += () => Dispatcher.CurrentDispatcher.BeginInvoke(() =>
        {
            if(playingIdle) { StartIdleDemo();return; }
            if(CommandOverride?.Invoke("decoder_completed")!=true)NextRequested?.Invoke();
        });
        if (File.Exists(stateFile)) Decoder.SetOutputVolumeStep(JsonSerializer.Deserialize<PlaybackPreferences>(File.ReadAllText(stateFile))!.Volume);
    }
    public void ShowTelevision(Window panel)
    { Television.Show(); }
    public Image CreatePanelPreview()
    {
        var image=new Image { Stretch=Stretch.Uniform,Source=PreviewFrame };
        void Update(BitmapSource frame)=>image.Source=frame;
        image.Loaded+=(_,_)=> { image.Source=PreviewFrame;PreviewFrameChanged+=Update; };
        image.Unloaded+=(_,_)=>PreviewFrameChanged-=Update;
        return image;
    }
    public bool StartIdleDemo()
    {
        // BroadcastListManager / USBSetBroadcastDialog: Demo.mp4 is a separate
        // idle broadcast, not the APK's grade_video.mp4 scoring animation.
        var paths=new[] { Path.Combine(Path.GetDirectoryName(stateFile)!,"Demo.mp4"),
            Path.Combine(AppContext.BaseDirectory,"Demo.mp4"),
            Path.Combine(AppContext.BaseDirectory,"Original","player","Demo.mp4") };
        var demo=paths.FirstOrDefault(File.Exists);
        playingIdle=false;Player.Stop();CurrentMedia=null;ResetPreview();
        Television.Overlay.SetSong("");
        if(demo is null)return false;
        demo=Path.GetFullPath(demo);
        Decoder.PreserveStereo=true;Player.SetTrackInfo(0,1);Player.SetVolume(1);
        playingIdle=Player.SetSource(demo)==0 && Player.Play()==0;
        return playingIdle;
    }
    private void ResetPreview()
    {
        pendingPreview=null;previewVideo=null;
        Television.Overlay.SetPaused(false);
    }
    public bool PlayMedia(string path, SongMedia? metadata = null,bool preserveStereo=false)
    {
        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri) || (uri.IsFile && !File.Exists(uri.LocalPath))) return false;
        LocalMediaRequested?.Invoke();
        playingIdle=false;Player.Stop(); CurrentMedia = metadata;ResetPreview();
        Decoder.PreserveStereo=preserveStereo;
        Player.SetTrackInfo(metadata?.OriginalTrack ?? 0, metadata?.AccompanyTrack ?? 1);
        // KmPlayerCtrlImpl.getMediaVolume; configured HDD scale defaults to 1.
        var gain=(metadata?.DefaultVolume??100)/100f;
        Player.SetVolume(gain <= 0 ? 0.8f : gain);
        return Player.SetSource(path) == 0 && Player.Play() == 0;
    }
    public void Command(string command)
    {
        if(CommandOverride?.Invoke(command)==true)return;
        switch (command)
        {
            case "play_imv": case "pause_imv":
                if (Player.State == OriginalVideoState.Play) Player.Pause();
                else if (Player.State == OriginalVideoState.Pause) Player.Play();
                Television.Overlay.SetPaused(Player.State==OriginalVideoState.Pause);
                if(Player.State==OriginalVideoState.Play)Television.Overlay.ShowControl("play");
                break;
            case "ori_imv": case "accp_imv":
                if (CurrentMedia is { OriginalTrack: 0, AccompanyTrack: 5 } or { OriginalTrack: 5, AccompanyTrack: 0 }) break;
                Player.SetSingMode(Player.SingMode == OriginalSingMode.Original ? OriginalSingMode.Accompaniment : OriginalSingMode.Original);
                break;
            case "replay_imv":
                if ((Player.State is OriginalVideoState.Play or OriginalVideoState.Pause) && Player.Source is { } path)
                    PlayMedia(path, CurrentMedia,Decoder.PreserveStereo);
                Television.Overlay.ShowControl("replay");
                break;
            case "cut_song_imv": Player.Stop(); NextRequested?.Invoke(); break;
            case "volinc": case "voldec":
                Decoder.SetOutputVolumeStep(Decoder.OutputVolumeStep + (command == "volinc" ? 1 : -1));
                Directory.CreateDirectory(Path.GetDirectoryName(stateFile)!);
                File.WriteAllText(stateFile, JsonSerializer.Serialize(new PlaybackPreferences(Decoder.OutputVolumeStep)));
                Television.Overlay.ShowControl("play_ctrl_audio_bg",Decoder.OutputVolumeStep);
                break;
        }
    }
    private void RefreshPreview()
    {
        if(PreviewFrameChanged is null)return;
        try
        {
            if(pendingPreview is not null && DateTime.UtcNow-pendingPreviewAt>TimeSpan.FromSeconds(3))pendingPreview=null;
            if(pendingPreview is not null && File.Exists(pendingPreview) && new FileInfo(pendingPreview).Length>0)
            {
                using var stream=File.OpenRead(pendingPreview);var image=new BitmapImage();image.BeginInit();
                image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();
                previewVideo=image;pendingPreview=null;DecodedPreviewFrames++;
            }
            PreviewFrame=Television.CompositePreview(previewVideo);PreviewFrameChanged?.Invoke(PreviewFrame);
            if(pendingPreview is null && !Television.BlackVisible)
            {
                var path=Path.GetFullPath(Path.Combine(previewDirectory,"frame-"+(previewSequence++%2)+".png"));
                if(File.Exists(path))File.Delete(path);
                if(Decoder.Native.TakeSnapshot(0,path,640,360)) { pendingPreview=path;pendingPreviewAt=DateTime.UtcNow; }
            }
        }
        catch(IOException) { }
        catch(System.IO.FileFormatException) { }
        catch(System.NotSupportedException) { }
    }
    public void Dispose() { previewTimer.Stop();Television.Overlay.Stop();Television.Detach(); Television.ClosePermanently(); Decoder.Dispose(); }
    private sealed record PlaybackPreferences(int Volume);
}
