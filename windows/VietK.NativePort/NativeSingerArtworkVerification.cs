using System.Buffers.Binary;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeSingerArtworkVerification
{
    internal static async Task Run(string directory,string output)
    {
        var colors=new[]{Colors.Red,Colors.Green,Colors.Blue,Colors.Yellow,Colors.Magenta,Colors.Cyan,Colors.White,Colors.Gray,Colors.Black};
        var pixels=new byte[6*6*4];
        for(var y=0;y<6;y++)for(var x=0;x<6;x++)
        { var color=colors[y/2*3+x/2];var offset=(y*6+x)*4;pixels[offset]=color.B;pixels[offset+1]=color.G;pixels[offset+2]=color.R;pixels[offset+3]=255; }
        var png=Encode(pixels,false);var chunk=new byte[84];chunk[1]=chunk[2]=2;chunk[3]=9;
        for(var index=0;index<4;index++)BinaryPrimitives.WriteInt32BigEndian(chunk.AsSpan(32+4*index,4),index%2==0?2:4);
        var compiled=AddChunk(png,chunk);
        var background=new OriginalNinePatch(compiled) { Width=60,Height=40 };
        background.Measure(new Size(60,40));background.Arrange(new Rect(0,0,60,40));
        var rendered=new RenderTargetBitmap(60,40,96,96,PixelFormats.Pbgra32);rendered.Render(background);
        Require(ColorAt(rendered,1,1)==Colors.Red&&ColorAt(rendered,58,1)==Colors.Blue&&ColorAt(rendered,30,20)==Colors.Magenta&&ColorAt(rendered,30,38)==Colors.Gray,"Nine-patch did not preserve corners and stretch its center/edges");
        background.Width=background.Height=2;background.Measure(new Size(2,2));background.Arrange(new Rect(0,0,2,2));
        var tiny=new RenderTargetBitmap(2,2,96,96,PixelFormats.Pbgra32);tiny.Render(background);
        Require(ColorAt(tiny,0,0)==Colors.Red&&ColorAt(tiny,1,1)==Colors.Black,"Undersized nine-patch produced negative stretch regions");
        var rejected=false;try { _=new OriginalNinePatch(png); }catch(InvalidDataException) { rejected=true; }
        Require(rejected,"A normal PNG was interpreted as compiled nine-patch artwork");
        var root=Path.Combine(directory,"picture-volume");var local=Path.Combine(root,"kmbox","picture");Directory.CreateDirectory(local);
        var red=Solid(Colors.Red);var blue=Solid(Colors.Blue);File.WriteAllBytes(Path.Combine(local,"Local singer.jpg"),Encode(red,true));
        var requests=new List<string>();var delayed=new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var pictures=new NativeSingerPictures(()=>new[]{root},directory,(url,_)=>
        { requests.Add(url);return url.EndsWith("=1",StringComparison.Ordinal)?delayed.Task:Task.FromResult(Encode(blue,false)); });
        var localImage=new Image();pictures.Load(localImage,new(1,"Local singer","LS","7",""));
        await Until(()=>localImage.Source is not null,"Local singer portrait did not load");
        Require(requests.Count==0&&ColorAt((BitmapSource)localImage.Source,0,0).R>240,"Local singer file did not take precedence over the network");
        var remoteImage=new Image();pictures.Load(remoteImage,new(2,"Remote singer","RS","2",""));
        await Until(()=>remoteImage.Source is not null,"Picture-ID fallback did not load");
        Require(requests.Single()==NativeSingerPictures.DefaultHeader+"2"&&ColorAt((BitmapSource)remoteImage.Source,0,0).B==255,"Vendor fallback URL or picture pixels differed");
        using(var cached=new NativeSingerPictures(()=>new[]{root},directory,(_,_)=>throw new InvalidOperationException("Completed picture cache was ignored")))
        {
            var cachedImage=new Image();cached.Load(cachedImage,new(2,"Remote singer","RS","2",""));
            await Until(()=>cachedImage.Source is not null,"Persisted singer picture did not load");
            Require(ColorAt((BitmapSource)cachedImage.Source,0,0).B==255,"Cached picture pixels changed");
        }
        var reused=new Image();pictures.Load(reused,new(3,"Slow singer","SS","1",""));
        pictures.Load(reused,new(2,"Remote singer","RS","2",""));
        await Until(()=>reused.Source is not null,"Recycled image target did not receive its current singer");
        delayed.SetResult(Encode(red,false));await Task.Delay(100);
        Require(ColorAt((BitmapSource)reused.Source,0,0).B==255,"An old singer request overwrote a recycled card");
        using(var failed=new NativeSingerPictures(()=>new[]{root},Path.Combine(directory,"bad-picture"),(_,_)=>Task.FromResult(new byte[]{1,2,3})))
        {
            var placeholder=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);placeholder.Freeze();
            var failedImage=new Image { Source=placeholder };failed.Load(failedImage,new(4,"Bad singer","BS","4",""));await Task.Delay(100);
            Require(ReferenceEquals(failedImage.Source,placeholder),"Invalid remote image removed the original placeholder");
        }
        File.WriteAllText(Path.Combine(output,"singer-artwork-verification.json"),JsonSerializer.Serialize(new {
            compiledNinePatchCornersEdgesCenter=true,undersizedNinePatch=true,normalPngRejected=true,localPortraitPrecedence=true,
            exactDefaultVendorPictureUrl=true,persistedPictureCache=true,recycledTargetProtection=true,invalidImageRetainsPlaceholder=true,
            liveVendorImageServerTested=false,originalPopupArtworkPresent=File.Exists(Path.Combine(OriginalSupplement.Root,"ambience","singer","dialog_category_background.9.png"))
        },new JsonSerializerOptions { WriteIndented=true }));
    }
    private static byte[] Solid(Color color)
    { var bytes=new byte[144];for(var index=0;index<bytes.Length;index+=4) { bytes[index]=color.B;bytes[index+1]=color.G;bytes[index+2]=color.R;bytes[index+3]=255; }return bytes; }
    private static byte[] Encode(byte[] pixels,bool jpeg)
    {
        var bitmap=BitmapSource.Create(6,6,96,96,PixelFormats.Bgra32,null,pixels,24);
        BitmapEncoder encoder=jpeg?new JpegBitmapEncoder { QualityLevel=100 }:new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream=new MemoryStream();encoder.Save(stream);return stream.ToArray();
    }
    private static byte[] AddChunk(byte[] png,byte[] data)
    {
        using var stream=new MemoryStream();stream.Write(png,0,33);var header=new byte[8];BinaryPrimitives.WriteInt32BigEndian(header,data.Length);"npTc"u8.CopyTo(header.AsSpan(4));stream.Write(header);stream.Write(data);
        uint crc=uint.MaxValue;foreach(var value in header.Skip(4).Concat(data)) { crc^=value;for(var bit=0;bit<8;bit++)crc=crc%2==1?(crc>>1)^0xedb88320:crc>>1; }
        var checksum=new byte[4];BinaryPrimitives.WriteUInt32BigEndian(checksum,crc^uint.MaxValue);stream.Write(checksum);stream.Write(png,33,png.Length-33);return stream.ToArray();
    }
    private static Color ColorAt(BitmapSource bitmap,int x,int y)
    { var formatted=new FormatConvertedBitmap(bitmap,PixelFormats.Bgra32,null,0);var pixel=new byte[4];formatted.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return Color.FromArgb(pixel[3],pixel[2],pixel[1],pixel[0]); }
    private static async Task Until(Func<bool> condition,string message)
    { var until=DateTime.UtcNow.AddSeconds(5);while(!condition()&&DateTime.UtcNow<until)await Task.Delay(25);Require(condition(),message); }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
}
