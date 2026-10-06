using Microsoft.Data.Sqlite;
using VietK.Core;

internal static class BroadcastSearchChecks
{
    internal static void Run()
    {
        using var db=new SqliteConnection("Data Source=:memory:");db.Open();
        using(var cmd=db.CreateCommand())
        {
            cmd.CommandText="""
                CREATE TABLE tblSong(SongID INT,SongName TEXT,SongPy TEXT,SongWord INT,songsterName TEXT,LanguageTypeID INT,PlayNum INT,hasRemote INT,song_name_en TEXT,IsLocalExist INT,is_psl INT);
                INSERT INTO tblSong VALUES
                  (1,'Alpha name','ZN',2,'One',8,9,1,'English',1,0),
                  (2,'Other','AL',2,'Two',8,1,1,'Other',2,0),
                  (3,'Other name','ZZ',2,'Three',8,20,1,'Alpha English',1,0),
                  (4,'Remote','ALR',3,'Four',8,100,1,'Remote',0,0),
                  (5,'Alpha psl','AP',2,'Five',8,9999,1,'Psl',1,1),
                  (6,'X Alpha','XA',2,'Six',8,9999,1,'X English',1,0),
                  (100000001,'Alpha midi','A',1,'Seven',8,10000,1,'Midi',1,0);
                """;cmd.ExecuteNonQuery();
        }
        var search=new SongSearch(db);var offline=new SongQueryContext();
        Require(search.ByBroadcastSpell("Alpha",new(0,50),offline).Select(s=>s.Id).SequenceEqual(new[]{3,1}),"Broadcast name/English prefix differs or substring/PSL leaked");
        Require(search.ByBroadcastSpell("AL",new(0,50),offline).Select(s=>s.Id).SequenceEqual(new[]{2,3,1}),"Broadcast initial-prefix priority differs");
        Require(search.ByBroadcastSpell("AL",new(0,50),new(true,true)).Select(s=>s.Id).SequenceEqual(new[]{2,4,3,1}),"Broadcast connected visibility or spell-length order differs");
        Require(search.ByBroadcastSpell("",new(0,50),offline).Any(s=>s.Id==5),"Original empty-input PSL branch was lost");
        Require(search.ByBroadcastSpell("A",new(0,1),offline).Count==0&&search.ByBroadcastSpell("A",new(1,1),offline).Single().Id==2,"Broadcast MIDI filter changed SQL page boundaries");
        Require(search.ByBroadcastSpell("' OR 1=1 --",new(0,50),offline).Count==0,"Broadcast search interpreted input as SQL");
        Console.WriteLine("Original broadcast search: initial/name/English prefixes, ordering, online visibility, PSL branch and post-LIMIT MIDI filtering verified.");
    }
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
