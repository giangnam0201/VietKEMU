using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using PixelFormats = System.Windows.Media.PixelFormats;
using LibVLCSharp.Shared;
using VietK.Core;

namespace VietK.NativePort;

public static class NativePlaybackVerification
{
    public static int Run(Application app, Canvas panel, BottomBar bottom, string root, string fixtures, string output)
    {
        Directory.CreateDirectory(output);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var host = new Window { Title = "VietK playback verification", Width = 1280, Height = 800,
            Content = new Viewbox { Child = panel } };
        app.MainWindow = host;
        using var playback = new NativePlayback(bottom, output);
        playback.PreviewFrameChanged += _ => { };
        using var tap = new PcmTap(playback.Decoder.Native);
        var result = 1;
        host.Loaded += async (_, _) =>
        {
            try
            {
                playback.ShowTelevision(host);
                Require(new WindowInteropHelper(host).Handle != IntPtr.Zero &&
                    new WindowInteropHelper(playback.Television).Handle != IntPtr.Zero &&
                    playback.Television.Owner is null, "Independent panel/TV window handles missing");
                var played = 0; playback.Player.Played += () => played++;
                await NativeMusicPipelineVerification.Run(playback,root,fixtures,output);
                played=0;
                var idlePreviewBefore=playback.DecodedPreviewFrames;
                Require(playback.StartIdleDemo(),"Bundled original idle background unavailable");
                await Until(()=>played>0 && playback.DecodedPreviewFrames>idlePreviewBefore,
                    "Bundled original idle background did not decode into the panel preview");
                Require(!string.IsNullOrWhiteSpace(playback.Television.Overlay.MarqueeText),"Idle marquee missing");
                var idleEncoder=new PngBitmapEncoder();idleEncoder.Frames.Add(BitmapFrame.Create(playback.PreviewFrame!));
                using(var idleFile=File.Create(Path.Combine(output,"bundled-idle-preview.png")))idleEncoder.Save(idleFile);
                played=0;
                var clip = Path.GetFullPath(Path.Combine(root, "player", "grade_video.mp4"));
                var previewFramesBefore=playback.DecodedPreviewFrames;
                Require(playback.PlayMedia(clip), "Original APK grading video rejected");
                await Until(() => played > 0 && playback.Decoder.Position > 0, "Original video did not decode/render");
                Require(!playback.Television.BlackVisible, "Original black cover stayed above playing media");
                var previewFixture=new byte[640*360*4];
                for(var pixel=0;pixel<previewFixture.Length;pixel+=4)
                { previewFixture[pixel]=31;previewFixture[pixel+1]=63;previewFixture[pixel+2]=127;previewFixture[pixel+3]=255; }
                var composed=playback.Television.CompositePreview(BitmapSource.Create(640,360,96,96,
                    PixelFormats.Bgra32,null,previewFixture,640*4));
                var corner=new byte[4];composed.CopyPixels(new Int32Rect(500,300,1,1),corner,4,0);
                Require(corner.SequenceEqual(new byte[] {31,63,127,255}),"TV logo overlay stretched across the panel video preview");
                var snapshot = Path.GetFullPath(Path.Combine(output, "original-tv-video.png"));
                playback.SaveVideoFrame(snapshot);
                await Until(() => File.Exists(snapshot) && new FileInfo(snapshot).Length > 1024, "Decoded video snapshot missing");
                await Until(() => playback.DecodedPreviewFrames>previewFramesBefore && playback.PreviewFrame is not null,
                    "Panel preview did not receive real decoder pixels");
                var previewEncoder=new PngBitmapEncoder();previewEncoder.Frames.Add(BitmapFrame.Create(playback.PreviewFrame!));
                using(var previewFile=File.Create(Path.Combine(output,"panel-tv-preview.png")))previewEncoder.Save(previewFile);

                var sourceStereo = Path.GetFullPath(Path.Combine(fixtures, "stereo.mkv"));
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
                string stereo;
                try
                {
                    var url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/stereo.mkv";
                    var serving=Task.Run(async()=>
                    {
                        for(var request=0;request<2;request++)
                        {
                        using var socket=await listener.AcceptTcpClientAsync();
                        await using var stream=socket.GetStream();
                        using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                        while(!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+new FileInfo(sourceStereo).Length+"\r\nConnection: close\r\n\r\n"));
                        if(request==1) { await using var file=File.OpenRead(sourceStereo);await file.CopyToAsync(stream); }
                        }
                    });
                    using var transfer=new OriginalMusicTransfer();
                    stereo=await transfer.Download(101000,url,Path.Combine(output,"downloaded"),(_,_)=>{},CancellationToken.None);
                    await serving.WaitAsync(TimeSpan.FromSeconds(15));
                    Require(SHA256.HashData(File.ReadAllBytes(stereo)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(sourceStereo))),
                        "HTTP download changed media bytes");
                }
                finally { listener.Stop(); }
                Require(playback.PlayMedia(stereo, Metadata(stereo, 0, 1)), "Stereo test media rejected");
                await Until(() => playback.Player.State == OriginalVideoState.Play && playback.Decoder.Native.AudioTrackDescription.Count(t => t.Id >= 0) == 1,
                    "Single audio track did not start");
                await Tone(tap, 880, 440, "Original channel should duplicate the right channel");
                playback.Command("ori_imv");
                await Tone(tap, 440, 880, "Accompaniment channel should duplicate the left channel");
                await Until(() => !bottom.OriginalVocal, "Panel track icon did not observe the decoder");
                playback.Command("pause_imv");
                await Until(() => bottom.Paused && playback.Decoder.Native.State == VLCState.Paused, "Pause button did not pause decoder");
                Require(playback.Television.Overlay.Paused,"TV pause indicator missing");
                var pausedAt = playback.Decoder.Position;
                await Task.Delay(350);
                Require(Math.Abs(playback.Decoder.Position - pausedAt) < 100, "Paused decoder clock kept running");
                playback.Command("play_imv");
                await Until(() => !bottom.Paused && playback.Decoder.Position > pausedAt, "Play button did not resume decoder");
                Require(playback.Player.Seek(6000) == 0, "Valid original seek rejected");
                await Until(() => playback.Decoder.Position >= 5800, "Native seek did not reach target");
                Require(playback.Player.Seek(-1) == -1 && playback.Player.Seek(playback.Decoder.Duration + 1) == -1,
                    "Original seek bounds were bypassed");
                playback.Command("replay_imv");
                await Until(() => playback.Player.State == OriginalVideoState.Play && playback.Decoder.Position is > 0 and < 2000,
                    "Replay did not restart actual media");
                var volume = playback.Decoder.OutputVolumeStep;
                var quietPower=await MeasurePower(tap,440);
                playback.Command("volinc");
                Require(playback.Decoder.OutputVolumeStep == volume + 1,"Original 0..20 volume step differs");
                Require(playback.Television.Overlay.LastControl=="play_ctrl_audio_bg" && playback.Television.Overlay.ControlVisible,
                    "TV volume feedback missing");
                // libVLC's amem output does not publish a volume report for its
                // getter. Verify actual decoded sample amplitude instead.
                var loudPower=await UntilPower(tap,440,value=>value>quietPower*1.15,
                    "Volume increment did not increase actual output PCM amplitude");
                playback.Command("voldec");
                Require(playback.Decoder.OutputVolumeStep == volume, "Volume decrement did not restore level");
                var restoredPower=await UntilPower(tap,440,value=>value>=quietPower*.8 && value<=quietPower*1.2,
                    "Volume decrement did not restore actual output PCM amplitude");

                Require(playback.PlayMedia(stereo,preserveStereo:true), "YouTube-style stereo media rejected");
                await Stereo(tap);
                playback.Command("replay_imv");
                await Stereo(tap);
                Require(playback.Decoder.PreserveStereo,"Replay lost stereo playback mode");

                var multi = Path.GetFullPath(Path.Combine(fixtures, "multiple.ts"));
                using(var growing=new ProgressiveVideo(Path.Combine(output,"growing-video.ts"),async (target,cancel)=>
                {
                    await using var source=File.OpenRead(multi);var chunk=new byte[8192];
                    while(true)
                    {
                        var count=await source.ReadAsync(chunk,cancel);if(count==0)break;
                        await target.WriteAsync(chunk.AsMemory(0,count),cancel);await target.FlushAsync(cancel);
                        await Task.Delay(100,cancel);
                    }
                    await Task.Delay(3000,cancel);
                },CancellationToken.None))
                {
                    await growing.WaitUntilReady(CancellationToken.None);
                    var before=playback.DecodedPreviewFrames;
                    Require(playback.PlayMedia(growing.Url,preserveStereo:true),"Growing video stream rejected");
                    await Until(()=>playback.DecodedPreviewFrames>before+10,"Growing stream did not produce live decoded frames");
                    Require(!growing.Completion.IsCompleted,"Video only started after download completed");
                    var frameStart=playback.DecodedPreviewFrames;await Task.Delay(1000);
                    Require(playback.DecodedPreviewFrames-frameStart>=10,"Shared TV/preview source still updates like snapshot polling");
                    playback.Player.Stop();
                }
                // Vocal mode persists between songs in the original player.
                // Select original explicitly before testing stream index 1.
                playback.Player.SetSingMode(OriginalSingMode.Original);
                Require(playback.PlayMedia(multi, Metadata(multi, 1, 0)), "Multiple-track MPEG media rejected");
                await Until(() => playback.Player.State == OriginalVideoState.Play && playback.Decoder.Native.AudioTrackDescription.Count(t => t.Id >= 0) == 2,
                    "Two MPEG audio streams not available");
                await Tone(tap, 1200, 480, "Original stream must select audio ordinal 1");
                playback.Command("accp_imv");
                await Tone(tap, 480, 1200, "Accompaniment stream must select audio ordinal 0");
                var next = 0; playback.NextRequested += () => next++;
                playback.Command("cut_song_imv");
                Require(next == 1 && playback.Player.State == OriginalVideoState.Idle && playback.Television.BlackVisible,
                    "Next button did not stop decoder and request queue advance");
                // Verify actual decoder end-of-stream, not a timer-generated
                // completion. Seek close to the end of the same MPEG file.
                Require(playback.PlayMedia(multi, Metadata(multi, 1, 0)), "Completion media failed to restart");
                await Until(() => playback.Player.State == OriginalVideoState.Play && playback.Decoder.Duration > 0, "Completion media did not start");
                playback.Player.Seek(playback.Decoder.Duration - 800);
                await Until(() => next == 2, "Actual decoder completion did not request queue advance");
                Require(playback.Player.State == OriginalVideoState.Idle, "Original completion state not idle");
                // This is explicitly a test fixture, not the original idle asset.
                File.Copy(multi,Path.Combine(output,"Demo.mp4"),true);
                var idleStarts=played;
                Require(playback.StartIdleDemo(),"Configured Demo.mp4 did not start");
                await Until(()=>played>idleStarts && playback.Decoder.Duration>0,"Idle video did not decode");
                playback.Player.Seek(playback.Decoder.Duration-800);
                await Until(()=>played>idleStarts+1,"Idle video did not loop after real decoder completion");
                Require(next==2,"Idle broadcast advanced the song queue");
                playback.Player.Stop();File.Delete(Path.Combine(output,"Demo.mp4"));
                File.WriteAllText(Path.Combine(output, "playback-verification.json"), JsonSerializer.Serialize(new
                {
                    nativeWindowsDecoder = "bundled libVLC", androidRuntimeUsed = false,
                    independentPanelAndTvWindows = true, originalApkVideoDecoded = true,
                    decodedPanelPreviewVerified=true, tvPauseAndVolumeFeedbackVerified=true,
                    configuredIdleDemoDecoderAndLoopVerified=true,
                    bundledOriginalBackgroundDecodedIntoPreview=true,
                    playbackBeforeDownloadCompletionVerified=true,sharedFrameRateAbove10FpsVerified=true,
                    stereoChannelPcmVerified = true, multipleAudioStreamPcmVerified = true,
                    youtubeStereoPcmAndReplayVerified = true,
                    pauseResumeClockVerified = true, nativeSeekReplayVerified = true,
                    panelPlaybackObserverVerified = true, volumeStepVerified = true,
                    volumePcmPowerBefore=quietPower,volumePcmPowerAfterIncrement=loudPower,volumePcmPowerRestored=restoredPower,
                    nextAndDecoderCompletionVerified = true,
                    httpDownloadedVideoHashAndDecoderVerified = true,
                    signedHttpLoginMediaRequestCacheQueueAndDecoderVerified = true,
                    scope = "Real video pixels and decoded PCM, with original player/control rules. Audio-device playback, full TV OSD, encrypted karaoke, storage, scoring and live downloads remain unverified."
                }, new JsonSerializerOptions { WriteIndented = true }));
                result = 0;
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(output, "playback-error.txt"), error+
                $"\nAudio tracks={playback.Decoder.LastAudioTrackCount}; switch succeeded={playback.Decoder.LastTrackSwitchSucceeded}; channel={playback.Decoder.Native.Channel}"); }
            finally { app.Shutdown(); }
        };
        app.Run(host);
        return result;
    }
    private static SongMedia Metadata(string path, int original, int accompaniment) =>
        new(1, 101000, path, 100, original, accompaniment, "0", "0", 1, "", "", "", "", 0, null, null, "decoder-fixture");
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static async Task Until(Func<bool> condition, string message)
    {
        var stop = DateTime.UtcNow.AddSeconds(15);
        while (!condition()) { if (DateTime.UtcNow >= stop) throw new TimeoutException(message); await Task.Delay(50); }
    }
    private static async Task Tone(PcmTap tap, int expected, int unwanted, string message)
    {
        var stop = DateTime.UtcNow.AddSeconds(5);
        double signal = 0, other = 0;
        do
        {
            tap.Reset(); await Task.Delay(300);
            var samples = tap.Read();
            signal = Math.Min(PcmTap.Power(samples, expected,0),PcmTap.Power(samples, expected,1));
            other = Math.Max(PcmTap.Power(samples, unwanted,0),PcmTap.Power(samples, unwanted,1));
            if (samples.Length >= 4800 && signal > 0.000001 && signal > other * 25) return;
        } while (DateTime.UtcNow < stop);
        throw new InvalidDataException($"{message}; decoded power expected={signal}, unwanted={other}");
    }
    private static async Task Stereo(PcmTap tap)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);
        do
        {
            tap.Reset();await Task.Delay(300);var samples=tap.Read();
            if(samples.Length>=4800 && PcmTap.Power(samples,440,0)>0.000001 &&
                PcmTap.Power(samples,880,1)>0.000001 &&
                PcmTap.Power(samples,440,0)>PcmTap.Power(samples,880,0)*25 &&
                PcmTap.Power(samples,880,1)>PcmTap.Power(samples,440,1)*25)return;
        }while(DateTime.UtcNow<deadline);
        throw new InvalidDataException("Native stereo playback collapsed or swapped the decoded left/right audio channels");
    }
    private static async Task<double> MeasurePower(PcmTap tap,int frequency)
    {
        tap.Reset();await Task.Delay(400);
        var samples=tap.Read();
        if(samples.Length<4800)throw new InvalidDataException("No decoded PCM available for gain verification");
        return Math.Min(PcmTap.Power(samples,frequency,0),PcmTap.Power(samples,frequency,1));
    }
    private static async Task<double> UntilPower(PcmTap tap,int frequency,Func<double,bool> accepted,string message)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);double power;
        do { power=await MeasurePower(tap,frequency);if(accepted(power))return power; } while(DateTime.UtcNow<deadline);
        throw new InvalidDataException(message+"; measured power="+power);
    }
    // Native audio output tap enables CI without an audio device, while checking
    // the actual downmix/stream-selection result in the decoder's PCM output.
    private sealed class PcmTap : IDisposable
    {
        private readonly object gate = new();
        private readonly List<short> samples = new();
        private readonly MediaPlayer.LibVLCAudioPlayCb callback;
        public PcmTap(MediaPlayer player)
        {
            callback = (_, data, count, _) =>
            {
                var values = new short[checked((int)count * 2)];
                Marshal.Copy(data, values, 0, values.Length);
                lock (gate) { if (samples.Count < 96000) samples.AddRange(values); }
            };
            player.SetAudioFormat("S16N", 48000, 2);
            player.SetAudioCallbacks(callback, null!, null!, null!, null!);
        }
        public void Reset() { lock (gate) samples.Clear(); }
        public short[] Read() { lock (gate) return samples.ToArray(); }
        public static double Power(short[] data, int frequency,int channel)
        {
            var count = data.Length / 2;
            if (count == 0) return 0;
            double sin = 0, cos = 0;
            for (var i = 0; i < count; i++)
            {
                var sample = data[i * 2+channel] / 32768.0;
                var phase = 2 * Math.PI * frequency * i / 48000;
                sin += sample * Math.Sin(phase); cos += sample * Math.Cos(phase);
            }
            return (sin * sin + cos * cos) / (count * (double)count);
        }
        public void Dispose() => GC.KeepAlive(callback);
    }
}
