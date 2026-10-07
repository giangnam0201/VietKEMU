using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VietK.Core;

// Original service roles: top/search -> cloud key -> direct audio URL.
// Public provider metadata and yt-dlp replace the Android-only companion service.
public sealed class CloudMusicClient(string tools,Func<string?>? cookiesFile=null,Func<bool>? firefox=null)
{
    private static readonly HttpClient http=new() { Timeout=TimeSpan.FromSeconds(30) };
    public static string? TrackUrl(string value,string provider)
    {
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri)||uri.Scheme!="https"||uri.UserInfo.Length!=0||!uri.IsDefaultPort)return null;
        var host=provider=="SoundCloud"?"soundcloud.com":provider=="Mixcloud"?"mixcloud.com":"";
        if(host.Length==0||uri.Host!=host&&uri.Host!="www."+host)return null;
        var parts=uri.AbsolutePath.Trim('/').Split('/');
        if(parts.Length!=2||parts.Any(part=>part.Length==0||part is "." or ".."))return null;
        if(provider=="SoundCloud"&&parts[0] is "discover" or "charts" or "search")return null;
        return "https://"+host+uri.AbsolutePath.TrimEnd('/')+(provider=="Mixcloud"?"/":"");
    }
    public static YouTubeVideo Item(string url,string provider,string title,string creator="",string thumbnail="")
    {
        url=TrackUrl(url,provider)??throw new ArgumentException("Invalid cloud track URL");
        var id=provider.ToLowerInvariant()+"-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url))).ToLowerInvariant()[..24];
        return new(id,title,creator,thumbnail,provider,url);
    }
    private string Tool()=>File.Exists(Path.Combine(tools,"yt-dlp.exe"))?Path.GetFullPath(Path.Combine(tools,"yt-dlp.exe")):
        throw new FileNotFoundException("Thiếu yt-dlp. Hãy giải nén đầy đủ bản Windows.");
    private IEnumerable<string> Arguments()
    {
        var args=new List<string>{"--ignore-config","--no-plugin-dirs","--no-warnings","--no-colors","--encoding","utf-8","--socket-timeout","20","--retries","2"};
        if(cookiesFile?.Invoke() is { Length:>0 } file) { if(!File.Exists(file))throw new FileNotFoundException("Không tìm thấy tệp cookies.");args.AddRange(["--cookies",Path.GetFullPath(file)]); }
        else if(firefox?.Invoke()==true)args.AddRange(["--cookies-from-browser","firefox"]);
        return args;
    }
    public async Task<IReadOnlyList<YouTubeVideo>> Search(string provider,string query,CancellationToken token)
    {
        if(provider is not ("SoundCloud" or "Mixcloud"))throw new ArgumentException("Unknown audio provider");
        if(TrackUrl(query.Trim(),provider) is { } link)
        {
            var info=await YouTubeMusicClient.Run(Tool(),Arguments().Concat(["--skip-download","--no-playlist","--dump-single-json","--",link]),null,token,TimeSpan.FromMinutes(2));
            return ParseExtractor(info,provider);
        }
        if(Uri.TryCreate(query,UriKind.Absolute,out var _))throw new ArgumentException("Liên kết không thuộc nguồn âm nhạc đang chọn.");
        if(provider=="SoundCloud")
        {
            // A blank search is a named discovery query, never mislabeled as
            // the unavailable manufacturer's TOP ranking.
            var term=string.IsNullOrWhiteSpace(query)?"Nhạc Việt":query;
            var json=await YouTubeMusicClient.Run(Tool(),Arguments().Concat(["--flat-playlist","--dump-single-json","--","scsearch48:"+term]),null,token,TimeSpan.FromMinutes(2));
            return ParseExtractor(json,provider);
        }
        var endpoint=string.IsNullOrWhiteSpace(query)?"popular/?limit=48":"search/?type=cloudcast&limit=48&q="+Uri.EscapeDataString(query);
        using var response=await http.GetAsync("https://api.mixcloud.com/"+endpoint,HttpCompletionOption.ResponseHeadersRead,token);
        response.EnsureSuccessStatusCode();
        await using var stream=await response.Content.ReadAsStreamAsync(token);
        using var memory=new MemoryStream();var buffer=new byte[16384];int count;
        while((count=await stream.ReadAsync(buffer,token))>0) { if(memory.Length+count>4000000)throw new IOException("Danh sách Mixcloud quá lớn.");await memory.WriteAsync(buffer.AsMemory(0,count),token); }
        return ParseMixcloud(Encoding.UTF8.GetString(memory.ToArray()));
    }
    public static IReadOnlyList<YouTubeVideo> ParseExtractor(string json,string provider)
    {
        using var document=JsonDocument.Parse(json);var root=document.RootElement;var result=new List<YouTubeVideo>();
        IEnumerable<JsonElement> entries=root.TryGetProperty("entries",out var list)&&list.ValueKind==JsonValueKind.Array?list.EnumerateArray():[root];
        foreach(var entry in entries.Take(48))
        {
            if(entry.ValueKind!=JsonValueKind.Object)continue;
            var url=Text(entry,"webpage_url");if(TrackUrl(url,provider) is null)url=Text(entry,"url");
            if(TrackUrl(url,provider) is null)continue;
            var thumbnail=Text(entry,"thumbnail");
            if(thumbnail.Length==0&&entry.TryGetProperty("thumbnails",out var thumbs)&&thumbs.ValueKind==JsonValueKind.Array)
                thumbnail=thumbs.EnumerateArray().Select(image=>Text(image,"url")).LastOrDefault()??"";
            var creator=Text(entry,"uploader");if(creator.Length==0)creator=Text(entry,"channel");
            result.Add(Item(url,provider,Text(entry,"title"),creator,ImageUrl(thumbnail)));
        }
        return result.DistinctBy(item=>item.Id).ToArray();
    }
    public static IReadOnlyList<YouTubeVideo> ParseMixcloud(string json)
    {
        using var document=JsonDocument.Parse(json);var result=new List<YouTubeVideo>();
        if(!document.RootElement.TryGetProperty("data",out var list)||list.ValueKind!=JsonValueKind.Array)return result;
        foreach(var entry in list.EnumerateArray().Take(48))
        {
            var url=Text(entry,"url");if(TrackUrl(url,"Mixcloud") is null)continue;
            var creator=entry.TryGetProperty("user",out var user)?Text(user,"name"):"";
            var image=entry.TryGetProperty("pictures",out var images)?Text(images,"large"):"";
            result.Add(Item(url,"Mixcloud",Text(entry,"name"),creator,ImageUrl(image)));
        }
        return result.DistinctBy(item=>item.Id).ToArray();
    }
    public async Task<string> Resolve(YouTubeVideo track,CancellationToken token)
    {
        var link=TrackUrl(track.Url,track.Provider)??throw new ArgumentException("Invalid cloud track");
        var json=await YouTubeMusicClient.Run(Tool(),Arguments().Concat(["--no-playlist","--skip-download","--format","bestaudio/best","--dump-single-json","--",link]),null,token,TimeSpan.FromMinutes(2));
        using var document=JsonDocument.Parse(json);var url=Text(document.RootElement,"url");
        if(!Uri.TryCreate(url,UriKind.Absolute,out var media)||media.Scheme is not ("http" or "https")||media.UserInfo.Length!=0)
            throw new IOException("Nguồn không trả về luồng âm thanh phát được. Nội dung có thể yêu cầu tài khoản hoặc không còn khả dụng.");
        return media.AbsoluteUri;
    }
    private static string Text(JsonElement entry,string field)=>entry.ValueKind==JsonValueKind.Object&&entry.TryGetProperty(field,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString()??"":"";
    private static string ImageUrl(string url)=>Uri.TryCreate(url,UriKind.Absolute,out var uri)&&uri.Scheme=="https"?uri.AbsoluteUri:"";
}
