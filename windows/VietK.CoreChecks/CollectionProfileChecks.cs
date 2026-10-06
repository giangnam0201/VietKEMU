using VietK.Core;

internal static class CollectionProfileChecks
{
    public static void Run()
    {
        static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
        var root=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-collection-check-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(root);
        try
        {
            var profile=new OriginalCollectionProfiles(root);
            Require(profile.Toggle(1)==CollectionToggleResult.LoginRequired,"Favorite bypassed collection login");
            Require(profile.Login("a","pass")==CollectionLoginResult.InvalidLength,"Short profile accepted");
            Require(profile.Login("evil\\path","pass")==CollectionLoginResult.InvalidFilename,"Windows path escape accepted");
            Require(profile.Login("user/one","pass/one")==CollectionLoginResult.Success&&profile.CurrentUser=="user_one","Original slash calibration or creation failed");
            Require(profile.Toggle(10)==CollectionToggleResult.Added&&profile.Toggle(20)==CollectionToggleResult.Added,"Favorite did not persist");
            var restored=new OriginalCollectionProfiles(root);
            Require(restored.CurrentUser=="user_one"&&restored.Snapshot().SequenceEqual(new[]{10,20}),"Profile/session failed to restore");
            Require(restored.Login("user/one","wrong")==CollectionLoginResult.WrongPassword&&restored.Snapshot().SequenceEqual(new[]{10,20}),"Wrong password changed collection");
            restored.Logout();Require(new OriginalCollectionProfiles(root).CurrentUser=="","Logout did not persist");
            Require(restored.Login("second","second")==CollectionLoginResult.Success&&restored.Snapshot().Count==0,"Profiles mixed collections");
            Require(restored.Toggle(30)==CollectionToggleResult.Added,"Second profile failed");
            Require(restored.Login("user/one","pass/one")==CollectionLoginResult.Success&&restored.Snapshot().SequenceEqual(new[]{10,20}),"Switching profile lost original songs");
            Require(restored.Toggle(10)==CollectionToggleResult.Removed&&restored.Toggle(10)==CollectionToggleResult.Added&&restored.Snapshot().SequenceEqual(new[]{20,10}),"Original insertion order differs");
            var file=Path.Combine(root,"kmbox","muilt_collect","user_one-VietK-pass_one");
            Require(File.ReadAllText(file)=="20\n10\n","Original UTF-8 line file format differs");
            // A reader without write sharing reproduces a real Windows locked
            // profile; an unsuccessful save must not light up the favorite.
            using(var locked=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.Read))
                Require(restored.Toggle(40)==CollectionToggleResult.StorageError&&!restored.Contains(40),"Failed write changed confirmed favorites");
            File.WriteAllText(file,"20\n20\n0\n-1\ninvalid\n10\n");
            restored=new(root);Require(restored.Snapshot().SequenceEqual(new[]{20,10}),"Invalid/duplicate imported IDs were retained");
            LocalSong Song(int id,int local,int remote,int psl=0)=>new(id,"fixture","F",1,"Singer",[],[],[],0,0,0,null,null,remote,null,local,psl);
            var songs=new Dictionary<int,LocalSong> { [20]=Song(20,0,1),[10]=Song(10,1,1) };
            LocalSong? Lookup(int id)=>songs.GetValueOrDefault(id);
            Require(restored.Visible(Lookup,new(0,1),new()).Count==0&&restored.Visible(Lookup,new(1,1),new()).Single().Id==10,"Collection paged after visibility filtering");
            Require(restored.Visible(Lookup,new(),new(true,false)).Count==1&&restored.Visible(Lookup,new(),new(true,true)).Count==2,"Collection exposed disconnected remote songs");
            songs[20]=Song(20,0,-1);songs[10]=Song(10,1,1,1);
            Require(restored.Visible(Lookup,new(),new(true,true)).Count==0&&restored.Visible(Lookup,new(),new(true,true,true)).Single().Id==10,"PSL/unavailable filtering differs");
            for(var id=100;id<248;id++)Require(restored.Toggle(id)==CollectionToggleResult.Added,"Collection filled incorrectly");
            Require(restored.Snapshot().Count==150&&restored.Toggle(999)==CollectionToggleResult.LimitReached&&!restored.Contains(999),"Original 150-song limit differs");
            Require(restored.Toggle(20)==CollectionToggleResult.Removed&&restored.Toggle(999)==CollectionToggleResult.Added,"Full collection cannot remove/replace a song");
            Require(new OriginalCollectionProfiles(root).Snapshot().Count==150,"Limit state failed to persist");
            Console.WriteLine("Original local collection: profile login, isolation, persistence, rollback, page/filter order and 150-song limit verified.");
        }
        finally
        {
            var parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!root.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("vietk-collection-check-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture cleanup path");
            Directory.Delete(root,true);
        }
    }
}
