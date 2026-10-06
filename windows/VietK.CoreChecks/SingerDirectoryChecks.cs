using Microsoft.Data.Sqlite;
using VietK.Core;

internal static class SingerDirectoryChecks
{
    internal static void Run()
    {
        static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
        using var database=new SqliteConnection("Data Source=:memory:");database.Open();
        using(var command=database.CreateCommand())
        {
            command.CommandText="""
                CREATE TABLE tblSinger(SongsterID INTEGER,SongsterName TEXT,SongsterPy TEXT,Pic_FileID_L INTEGER,singer_name_en TEXT,SongsterTypeID INTEGER,SongsterOrderRank INTEGER);
                INSERT INTO tblSinger VALUES
                (1,'Alpha','AL',1,'Alpha',8,10),(2,'Long Alpha','ALA',2,'Long Alpha',9,100),
                (3,'Band','BA',3,'The Alpha Band',10,1000),(4,'China male','CM',4,'China male',3,2000),
                (5,'Western','WE',5,'Western',35,3000),(6,'Other','OT',6,'Other',53,9999),
                (7,'Empty spell','',7,'Empty',8,1),(8,'Long spell','ABCDEFGHI',8,'Long',8,2),
                (9,'Alpha longer','ALONG',9,'Alpha longer',8,999);
                """;command.ExecuteNonQuery();
        }
        var directory=new OriginalSingerDirectory(database);
        Require(directory.BySpell("",0,0).All(singer=>singer.Id!=6)&&directory.BySpell("",0,0).Count==8,"Original all-country mapping must exclude type 53");
        Require(directory.CountBySpell("",0,0)==7&&directory.BySpell("",1,1).Any(singer=>singer.Id==7),"Original count/list empty-spelling distinction changed");
        Require(directory.BySpell("",1,2).Single().Id==2&&directory.BySpell("",1,3).Single().Id==3,"Vietnam sex mapping failed");
        Require(directory.BySpell("",3,1).Single().Id==4&&directory.BySpell("",2,1).Single().Id==5,"China/Western type mapping failed");
        Require(directory.BySpell("AL",1,0).Select(singer=>singer.Id).SequenceEqual(new[]{1,2,9,3}),"Exact initials, prefix length, English substring and rank sorting differ");
        Require(directory.ByName("Alpha",1,0,false).Select(singer=>singer.Id).SequenceEqual(new[]{1,9,2,3})&&directory.ByName("Alpha",1,0,true).Select(singer=>singer.Id).SequenceEqual(new[]{1,9}),"Name prefix/substring branch differs");
        Require(directory.BySpell("",1,1,length:9).Single().Id==8&&directory.CountBySpell("",1,1,length:2)==1,"Original spelling-length filters differ");
        Require(directory.BySpell("'",0,0).Count==0,"Directory search did not safely bind literal input");
        using(var transaction=database.BeginTransaction())
        {
            for(var index=0;index<83;index++)
            {
                using var command=database.CreateCommand();command.Transaction=transaction;
                command.CommandText="INSERT INTO tblSinger VALUES($id,'Batch','ZZ',0,'Batch',8,$rank)";
                command.Parameters.AddWithValue("$id",100+index);command.Parameters.AddWithValue("$rank",1000-index);command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
        Require(directory.CountBySpell("ZZ",1,1)==83&&directory.BySpell("ZZ",1,1).Count==80&&directory.BySpell("ZZ",1,1,1).Select(singer=>singer.Id).SequenceEqual(new[]{180,181,182}),"80-row directory pagination boundaries failed");
        Console.WriteLine("Original singer directory country/sex mapping, spelling/name ordering, count quirks and 80-row batches verified.");
    }
}
