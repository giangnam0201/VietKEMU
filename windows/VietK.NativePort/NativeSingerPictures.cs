using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

// EvStorageManager.getSingerImgPath -> SingerImageManager DEFAULT_HEADER.
// Uses public picture IDs only; device credentials are not sent by this loader.
public sealed class NativeSingerPictures : IDisposable
{
    private readonly Func<IReadOnlyList<string>> roots;
    private readonly string cache;
    private readonly HttpClient http=new() { Timeout=TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim concurrency=new(4);
    private readonly CancellationTokenSource lifetime=new();
    private readonly Func<string,CancellationToken,Task<byte[]>>? fetch;
    private readonly Dictionary<string,Task<BitmapSource?>> pending=new();
    public const string DefaultHeader="https://os.duochang.cc/picture/show?fileid=";
    public NativeSingerPictures(Func<IReadOnlyList<string>> storageRoots,string stateDirectory,
        Func<string,CancellationToken,Task<byte[]>>? fixtureFetch=null)
    { roots=storageRoots;cache=Path.Combine(stateDirectory,"singer-pictures");fetch=fixtureFetch; }
    public async void Load(Image target,OriginalSinger singer)
    {
        var identity=new object();target.Tag=identity;
        try
        {
            var key=string.Join("\n",roots())+"\n"+singer.Name+"\n"+singer.PictureResourceId;
            if(!pending.TryGetValue(key,out var request))
            {
                if(pending.Count>=40)pending.Clear();
                request=Resolve(singer,lifetime.Token);pending[key]=request;
            }
            var picture=await request;
            if(picture is not null&&ReferenceEquals(target.Tag,identity))target.Source=picture;
        }
        catch(Exception error) when(error is IOException or HttpRequestException or OperationCanceledException or NotSupportedException or ArgumentException or FormatException) { }
    }
    private async Task<BitmapSource?> Resolve(OriginalSinger singer,CancellationToken cancel)
    {
        if(singer.Name.Length>0&&singer.Name.IndexOfAny(Path.GetInvalidFileNameChars())<0)
            foreach(var root in roots())
            {
                var path=Path.Combine(root,"kmbox","picture",singer.Name+".jpg");
                if(File.Exists(path))return Decode(await File.ReadAllBytesAsync(path,cancel));
            }
        if(!long.TryParse(singer.PictureResourceId,out var resource)||resource<=0)return null;
        var url=DefaultHeader+resource;
        var pathInCache=Path.Combine(cache,Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))+".image");
        if(File.Exists(pathInCache))
        {
            try { return Decode(await File.ReadAllBytesAsync(pathInCache,cancel)); }
            catch(Exception error) when(error is IOException or NotSupportedException or ArgumentException or FormatException) { File.Delete(pathInCache); }
        }
        await concurrency.WaitAsync(cancel);
        try
        {
            byte[] bytes;
            if(fetch is not null)bytes=await fetch(url,cancel);
            else
            {
                using var response=await http.GetAsync(url,HttpCompletionOption.ResponseHeadersRead,cancel);response.EnsureSuccessStatusCode();
                if(response.Content.Headers.ContentLength>8*1024*1024)throw new InvalidDataException("Singer image too large");
                await using var input=await response.Content.ReadAsStreamAsync(cancel);using var output=new MemoryStream();var buffer=new byte[16384];int count;
                while((count=await input.ReadAsync(buffer,cancel))>0)
                { if(output.Length+count>8*1024*1024)throw new InvalidDataException("Singer image too large");output.Write(buffer,0,count); }
                bytes=output.ToArray();
            }
            var bitmap=Decode(bytes);Directory.CreateDirectory(cache);var temporary=pathInCache+"."+Guid.NewGuid().ToString("N")+".tmp";
            try { await File.WriteAllBytesAsync(temporary,bytes,cancel);File.Move(temporary,pathInCache,true); }
            finally { if(File.Exists(temporary))File.Delete(temporary); }
            return bitmap;
        }
        finally { concurrency.Release(); }
    }
    private static BitmapSource Decode(byte[] bytes)
    {
        using var stream=new MemoryStream(bytes,false);var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;
        image.DecodePixelWidth=180;image.StreamSource=stream;image.EndInit();image.Freeze();return image;
    }
    public void Dispose() { lifetime.Cancel();http.Dispose(); }
}
