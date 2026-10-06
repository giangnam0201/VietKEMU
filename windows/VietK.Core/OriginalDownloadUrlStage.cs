namespace VietK.Core;

// LocalOnlineSongManager.DCThread.run. Runs on the downloader's background
// request thread; UI-posted error transitions belong to its host callback.
public sealed class OriginalDownloadUrlStage(Func<bool> networkConnected,
    Func<int,IReadOnlyList<RemoteSongMedia>> requestMedia,Action stop,
    Action<int,bool> error,Action<RemoteSongMedia> updateMedia,Action downloadVideo)
{
    public string? VideoUrl { get; private set; }
    public void Run(int songId)
    {
        if(!networkConnected()) { stop();error(1015,false);return; }
        try
        {
            var media=requestMedia(songId)[0];
            VideoUrl=media.Url.Replace("%2F","/",StringComparison.Ordinal).Replace("?attname=","",StringComparison.Ordinal);
            if(!string.IsNullOrEmpty(VideoUrl)) { updateMedia(media);downloadVideo(); }
            else error(1012,false);
        }
        catch(Exception) { error(1013,false); }
    }
    public void Reset()=>VideoUrl=null;
}
