using System.Text.Json;
using VietK.Core;
internal static class CloudMusicChecks
{
    public static void Run()
    {
        static void Require(bool valid,string message) { if(!valid)throw new InvalidDataException(message); }
        foreach(var provider in new[]{"SoundCloud","Mixcloud"})
        {
            var host=provider=="SoundCloud"?"soundcloud.com":"mixcloud.com";
            var url="https://"+host+"/fixture/track";
            var item=CloudMusicClient.Item(url,provider,"Nhạc thử","Tác giả");
            Require(item.Id==CloudMusicClient.Item(url,provider,"Other title").Id,"Cloud queue identity depends on title");
            Require(JsonSerializer.Deserialize<YouTubeVideo>(JsonSerializer.Serialize(item))==item,"Cloud queue did not round-trip");
            foreach(var bad in new[]{"file:///secret","https://"+host+".evil.invalid/artist/track","https://user@"+host+"/artist/track","--exec=bad","https://"+host+"/artist"})
                Require(CloudMusicClient.TrackUrl(bad,provider) is null,"Invalid provider URL admitted");
        }
        var sc=CloudMusicClient.ParseExtractor("""{"entries":[null,{"webpage_url":"https://soundcloud.com/fixture/track","title":"Nhạc Việt","uploader":"Creator","thumbnails":[{"url":"https://i1.sndcdn.com/example.jpg"}]},{"url":"https://evil.invalid/a/b"}]}""","SoundCloud");
        Require(sc.Count==1&&sc[0].Channel=="Creator"&&sc[0].Provider=="SoundCloud"&&sc[0].Thumbnail.Contains("sndcdn"),"SoundCloud metadata missing or untrusted result admitted");
        var mix=CloudMusicClient.ParseMixcloud("""{"data":[{"url":"https://www.mixcloud.com/fixture/show/","name":"Mix tiếng Việt","user":{"name":"DJ"},"pictures":{"large":"https://thumbnailer.mixcloud.com/test.jpg"}},{"url":"https://evil.invalid/test/show/"}]}""");
        Require(mix.Count==1&&mix[0].Title=="Mix tiếng Việt"&&mix[0].Channel=="DJ"&&mix[0].Provider=="Mixcloud","Mixcloud metadata not retained");
        var old=JsonSerializer.Deserialize<YouTubeVideo>("""{"Id":"YE7VzlLtp-4","Title":"Original queue","Channel":"","Thumbnail":""}""");
        Require(old?.Provider=="YouTube","Existing YouTube queues lost default provider");
        Console.WriteLine("Cloud provider metadata, mixed queue persistence and validated track identities verified; live availability not claimed.");
    }
}
