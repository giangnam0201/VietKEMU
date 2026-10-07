using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using Arirang.Core;
using Microsoft.Win32;

namespace Arirang.MidiPlayer;

internal sealed record LibraryItem(string Path, string Title, int? DiscSongCode = null) { public override string ToString() => Title; }
internal sealed class Panel : Window
{
    private readonly Television tv = new();
    private MidiPlayback? playback;
    private readonly List<LibraryItem> library = [];
    private readonly List<LibraryItem> queue = [];
    private readonly ListBox songs = new(), selected = new();
    private readonly TextBox search = new() { Height = 38, FontSize = 20 };
    private readonly TextBlock status = new() { Text = "Nhập tệp MIDI/KAR để bắt đầu", TextWrapping = TextWrapping.Wrap, Foreground = Brushes.Gold, Margin = new Thickness(0, 10, 0, 10) };
    private readonly Slider seek = new() { Minimum = 0, Maximum = 1, Margin = new Thickness(0, 15, 0, 10) };
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly string stateDirectory = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArirangMidiPlayer");
    private bool updating, started, loading;
    private int pitch;
    private int gain = 80;
    private double rate = 1;
    public Panel()
    {
        Title = "Arirang MIDI — Điều khiển"; Width = 1280; Height = 820; MinWidth = 900; MinHeight = 600;
        Background = new SolidColorBrush(Color.FromRgb(10, 18, 34)); Foreground = Brushes.White;
        Icon = Brand.Source;
        var root = new DockPanel { Margin = new Thickness(24) };
        var top = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 20) };
        top.Children.Add(Brand.Logo(200));
        top.Children.Add(new TextBlock { Text = "MIDI KARAOKE", FontSize = 25, Margin = new Thickness(25, 15, 25, 0) });
        top.Children.Add(Button("Nhập MIDI / KAR", async () => await Import()));
        top.Children.Add(Button("Kiểm tra đĩa ISO", async () => await InspectDisc()));
        top.Children.Add(Button("Nhập MULTAK ISO (thử nghiệm)", async () => await ImportMultak()));
        top.Children.Add(Button("Danh mục INFO.DAT", async () => await InspectIndex()));
        top.Children.Add(Button("Màn hình TV", () => { tv.Show(); tv.Activate(); }));
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
        var footer = new StackPanel(); footer.Children.Add(status); footer.Children.Add(seek);
        var controls = new WrapPanel();
        controls.Children.Add(Button("▶ Phát / Dừng", Toggle));
        controls.Children.Add(Button("↻ Phát lại", () => { playback?.Seek(0); playback?.Play(); started = playback?.Playing == true; }));
        controls.Children.Add(Button("⏭ Qua bài", Next));
        controls.Children.Add(Button("♭ Giảm tông", () => SetPitch(pitch - 1)));
        controls.Children.Add(Button("♯ Tăng tông", () => SetPitch(pitch + 1)));
        controls.Children.Add(new TextBlock { Text = "Âm lượng", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(15, 0, 10, 0) });
        var volume = new Slider { Minimum = 0, Maximum = 100, Value = 80, Width = 130, VerticalAlignment = VerticalAlignment.Center };
        volume.ValueChanged += (_, _) => { gain = (int)volume.Value; Safe(() => playback?.SetVolume(gain)); }; controls.Children.Add(volume);
        controls.Children.Add(new TextBlock { Text = "Tốc độ", Margin = new Thickness(15, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center });
        var speed = new Slider { Minimum = .5, Maximum = 1.5, Value = 1, Width = 100, VerticalAlignment = VerticalAlignment.Center };
        speed.ValueChanged += (_, _) => { rate = speed.Value; playback?.SetSpeed(rate); }; controls.Children.Add(speed);
        footer.Children.Add(controls); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        var columns = new Grid(); columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3, GridUnitType.Star) });
        columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        var tabs = new TabControl { Margin = new Thickness(0, 0, 20, 0), FontSize = 18 };
        var songContent = new DockPanel { Margin = new Thickness(12) };
        DockPanel.SetDock(search, Dock.Top); songContent.Children.Add(search);
        var add = Button("+ Đã chọn", AddSelected); DockPanel.SetDock(add, Dock.Bottom); songContent.Children.Add(add);
        songs.FontSize = 20; songs.SelectionMode = SelectionMode.Extended; songContent.Children.Add(songs);
        tabs.Items.Add(new TabItem { Header = "Bài MIDI", Content = songContent });
        var archive = new DockPanel { Margin = new Thickness(12) };
        var archiveStatus = new TextBlock { Text = "Danh mục đĩa Maseco trên Internet Archive. Đĩa / video nền chưa phải bài hát có thể phát.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 10) };
        var archiveList = new ListBox { FontSize = 17 };
        var refresh = Button("Tải danh mục đĩa", async () =>
        {
            archiveStatus.Text = "Đang đọc danh mục JSON…";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                using var data = JsonDocument.Parse(await http.GetStringAsync("https://archive.org/advancedsearch.php?q=creator%3A%22Maseco%22&output=json&rows=100&fl%5B%5D=identifier&fl%5B%5D=title"));
                var response = data.RootElement.GetProperty("response");
                archiveList.Items.Clear();
                foreach (var item in response.GetProperty("docs").EnumerateArray())
                    archiveList.Items.Add(new ArchiveItem(item.GetProperty("identifier").GetString()!, item.GetProperty("title").GetString()!));
                archiveStatus.Text = $"{archiveList.Items.Count} mục / {response.GetProperty("numFound").GetInt32()} kết quả. Nhấp đúp để xem danh sách tệp.";
            }
            catch (Exception error) { archiveStatus.Text = "Không tải được danh mục: " + error.Message; }
        });
        DockPanel.SetDock(refresh, Dock.Top); archive.Children.Add(refresh); DockPanel.SetDock(archiveStatus, Dock.Top); archive.Children.Add(archiveStatus); archive.Children.Add(archiveList);
        archiveList.MouseDoubleClick += async (_, _) =>
        {
            if (archiveList.SelectedItem is not ArchiveItem item) return;
            archiveStatus.Text = "Đang đọc danh sách tệp…";
            try
            {
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
                using var data = JsonDocument.Parse(await http.GetStringAsync("https://archive.org/metadata/" + Uri.EscapeDataString(item.Id)));
                var lines = data.RootElement.GetProperty("files").EnumerateArray().Where(f => f.TryGetProperty("name", out _))
                    .Select(f => f.GetProperty("name").GetString()!).Where(n => n.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || n.EndsWith(".mp4", StringComparison.OrdinalIgnoreCase) || n.Contains("/midi", StringComparison.OrdinalIgnoreCase)).ToArray();
                ShowReport(item.Title, string.Join("\n", lines) + "\n\nĐây là tệp đĩa / dữ liệu, chưa phải danh sách bài hát đã giải mã.");
                archiveStatus.Text = "Đã đọc danh sách tệp. Không tải đĩa lớn tự động.";
            }
            catch (Exception error) { archiveStatus.Text = error.Message; }
        };
        tabs.Items.Add(new TabItem { Header = "Đĩa Arirang", Content = archive }); columns.Children.Add(tabs);
        var right = new Grid(); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230) }); right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        var preview = new Rectangle { Fill = new VisualBrush(tv.Scene) { Stretch = Stretch.Uniform }, Margin = new Thickness(0, 0, 0, 15) }; right.Children.Add(preview);
        var queuePanel = new DockPanel();
        var heading = new TextBlock { Text = "Đã chọn", FontSize = 24, Margin = new Thickness(0, 5, 0, 10) }; DockPanel.SetDock(heading, Dock.Top); queuePanel.Children.Add(heading);
        var remove = Button("Xoá khỏi hàng chờ", () => { if (selected.SelectedIndex >= 0) { queue.RemoveAt(selected.SelectedIndex); RefreshQueue(); } });
        DockPanel.SetDock(remove, Dock.Bottom); queuePanel.Children.Add(remove); selected.FontSize = 18; queuePanel.Children.Add(selected);
        Grid.SetRow(queuePanel, 1); right.Children.Add(queuePanel); Grid.SetColumn(right, 1); columns.Children.Add(right); root.Children.Add(columns); Content = root;
        search.TextChanged += (_, _) => RefreshLibrary();
        songs.MouseDoubleClick += async (_, _) => { if (songs.SelectedItem is LibraryItem item) await Start(item); };
        selected.MouseDoubleClick += async (_, _) => { if (selected.SelectedItem is LibraryItem item) { queue.RemoveAt(selected.SelectedIndex); RefreshQueue(); await Start(item); } };
        seek.ValueChanged += (_, _) => { if (!updating && playback?.Song is not null) Safe(() => playback.Seek(seek.Value)); };
        timer.Tick += (_, _) => Tick();
        Loaded += (_, _) => { LoadLibrary(); tv.Show(); timer.Start(); };
        Closed += (_, _) => { timer.Stop(); playback?.Dispose(); tv.AllowClose = true; tv.Close(); };
    }
    private sealed record ArchiveItem(string Id, string Title) { public override string ToString() => Title; }
    private static Button Button(string label, Action click)
    {
        var button = new Button { Content = label, Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(4), FontSize = 16 };
        button.Click += (_, _) => click(); return button;
    }
    private async Task Import()
    {
        var picker = new OpenFileDialog { Filter = "MIDI karaoke (*.mid;*.midi;*.kar)|*.mid;*.midi;*.kar", Multiselect = true };
        if (picker.ShowDialog(this) != true) return;
        status.Text = "Đang kiểm tra MIDI…";
        var errors = new List<string>();
        foreach (string path in picker.FileNames)
        {
            try
            {
                var song = await Task.Run(() => MidiSong.Read(path));
                if (!library.Any(s => s.Path == path)) library.Add(new(path, song.Title));
            }
            catch (Exception error) { errors.Add(System.IO.Path.GetFileName(path) + ": " + error.Message); }
        }
        SaveLibrary(); RefreshLibrary(); status.Text = $"{library.Count} bài MIDI trong thư viện";
        if (errors.Count > 0) ShowReport("Tệp chưa được hỗ trợ", string.Join("\n", errors));
    }
    private void LoadLibrary()
    {
        try
        {
            string path = System.IO.Path.Combine(stateDirectory, "library.json");
            if (File.Exists(path)) library.AddRange((JsonSerializer.Deserialize<List<LibraryItem>>(File.ReadAllText(path)) ?? []).Where(s => File.Exists(s.Path)));
            RefreshLibrary();
        }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void SaveLibrary() { Directory.CreateDirectory(stateDirectory); File.WriteAllText(System.IO.Path.Combine(stateDirectory, "library.json"), JsonSerializer.Serialize(library)); }
    private void RefreshLibrary() { songs.ItemsSource = library.Where(s => s.Title.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToArray(); }
    private void RefreshQueue() { selected.ItemsSource = queue.ToArray(); }
    private void AddSelected() { foreach (LibraryItem item in songs.SelectedItems) queue.Add(item); RefreshQueue(); status.Text = $"Đã chọn {queue.Count} bài"; }
    private async Task Start(LibraryItem item)
    {
        if (loading) return;
        loading = true;
        try
        {
            status.Text = "Đang mở " + item.Title;
            var song = await Task.Run(() =>
            {
                if (item.DiscSongCode is not int code) return MidiSong.Read(item.Path);
                var inventory = DiscInventory.Read(item.Path);
                var index = MultakIndex.ReadIso(item.Path, inventory);
                var entry = MasecoIndex.ReadIso(item.Path, inventory).Single(s => s.DeviceCode == code);
                var decoded = MultakPlaybackSong.Parse(index.ReadSongRecordIso(item.Path, inventory, code), item.Title, entry.LanguageId);
                if (decoded.Lyrics.Count == 0) return decoded;
                try
                {
                    var font = MultakBitmapFont.ReadIso(item.Path, inventory, entry.LanguageId);
                    return decoded with { LyricFont = font, Notice = font is null ? "Phông chữ gốc không có trên đĩa này." : decoded.Notice };
                }
                catch (InvalidDataException)
                {
                    return decoded with { Notice = "Phông chữ gốc trên đĩa này chưa hỗ trợ; dùng phông mặc định." };
                }
            });
            playback ??= new MidiPlayback(new WindowsMidiOutput());
            playback.Load(song); playback.SetKey(pitch); playback.SetVolume(gain); playback.SetSpeed(rate); playback.Play(); started = true;
            seek.Maximum = Math.Max(1, song.Duration); status.Text = "Đang phát: " + song.Title;
            if (item.DiscSongCode is not null) status.Text += " • Thử nghiệm: âm sắc Windows";
            if (song.Notice is not null) status.Text += " • " + song.Notice;
        }
        catch (Exception error) { status.Text = error.Message; }
        finally { loading = false; }
    }
    private void Toggle() { Safe(() => { if (playback?.Playing == true) playback.Pause(); else if (playback?.Song is not null) { playback.Play(); started = true; } else if (songs.SelectedItem is LibraryItem item) _ = Start(item); }); }
    private async void Next()
    {
        playback?.Pause(); started = false;
        if (queue.Count == 0) { playback?.Seek(0); status.Text = "Hàng chờ trống"; return; }
        var item = queue[0]; queue.RemoveAt(0); RefreshQueue(); await Start(item);
    }
    private void SetPitch(int value) { pitch = Math.Clamp(value, -12, 12); Safe(() => playback?.SetKey(pitch)); status.Text = $"Tông: {pitch:+0;-0;0}"; }
    private void Safe(Action action) { try { action(); } catch (Exception error) { status.Text = error.Message; } }
    private void Tick()
    {
        double position = playback?.Position ?? 0;
        updating = true; seek.Value = position; updating = false;
        tv.Update(playback?.Song, position, playback?.Playing == true);
        if (playback?.Error is string error) { started = false; status.Text = error; }
        else if (started && playback?.Playing == false && position >= (playback.Song?.Duration ?? double.MaxValue)) { started = false; if (queue.Count > 0) Next(); }
    }
    private async Task InspectDisc()
    {
        var picker = new OpenFileDialog { Filter = "Disc image (*.iso;*.img)|*.iso;*.img" };
        if (picker.ShowDialog(this) != true) return;
        status.Text = "Đang đọc thư mục đĩa…";
        try
        {
            var inventory = await Task.Run(() => DiscInventory.Read(picker.FileName));
            string musicIndex = "";
            if (inventory.Files.Any(f => f.Name.Equals("MULTAK.DAT", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var table = await Task.Run(() => MultakIndex.ReadIso(picker.FileName, inventory));
                    musicIndex = $"\n\nMULTAK: {table.Slots:N0} vị trí, {table.NullSlots:N0} vị trí trống; " +
                        $"{table.Pointers.Count(p => p.StorageFile == 0):N0} bản ghi DAT, " +
                        $"{table.Pointers.Count(p => p.StorageFile == 1):N0} bản ghi DA1.\n" +
                        "Đã xác định vị trí dữ liệu. Có thể thử bộ đọc MIDI với định dạng bản ghi hỗ trợ.";
                    if (inventory.Files.Any(f => f.Name.Equals("MASECOS4.IDX", StringComparison.OrdinalIgnoreCase)))
                    {
                        var catalogue = await Task.Run(() => MasecoIndex.ReadIso(picker.FileName, inventory));
                        int mapped = catalogue.Count(s => table.FindSong(s.DeviceCode) is not null);
                        musicIndex += $"\nMASECOS4: {catalogue.Count:N0} mã bài, {mapped:N0} mã tìm được dữ liệu nhạc.\n" +
                            "Tên tiếng Việt / Anh (100 mục đầu):\n" +
                            string.Join("\n", catalogue.Where(s => s.SupportedTitle is not null).Take(100)
                                .Select(s => $"{s.DeviceCode}: {s.SupportedTitle}"));
                        var sample = catalogue.FirstOrDefault(s => s.EnglishTitle is not null && table.FindSong(s.DeviceCode) is not null);
                        if (sample is not null)
                        {
                            try
                            {
                                var raw = await Task.Run(() => table.ReadSongRecordIso(picker.FileName, inventory, sample.DeviceCode));
                                var layout = MultakSongLayout.Parse(raw);
                                musicIndex += $"\n\nBản ghi mẫu {sample.DeviceCode}: {layout.Tracks.Count} luồng kênh, " +
                                    $"vị trí nhạc {layout.MusicOffset:N0} byte.\nKênh: " + string.Join(", ", layout.Tracks.Select(t => t.Channel + 1)) +
                                    "\nSố bit cao độ: " + string.Join(", ", layout.Tracks.Select(t => t.PitchBits));
                                var expanded = await Task.Run(() => MultakSongStreams.Parse(raw).Channels.Select(MultakCompactNotes.Parse).ToArray());
                                musicIndex += $"\nĐã đọc {expanded.Sum(n => n.Events.Count):N0} sự kiện nhạc; " +
                                    $"thời lượng tính theo tempo {MultakPlaybackSong.FromChannels(expanded, sample.EnglishTitle!).Duration:F2} giây. " +
                                    "Thời gian trên thiết bị, lời và âm sắc gốc vẫn cần xác minh.";
                            }
                            catch (InvalidDataException error) { musicIndex += "\n\nCấu trúc bản ghi mẫu: " + error.Message; }
                        }
                    }
                }
                catch (InvalidDataException error) { musicIndex = "\n\nMULTAK: " + error.Message; }
            }
            ShowReport("Đĩa Arirang — " + inventory.Format, string.Join("\n", inventory.Files.Select(f => $"{f.Name}   ({f.Bytes:N0} bytes)")) +
                musicIndex + "\n\nMULTAK có trình phát MIDI thử nghiệm; ARVNKR chưa hỗ trợ. Video nền không được thêm như một bài hát.");
            status.Text = $"{inventory.Format}: {inventory.Files.Count} tệp";
        }
        catch (Exception error) { status.Text = error.Message; }
    }
    private async Task ImportMultak()
    {
        var picker = new OpenFileDialog { Filter = "MULTAK disc image (*.iso;*.img)|*.iso;*.img" };
        if (picker.ShowDialog(this) != true) return;
        status.Text = "Đang đọc danh mục MULTAK…";
        try
        {
            var items = await Task.Run(() =>
            {
                var inventory = DiscInventory.Read(picker.FileName);
                var index = MultakIndex.ReadIso(picker.FileName, inventory);
                return MasecoIndex.ReadIso(picker.FileName, inventory)
                    .Where(s => s.SupportedTitle is not null && index.FindSong(s.DeviceCode) is not null)
                    .Select(s => new LibraryItem(picker.FileName, $"{s.DeviceCode}: {s.SupportedTitle} — MULTAK thử nghiệm", s.DeviceCode)).ToArray();
            });
            foreach (var item in items)
                if (!library.Any(s => s.Path == item.Path && s.DiscSongCode == item.DiscSongCode)) library.Add(item);
            SaveLibrary(); RefreshLibrary();
            status.Text = $"Đã nhập {items.Length:N0} tên bài tiếng Việt / Anh. Chọn bài để thử phát MIDI và lời gốc hỗ trợ. Âm sắc Windows; một số định dạng bài / lời chưa hỗ trợ.";
        }
        catch (Exception error) { status.Text = error.Message; }
    }
    private async Task InspectIndex()
    {
        var picker = new OpenFileDialog { Filter = "Arirang song index (INFO.DAT)|INFO.DAT|Data index (*.dat)|*.dat" };
        if (picker.ShowDialog(this) != true) return;
        status.Text = "Đang đọc danh mục đĩa…";
        try
        {
            var records = await Task.Run(() => InfoDatIndex.ReadEnglish(File.ReadAllBytes(picker.FileName)));
            ShowReport($"INFO.DAT — {records.Count} bài tiếng Anh", string.Join("\n", records.Select(r => $"{r.Title} — {r.Artist}")) +
                "\n\nĐã đọc tên bài. Mã bài và dữ liệu MIDI chưa được giải mã; các mục này chưa thể phát.");
            status.Text = $"Đọc được {records.Count} tên bài tiếng Anh; chưa có ánh xạ đến dữ liệu nhạc.";
        }
        catch (Exception error) { status.Text = error.Message; }
    }
    private void ShowReport(string title, string report)
    {
        new Window { Owner = this, Title = title, Width = 850, Height = 550,
            Content = new TextBox { Text = report, IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16, Padding = new Thickness(20) } }.Show();
    }
    internal void PreviewFixture(MidiSong song, string path)
    {
        library.Clear(); queue.Clear(); library.Add(new(path, "Mẫu MIDI tổng hợp — " + song.Title)); RefreshLibrary();
        songs.SelectedIndex = 0; AddSelected();
        if (queue.Count != 1) throw new InvalidOperationException("Selected queue did not receive the chosen MIDI.");
        tv.Update(song, .55, true); status.Text = "Kiểm tra hiển thị bằng MIDI tổng hợp";
    }
    internal void CloseForVerification() { tv.AllowClose = true; Close(); }
}
