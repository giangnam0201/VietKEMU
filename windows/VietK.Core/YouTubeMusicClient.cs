using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VietK.Core;

public sealed record YouTubeVideo(string Id,string Title,string Channel,string Thumbnail);
public sealed record YouTubeTransferProgress(long Received,long Total,string State);

// Arguments never pass through a shell. Authentication is optional and only
// uses an explicitly selected cookie file or Firefox login; public-only is default.
public sealed class YouTubeMusicClient(string toolDirectory,string cacheDirectory,Func<string?>? cookiesFile=null,Func<bool>? useFirefoxCookies=null)
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
        else if(useFirefoxCookies?.Invoke()==true)
        { arguments.Add("--cookies-from-browser");arguments.Add("firefox"); }
        return arguments;
    }
    public async Task<IReadOnlyList<YouTubeVideo>> Search(string query,CancellationToken cancellation)
    {
        if(string.IsNullOrWhiteSpace(query))return [];
        if(VideoId(query.Trim()) is { } id)return [new(id,"YouTube "+id,"","")];
        var json=await Run(Tool("yt-dlp"),Common().Concat(new[]{"--flat-playlist","--dump-single-json","--","ytsearch48:"+query}),null,cancellation,TimeSpan.FromMinutes(2));
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
    public ProgressiveVideo StartProgressive(YouTubeVideo video,Action<YouTubeTransferProgress> progress,
        CancellationToken cancellation,string? verificationInfoFile=null)
    {
        if(VideoId(video.Id)!=video.Id)throw new ArgumentException("Invalid YouTube video ID");
        var directory=Path.GetFullPath(Path.Combine(cacheDirectory,video.Id));Directory.CreateDirectory(directory);
        var file=Path.Combine(directory,video.Id+".stream.ts");
        if(File.Exists(file+".complete"))File.Delete(file+".complete");
        return new ProgressiveVideo(file,async (output,token)=>
        {
            var arguments=Common().Concat(new[]{"--no-playlist","--no-simulate","--quiet","--no-progress",
                "--ffmpeg-location",Path.GetFullPath(toolDirectory),"--downloader","ffmpeg",
                "--downloader-args","ffmpeg_o:-f mpegts -flush_packets 1 -mpegts_flags +resend_headers",
                "--format","bv[vcodec^=avc1][height<=1080]+ba[acodec^=mp4a]/b[ext=mp4][height<=1080]",
                "--output","-"}).ToList();
            if(verificationInfoFile is not null)arguments.AddRange(["--load-info-json",Path.GetFullPath(verificationInfoFile)]);
            else arguments.AddRange(["--","https://www.youtube.com/watch?v="+video.Id]);
            var start=new ProcessStartInfo(Tool("yt-dlp")) { UseShellExecute=false,CreateNoWindow=true,
                RedirectStandardOutput=true,RedirectStandardError=true,StandardErrorEncoding=Encoding.UTF8 };
            foreach(var argument in arguments)start.ArgumentList.Add(argument);
            using var process=Process.Start(start)??throw new IOException("Could not start progressive YouTube transfer");
            var errors=process.StandardError.ReadToEndAsync(token);
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var registration=timeout.Token.Register(()=> { try { if(!process.HasExited)process.Kill(true); } catch(InvalidOperationException) { } });
            try
            {
                var buffer=new byte[65536];long received=0;
                while(true)
                {
                    var count=await process.StandardOutput.BaseStream.ReadAsync(buffer,timeout.Token);if(count==0)break;
                    await output.WriteAsync(buffer.AsMemory(0,count),timeout.Token);await output.FlushAsync(timeout.Token);
                    received+=count;progress(new(received,0,"downloading"));
                }
                await process.WaitForExitAsync(timeout.Token);
                var error=await errors;
                if(process.ExitCode!=0)throw new IOException("YouTube progressive fetch failed: "+string.Join('\n',error.Split('\n').TakeLast(8)));
                var probe=await Run(Tool("ffprobe"),["-v","error","-show_entries","stream=codec_type","-of","json",file],null,token,TimeSpan.FromSeconds(30));
                using var inspection=JsonDocument.Parse(probe);
                var types=inspection.RootElement.GetProperty("streams").EnumerateArray().Select(item=>item.GetProperty("codec_type").GetString()).ToArray();
                if(!types.Contains("audio") || !types.Contains("video"))throw new IOException("Progressive transfer is missing video or audio");
                await File.WriteAllTextAsync(file+".complete",received.ToString(),token);
                progress(new(received,received,"finished"));
            }
            catch { if(!process.HasExited)process.Kill(true);throw; }
        },cancellation);
    }
    public string? CompletedVideo(YouTubeVideo video)
    {
        if(VideoId(video.Id)!=video.Id)return null;
        foreach(var extension in new[]{".stream.ts",".mkv"})
        {
            var path=Path.GetFullPath(Path.Combine(cacheDirectory,video.Id,video.Id+extension));
            if(File.Exists(path+".complete") && File.Exists(path) && long.TryParse(File.ReadAllText(path+".complete"),out var size)
                && size>0 && new FileInfo(path).Length==size)return path;
        }
        return null;
    }
}
