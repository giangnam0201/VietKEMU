using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Arirang.Core;

namespace Arirang.MidiPlayer;

internal sealed class Television : Window
{
    public Grid Scene { get; } = new();
    private readonly TextBlock title = new() { FontSize = 28, Foreground = Brushes.White, TextAlignment = TextAlignment.Center };
    private readonly TextBlock lyrics = new() { FontSize = 50, FontWeight = FontWeights.Bold, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock upcoming = new() { FontSize = 32, Foreground = Brushes.WhiteSmoke, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock progress = new() { Foreground = Brushes.WhiteSmoke, FontSize = 18 };
    public bool AllowClose { get; set; }
    public Television()
    {
        Title = "Arirang — Màn hình TV"; Width = 1000; Height = 650;
        Scene.Background = new LinearGradientBrush(Color.FromRgb(5, 12, 26), Color.FromRgb(18, 35, 65), 45);
        var header = new DockPanel { Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Top };
        header.Children.Add(Brand.Logo(190));
        var brand = new TextBlock { Text = "MIDI KARAOKE", Foreground = Brushes.WhiteSmoke, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Right };
        header.Children.Add(brand); Scene.Children.Add(header);
        var lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(60) };
        lines.Children.Add(title); lyrics.Margin = new Thickness(0, 45, 0, 20);
        lines.Children.Add(lyrics); lines.Children.Add(upcoming); Scene.Children.Add(lines);
        progress.HorizontalAlignment = HorizontalAlignment.Right; progress.VerticalAlignment = VerticalAlignment.Bottom;
        progress.Margin = new Thickness(24); Scene.Children.Add(progress);
        Content = Scene; Icon = Brand.Source;
        MouseDoubleClick += (_, _) =>
        {
            bool fullscreen = WindowStyle != WindowStyle.None;
            WindowStyle = fullscreen ? WindowStyle.None : WindowStyle.SingleBorderWindow;
            WindowState = fullscreen ? WindowState.Maximized : WindowState.Normal;
        };
        Closing += (_, e) => { if (!AllowClose) { e.Cancel = true; Hide(); } };
        Update(null, 0, false);
    }
    public void Update(MidiSong? song, double seconds, bool playing)
    {
        title.Text = song?.Title ?? "ARIRANG MIDI KARAOKE";
        progress.Text = song is null ? "" : $"{TimeSpan.FromSeconds(seconds):mm\\:ss} / {TimeSpan.FromSeconds(song.Duration):mm\\:ss}  •  {(playing ? "Đang phát" : "Dừng")}";
        lyrics.Inlines.Clear(); upcoming.Text = "";
        if (song is null) { lyrics.Text = "Chọn bài hát để bắt đầu"; lyrics.Foreground = Brushes.White; return; }
        if (song.Lyrics.Count == 0) { lyrics.Text = "♫"; lyrics.Foreground = Brushes.Gold; upcoming.Text = "Bài MIDI này không có lời nhúng"; return; }
        lyrics.Foreground = Brushes.White;
        int current = -1;
        for (int i = 0; i < song.Lyrics.Count && song.Lyrics[i].Seconds <= seconds; i++) current = i;
        int start = Math.Max(current, 0);
        while (start > 0 && !song.Lyrics[start].NewLine) start--;
        int end = start + 1;
        while (end < song.Lyrics.Count && !song.Lyrics[end].NewLine) end++;
        for (int i = start; i < end; i++) lyrics.Inlines.Add(new System.Windows.Documents.Run(song.Lyrics[i].Text)
            { Foreground = i <= current ? Brushes.Gold : Brushes.White });
        int nextEnd = end + 1;
        while (nextEnd < song.Lyrics.Count && !song.Lyrics[nextEnd].NewLine) nextEnd++;
        upcoming.Text = string.Concat(song.Lyrics.Skip(end).Take(nextEnd - end).Select(c => c.Text));
    }
}

internal static class Brand
{
    public static BitmapImage Source { get; } = new(new Uri("pack://application:,,,/Arirang.MidiPlayer;component/Assets/arirang-logo.png"));
    public static Image Logo(double width) => new() { Source = Source, Width = width, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
}
