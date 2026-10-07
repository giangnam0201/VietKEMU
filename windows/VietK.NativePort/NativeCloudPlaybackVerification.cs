using System.IO;
using System.Text.Json;
using VietK.Core;
namespace VietK.NativePort;
internal static class NativeCloudPlaybackVerification
{
    public static async Task Run(NativePlayback playback,string fixtures,string output)
    {
        NativePlaybackVerification.PcmTap? tap=null;
        playback.ConfigureCloudDecoder=decoder=>tap=new(decoder.Native);
        try
        {
            var before=playback.DecodedPreviewFrames;
            if(!playback.PlayCloudAudio(new Uri(Path.GetFullPath(Path.Combine(fixtures,"stereo.mkv"))).AbsoluteUri))throw new InvalidDataException("Cloud audio fixture did not open");
            var deadline=DateTime.UtcNow.AddSeconds(15);
            while((playback.Player.State!=OriginalVideoState.Play||playback.DecodedPreviewFrames<before+3)&&DateTime.UtcNow<deadline)await Task.Delay(50);
            if(!playback.IsCloudAudio||playback.Player.State!=OriginalVideoState.Play||playback.DecodedPreviewFrames<before+3||tap is null)throw new InvalidDataException("Audio and background preview did not run independently");
            var samples=await NativePlaybackVerification.CaptureSamples(tap);
            if(NativePlaybackVerification.PcmTap.Power(samples,440,0)<.000001||NativePlaybackVerification.PcmTap.Power(samples,880,1)<.000001)throw new InvalidDataException("Cloud audio stereo PCM missing");
            if(playback.Television.Overlay.LoadingVisible)throw new InvalidDataException("Cloud loading indicator did not clear on audio start");
            playback.Command("pause_imv");await Task.Delay(250);
            if(playback.Player.State!=OriginalVideoState.Pause)throw new InvalidDataException("Cloud pause failed");
            var pausedFrames=playback.DecodedPreviewFrames;await Task.Delay(250);
            if(playback.DecodedPreviewFrames<=pausedFrames)throw new InvalidDataException("Audio pause froze background video");
            playback.Command("play_imv");await Task.Delay(250);
            if(playback.Player.State!=OriginalVideoState.Play)throw new InvalidDataException("Cloud resume failed");
            var volume=playback.Decoder.OutputVolumeStep;playback.Command("voldec");
            if(playback.Decoder.OutputVolumeStep!=Math.Max(0,volume-1))throw new InvalidDataException("Cloud volume did not target audio decoder");
            playback.Command("mute");if(!playback.Decoder.Muted)throw new InvalidDataException("Cloud mute failed");playback.Command("mute");
            playback.Player.Seek(5000);await Task.Delay(300);
            if(playback.Decoder.Position<4900)throw new InvalidDataException("Cloud audio seek failed");
            playback.StartIdleDemo();
            if(playback.IsCloudAudio||playback.Source!=PlaybackSource.Idle)throw new InvalidDataException("Cloud audio survived return to idle");
            File.WriteAllText(Path.Combine(output,"cloud-audio-verification.json"),JsonSerializer.Serialize(new { actualStereoPcm=true,independentDemoAndAudio=true,sharedPreviewContinuesDuringPause=true,loadingClearsOnStart=true,pauseResume=true,volumeMute=true,seek=true,returnsToIdle=true,liveProviderAccessVerified=false }));
        }
        finally { playback.ConfigureCloudDecoder=null;tap?.Dispose(); }
    }
}
