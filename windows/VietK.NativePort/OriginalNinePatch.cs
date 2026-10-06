using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace VietK.NativePort;

// APK PNGs contain compiled npTc divisions, not the source's black guide border.
// Keep fixed corner regions and distribute remaining space across stretch spans.
public sealed class OriginalNinePatch : FrameworkElement
{
    private readonly BitmapSource bitmap;
    private readonly int[] x,y;
    private readonly CroppedBitmap[,] cells;
    public OriginalNinePatch(byte[] png)
    {
        using var stream=new MemoryStream(png,false);
        var image=new BitmapImage();image.BeginInit();image.StreamSource=stream;image.CacheOption=BitmapCacheOption.OnLoad;image.EndInit();image.Freeze();bitmap=image;
        var chunk=FindChunk(png);
        if(chunk.Length<32||chunk[1]==0||chunk[2]==0||chunk[1]%2!=0||chunk[2]%2!=0||32+4*(chunk[1]+chunk[2])>chunk.Length)
            throw new InvalidDataException("Invalid compiled nine-patch divisions");
        x=Axis(chunk,32,chunk[1],bitmap.PixelWidth);y=Axis(chunk,32+4*chunk[1],chunk[2],bitmap.PixelHeight);
        cells=new CroppedBitmap[x.Length-1,y.Length-1];
        for(var column=0;column<x.Length-1;column++)for(var row=0;row<y.Length-1;row++)
        {
            if(x[column+1]==x[column]||y[row+1]==y[row])continue;
            var crop=new CroppedBitmap(bitmap,new Int32Rect(x[column],y[row],x[column+1]-x[column],y[row+1]-y[row]));crop.Freeze();cells[column,row]=crop;
        }
        IsHitTestVisible=false;
    }
    public static Grid Wrap(UIElement child,string file,Brush fallback)
    {
        var grid=new Grid();
        if(File.Exists(file))grid.Children.Add(new OriginalNinePatch(File.ReadAllBytes(file)));
        else grid.Children.Add(new Border { Background=fallback,IsHitTestVisible=false });
        grid.Children.Add(child);return grid;
    }
    protected override void OnRender(DrawingContext context)
    {
        base.OnRender(context);var xs=Scale(x,ActualWidth);var ys=Scale(y,ActualHeight);
        for(var column=0;column<x.Length-1;column++)for(var row=0;row<y.Length-1;row++)
            if(cells[column,row] is { } image&&xs[column+1]>xs[column]&&ys[row+1]>ys[row])
                context.DrawImage(image,new Rect(xs[column],ys[row],xs[column+1]-xs[column],ys[row+1]-ys[row]));
    }
    private static byte[] FindChunk(byte[] png)
    {
        ReadOnlySpan<byte> signature=[137,80,78,71,13,10,26,10];
        if(!png.AsSpan().StartsWith(signature))throw new InvalidDataException("Expected PNG nine-patch");
        for(var offset=8;offset+12<=png.Length;)
        {
            var size=BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset,4));
            if(size>int.MaxValue||size>png.Length-offset-12)throw new InvalidDataException("Truncated nine-patch PNG");
            if(png.AsSpan(offset+4,4).SequenceEqual("npTc"u8))return png.AsSpan(offset+8,(int)size).ToArray();
            offset=checked(offset+12+(int)size);
        }
        throw new InvalidDataException("PNG has no compiled nine-patch chunk");
    }
    private static int[] Axis(byte[] chunk,int offset,int count,int extent)
    {
        var result=new int[count+2];result[^1]=extent;
        for(var index=0;index<count;index++)
        {
            var value=BinaryPrimitives.ReadInt32BigEndian(chunk.AsSpan(offset+index*4,4));
            if(value<result[index]||value>extent)throw new InvalidDataException("Unordered nine-patch division");
            result[index+1]=value;
        }
        return result;
    }
    private static double[] Scale(int[] divisions,double extent)
    {
        double fixedSize=0,stretchSize=0;
        for(var index=0;index<divisions.Length-1;index++)
            if(index%2==0)fixedSize+=divisions[index+1]-divisions[index];else stretchSize+=divisions[index+1]-divisions[index];
        var fixedScale=extent<fixedSize&&fixedSize>0?extent/fixedSize:1;
        var stretchScale=stretchSize>0?Math.Max(0,extent-fixedSize*fixedScale)/stretchSize:0;
        var result=new double[divisions.Length];
        for(var index=0;index<result.Length-1;index++)result[index+1]=result[index]+(divisions[index+1]-divisions[index])*(index%2==0?fixedScale:stretchScale);
        return result;
    }
}
