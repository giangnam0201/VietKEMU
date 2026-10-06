namespace VietK.Core;

// classes11.dex: DanmakusRetainer.RLDanmakusRetainer.fix/isOutVerticalEdge,
// MaximumLinesFilter.filter and OverlappingFilter.filter. Representatives
// outlive their visible images until replaced, as in the original sorted set.
public sealed class OriginalBarrageRetainer(float displayHeight,int maximumLines=10,bool preventOverlap=true)
{
    private readonly List<Item> retained=[];
    public int Count=>retained.Count;
    public sealed class Item(OriginalBarrageMotion motion,long start,float height)
    {
        public OriginalBarrageMotion Motion { get; }=motion;
        public long Start { get; }=start;
        public float Height { get; }=height;
        public float Top { get; internal set; }
        public float Bottom=>Top+Height;
        public bool Shown { get; internal set; }
        public float Right(long now)=>Motion.Left(now-Start)+Motion.PaintWidth;
    }
    public sealed record Placement(bool Accepted,float Top,int Lines,bool Overlap,bool VerticalEdge);
    public Placement Place(Item item,long now,bool priority=false)
    {
        if(now<=item.Start || now>=item.Start+item.Motion.Duration)return new(false,item.Top,0,false,false);
        if(item.Shown)return new(true,item.Top,0,false,false);
        var top=0f;var lines=0;var overlap=retained.Count>0;var reused=false;
        var checkEdge=true;var edge=false;var overflow=false;
        Item? insert=null,first=null,previous=null,minimumRight=null;
        foreach(var existing in retained.OrderBy(value=>value.Top).ToArray())
        {
            lines++;
            if(ReferenceEquals(existing,item)) { insert=existing;previous=null;reused=true;overlap=false;break; }
            first??=existing;
            if(item.Height+existing.Top>displayHeight) { overflow=true;break; }
            if(minimumRight is null || minimumRight.Right(now)>=existing.Right(now))minimumRight=existing;
            overlap=existing.Motion.WillHit(item.Motion,existing.Start,item.Start,now);
            if(!overlap) { insert=existing;break; }
            previous=existing;
        }
        if(insert is not null)
        {
            top=previous?.Bottom??insert.Top;
            // Removal precedes verifier filtering in the APK, even if that
            // verifier subsequently rejects this candidate's line count.
            if(!ReferenceEquals(insert,item))retained.Remove(insert);
        }
        else if(overflow && minimumRight is not null)
        { top=minimumRight.Top;checkEdge=false;reused=false; }
        else if(previous is not null)
        { top=previous.Bottom;overlap=false; }
        else if(first is not null)
        { top=first.Top;retained.Remove(first);reused=false; }
        if(checkEdge)
        {
            edge=top<0 || first is not null && first.Top>0 || top+item.Height>displayHeight;
            if(edge) { top=0;overlap=true; }
        }
        if(top==0)reused=false;
        if(!priority && (lines>=maximumLines || preventOverlap && overlap))
            return new(false,top,lines,overlap,edge);
        if(edge)retained.Clear();
        item.Top=top;item.Shown=true;
        if(!reused && !retained.Contains(item))retained.Add(item);
        return new(true,top,lines,overlap,edge);
    }
    public void Clear()=>retained.Clear();
}
