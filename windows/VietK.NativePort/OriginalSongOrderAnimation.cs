using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VietK.NativePort;

// BaseSongForGridViewFragment.addSong / AnimOrderSongUtil.SongItemAnim.
internal static class OriginalSongOrderAnimation
{
    internal static void Play(Canvas host,FrameworkElement copy,Point source,string tag)
    {
        copy.Tag=tag;copy.IsHitTestVisible=false;copy.RenderTransformOrigin=new(.5,.5);
        var scale=new ScaleTransform(1,1);var move=new TranslateTransform();var transform=new TransformGroup();transform.Children.Add(scale);transform.Children.Add(move);copy.RenderTransform=transform;
        Canvas.SetLeft(copy,0);Canvas.SetTop(copy,0);Panel.SetZIndex(copy,100);host.Children.Add(copy);
        DoubleAnimation Animate(double from,double to,bool accelerated=false)=>new(from,to,TimeSpan.FromMilliseconds(500))
        { EasingFunction=accelerated?new QuadraticEase { EasingMode=EasingMode.EaseIn }:new SineEase { EasingMode=EasingMode.EaseInOut } };
        move.BeginAnimation(TranslateTransform.XProperty,Animate(source.X-30,1000,true));
        move.BeginAnimation(TranslateTransform.YProperty,Animate(source.Y-56,550));
        scale.BeginAnimation(ScaleTransform.ScaleXProperty,Animate(1,.2));scale.BeginAnimation(ScaleTransform.ScaleYProperty,Animate(1,.2));
        var fade=Animate(1,.4);fade.Completed+=(_,_)=>host.Children.Remove(copy);copy.BeginAnimation(UIElement.OpacityProperty,fade);
    }
}
