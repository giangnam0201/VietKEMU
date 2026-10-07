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
    private MidiSong? lyricSong;
    private LyricCue[] voiceOne = [], voiceTwo = [];
    internal (string First, string Second, int FirstHighlighted, int SecondHighlighted) RenderedVoiceRows =>
        (lyrics.Text, upcoming.Text,
         lyrics.Inlines.OfType<System.Windows.Documents.Run>().Where(r => r.Foreground == Brushes.Gold).Sum(r => r.Text.Length),
         upcoming.Inlines.OfType<System.Windows.Documents.Run>().Where(r => r.Foreground == Brushes.Gold).Sum(r => r.Text.Length));
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
        if (!ReferenceEquals(lyricSong, song))
        {
            lyricSong = song;
            voiceOne = song?.Lyrics.Where(c => c.Voice == 1).ToArray() ?? [];
            voiceTwo = song?.Lyrics.Where(c => c.Voice == 2).ToArray() ?? [];
        }
        title.Text = song?.Title ?? "ARIRANG MIDI KARAOKE";
        progress.Text = song is null ? "" : $"{TimeSpan.FromSeconds(seconds):mm\\:ss} / {TimeSpan.FromSeconds(song.Duration):mm\\:ss}  •  {(playing ? "Đang phát" : "Dừng")}";
        lyrics.Inlines.Clear(); upcoming.Inlines.Clear(); upcoming.FontSize = 32;
        if (song is null) { lyrics.Text = "Chọn bài hát để bắt đầu"; lyrics.Foreground = Brushes.White; return; }
        if (song.Lyrics.Count == 0) { lyrics.Text = "♫"; lyrics.Foreground = Brushes.Gold; upcoming.Text = song.Notice ?? "Chưa có lời hiển thị"; return; }
        if (voiceOne.Length > 0 || voiceTwo.Length > 0)
        {
            upcoming.FontSize = lyrics.FontSize;
            RenderVoice(lyrics, voiceOne, seconds);
            RenderVoice(upcoming, voiceTwo, seconds);
            return;
        }
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
    private static void RenderVoice(TextBlock line, IReadOnlyList<LyricCue> cues, double seconds)
    {
        if (cues.Count == 0) return;
        int low = 0, high = cues.Count;
        while (low < high)
        {
            int middle = low + (high - low) / 2;
            if (cues[middle].Seconds <= seconds) low = middle + 1; else high = middle;
        }
        int current = low - 1;
        int start = Math.Max(current, 0);
        while (start > 0 && !cues[start].NewLine) start--;
        int end = start + 1;
        while (end < cues.Count && !cues[end].NewLine) end++;
        for (int i = start; i < end; i++) line.Inlines.Add(new System.Windows.Documents.Run(cues[i].Text)
            { Foreground = i <= current ? Brushes.Gold : Brushes.White });
    }
}

internal static class Brand
{
    public static BitmapImage Source { get; } = new(new Uri("pack://application:,,,/Arirang.MidiPlayer;component/Assets/arirang-logo.png"));
    public static Image Logo(double width) => new() { Source = Source, Width = width, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
}
