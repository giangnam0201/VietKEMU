namespace VietK.Core;

// Original queue UI has waiting, progress and retry/error states. Streaming
// YouTube transfers lack a total byte count; display bytes instead of a guess.
public sealed record QueueTransferDisplay(long Received=0,long Total=0,bool Waiting=false,string Error="")
{
    public int? Percent=>Total>0?(int)Math.Clamp(Math.Floor(100d*Math.Max(0,Received)/Total),0,100):null;
    public string Caption=>Error.Length>0?"Lỗi tải":Waiting?"Chờ đợi":Percent is int percent?percent+"%":$"{Math.Max(0,Received)/1048576d:0.0} MiB";
}
