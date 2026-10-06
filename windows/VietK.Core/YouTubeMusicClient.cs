using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VietK.Core;

public sealed record YouTubeVideo(string Id,string Title,string Channel,string Thumbnail);
public sealed record YouTubeTransferProgress(long Received,long Total,string State);

// Arguments never pass through a shell. Authentication is optional and only
// uses a cookie file explicitly supplied by the user; no browser auto-discovery.
public sealed class YouTubeMusicClient(string toolDirectory,string cacheDirectory,Func<string?>? cookiesFile=null)
{
    public static string? VideoId(string value)
    {
        if(Regex.IsMatch(value,"^[a-zA-Z0-9_-]{11}$"))return value;
        if(!Uri.TryCreate(value,UriKind.Absolute,out var uri) || uri.Scheme is not ("https" or "http"))return null;
        if(uri.Host is "youtu.be" or "www.youtu.be")return VideoId(uri.AbsolutePath.Trim('/'));
        if(uri.Host is not ("youtube.com" or "www.youtube.com" or "m.youtube.com" or "music.youtube.com"))return null;
        if(uri.AbsolutePath is "/watch")
            foreach(var field in uri.Query.TrimStart('?').Split('&'))
            { var pair=field.Split('=',2);if(pair.Length==2 && pair[0]=="v")return VideoId(Uri.UnescapeDataString(pair[1])); }
        var parts=uri.AbsolutePath.Trim('/').Split('/');
        return parts.Length==2 && parts[0] is "shorts" or "live" or "embed"?VideoId(parts[1]):null;
    }
    private string Tool(string name)
    {
        var path=Path.GetFullPath(Path.Combine(toolDirectory,name+".exe"));
        if(!File.Exists(path))throw new FileNotFoundException("YouTube tool missing: "+name+". Extract the complete Windows test ZIP.");
        return path;
    }
    private IEnumerable<string> Common()
    {
        var arguments=new List<string> { "--ignore-config","--no-plugin-dirs","--no-warnings","--no-colors","--encoding","utf-8",
            "--js-runtimes","deno:"+Tool("deno"),"--socket-timeout","20","--retries","3" };
        if(cookiesFile?.Invoke() is { Length:>0 } file)
        {
            if(!File.Exists(file))throw new FileNotFoundException("The configured YouTube cookie file is missing; choose it again or clear it in the panel.");
            arguments.Add("--cookies");arguments.Add(Path.GetFullPath(file));
        }
        return arguments;
    }
    public async Task<IReadOnlyList<YouTubeVideo>> Search(string query,CancellationToken cancellation)
    {
        if(string.IsNullOrWhiteSpace(query))return [];
        if(VideoId(query.Trim()) is { } id)return [new(id,"YouTube "+id,"","")];
        var json=await Run(Tool("yt-dlp"),Common().Concat(new[]{"--flat-playlist","--dump-single-json","--","ytsearch12:"+query}),null,cancellation,TimeSpan.FromMinutes(2));
        return ParseSearch(json);
    }
    public static IReadOnlyList<YouTubeVideo> ParseSearch(string json)
    {
        using var document=JsonDocument.Parse(json);var result=new List<YouTubeVideo>();
        if(!document.RootElement.TryGetProperty("entries",out var entries) || entries.ValueKind!=JsonValueKind.Array)return result;
        foreach(var item in entries.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object || !item.TryGetProperty("id",out var identifier) ||
                identifier.ValueKind!=JsonValueKind.String || VideoId(identifier.GetString()!) is not { } id)continue;
            string Text(string name)=>item.TryGetProperty(name,out var value) && value.ValueKind==JsonValueKind.String?value.GetString()!:"";
            var thumbnail="";
            if(item.TryGetProperty("thumbnails",out var thumbnails) && thumbnails.ValueKind==JsonValueKind.Array)
                foreach(var image in thumbnails.EnumerateArray())
                    if(image.TryGetProperty("url",out var url) && url.ValueKind==JsonValueKind.String &&
                        Uri.TryCreate(url.GetString(),UriKind.Absolute,out var uri) && uri.Scheme=="https")thumbnail=uri.AbsoluteUri;
            result.Add(new(id,Text("title"),Text("channel"),thumbnail));
        }
        return result;
    }
    public async Task<string> Download(YouTubeVideo video,Action<YouTubeTransferProgress> progress,CancellationToken cancellation)
    {
        if(VideoId(video.Id)!=video.Id)throw new ArgumentException("Invalid YouTube video ID");
        var directory=Path.GetFullPath(Path.Combine(cacheDirectory,video.Id));Directory.CreateDirectory(directory);
        var destination=Path.Combine(directory,video.Id+".mkv");var marker=destination+".complete";
        if(File.Exists(marker) && File.Exists(destination) &&
            long.TryParse(await File.ReadAllTextAsync(marker,cancellation),out var length) && new FileInfo(destination).Length==length && length>0)return destination;
        var completed="";
        await Run(Tool("yt-dlp"),Common().Concat(new[]{"--no-playlist","--no-simulate","--newline","--progress",
            "--ffmpeg-location",Path.GetFullPath(toolDirectory),"--format","bv*[height<=1080]+ba/b[height<=1080]/b",
            "--merge-output-format","mkv","--remux-video","mkv","--output",Path.Combine(directory,video.Id+".%(ext)s"),
            "--progress-template","download:VietKProgress:%(progress)j","--print","after_move:VietKFile:%(filepath)s",
            "--","https://www.youtube.com/watch?v="+video.Id}),line=>
        {
            if(line.StartsWith("VietKFile:",StringComparison.Ordinal))completed=Path.GetFullPath(line[10..]);
            else if(line.StartsWith("VietKProgress:",StringComparison.Ordinal))
            {
                using var data=JsonDocument.Parse(line[14..]);var root=data.RootElement;
                long Number(string key)=>root.TryGetProperty(key,out var value) && value.ValueKind==JsonValueKind.Number && value.TryGetInt64(out var n)?n:0;
                progress(new(Number("downloaded_bytes"),Math.Max(Number("total_bytes"),Number("total_bytes_estimate")),
                    root.TryGetProperty("status",out var state)?state.GetString()??"":""));
            }
        },cancellation,TimeSpan.FromMinutes(30));
        cancellation.ThrowIfCancellationRequested();
        if(!string.Equals(completed,destination,StringComparison.OrdinalIgnoreCase) || !File.Exists(destination) || new FileInfo(destination).Length==0)
            throw new IOException("YouTube transfer did not produce a completed video");
        var probe=await Run(Tool("ffprobe"),["-v","error","-show_entries","stream=codec_type","-of","json",destination],null,cancellation,TimeSpan.FromSeconds(30));
        using var inspection=JsonDocument.Parse(probe);
        var streams=inspection.RootElement.GetProperty("streams").EnumerateArray().Select(s=>s.GetProperty("codec_type").GetString()).ToArray();
        if(!streams.Contains("video") || !streams.Contains("audio"))throw new IOException("Downloaded video is missing video or audio");
        await File.WriteAllTextAsync(marker+".tmp",new FileInfo(destination).Length.ToString(),cancellation);
        File.Move(marker+".tmp",marker,true);
        return destination;
    }
    private static async Task<string> Run(string executable,IEnumerable<string> arguments,Action<string>? output,
        CancellationToken cancellation,TimeSpan duration)
    {
        var start=new ProcessStartInfo(executable) { UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 };
        foreach(var argument in arguments)start.ArgumentList.Add(argument);
        using var process=Process.Start(start)??throw new IOException("Could not start YouTube tool");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation);timeout.CancelAfter(duration);
        var errors=new Queue<string>();var text=new StringBuilder();
        async Task ReadOutput()
        {
            while(await process.StandardOutput.ReadLineAsync(timeout.Token) is { } line)
            { if(output is not null)output(line);else { if(text.Length+line.Length>8000000)throw new IOException("YouTube response is too large");text.AppendLine(line); } }
        }
        async Task ReadErrors()
        {
            while(await process.StandardError.ReadLineAsync(timeout.Token) is { } line)
            { errors.Enqueue(line);if(errors.Count>8)errors.Dequeue(); }
        }
        try { await Task.WhenAll(ReadOutput(),ReadErrors(),process.WaitForExitAsync(timeout.Token)); }
        catch
        { if(!process.HasExited)process.Kill(true);await process.WaitForExitAsync(CancellationToken.None);throw; }
        if(process.ExitCode!=0)throw new IOException("YouTube fetch failed: "+string.Join("\n",errors));
        return text.ToString();
    }
}
