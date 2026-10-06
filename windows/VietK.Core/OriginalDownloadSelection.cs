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
    public void DownloadFirst()
    {
        queue.WithLockedItems(items=>
        {
            if(IsStopped || IsDownloading)return;
            IsDownloading=true;
            if(items.Count==0) { IsDownloading=false;return; }
            var first=items[0];
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
    // Selection portion of resetState; transfer fields are owned by the future
    // downloader port. Reset does not start the next song or clear stop state.
    public void Reset()=>queue.WithLockedItems(_=> { IsDownloading=false;CurrentSongId=0;CurrentState=200; });
}
