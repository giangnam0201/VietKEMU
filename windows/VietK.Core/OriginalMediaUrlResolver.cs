using System.Globalization;
using System.Text.Json;

namespace VietK.Core;

public sealed record RemoteSongMedia(SongMedia Metadata,string Url,string RemoteSubtitle,string LocalSubtitle);

// DCDomain.requestMediaList, checked against classes26.dex. The source's
// computed type/extension/filesize are not passed to the Media constructor.
public sealed class OriginalMediaUrlResolver(
    Func<IReadOnlyDictionary<string,object>,JsonElement> sendMessage,
    Func<string?> ethernetMac,Action<string> probeHttpFile,
    Func<int,LocalSong?> lookup,Func<int,bool> canScore,Action<LocalSong,int> updateScore,
    Func<bool> linked,Func<string?> firstActiveVolumeUuid)
{
    public IReadOnlyList<RemoteSongMedia> Request(int songId)
    {
        var request=new Dictionary<string,object> { ["cmdid"]="sn_song_media_list",["songid"]=songId,["token"]="" };
        // JSONObject.put(key, null) removes the key.
        var mac=ethernetMac();if(mac is not null)request["mac"]=mac;
        var response=sendMessage(request);
        var array=response.GetProperty("medialist");
        if(array.ValueKind!=JsonValueKind.Array)throw new JsonException("medialist is not an array");
        var original=HeaderInt(response,"origininfo");var accompany=HeaderInt(response,"accompanyinfo");
        var volume=HeaderInt(response,"vol");var subtitleType=HeaderInt(response,"subtitletype");
        var subtitle=MessageString(response,"subtitleurl");var result=new List<RemoteSongMedia>();
        foreach(var entry in array.EnumerateArray())
        {
            _=EntryInt(entry.GetProperty("type"));
            var url=JsonString(entry.GetProperty("url"));
            _=EntryInt(entry.GetProperty("filesize"));
            probeHttpFile(url);
            var song=lookup(songId);
            if(song is not null && subtitleType!=(canScore(songId)?0:-1))updateScore(song,subtitleType);
            var uuid=linked()?"":firstActiveVolumeUuid();
            var metadata=new SongMedia(0,songId,"songname",volume,original,accompany,"0","0",1,
                "0","0","0","0",0,"0","0",uuid);
            result.Add(new(metadata,url,subtitle,""));
        }
        return result;
    }
    private static string MessageString(JsonElement obj,string key)=>obj.TryGetProperty(key,out var value)?JsonString(value):"";
    private static string JsonString(JsonElement value)=>value.ValueKind==JsonValueKind.String?value.GetString()!:value.GetRawText();
    private static int HeaderInt(JsonElement obj,string key)
    {
        // Integer.valueOf(DataCenterMessage.get): unlike JSONObject.getInt,
        // fractions and surrounding whitespace are rejected.
        var value=MessageString(obj,key);var index=0;var negative=false;
        if(value.Length>0 && value[0] is '+' or '-') { negative=value[0]=='-';index++; }
        if(index==value.Length)throw new FormatException("Missing integer: "+key);
        long number=0;var limit=negative?2147483648L:int.MaxValue;
        for(;index<value.Length;index++)
        {
            var digit=CharUnicodeInfo.GetDecimalDigitValue(value[index]);
            if(digit<0 || number>(limit-digit)/10)throw new FormatException("Invalid integer: "+key);
            number=number*10+digit;
        }
        return (int)(negative?-number:number);
    }
    private static int EntryInt(JsonElement value)
    {
        // Android org.json.JSON.toInteger: integral Number.intValue wraps a
        // long; floating numbers/strings truncate and saturate like Java casts.
        if(value.ValueKind==JsonValueKind.Number && value.TryGetInt64(out var integer))return unchecked((int)integer);
        double number;
        if(value.ValueKind==JsonValueKind.Number)number=value.GetDouble();
        else if(value.ValueKind==JsonValueKind.String && double.TryParse(value.GetString(),NumberStyles.Float,CultureInfo.InvariantCulture,out var parsed))number=parsed;
        else throw new JsonException("Invalid media entry integer");
        return double.IsNaN(number)?0:number>=int.MaxValue?int.MaxValue:number<=int.MinValue?int.MinValue:(int)number;
    }
}
