using System.Text.Encodings.Web;
using System.Text.Json.Nodes;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace VietK.Core;

public sealed class OriginalDataCenterTokens
{
    private readonly Dictionary<string,string?> tokens=new();
    private readonly object gate=new();private string? nullKeyToken;
    public string? Get(string? uri) { lock(gate)return uri is null?nullKeyToken:tokens.GetValueOrDefault(uri); }
    public void Set(string? uri,string? token) { lock(gate) { if(uri is null)nullKeyToken=token;else tokens[uri]=token; } }
}
public sealed record DataCenterPost(string Url,string Body,IReadOnlyList<KeyValuePair<string,string?>> Headers,bool Https);

// BaseDataCenterCommu + the normal DataCenterCommu token/URL-list overrides.
// Device identity, native signing and actual HTTP I/O are required dependencies.
// No synthesized token or authentication-success fallback is provided.
public sealed class OriginalDataCenterClient(OriginalDataCenterTokens tokens,Func<bool> networkConnected,
    Func<string?> chipId,Func<string?> mac,Func<string?> userAgent,string signVersion,
    Func<string?,string?,string?> sign,Func<DataCenterPost,string?> post,
    Action<string,string> configuration,Action<string> countryChanged,Action<string> broadcastIp,
    bool requireToken=true,bool needBroadcastIp=true)
{
    private readonly object gate=new();
    private static readonly JsonSerializerOptions JsonOptions=new() { Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    public string? LoginUri { get; private set; }="";
    public string RequestUri { get; private set; }="";
    public string ValidateCode { get; private set; }="";
    public string CommandFilter { get; private set; }="";
    public string LoginErrorCode { get; private set; }="";
    public string LoginErrorMessage { get; private set; }="";
    public bool IsKtv { get; set; }
    public bool NeedSelfRabbitMq { get; private set; }
    public Dictionary<string,string> ServerUrls { get; }=new();
    public bool IsLoggedIn { get { lock(gate)return ValidateCode.Length>0 && RequestUri.Length>0 &&
        (!requireToken || !string.IsNullOrEmpty(tokens.Get(LoginUri))); } }
    public void SetLoginUri(string? uri)
    {
        lock(gate)
        {
            ValidateCode="";CommandFilter="";RequestUri="";LoginErrorCode="";LoginErrorMessage="";
            LoginUri=uri; // Original logout leaves URI-scoped tokens and URL registry intact.
        }
    }
    public JsonObject Send(JsonObject request)=>SendInternal(request,false,null);
    // Host replacement for the Android service's explicit initial login task.
    public void Connect() { lock(gate) { if(!networkConnected())throw new IOException("Network conn error.");if(!IsLoggedIn)Login(); } }
    public JsonObject SendTo(JsonObject request,string? uri)=>SendInternal(request,true,uri);
    private JsonObject SendInternal(JsonObject request,bool explicitUri,string? uri)
    {
        lock(gate)
        {
            if(!networkConnected())throw new IOException("Network conn error.");
            if(!IsLoggedIn)Login();
            var command=Get(request,"cmdid");
            if(!IsKtv && CommandFilter.Length>0 && command.Length>0 &&
                !Regex.IsMatch(command,"\\A(?:"+CommandFilter+")\\z"))
                throw new InvalidOperationException("DataCenter commu check permission failed");
            var response=Request(request,explicitUri?uri:RequestUri);
            var content=response.ToJsonString(JsonOptions);
            if(Get(response,"errorcode")=="" && Get(response,"errormessage")=="" && content.Length>50)
                response=Parse(content[..50]); // Preserve original truncation/JSON parse failure.
            return response;
        }
    }
    private void Login()
    {
        var request=new JsonObject { ["cmdid"]=IsKtv?"ktv_device_login":"bs_device_login" };
        var chip=chipId();if(chip is not null)request["chipid"]=chip;
        var address=mac();if(address is not null)request["mac"]=address;
        var response=Request(request,LoginUri);
        var code=Get(response,"errorcode");var message=Get(response,"errormessage");
        if(!IsKtv)
        {
            CommandFilter=Get(response,"cmdid_filter");ServerUrls.Clear();
            var encodedUrls=Get(response,"serverUrlList");
            if(encodedUrls.Length>0)
            {
                var urls=JsonNode.Parse(encodedUrls) as JsonArray??throw new JsonException("serverUrlList is not an array");
                foreach(var entry in urls)
                {
                    var obj=entry as JsonObject??throw new JsonException("Server URL entry is not an object");
                    var type=obj["type"] is null?"":Get(obj,"type");
                    var url=obj["url"] is null?"":Get(obj,"url");
                    if(type.Length>0 && url.Length>0)ServerUrls[type]=url;
                }
            }
        }
        if(code!="0") { LoginErrorCode=code;LoginErrorMessage=message; }
        if(!IsKtv)tokens.Set(LoginUri,Get(response,"token"));
        RequestUri=Get(response,"serverip");ValidateCode=Get(response,"validatecode");
        var vietApi=Get(response,"viet_api");if(vietApi.Length>0)configuration("config_youtube_base_url",vietApi);
        var images=Get(response,"video_img_url");if(images.Length>0)configuration("key_song_img_url",images);
        if(IsKtv)NeedSelfRabbitMq=Get(response,"self_msg_push_state")=="1";
        else countryChanged(Get(response,"countrycode"));
        if(RequestUri.Length>0 && needBroadcastIp)
        {
            var rest=RequestUri[(RequestUri.IndexOf("://",StringComparison.Ordinal)+3)..];
            broadcastIp(rest[..rest.IndexOf('/')]); // Original string slicing, including port.
        }
        if(ValidateCode.Length==0)throw new InvalidOperationException("validatecode expect not empty, but received:"+ValidateCode);
    }
    private JsonObject Request(JsonObject message,string? uri)
    {
        if(string.IsNullOrEmpty(uri))throw new InvalidOperationException("Original HTTP factory returned no transport");
        var body=message.ToJsonString(JsonOptions);
        var headers=new List<KeyValuePair<string,string?>>
        {
            new("Accept-Charset","utf8"),new("Accept-Encoding",""),new("Accept","text/json"),new("doubledecode",""),
            new("User-Agent",userAgent()),new("sessionid",""),new("validcode",ValidateCode),new("devicetag",chipId())
        };
        if(!message.ContainsKey("cmdid"))throw new JsonException("Missing cmdid for signing header selection");
        var command=Get(message,"cmdid");
        if(command=="bs_device_login" || (!command.StartsWith("ktv_",StringComparison.Ordinal) && !command.StartsWith("pm_",StringComparison.Ordinal)))
        {
            headers.Add(new("signversion",signVersion));
            headers.Add(new("sign",sign(chipId(),command=="bs_device_login"?command:tokens.Get(LoginUri))));
        }
        var response=post(new(uri,body,headers,uri.StartsWith("https",StringComparison.Ordinal)));
        return string.IsNullOrEmpty(response)?new():Parse(response);
    }
    private static JsonObject Parse(string text)=>JsonNode.Parse(text) as JsonObject??throw new JsonException("Data-center message is not an object");
    private static string Get(JsonObject obj,string key)
    {
        if(!obj.TryGetPropertyValue(key,out var node))return "";
        if(node is null)return "null";
        return node is JsonValue value && value.TryGetValue<string>(out var text)?text:node.ToJsonString(JsonOptions);
    }
}
