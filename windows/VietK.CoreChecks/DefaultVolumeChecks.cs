using VietK.Core;

internal static class DefaultVolumeChecks
{
    internal static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"vietk-default-volume-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            var settings=new OriginalDefaultVolumeSettings(folder);
            Require(settings.Volume==15,"Original default volume differs");
            foreach(var value in new[]{0,20,7}) { settings.Save(value);Require(new OriginalDefaultVolumeSettings(folder).Volume==value,"Default volume did not persist"); }
            foreach(var value in new[]{-1,21})
            { try { settings.Save(value);throw new InvalidDataException("Invalid default volume accepted"); }catch(ArgumentOutOfRangeException) { } }
            Require(new OriginalDefaultVolumeSettings(folder).Volume==7,"Rejected volume changed saved default");
            File.WriteAllText(Path.Combine(folder,"original-default-volume.json"),"{\""+OriginalDefaultVolumeSettings.Key+"\":99}");
            Require(new OriginalDefaultVolumeSettings(folder).Volume==15,"Invalid persisted default was applied");
            Console.WriteLine("Separate default volume: original 15, 0..20 persistence and rejected values verified.");
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(folder).StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("vietk-default-volume-check-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected volume fixture cleanup path");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
