using VietK.Core;

internal static class BroadcastRankCacheChecks
{
    internal static void Run()
    {
        var folder=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-broadcast-rank-"+Guid.NewGuid().ToString("N")));
        try
        {
            LocalSong? Lookup(int id)=>id is >=1 and <=5?new(id,id==5?"":"Rank song "+id,"",2,"Singer",[0,0,0,0],[0,0,0,0],[8,0,0,0],0,0,0,null,null,1,null,id==2?0:1,id==3?1:id==4?2:0):null;
            var rank=new OriginalBroadcastRankCache(folder,Lookup);var offline=new SongQueryContext();
            Require(rank.Page(0,offline).Count==0,"Missing rank cache invented an ordering");
            var cache=Path.Combine(folder,"rankcache","0.txt");Directory.CreateDirectory(Path.GetDirectoryName(cache)!);
            File.WriteAllText(cache,"[1/0, 2/1, 3/2, 4/0, 5/0, 999, -1/0, 1/1]");
            Require(rank.Page(0,offline).Select(s=>s.Id).SequenceEqual(new[]{1,1}),"Rank order, duplicate or visibility filters differ");
            Require(rank.Page(0,new(true,true,true)).Select(s=>s.Id).SequenceEqual(new[]{1,2,3,4,1}),"Connected/PSL rank filtering differs");
            Require(rank.Page(0,new(false,true)).Select(s=>s.Id).SequenceEqual(new[]{1,1}),"Connected flag alone admitted an online row");
            File.WriteAllText(cache,"["+string.Join(", ",Enumerable.Repeat("2/0",50).Append("1/0"))+"]");
            Require(rank.Page(0,offline).Count==0&&rank.Page(1,offline).Single().Id==1,"Filtering happened before the original raw 50-entry page slice");
            File.WriteAllText(cache,"[1/0, 999, 2/bad, 1/0]");
            Require(rank.Page(0,new(true,true)).Select(s=>s.Id).SequenceEqual(new[]{1}),"Malformed type did not preserve preceding results and stop the page");
            File.WriteAllText(cache,"[1/0, bad/0, 1/0]");Require(rank.Page(0,offline).Count==1,"Malformed ID did not stop rank parsing");
            File.WriteAllText(cache,"[]");Require(rank.Page(0,offline).Count==0&&rank.Page(int.MaxValue/50,offline).Count==0,"Empty/out-of-range rank page fabricated rows");
            Console.WriteLine("Original broadcast rank cache: order/duplicates, raw page slicing, database/PSL/local filters, missing and malformed cache behavior verified.");
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!folder.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("vietk-broadcast-rank-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected rank fixture cleanup path");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
