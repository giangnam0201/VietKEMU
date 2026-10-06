using VietK.Core;

internal static class BroadcastVolumeChecks
{
    internal static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"vietk-broadcast-volume-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            var settings=new OriginalBroadcastVolumeSettings(folder);Require(settings.Volume==15&&!settings.Muted,"Original idle defaults differ");
            var song=new OriginalDefaultVolumeSettings(folder);song.Save(7);
            foreach(var value in new[]{0,20,8})foreach(var mute in new[]{true,false})
            { settings.Save(value,mute);var restored=new OriginalBroadcastVolumeSettings(folder);Require(restored.Volume==value&&restored.Muted==mute,"Idle preferences failed round trip"); }
            foreach(var value in new[]{-1,21}) { try { settings.Save(value,true);throw new InvalidDataException("Invalid idle volume accepted"); }catch(ArgumentOutOfRangeException) { } }
            Require(new OriginalBroadcastVolumeSettings(folder).Volume==8&&!new OriginalBroadcastVolumeSettings(folder).Muted,"Rejected write changed saved pair");
            Require(new OriginalDefaultVolumeSettings(folder).Volume==7,"Idle settings changed song default");
            File.WriteAllText(Path.Combine(folder,"original-broadcast-volume.json"),"{\"broadcast_volume\":99,\"is_broadcast_mute\":false}");
            Require(new OriginalBroadcastVolumeSettings(folder).Volume==15,"Invalid saved idle gain applied");
            Console.WriteLine("Idle volume: separate original defaults, mute persistence and 0..20 limits verified.");
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(folder).StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("vietk-broadcast-volume-check-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected idle fixture cleanup path");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
    private static void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
}
