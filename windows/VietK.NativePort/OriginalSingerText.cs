using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace VietK.NativePort;

internal static class OriginalSingerText
{
    // Clickable spans exclude comma separators and do not underline the names.
    internal static TextBlock Create(string names,double size,Action<string> clicked,string tag)
    {
        var text=new TextBlock { FontSize=size,FontFamily=OriginalFont.Family,Foreground=Brushes.White,
            TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,Tag=tag };
        var parts=names.Split(',');
        for(var i=0;i<parts.Length;i++)
        {
            if(i>0)text.Inlines.Add(new Run(", "));
            var name=parts[i];var link=new Hyperlink(new Run(name)) { Foreground=Brushes.White,TextDecorations=null,Tag=name };
            link.Click+=(_,e)=> { e.Handled=true;if(name.Length>0)clicked(name); };
            text.Inlines.Add(link);
        }
        // The containing song must never be ordered by a singer-span click.
        text.MouseLeftButtonUp+=(_,e)=>e.Handled=true;
        return text;
    }
}
