using NAudio.CoreAudioApi;
using System.Runtime.InteropServices;

namespace NovaLab;
// Only the endpoint selection method is used. Earlier COM slots preserve the ABI.
// Windows does not publish this interface as a supported SDK contract.
static class EndpointPolicy {
    [ComImport,Guid("F8679F50-850A-41CF-9C72-430F290290C8"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicy {
        void GetMixFormat(); void GetDeviceFormat(); void ResetDeviceFormat(); void SetDeviceFormat();
        void GetProcessingPeriod(); void SetProcessingPeriod(); void GetShareMode(); void SetShareMode();
        void GetPropertyValue(); void SetPropertyValue();
        [PreserveSig]int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)]string id,Role role);
    }
    public static string Set(MMDeviceEnumerator e,string id,string value){
        if(!Enum.TryParse<Role>(value,false,out var role)||!Enum.IsDefined(role))throw new ArgumentException("Console, Multimedia oder Communications auswählen");
        using var selected=e.GetDevice(id);Model.Require(selected.State==DeviceState.Active,"Audiogerät inaktiv");
        object? com=null;try{
            com=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9"),true)!)!;
            Marshal.ThrowExceptionForHR(((IPolicy)com).SetDefaultEndpoint(selected.ID,role));
            using var current=e.GetDefaultAudioEndpoint(selected.DataFlow,role);
            if(current.ID!=selected.ID)throw new InvalidOperationException("Windows hat den Wechsel nicht bestätigt");
            return role+" · "+current.FriendlyName;
        }catch(Exception ex)when(ex is COMException or InvalidCastException){throw new InvalidOperationException("Gerätewechsel wird hier nicht unterstützt. Am PC die Windows-Soundeinstellungen öffnen.");}
        finally{if(com!=null)Marshal.FinalReleaseComObject(com);}
    }
}
