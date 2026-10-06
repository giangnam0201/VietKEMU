namespace VietK.Core;

// LocalOnlineSongManager.downloadFirstSong/startDownload/stopDownload state.
// URL resolution and transfer are separate host services, not a fake download.
public sealed class OriginalDownloadSelection(OriginalDownloadQueue queue,Action changed,
    Action<int> requestMediaUrl,Action<SelectedPlaylistItem,int> requestOtherDownload)
{
    public bool IsDownloading { get; private set; }
    public bool IsStopped { get; private set; }
    public int CurrentSongId { get; private set; }
    public int CurrentState { get; private set; } // Java default 0; reset assigns 200.
    private int currentIndex;
    public void DownloadFirst()=>Select(false);
    public void DownloadNext()=>Select(true);
    private void Select(bool next)
    {
        queue.WithLockedItems(items=>
        {
            if(IsStopped || IsDownloading)return;
            IsDownloading=true;
            currentIndex=next?currentIndex+1:0;
            if(items.Count<=currentIndex) { IsDownloading=false;return; }
            var first=items[currentIndex];
            if(first.PlayType is "normal" or "kmtrain" or "photomv" or "mdream")
            {
                CurrentSongId=first.SongMetadata.Id;
                foreach(var item in items.Where(item=>item.SongMetadata.Id==CurrentSongId))item.DownloadState=202;
                CurrentState=202;changed();requestMediaUrl(CurrentSongId);
            }
            else requestOtherDownload(first,1);
        });
    }
    public void Stop()=>queue.WithLockedItems(_=>IsStopped=true);
    public void Start() { queue.WithLockedItems(_=>IsStopped=false);DownloadFirst(); }
    // Non-interrupting errors retain the active selection until the original
    // 1000 ms recovery delay. Notify after marking every matching song row.
    public void RecordError(int errorCode,bool interrupt)
    {
        queue.WithLockedItems(items=>
        {
            foreach(var item in items.Where(item=>item.SongMetadata.Id==CurrentSongId))item.DownloadState=errorCode;
            CurrentState=errorCode;changed();
            if(interrupt)Reset();
        });
    }
    public void AdvanceAfterError() { Reset();DownloadNext(); }
    // Selection portion of resetState; transfer fields are owned by the future
    // downloader port. Reset does not start the next song or clear stop state.
    public void Reset()=>queue.WithLockedItems(_=> { IsDownloading=false;CurrentSongId=0;CurrentState=200; });
}
