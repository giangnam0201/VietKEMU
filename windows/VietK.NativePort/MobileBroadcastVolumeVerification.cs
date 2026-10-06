using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using VietK.Core;

namespace VietK.NativePort;

internal static class MobileBroadcastVolumeVerification
{
    internal static async Task<(int Volume,bool Muted)> Run(NativePlayback playback,HttpClient client,string output)
    {
        var original=(playback.BroadcastVolumeSettings.Volume,playback.BroadcastVolumeSettings.Muted);
        var live=playback.Decoder.OutputVolumeStep;var session=playback.BroadcastSessionVolume;var muted=playback.BroadcastSessionMuted;var source=playback.Source;var songDefault=playback.DefaultVolumeSettings.Volume;
        using(var unpaired=new HttpClient { BaseAddress=client.BaseAddress })
        {
            Require((await unpaired.GetAsync("api/settings/broadcast-volume")).StatusCode==HttpStatusCode.Unauthorized,"Unpaired idle-volume read accepted");
            using var write=await unpaired.PostAsJsonAsync("api/settings/broadcast-volume",new { publishVolume=0,isMuteOfPublishVolume="1" });Require(write.StatusCode==HttpStatusCode.Unauthorized,"Unpaired idle-volume write accepted");
        }
        using(var cross=new HttpRequestMessage(HttpMethod.Post,"api/settings/broadcast-volume"))
        {
            cross.Headers.Add("Origin","https://example.invalid");cross.Content=JsonContent.Create(new { publishVolume=0,isMuteOfPublishVolume="1" });
            Require((await client.SendAsync(cross)).StatusCode==HttpStatusCode.Forbidden,"Cross-origin idle-volume write accepted");
        }
        foreach(var volume in new[]{0,20,8})foreach(var mute in new[]{"0","1"})
        {
            using var response=await client.PostAsJsonAsync("api/settings/broadcast-volume",new { publishVolume=volume,isMuteOfPublishVolume=mute });Require(response.IsSuccessStatusCode,"Idle settings request rejected");
            using var value=JsonDocument.Parse(await client.GetStringAsync("api/settings/broadcast-volume"));
            Require(value.RootElement.GetProperty("publishVolume").GetInt32()==volume&&value.RootElement.GetProperty("isMuteOfPublishVolume").GetString()==mute&&value.RootElement.GetProperty("maxPublishMusic").GetInt32()==20&&value.RootElement.GetProperty("restartTip").GetString()==OriginalBroadcastVolumeSettings.RestartTip,"Original idle fields lost");
        }
        foreach(var json in new[]{"{}","{\"publishVolume\":21,\"isMuteOfPublishVolume\":\"0\"}","{\"publishVolume\":-1,\"isMuteOfPublishVolume\":\"0\"}","{\"publishVolume\":1.5,\"isMuteOfPublishVolume\":\"0\"}","{\"publishVolume\":\"8\",\"isMuteOfPublishVolume\":\"0\"}","{\"publishVolume\":8,\"isMuteOfPublishVolume\":true}","{\"publishVolume\":8,\"isMuteOfPublishVolume\":\"2\"}"})
        { using var response=await client.PostAsync("api/settings/broadcast-volume",new StringContent(json,System.Text.Encoding.UTF8,"application/json"));Require(response.StatusCode==HttpStatusCode.BadRequest,"Invalid idle settings accepted");Require(playback.BroadcastVolumeSettings.Volume==8&&playback.BroadcastVolumeSettings.Muted,"Rejected idle request changed settings"); }
        Require(playback.Decoder.OutputVolumeStep==live&&playback.BroadcastSessionVolume==session&&playback.BroadcastSessionMuted==muted&&playback.Source==source&&playback.DefaultVolumeSettings.Volume==songDefault,"Phone idle configuration changed live playback or song default");
        using(var seed=await client.PostAsJsonAsync("api/settings/broadcast-volume",new { publishVolume=8,isMuteOfPublishVolume="0" }))Require(seed.IsSuccessStatusCode,"Idle browser fixture rejected");
        File.WriteAllText(Path.Combine(output,"broadcast-volume-http-verification.json"),JsonSerializer.Serialize(new { originalFieldsAndBounds=true,invalidRequestsPreserveConfig=true,pairedAuthorizationAndOrigin=true,sessionAndSongDefaultUnaffected=true }));
        return original;
    }
    private static void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
}
