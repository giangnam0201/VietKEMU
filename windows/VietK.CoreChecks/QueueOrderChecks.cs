using VietK.Core;

internal static class QueueOrderChecks
{
    public static void Run()
    {
        void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
        var queue=new List<int> { 0,1,2,3,4 };
        Require(!OriginalQueueOrder.Top(queue,0) && !OriginalQueueOrder.Top(queue,1) && !OriginalQueueOrder.Top(queue,9),
            "Top moved the current/next song or accepted a missing item");
        Require(OriginalQueueOrder.Top(queue,4) && queue.SequenceEqual(new[]{0,4,1,2,3}),"Top did not insert immediately after the head");
        Require(OriginalQueueOrder.Shuffle(queue,_=>0) && queue.SequenceEqual(new[]{0,1,2,3,4}),"Shuffle changed the playing head or tail permutation");
        OriginalQueueOrder.ClearExceptPlaying(queue,false);
        Require(queue.SequenceEqual(new[]{0}),"Clear-all removed the currently playing song");
        Require(OriginalQueueOrder.Shuffle(queue,_=>throw new InvalidOperationException()),"Head-only shuffle should succeed without random draws");
        OriginalQueueOrder.ClearExceptPlaying(queue,true);
        Require(queue.Count==0 && !OriginalQueueOrder.Shuffle(queue,_=>0),"Idle clear or empty shuffle differs");
        OriginalQueueOrder.ClearExceptPlaying(queue,false);
        Require(queue.Count==0,"Empty clear introduced a song");
        Require(new QueueTransferDisplay(Waiting:true).Caption=="Chờ đợi","Queue waiting label differs");
        Require(new QueueTransferDisplay(45,100).Percent==45 && new QueueTransferDisplay(45,100).Caption=="45%","Known byte progress differs");
        Require(new QueueTransferDisplay(1048576).Percent is null && new QueueTransferDisplay(1048576).Caption.EndsWith(" MiB"),"Unknown streaming total invented a percentage");
        Require(new QueueTransferDisplay(-10,100).Percent==0 && new QueueTransferDisplay(long.MaxValue,1).Percent==100,"Progress bounds or integer overflow differ");
        Require(new QueueTransferDisplay(Error:"failure").Caption=="Lỗi tải","Failure is displayed as successful progress");
        Console.WriteLine("Original selected queue top, tail-only shuffle and clear-except-playing rules verified.");
    }
}
