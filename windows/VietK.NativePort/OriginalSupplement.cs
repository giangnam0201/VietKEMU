using System.IO;
using System.IO.Compression;

namespace VietK.NativePort;

// Load the owner's recovered static resources locally. No firmware, browser
// data or supplemental media is uploaded by this bootstrap.
public static class OriginalSupplement
{
    public static string Root { get; private set; }=Path.Combine(AppContext.BaseDirectory,"Original");
    public static void Initialize()
    {
        var state=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"VietKNativePort");
        var candidates=new List<string>();
        if(Environment.GetEnvironmentVariable("VIETK_ORIGINAL_RESOURCES") is { Length:>0 } supplied)candidates.Add(supplied);
        candidates.Add(Path.Combine(AppContext.BaseDirectory,"original-ambience-idle.zip"));
        candidates.Add(Path.Combine(state,"original-ambience-idle.zip"));
        for(var parent=new DirectoryInfo(AppContext.BaseDirectory);parent is not null;parent=parent.Parent)
            candidates.Add(Path.Combine(parent.FullName,".reference","recovered-original","original-ambience-idle.zip"));
        candidates.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"ChatGPT","VietKEMU",
            ".reference","recovered-original","original-ambience-idle.zip"));
        var source=candidates.FirstOrDefault(File.Exists);
        if(source is null)return;
        var directory=Path.GetFullPath(Path.Combine(state,"original-supplement"));
        var stamp=new FileInfo(source).Length+":"+File.GetLastWriteTimeUtc(source).Ticks;
        var marker=Path.Combine(directory,"source-stamp.txt");
        if(File.Exists(marker) && File.ReadAllText(marker)==stamp && File.Exists(Path.Combine(directory,"player","60003950.mp4")))
        { Root=directory;return; }
        Directory.CreateDirectory(directory);
        using var bundle=ZipFile.OpenRead(source);
        foreach(var entry in bundle.Entries)
        {
            var name=entry.FullName.Replace('\\','/');
            if(name.EndsWith('/'))continue;
            if(!(name.StartsWith("ambience/",StringComparison.Ordinal) || name=="player/60003950.mp4") ||
                !new[]{".png",".wav",".mp4",".xml",".json"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase))
                throw new InvalidDataException("Unexpected original supplemental resource");
            var target=Path.GetFullPath(Path.Combine(directory,name));
            if(!target.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Original resource path escapes its local directory");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);entry.ExtractToFile(target,true);
        }
        if(!File.Exists(Path.Combine(directory,"player","60003950.mp4")))throw new InvalidDataException("Original idle clip missing from supplement");
        File.WriteAllText(marker,stamp);Root=directory;
    }
}
