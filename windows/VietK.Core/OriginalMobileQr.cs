using ZXing;
using ZXing.QrCode;
using ZXing.QrCode.Internal;
using ZXing.Rendering;

namespace VietK.Core;

public sealed record MobileQrBinding(string Serial="",string BindingPrefix="",string? RandomCode=null,string TvBindCode="",bool InsideNet=false)
{
    // Windows has no Android Build.SERIAL or manufacturer binding service.
    // Presentation is admitted only for supplied identity and a real URL prefix.
    public bool CanPresent=>!string.IsNullOrWhiteSpace(Serial) &&
        Uri.TryCreate(BindingPrefix,UriKind.Absolute,out var uri) && uri.Scheme is "http" or "https";
}

// The two APK QRCodeManager implementations differ in their fallback values.
public static class OriginalMobileQr
{
    public static string PanelPayload(MobileQrBinding binding)
    {
        var prefix=string.IsNullOrEmpty(binding.BindingPrefix)?"http://user.duochang.cc/Home/User/weixin2":binding.BindingPrefix;
        if(binding.InsideNet)
        {
            var index=prefix.IndexOf("http://user",StringComparison.Ordinal);
            if(index>=0)prefix=prefix[..index]+"http://test"+prefix[(index+11)..];
        }
        return prefix+(binding.RandomCode??"10")+"&sn="+binding.Serial+"&from=603";
    }
    public static string TelevisionPayload(MobileQrBinding binding)=>
        binding.BindingPrefix+(binding.RandomCode??"")+"&sn="+binding.Serial+"&from=603";
    public static PixelData Render(string payload,bool television)
    {
        if(string.IsNullOrEmpty(payload))throw new ArgumentException("Empty QR payload",nameof(payload));
        return new BarcodeWriterPixelData
        {
            Format=BarcodeFormat.QR_CODE,
            Options=new QrCodeEncodingOptions { Width=400,Height=400,Margin=1,CharacterSet="UTF-8",
                ErrorCorrection=television?ErrorCorrectionLevel.M:ErrorCorrectionLevel.L }
        }.Write(payload);
    }
}
