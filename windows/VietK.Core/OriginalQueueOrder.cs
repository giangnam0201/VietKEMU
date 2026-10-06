namespace VietK.Core;

// SelectedLocalListManager.topItemBySerial, shuffle, clearListExceptPlaying.
// The currently selected first item is protected by Top and shuffle.
public static class OriginalQueueOrder
{
    public static bool Top<T>(IList<T> items,int index)
    {
        if(index<=1 || index>=items.Count)return false;
        var item=items[index];items.RemoveAt(index);items.Insert(1,item);return true;
    }
    public static bool Shuffle<T>(IList<T> items,Func<int,int> next)
    {
        if(items.Count==0)return false;
        for(var index=items.Count-1;index>1;index--)
        {
            var other=1+next(index);
            if(other<1 || other>index)throw new ArgumentOutOfRangeException(nameof(next));
            (items[index],items[other])=(items[other],items[index]);
        }
        return true;
    }
    public static void ClearExceptPlaying<T>(IList<T> items,bool idle)
    {
        if(idle)items.Clear();
        else while(items.Count>1)items.RemoveAt(items.Count-1);
    }
    public static bool Move<T>(IList<T> items,int source,int target)
    {
        if(source<1 || source>=items.Count || target<1 || target>=items.Count || source==target)return false;
        var item=items[source];items.RemoveAt(source);items.Insert(target,item);return true;
    }
}
