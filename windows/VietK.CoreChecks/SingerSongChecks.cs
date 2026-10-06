using Microsoft.Data.Sqlite;
using VietK.Core;

internal static class SingerSongChecks
{
    internal static void Run()
    {
        static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
        using var database=new SqliteConnection("Data Source=:memory:");database.Open();
        using(var create=database.CreateCommand())
        {
            create.CommandText="""
                CREATE TABLE tblSinger(SongsterID INTEGER,SongsterName TEXT,SongsterPy TEXT,Pic_FileID_L INTEGER,singer_name_en TEXT);
                INSERT INTO tblSinger VALUES(9,'Singer one','SO',42,'First Artist'),(10,'Singer two','ST',43,'Second Artist');
                CREATE TABLE tblSong(SongID INTEGER,SongName TEXT,SongPy TEXT,SongWord INTEGER,songsterName TEXT,LanguageTypeID INTEGER,PlayNum INTEGER,hasRemote INTEGER,song_name_en TEXT,IsLocalExist INTEGER,is_psl INTEGER,SongsterID1 INTEGER,SongsterID2 INTEGER,SongsterID3 INTEGER,SongsterID4 INTEGER);
                INSERT INTO tblSong VALUES
                  (1,'Alpha','AA',2,'Singer one',8,10,1,'Alpha',1,0,9,0,0,0),
                  (2,'Beta Alpha','BA',2,'Singer two,Singer one',4,100,1,'Beta Alpha',2,0,10,9,0,0),
                  (3,'Remote','RA',2,'Singer one',8,1000,1,'Remote',0,0,0,0,9,0),
                  (4,'Unavailable','UA',2,'Singer one',8,2000,-1,'Unavailable',0,0,0,0,0,9),
                  (5,'Fourth slot','FA',2,'Singer one',1,9999,1,'Fourth slot',1,0,0,0,0,9),
                  (6,'PSL','PA',2,'Singer one',8,99999,1,'PSL',1,1,9,0,0,0),
                  (7,'No spell','',0,'Singer one',8,999999,1,'No spell',1,0,9,0,0,0),
                  (8,'Other singer','OA',2,'Singer two',8,999999,1,'Other singer',1,0,10,0,0,0),
                  (100000000,'MIDI','MA',2,'Singer one',8,999999,1,'MIDI',1,0,9,0,0,0);
                """;create.ExecuteNonQuery();
        }
        var singers=new OriginalSingerSongs(database);var offline=new SongQueryContext();var online=new SongQueryContext(true,true);
        Require(singers.Find("Singer two")?.Id==10&&singers.Find("First Artist")?.Id==9&&singers.Find("missing") is null,"Original singer name/English lookup failed");
        Require(singers.Find("Singer two,Singer one") is null,"Duet name was incorrectly resolved as one singer");
        Require(singers.BySpell(9,"",0,0,new(),offline).Select(song=>song.Id).SequenceEqual(new[]{2,1,5}),"Singer songs ignored membership, local ordering, empty-spell/PSL/MIDI filtering");
        Require(singers.BySpell(9,"",0,0,new(),online).Select(song=>song.Id).SequenceEqual(new[]{2,1,5,3}),"Connected singer visibility or local-first sorting failed");
        Require(singers.BySpell(9,"",0,1,new(),offline).Single().Id==5,"Singer language filter failed");
        Require(singers.BySpell(9,"B",2,0,new(),offline).Single().Id==2,"Singer spell-prefix or length filter failed");
        Require(singers.BySpell(9,"",0,0,new(0,1),offline).Count==0&&singers.BySpell(9,"",0,0,new(1,1),offline).Single().Id==2,"Singer MIDI filtering moved SQL pagination boundaries");
        Require(singers.ByName(9,"Alpha",0,false,new(),offline).Select(song=>song.Id).SequenceEqual(new[]{1,2})&&singers.ByName(9,"Alpha",0,true,new(),offline).Single().Id==1,"Original singer-name substring/prefix branches changed");
        Require(singers.BySpell(0,"",0,0,new(),online).Count==0&&singers.BySpell(9,"",0,0,new(),new(true,true,true)).Any(song=>song.Id==6),"Invalid singer or PSL-enabled branch failed");
        Console.WriteLine("Original singer lookup, all four membership slots, duet separation, visibility, ordering, query branches and post-LIMIT MIDI filtering verified.");
    }
}
