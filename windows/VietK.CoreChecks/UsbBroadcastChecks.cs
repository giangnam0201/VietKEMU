using System.Security.Cryptography;
using VietK.Core;

internal static class UsbBroadcastChecks
{
    internal static async Task Run()
    {
        var root=Path.Combine(Path.GetTempPath(),"vietk-usb-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var source=Path.Combine(root,"removable","Demo.mp4");Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            var bytes=RandomNumberGenerator.GetBytes(131079);File.WriteAllBytes(source,bytes);
            var store=new OriginalUsbBroadcastStore(Path.Combine(root,"state"));var commits=0;
            Require(await store.ImportAsync(source,()=>commits++),"USB copy failed");
            Require(commits==1&&File.ReadAllBytes(store.DestinationPath).SequenceEqual(bytes),"Copy was not complete before publication");
            File.Delete(source);Require(File.ReadAllBytes(store.DestinationPath).SequenceEqual(bytes),"Imported video depended on removable source");
            Require(!await store.ImportAsync(source),"Missing source reported success");
            Require(File.ReadAllBytes(store.DestinationPath).SequenceEqual(bytes),"Failed import destroyed existing video");
            Require(!Directory.GetFiles(Path.GetDirectoryName(store.DestinationPath)!,"*.tmp").Any(),"Temporary copy leaked");
            File.WriteAllBytes(source,[9,8,7]);Require(await store.ImportAsync(source),"Replacement copy failed");
            Require(File.ReadAllBytes(store.DestinationPath).SequenceEqual(new byte[]{9,8,7}),"Replacement was not published");
            Require(store.Delete()&&!File.Exists(store.DestinationPath)&&File.Exists(source),"Delete affected removable source");
            try { File.CreateSymbolicLink(store.DestinationPath,source); }
            catch(UnauthorizedAccessException) { Console.WriteLine("USB link check unavailable: runner cannot create symbolic links");return; }
            Require(!store.Delete()&&!await store.ImportAsync(source)&&File.ReadAllBytes(source).SequenceEqual(new byte[]{9,8,7}),"Linked destination allowed an external write/delete");
            File.Delete(store.DestinationPath);
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(root).StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(root).StartsWith("vietk-usb-check-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected USB test cleanup path");
            Directory.Delete(root,true);
        }
    }
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
