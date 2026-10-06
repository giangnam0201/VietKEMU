using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VietK.NativePort;

// AnimCommonUtils.scaleXY: 25ms, Android's default cosine interpolator.
internal static class OriginalPressFeedback
{
    internal const int DurationMilliseconds=25;
    internal static void Bind(FrameworkElement element,double pressedScale,bool restoreAfterDrag=false)
    {
        var scale=new ScaleTransform(1,1);element.RenderTransformOrigin=new Point(.5,.5);element.RenderTransform=scale;
        Point? start=null;
        void Animate(double from,double to)
        {
            scale.ScaleX=scale.ScaleY=to;
            var animation=new DoubleAnimation(from,to,TimeSpan.FromMilliseconds(DurationMilliseconds)) { EasingFunction=new AndroidCosine() };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty,animation);scale.BeginAnimation(ScaleTransform.ScaleYProperty,animation);
        }
        void Restore() { if(start is null)return;start=null;Animate(pressedScale,1); }
        element.PreviewMouseLeftButtonDown+=(_,e)=> { start=e.GetPosition(element);Animate(1,pressedScale); };
        element.AddHandler(UIElement.MouseLeftButtonUpEvent,new MouseButtonEventHandler((_,_)=>Restore()),true);
        element.MouseMove+=(_,e)=> { if(restoreAfterDrag&&start is { } point&&Math.Abs(e.GetPosition(element).X-point.X)>30)Restore(); };
        element.MouseLeave+=(_,_)=>Restore();element.LostKeyboardFocus+=(_,_)=>Restore();element.LostMouseCapture+=(_,_)=>Restore();
        element.Unloaded+=(_,_)=> { start=null;scale.BeginAnimation(ScaleTransform.ScaleXProperty,null);scale.BeginAnimation(ScaleTransform.ScaleYProperty,null);scale.ScaleX=scale.ScaleY=1; };
    }
    private sealed class AndroidCosine : IEasingFunction
    { public double Ease(double normalizedTime)=>.5-.5*Math.Cos(Math.PI*normalizedTime); }
}
