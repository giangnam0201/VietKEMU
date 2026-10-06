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
        queue.AddRange(new[]{0,1,2,3});
        Require(!OriginalQueueOrder.Move(queue,0,2) && !OriginalQueueOrder.Move(queue,1,0) && !OriginalQueueOrder.Move(queue,1,4),"Drag moved the protected head or accepted the one-past-last marker");
        Require(OriginalQueueOrder.Move(queue,1,3) && queue.SequenceEqual(new[]{0,2,3,1}),"Downward drag adjusted the original target index incorrectly");
        Require(OriginalQueueOrder.Move(queue,3,1) && queue.SequenceEqual(new[]{0,1,2,3}),"Upward drag changed the wrong song");
        Require(!OriginalQueueOrder.Move(queue,2,2) && !OriginalQueueOrder.Move(queue,-1,1),"Invalid or unchanged drag unexpectedly persisted");
        var drag=new OriginalQueueDragPosition();
        Require(!drag.Update(0,0,0,4) && !drag.HasMarker,"Insertion before the playing head was allowed");
        Require(drag.Update(130,0,0,4) && drag.Target==2 && drag.MarkerY==119,"Original 65px insertion row or -11px marker offset differs");
        Require(!drag.Update(0,0,0,4) && drag.Target==2 && drag.MarkerY==119,"Invalid move lost the last valid insertion target");
        Require(drag.Update(260,0,0,4) && drag.Target==4,"Original one-past-last marker quirk was removed");
        Require(drag.Update(60,1,-10,20) && drag.Target==1 && drag.MarkerY==44,"Original partial-row division/remainder differs");
        Console.WriteLine("Original drag source/target guards, move order, retained insertion marker and partially scrolled rows verified.");
        Console.WriteLine("Original selected queue top, tail-only shuffle and clear-except-playing rules verified.");
    }
}
