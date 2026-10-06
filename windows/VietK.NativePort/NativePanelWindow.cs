using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace VietK.NativePort;

internal static class NativePanelWindow
{
    internal static Window Create(string title,Canvas panel)
    {
        var desktop=SystemParameters.WorkArea;
        var width=Math.Min(1280,desktop.Width-16);var height=Math.Min(800,desktop.Height-16);
        return new Window { Title=title,Width=width,Height=height,
            Left=desktop.Left+(desktop.Width-width)/2,Top=desktop.Top+(desktop.Height-height)/2,
            Background=Brushes.Black,FontFamily=OriginalFont.Family,
            Content=new Viewbox { Stretch=Stretch.Uniform,Child=panel } };
    }
}
