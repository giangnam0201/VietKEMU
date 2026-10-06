using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using VietK.Core;

namespace VietK.NativePort;

// Singer spans -> fragment 28; Back restores the actual previous view/state.
public sealed class OriginalSingerNavigation(string root,SongBrowserContract contract,MoreContract more,LocalSongDatabase database,
    SongGridContract grid,Func<Window?> host,Action<Canvas> decorate,Func<SongQueryContext> context,
    Func<NativePlayback?> playback,Func<IReadOnlySet<int>> queued,Func<IReadOnlySet<int>> collected)
{
    private readonly Stack<(object Content,SongBrowser? Browser)> history=new();
    public SongBrowser? Active { get; private set; }
    public event Action<CatalogueSong,string>? SongActionRequested;
    public event Action<int>? FragmentRequested;
    public event Action<string>? YoutubeRequested;
    public bool Open(string name)
    {
        var singer=database.Singers.Find(name);var window=host();
        if(singer is null||window?.Content is not Viewbox)return false;
        var browser=new SongBrowser(root,contract,more,database,grid,context,singer) { Playback=playback() };
        browser.SetConfirmedQueuedSongs(queued());browser.SetConfirmedCollectedSongs(collected());
        browser.SingerRequested+=next=>Open(next);
        browser.BackRequested+=Back;browser.HomeRequested+=()=>FragmentRequested?.Invoke(1);
        browser.YoutubeRequested+=query=> { Clear();YoutubeRequested?.Invoke(query); };
        browser.SongActionRequested+=(song,action)=>SongActionRequested?.Invoke(song,action);
        var panel=browser.Create();decorate(panel);
        history.Push((window.Content,Active));Active=browser;
        window.Content=new Viewbox { Stretch=Stretch.Uniform,Child=panel };return true;
    }
    public void Back()
    {
        if(history.Count==0)return;var window=host();if(window is null)return;
        var previous=history.Pop();Active=previous.Browser;window.Content=previous.Content;
    }
    public void SetConfirmedQueuedSongs(IReadOnlySet<int> ids)
    { foreach(var browser in Browsers())browser.SetConfirmedQueuedSongs(ids); }
    public void SetConfirmedCollectedSongs(IReadOnlySet<int> ids)
    { foreach(var browser in Browsers())browser.SetConfirmedCollectedSongs(ids); }
    public void Refresh()=>Active?.Refresh();
    public void Clear() { history.Clear();Active=null; }
    private IEnumerable<SongBrowser> Browsers()=>history.Select(entry=>entry.Browser).Append(Active).OfType<SongBrowser>().Distinct();
}
