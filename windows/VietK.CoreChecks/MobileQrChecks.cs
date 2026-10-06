using VietK.Core;
using ZXing;

internal static class MobileQrChecks
{
    public static void Run()
    {
        void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
        var binding=new MobileQrBinding("fixture-device");
        Require(!binding.CanPresent && !new MobileQrBinding(BindingPrefix:"https://example.invalid/bind?code=").CanPresent,"Missing binding identity was admitted");
        Require(OriginalMobileQr.PanelPayload(binding)=="http://user.duochang.cc/Home/User/weixin210&sn=fixture-device&from=603","Panel QR fallback differs");
        Require(OriginalMobileQr.TelevisionPayload(binding)=="&sn=fixture-device&from=603","TV fallback was replaced with the panel host/default random code");
        binding=binding with { BindingPrefix="http://user.example.invalid/bind?code=",RandomCode="1234",TvBindCode="5678",InsideNet=true };
        Require(binding.CanPresent && OriginalMobileQr.PanelPayload(binding)=="http://test.example.invalid/bind?code=1234&sn=fixture-device&from=603","Inside-network prefix replacement differs");
        Require(OriginalMobileQr.TelevisionPayload(binding).StartsWith("http://user.example.invalid/"),"TV wrongly adopted the panel's internal-host rewrite");
        Require(!OriginalMobileQr.TelevisionPayload(binding).Contains("5678"),"Independent TV display code entered the binding URL");
        foreach(var television in new[]{false,true})
        {
            var text=television?OriginalMobileQr.TelevisionPayload(binding):OriginalMobileQr.PanelPayload(binding);
            var pixels=OriginalMobileQr.Render(text,television);
            var decoded=new BarcodeReaderGeneric().Decode(pixels.Pixels,pixels.Width,pixels.Height,RGBLuminanceSource.BitmapFormat.BGRA32);
            Require(pixels.Width==400 && pixels.Height==400 && decoded?.Text==text,"Original-sized QR failed to decode its binding payload");
            Require(decoded.ResultMetadata[ResultMetadataType.ERROR_CORRECTION_LEVEL].ToString()==(television?"M":"L"),"Panel/TV QR error correction differs");
        }
        foreach(var text in new[]{OriginalMobileQr.TelevisionPayload(binding),"http://192.168.1.20:9167/#token="+new string('a',64)})
        {
            var compact=OriginalMobileQr.Render(text,true,31);
            var decoded=new BarcodeReaderGeneric { Options=new ZXing.Common.DecodingOptions { TryHarder=true } }
                .Decode(compact.Pixels,compact.Width,compact.Height,RGBLuminanceSource.BitmapFormat.BGRA32);
            Require(compact.Width<80&&decoded?.Text==text,"Compact QR lost modules or changed its connection payload");
        }
        var state=new OriginalTvQrState();state.ChangeMode(0);Require(state.ImageVisible,"Always-show mode hidden");
        state.ChangeMode(1);Require(!state.ImageVisible,"Mode-one source branch was replaced with an automatic timer");
        state.ChangeMode(0);state.ScheduleHide(100);Require(state.Tick(20099,100)==0 && state.ImageVisible,"QR hid before the original 20-second delay");
        Require(state.Tick(20100,100)==0 && state.ImageVisible && state.Tick(20600,100)==50,"Original one-second hide translation differs");
        Require(state.Tick(21100,100)==0 && !state.ImageVisible && state.SlideStartedAt is null,"Hide animation did not reset after hiding the image");
        state.Show();state.ScheduleHide(0);state.ChangeMode(2);Require(!state.ImageVisible && state.HideAt is null,"Hidden mode failed to cancel its delayed task");
        state.ChangeMode(0);state.ScheduleHide(0);state.CancelDelay();state.Tick(30000,100);Require(state.ImageVisible,"Cancelled QR delay still fired");
        Console.WriteLine("Original panel/TV binding payloads, QR size/error correction, decoding and visibility/delay branches verified; no live mobile connection claimed.");
    }
}
