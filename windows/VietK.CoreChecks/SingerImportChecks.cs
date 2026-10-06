using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using VietK.Core;

internal static class SingerImportChecks
{
    internal static void Run()
    {
        var folder=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-singer-import-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
        try
        {
            var seed=Path.Combine(folder,"seed.db");var whole=Path.Combine(folder,"whole.db");var state=Path.Combine(folder,"state.db");
            const string singerSchema="CREATE TABLE tblSinger(SongsterID INTEGER PRIMARY KEY,SongsterName TEXT,SongsterPy TEXT,SongsterLove INTEGER,SongsterTypeID INTEGER,SongsterOrderRank INTEGER,LastUpdateTime TEXT,Pic_FileID_H INTEGER,Pic_FileID_L INTEGER,Pic_FileID_M INTEGER,Pic_FileID_S INTEGER,Imitate_Pic_FileID_0 INTEGER,Imitate_Pic_FileID_1 INTEGER,Imitate_Pic_FileID_2 INTEGER,photopath TEXT,isGroup INTEGER,gender INTEGER,country INTEGER,singer_name_en TEXT)";
            void Write(string path,string sql)
            { using var db=new SqliteConnection("Data Source="+path+";Pooling=False");db.Open();using var cmd=db.CreateCommand();cmd.CommandText=sql;cmd.ExecuteNonQuery(); }
            Write(seed,singerSchema+";CREATE TABLE tblSong(SongID INTEGER PRIMARY KEY,SongsterID1 INTEGER,SongsterID2 INTEGER,SongsterID3 INTEGER,SongsterID4 INTEGER,IsLocalExist INTEGER);INSERT INTO tblSong VALUES(1,10,20,30,40,1),(2,20,NULL,-1,0,0);CREATE TABLE tblSelectedList(id INTEGER PRIMARY KEY,songid INTEGER,canscore INTEGER,sequence INTEGER,customerId TEXT,tableId INTEGER,stage INTEGER);INSERT INTO tblSinger(SongsterID,SongsterName) VALUES(10,'Preserved local name');");
            Write(whole,singerSchema+";INSERT INTO tblSinger(SongsterID,SongsterName) VALUES(-1,'Minus'),(0,'Zero'),(10,'Catalogue replacement'),(20,'Second slot'),(30,'Third slot'),(40,'Fourth slot'),(50,'Unreferenced');");
            var seedHash=SHA256.HashData(File.ReadAllBytes(seed));var wholeHash=SHA256.HashData(File.ReadAllBytes(whole));
            using(var local=new LocalSongDatabase(seed,state))
                if(local.ImportReferencedSingers(whole)!=5||local.ImportReferencedSingers(whole)!=0)throw new InvalidDataException("Singer import lost a referenced slot, duplicated IDs or changed warm-start behavior");
            using(var db=new SqliteConnection("Data Source="+state+";Mode=ReadOnly;Pooling=False"))
            {
                db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT SongsterID,SongsterName FROM tblSinger ORDER BY SongsterID";using var rows=cmd.ExecuteReader();var ids=new List<int>();
                while(rows.Read()) { var id=rows.GetInt32(0);ids.Add(id);if(id==10&&rows.GetString(1)!="Preserved local name")throw new InvalidDataException("Singer import overwrote local metadata"); }
                if(!ids.SequenceEqual(new[]{-1,0,10,20,30,40}))throw new InvalidDataException("Singer reference membership differs from original EXISTS semantics");
            }
            if(!seedHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(seed)))||!wholeHash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(whole))))throw new InvalidDataException("Singer import mutated source databases");
            Console.WriteLine("Singer import verified: all four reference slots, NULL/duplicate/zero/negative IDs, existing metadata, idempotence and untouched source databases.");
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!folder.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("vietk-singer-import-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected singer import cleanup path");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
}
