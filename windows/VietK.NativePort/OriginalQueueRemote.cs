using System.Windows;
using System.Windows.Controls;
using VietK.Core;

namespace VietK.NativePort;

// PlayControlAction / PlayListManager -> the real selected and download lists.
// Flow IDs identify repeated orders; a catalogue song ID is not a queue row ID.
public sealed class OriginalQueueRemote(OriginalSelectedQueue selected,OriginalDownloadQueue downloads,
    NativePlayback playback,BottomBar bottom,Action retry)
{
    private SelectedQueueDialog? dialog;
    public bool Active=>playback.Source==PlaybackSource.LocalKaraoke &&
        playback.Player.State is OriginalVideoState.Play or OriginalVideoState.Pause or OriginalVideoState.Buffering &&
        selected.Snapshot().FirstOrDefault()?.SongMetadata.Id==playback.CurrentMedia?.SongId && playback.CurrentMedia is not null;
    public bool ShouldPresent=>playback.Source!=PlaybackSource.YouTube && selected.Count+downloads.Count>0;
    public object State()=>new { queue=Rows(),active=Active,transfers=Transfers(),
        status=Active?"Đang phát: "+Title(selected.Snapshot()[0]):"Hàng chờ VietK: "+(selected.Count+downloads.Count)+" bài" };
    private static string Title(SelectedPlaylistItem item)=>item.SongName??item.SongMetadata.Name;
    private static string RowId(SelectedPlaylistItem item,bool download)=>(download?"download:":"local:")+item.FlowId;
    private OriginalRemoteRow[] Rows()=>selected.Snapshot().Select((item,index)=>new OriginalRemoteRow(RowId(item,false),Title(item),false,index>0,index>1))
        .Concat(downloads.Snapshot().Select((item,index)=>new OriginalRemoteRow(RowId(item,true),Title(item),true,false,index>1))).ToArray();
    private Dictionary<string,QueueTransferDisplay> Transfers()=>downloads.Snapshot().ToDictionary(item=>RowId(item,true),item=>
        item.DownloadState>=1000?new QueueTransferDisplay(Error:"Mã lỗi "+item.DownloadState):
        item.DownloadState==202?new QueueTransferDisplay(item.DownloadProgress,100):new QueueTransferDisplay(Waiting:true));
    public void Refresh()
    {
        var rows=Rows();dialog?.Refresh(rows.Select(row=>new YouTubeVideo(row.Id,row.Title,"","")).ToArray(),Active,Transfers());
        if(ShouldPresent)bottom.SetConfirmedQueueCount(rows.Length);
        if(playback.Source==PlaybackSource.LocalKaraoke)
        {
            var items=selected.Snapshot();
            playback.Television.Overlay.SetSong(Active?Title(items[0]):"",Active?items.Skip(1).FirstOrDefault() is { } next?Title(next):"":"");
        }
    }
    public void Action(string action,string id="",int target=0)
    {
        if(action=="clear") { selected.ClearExceptPlaying(!Active);downloads.Clear();Refresh();return; }
        if(action=="shuffle") { selected.Shuffle(Random.Shared.Next);Refresh();return; }
        if(action=="retry") { retry();return; }
        var local=selected.Snapshot();var index=Array.FindIndex(local.ToArray(),item=>RowId(item,false)==id);
        if(index>=0)
        {
            switch(action)
            {
                case "remove":selected.DeleteByIndex(index);break;
                case "top":selected.TopByIndex(index);break;
                case "move":selected.SortByIndex(index,target);break;
                default:throw new ArgumentException("Unknown original queue action");
            }
        }
        else
        {
            index=Array.FindIndex(downloads.Snapshot().ToArray(),item=>RowId(item,true)==id);
            if(index<0)throw new ArgumentException("Missing original queue row");
            switch(action)
            {
                case "remove":downloads.DeleteByIndex(index);break;
                case "top":downloads.TopByIndex(index);break;
                default:throw new ArgumentException("Unsupported original download action");
            }
        }
        Refresh();
    }
    public void ShowDialog()
    {
        if(Application.Current.MainWindow?.Content is not Viewbox { Child:Canvas panel })return;
        dialog?.Close();dialog=new SelectedQueueDialog(panel,video=>Action("remove",video.Id),video=>Action("top",video.Id),
            ()=>Action("clear"),()=>Action("shuffle"),retry,move:(video,target)=>Action("move",video.Id,target),youtubeIcons:false,
            canDrag:video=>video.Id.StartsWith("local:",StringComparison.Ordinal));
        Refresh();
    }
    private sealed record OriginalRemoteRow(string Id,string Title,bool Downloading,bool CanMove,bool CanTop);
}
