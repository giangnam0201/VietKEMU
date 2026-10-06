using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace VietK.NativePort;

// One decoder surface is shared by the TV and panel. No snapshot polling or
// second decoder; coalesce frames only when the UI cannot keep up.
sealed class SharedVideoFrames : IDisposable
{
    private const int Width=1280,Height=720,Pitch=Width*4;
    private readonly object gate=new();
    private readonly IntPtr pixels=Marshal.AllocHGlobal(Pitch*Height);
    private readonly Dispatcher dispatcher;
    private int queued;
    private bool disposed;
    public WriteableBitmap Surface { get; }=new(Width,Height,96,96,PixelFormats.Bgr32,null);
    public event Action? Updated;
    public SharedVideoFrames(MediaPlayer player,Dispatcher dispatcher)
    {
        this.dispatcher=dispatcher;
        player.SetVideoFormat("RV32",Width,Height,Pitch);
        player.SetVideoCallbacks(Lock,Unlock,Display);
    }
    private IntPtr Lock(IntPtr opaque,IntPtr planes)
    { Monitor.Enter(gate);Marshal.WriteIntPtr(planes,pixels);return pixels; }
    private void Unlock(IntPtr opaque,IntPtr picture,IntPtr planes)=>Monitor.Exit(gate);
    private void Display(IntPtr opaque,IntPtr picture)
    {
        if(disposed || Interlocked.Exchange(ref queued,1)!=0)return;
        dispatcher.BeginInvoke(DispatcherPriority.Render,new Action(()=>
        {
            try
            {
                if(disposed)return;
                lock(gate)Surface.WritePixels(new Int32Rect(0,0,Width,Height),pixels,Pitch*Height,Pitch);
                Updated?.Invoke();
            }
            finally { Interlocked.Exchange(ref queued,0); }
        }));
    }
    public void Dispose() { disposed=true;lock(gate)Marshal.FreeHGlobal(pixels); }
}
