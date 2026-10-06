namespace VietK.Core;

public sealed record DownloadQueueCommand(int What,int Arg1=0,SelectedPlaylistItem? Item=null);

// SongDownListManager's runtime state. Post/persistence, downloader, progress
// registry and song-update registry are explicit host dependencies.
public sealed class OriginalDownloadQueue(Action<DownloadQueueCommand> post,Action changed,
    Action downloadFirst,Action cancelCurrent,Action<int> removeProgress,Action<int> removeSongUpdate)
{
    private readonly List<SelectedPlaylistItem> items=new();
    public bool IsInitialized { get; private set; }
    public bool ClearOnInitialize { get; set; }=true;
    public int Count { get { lock(items)return items.Count; } }
    public IReadOnlyList<SelectedPlaylistItem> Snapshot() { lock(items)return items.ToArray(); }
    public SelectedPlaylistItem? At(int index) { lock(items)return index<0 || index>=items.Count?null:items[index]; }
    public bool MobileFirst { get { lock(items)return items.Count>0 && items[0].PlayType=="mobile"; } }

    public void Initialize(Action clearStored,Func<IReadOnlyList<SelectedPlaylistItem>> restore,bool onlineNeeded)
    {
        if(ClearOnInitialize)clearStored();
        var saved=restore().ToList();
        if(!onlineNeeded)clearStored();
        else
        {
            saved.RemoveAll(item=>item.PlayType=="mobile");
            foreach(var item in saved)Add(item);
        }
        IsInitialized=true;
        if(saved.Count>0)downloadFirst(); // Original requests this even when onlineNeeded is false.
    }
    public bool Add(SelectedPlaylistItem song)
    {
        lock(items)
        {
            var item=song.Copy();if(IsInitialized)item.FlowId=Guid.NewGuid().ToString();
            items.Add(item);
            if(IsInitialized) { post(new(4,Item:item));changed();downloadFirst(); }
            return true;
        }
    }
    public bool Top(SelectedPlaylistItem? song,bool exists,bool repeatNormal)
    {
        if(song is null)return false;
        lock(items)
        {
            var item=song.Copy();bool result;
            if(!exists || (repeatNormal && item.PlayType=="normal"))
            {
                var size=items.Count;result=Add(item);
                if(size>=2)result|=TopByIndex(size);
                changed();
            }
            else result=TopByIndex(FindSongIndex(item.SongMetadata.Id));
            downloadFirst();return result;
        }
    }
    private int FindSongIndex(int id)
    { if(id<=0)return 0;var index=items.FindIndex(item=>item.SongMetadata.Id==id);return index<0?0:index; }
    public int FindFlowIndex(string? flowId)
    { lock(items) { if(string.IsNullOrEmpty(flowId))return 0;var index=items.FindIndex(item=>item.FlowId==flowId);return index<0?0:index; } }
    public bool TopByIndex(int index)
    {
        lock(items)
        {
            if(index<=1 || index>=items.Count)return false;
            var item=items[index];items.RemoveAt(index);items.Insert(1,item);
            post(new(3,index+1));changed();return true;
        }
    }
    public bool Exists(SelectedPlaylistItem? item)
    { if(item is null)return false;lock(items)return items.Any(entry=>entry.InfoId==item.InfoId); }
    public bool ContainsSong(int songId)
    { lock(items)return songId>0 && items.Any(item=>item.SongMetadata.Id==songId); }
    public bool AddPublic(SelectedPlaylistItem? song)
    {
        if(song is null)return false;
        lock(items) { if(items.Count>0 && items[0].Broadcast)return false;items.Insert(0,song.Copy());return true; }
    }
    public bool DeleteByIndex(int index)
    {
        lock(items)
        {
            if(index<0 || index>=items.Count)return false;
            post(new(1,index+1));var item=items[index];removeProgress(item.SongMetadata.Id);
            if(ReferenceEquals(item.PlayType,"normal"))removeSongUpdate(item.SongMetadata.Id);
            items.RemoveAt(index);changed();return true;
        }
    }
    public void Clear()
    { lock(items)items.Clear();post(new(5));cancelCurrent(); }
    public bool SetProgressBySong(int songId,long maximum,long current)
    {
        lock(items)
        {
            var success=false;
            foreach(var item in items.Where(item=>item.SongMetadata.Id==songId))
            {
                item.DownloadState=202;
                if(maximum>0) { item.DownloadProgress=unchecked((int)(unchecked(100L*current)/maximum));success=true; }
            }
            return success;
        }
    }
    public bool SetProgressByFlow(string? flowId,int progress)
    {
        if(string.IsNullOrEmpty(flowId))return false;
        lock(items)
        {
            var matches=items.Where(item=>item.FlowId==flowId).ToArray();
            foreach(var item in matches) { item.DownloadState=202;item.DownloadProgress=progress; }
            return matches.Length>0;
        }
    }
    public bool SetError(int songId,int errorCode)
    {
        lock(items)
        {
            var matches=items.Where(item=>item.SongMetadata.Id==songId).ToArray();
            foreach(var item in matches)item.DownloadState=errorCode;return matches.Length>0;
        }
    }
    public void SetSongInfo(int songId,string? name,string? singer)
    {
        if(songId<=0)return;
        lock(items)
        {
            var item=items.FirstOrDefault(item=>item.SongMetadata.Id==songId);if(item is null)return;
            item.SongName=name??"";item.SingerName=singer??"";item.PlayName=name??"";
            item.InfoId=item.PlayType+"||"+item.PlayId+"||"+item.PlayName;
        }
    }
}
