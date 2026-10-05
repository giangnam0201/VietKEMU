using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using LibVLCSharp.Shared;
using LibVLCSharp.WinForms;

namespace VietK.Windows;

record Song(long Id, string Title, string Singer, string FileName, int OriginalTrack, int AccompanyTrack) {
    public string SearchKey { get; } = Normalize($"{Id} {Title} {Singer}");
    public static string Normalize(string value) => string.Concat(value.Normalize(NormalizationForm.FormD)
        .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark))
        .Replace('đ','d').Replace('Đ','D').ToUpperInvariant();
}
class SavedState {
    public string Library { get; set; } = "";
    public Dictionary<long,string> Files { get; set; } = new();
    public List<long> Queue { get; set; } = new();
    public int Volume { get; set; } = 80;
}
static class Program {
    [STAThread] static void Main(string[] args) {
        ApplicationConfiguration.Initialize();
        try {
            Core.Initialize();
            if (args.Contains("--self-test")) {
                var songs = PanelWindow.LoadCatalog();
                if (songs.Count != 72355 || !songs.Any(s => s.SearchKey.Contains("ME YEU")))
                    throw new Exception("Original catalogue integrity/search check failed");
                using var vlc = new LibVLC("--no-video-title-show");
                using var player = new MediaPlayer(vlc);
                File.WriteAllText("self-test.json", JsonSerializer.Serialize(new { count = songs.Count, vlc = vlc.Version, search = "passed" }));
                return;
            }
            Application.Run(new PanelWindow());
        } catch (Exception error) {
            File.WriteAllText(Path.Combine(Path.GetTempPath(),"vietkemu-startup.log"),error.ToString());
            MessageBox.Show(error.Message,"VietKEMU — startup error");
            Environment.ExitCode = 1;
        }
    }
}
sealed class TvWindow : Form {
    readonly VideoView video;
    public TvWindow(MediaPlayer player) {
        Text = "VietK — TV output"; BackColor = Color.Black;
        ClientSize = new Size(960,540); MinimumSize = new Size(480,270);
        video = new VideoView { Dock = DockStyle.Fill, MediaPlayer = player };
        Controls.Add(video); KeyPreview = true;
        KeyDown += (_,e) => { if (e.KeyCode == Keys.F11 || e.KeyCode == Keys.Escape) ToggleFullScreen(e.KeyCode == Keys.F11 && FormBorderStyle != FormBorderStyle.None); };
        DoubleClick += (_,_) => ToggleFullScreen(FormBorderStyle != FormBorderStyle.None);
        video.DoubleClick += (_,_) => ToggleFullScreen(FormBorderStyle != FormBorderStyle.None);
        var screen = Screen.AllScreens.FirstOrDefault(s => !s.Primary);
        if (screen != null) { StartPosition = FormStartPosition.Manual; Bounds = screen.WorkingArea; }
    }
    public void ToggleFullScreen(bool enabled) {
        WindowState = FormWindowState.Normal;
        FormBorderStyle = enabled ? FormBorderStyle.None : FormBorderStyle.Sizable;
        WindowState = enabled ? FormWindowState.Maximized : FormWindowState.Normal;
    }
}
sealed class PanelWindow : Form {
    readonly WebView2 web = new() { Dock = DockStyle.Fill };
    readonly LibVLC vlc = new("--no-video-title-show");
    readonly MediaPlayer player;
    readonly TvWindow tv;
    readonly List<Song> songs;
    readonly Dictionary<long,Song> byId;
    readonly List<Song> queue = new();
    readonly Dictionary<string,string> indexedFiles = new(StringComparer.OrdinalIgnoreCase);
    readonly string dataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VietKEMU");
    SavedState saved = new();
    Song? current;
    bool vocal = true;
    string status = "Chọn thư mục nhạc hoặc mở video để bắt đầu.";
    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    public static List<Song> LoadCatalog() {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder {
            DataSource = Path.Combine(AppContext.BaseDirectory,"assets","catalog.db"), Mode = SqliteOpenMode.ReadOnly }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT s.SongID,s.SongName,s.songsterName,COALESCE(m.SongFileName,''),COALESCE(m.OriginalTrack,0),COALESCE(m.AccompanyTrack,0) FROM tblSong s LEFT JOIN tblMedia m ON m.SongID=s.SongID ORDER BY s.PlayNum DESC,s.SongID";
        using var reader = command.ExecuteReader();
        var result = new List<Song>();
        while (reader.Read()) result.Add(new(reader.GetInt64(0),reader.GetString(1),reader.GetString(2),reader.GetString(3),reader.GetInt32(4),reader.GetInt32(5)));
        return result;
    }
    public PanelWindow() {
        Text = "VietK — Control panel (Windows port preview)";
        ClientSize = new Size(1100,700); MinimumSize = new Size(800,520);
        Directory.CreateDirectory(dataPath);
        var stateFile = Path.Combine(dataPath,"state.json");
        if (File.Exists(stateFile)) {
            try { saved = JsonSerializer.Deserialize<SavedState>(File.ReadAllText(stateFile)) ?? new(); }
            catch (JsonException) { status = "Không đọc được danh sách cũ; dữ liệu nhạc vẫn được giữ."; }
        }
        songs = LoadCatalog(); byId = songs.ToDictionary(s => s.Id);
        queue.AddRange(saved.Queue.Where(byId.ContainsKey).Select(id => byId[id]));
        player = new MediaPlayer(vlc); player.Volume = Math.Clamp(saved.Volume,0,100);
        tv = new TvWindow(player);
        tv.FormClosing += (_,e) => { if (e.CloseReason == CloseReason.UserClosing && !IsDisposed) { e.Cancel = true; tv.Hide(); } };
        player.EndReached += (_,_) => { if (!IsDisposed) BeginInvoke((Action)Next); };
        player.EncounteredError += (_,_) => { if (!IsDisposed) BeginInvoke((Action)(() => { status = "Không phát được tệp: codec, mã hóa hoặc tệp lỗi."; SendState(); })); };
        Controls.Add(web); Shown += async (_,_) => await Initialize();
        FormClosed += (_,_) => { Save(); tv.Dispose(); player.Dispose(); vlc.Dispose(); };
    }
    async Task Initialize() {
        try {
            var env = await CoreWebView2Environment.CreateAsync(null,Path.Combine(dataPath,"browser"));
            await web.EnsureCoreWebView2Async(env);
            web.CoreWebView2.SetVirtualHostNameToFolderMapping("vietk.local",AppContext.BaseDirectory,CoreWebView2HostResourceAccessKind.DenyCors);
            web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            web.CoreWebView2.NavigationStarting += (_,e) => { if (!e.Uri.StartsWith("https://vietk.local/",StringComparison.Ordinal)) e.Cancel = true; };
            web.CoreWebView2.WebMessageReceived += async (_,e) => {
                try {
                    if (!e.Source.StartsWith("https://vietk.local/",StringComparison.Ordinal)) return;
                    using var json = JsonDocument.Parse(e.WebMessageAsJson);
                    await Handle(json.RootElement);
                } catch (Exception error) { status = error.Message; SendState(); }
            };
            web.CoreWebView2.Navigate("https://vietk.local/ui/index.html");
            tv.Show(this);
            if (Directory.Exists(saved.Library)) await IndexLibrary(saved.Library);
        } catch (Exception error) { MessageBox.Show(this,error.Message + "\nCần Microsoft Edge WebView2 Runtime.","VietKEMU"); }
    }
    async Task Handle(JsonElement message) {
        string action = message.GetProperty("action").GetString() ?? "";
        switch (action) {
            case "ready": case "search":
                var text = message.TryGetProperty("text",out var value) ? value.GetString() ?? "" : "";
                var key = Song.Normalize(text);
                var matches = songs.Where(s => s.SearchKey.Contains(key)).Take(120).Select(s => new { s.Id,s.Title,s.Singer, available = Resolve(s) != null }).ToList();
                Post(new { type = "songs", songs = matches, total = songs.Count }); SendState(); break;
            case "add":
                var id = message.GetProperty("id").GetInt64();
                if (byId.TryGetValue(id,out var song)) { queue.Add(song); status = $"Đã chọn: {song.Title}"; Save(); SendState(); } break;
            case "remove":
                int index = message.GetProperty("index").GetInt32();
                if (index >= 0 && index < queue.Count) queue.RemoveAt(index);
                Save(); SendState(); break;
            case "play": case "play_imv": case "pause_imv":
                if (current == null) Next(); else if (player.IsPlaying) player.Pause(); else player.Play();
                SendState(); break;
            case "next": case "cut_song_imv": Next(); break;
            case "replay_imv": player.Time = 0; SendState(); break;
            case "volinc": player.Volume = Math.Min(100,player.Volume + 5); Save(); SendState(); break;
            case "voldec": player.Volume = Math.Max(0,player.Volume - 5); Save(); SendState(); break;
            case "ori_imv": case "accp_imv":
                if (current != null) {
                    int track = vocal ? current.AccompanyTrack : current.OriginalTrack;
                    if (player.AudioTrackDescription.Any(t => t.Id == track) && player.SetAudioTrack(track)) { vocal = !vocal; status = vocal ? "Nguyên ca" : "Nhạc đệm"; }
                    else status = "Tệp không có kênh âm thanh khớp dữ liệu gốc.";
                }
                SendState(); break;
            case "folder": case "USB":
                using (var dialog = new FolderBrowserDialog { Description = "Chọn thư mục chứa nhạc VietK", UseDescriptionForTitle = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) { saved.Library = dialog.SelectedPath; await IndexLibrary(saved.Library); Save(); }
                break;
            case "open":
                using (var dialog = MediaDialog()) if (dialog.ShowDialog(this) == DialogResult.OK) {
                    current = new(-DateTime.UtcNow.Ticks,Path.GetFileNameWithoutExtension(dialog.FileName),"",dialog.FileName,-1,-1);
                    PlayFile(current,dialog.FileName);
                } break;
            case "tv": tv.Show(this); tv.BringToFront(); break;
            case "full": tv.ToggleFullScreen(true); break;
            case "home_imv": case "logo": case "search_bg": Post(new { type = "navigate", view = "songs" }); break;
            case "order_bg": Post(new { type = "navigate", view = "queue" }); break;
            case "download": status = "Máy chủ tải nhạc chưa được port. Danh mục không chứa các tệp bài hát."; SendState(); break;
            default: status = "Chức năng này chưa được port: " + action; SendState(); break;
        }
    }
    static OpenFileDialog MediaDialog() => new() { Filter = "Video / âm thanh|*.mpg;*.mpeg;*.mp4;*.mkv;*.avi;*.vob;*.ts;*.mov;*.mp3;*.flac;*.wav|Tất cả|*.*", CheckFileExists = true };
    string? Resolve(Song song) {
        if (saved.Files.TryGetValue(song.Id,out var path) && File.Exists(path)) return path;
        return indexedFiles.GetValueOrDefault(Path.GetFileName(song.FileName));
    }
    async Task IndexLibrary(string path) {
        status = "Đang tìm tệp nhạc…"; SendState();
        var files = await Task.Run(() => Directory.EnumerateFiles(path,"*",new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System })
            .Where(p => new[] {".mpg",".mpeg",".mp4",".mkv",".avi",".vob",".ts",".mp3",".flac",".wav"}.Contains(Path.GetExtension(p).ToLowerInvariant())).ToList());
        indexedFiles.Clear(); foreach (var file in files) indexedFiles.TryAdd(Path.GetFileName(file),file);
        status = $"Đã tìm {indexedFiles.Count:N0} tệp. Danh mục gốc: {songs.Count:N0} bài."; SendState();
        Post(new { type = "refresh" });
    }
    void Next() {
        if (queue.Count == 0) { player.Stop(); current = null; status = "Danh sách đã chọn trống."; SendState(); return; }
        var song = queue[0]; var path = Resolve(song);
        if (path == null) {
            using var dialog = MediaDialog(); dialog.Title = "Chọn tệp cho: " + song.Title;
            if (dialog.ShowDialog(this) != DialogResult.OK) { status = "Chưa có tệp nhạc: " + song.FileName; SendState(); return; }
            path = dialog.FileName; saved.Files[song.Id] = path;
        }
        queue.RemoveAt(0); PlayFile(song,path); Save();
    }
    void PlayFile(Song song,string path) {
        using var media = new Media(vlc,path,FromType.FromPath);
        if (!player.Play(media)) { status = "Không mở được tệp nhạc."; SendState(); return; }
        current = song; vocal = true; tv.Show(this); tv.Text = "VietK — " + song.Title;
        status = "Đang phát: " + song.Title; SendState();
    }
    void Save() {
        saved.Queue = queue.Select(s => s.Id).ToList(); saved.Volume = player.Volume;
        var temp = Path.Combine(dataPath,"state.json.tmp");
        File.WriteAllText(temp,JsonSerializer.Serialize(saved)); File.Move(temp,Path.Combine(dataPath,"state.json"),true);
    }
    void SendState() => Post(new { type = "state", status, playing = player.IsPlaying, vocal, volume = player.Volume,
        current = current?.Title ?? "", queue = queue.Select((s,i) => new { index = i,s.Title,s.Singer }), library = saved.Library });
    void Post(object message) { if (web.CoreWebView2 != null) web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message,Json)); }
}
