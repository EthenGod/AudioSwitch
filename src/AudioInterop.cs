// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Runtime.InteropServices;

namespace AudioSwitch
{
    [StructLayout(LayoutKind.Sequential)]
    public struct PropertyKey { public Guid Format; public uint Id; }
    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct PropVariant
    {
        [FieldOffset(0)] public ushort Type;
        [FieldOffset(8)] public IntPtr Pointer;
    }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
        [PreserveSig] int RegisterEndpointNotificationCallback(IMMNotificationClient client);
        [PreserveSig] int UnregisterEndpointNotificationCallback(IMMNotificationClient client);
    }
    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out uint state);
    }
    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int Commit();
    }
    [ComVisible(true), Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMNotificationClient
    {
        [PreserveSig] int OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string id, uint state);
        [PreserveSig] int OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnDefaultDeviceChanged(int flow, int role, [MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string id, PropertyKey key);
    }
    // Windows private PolicyConfig interface; isolate it because it is not a public SDK contract.
    // Vtable includes ResetDeviceFormat before SetDeviceFormat (the Vista interface differs).
    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, out IntPtr format);
        [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, out IntPtr format);
        [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
        [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpoint, IntPtr mix);
        [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int useDefault, IntPtr period, IntPtr minimum);
        [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
        [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
        [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropertyKey key, out PropVariant value);
        [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, ref PropertyKey key, ref PropVariant value);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }
    [ComVisible(true), ClassInterface(ClassInterfaceType.None)]
    public sealed class EndpointNotifications : IMMNotificationClient
    {
        private readonly Action changed;
        public EndpointNotifications(Action changed) { this.changed = changed; }
        // Never enumerate or block inside a Core Audio callback.
        private int Signal() { try { changed(); } catch { } return 0; }
        public int OnDeviceStateChanged(string id, uint state) { return Signal(); }
        public int OnDeviceAdded(string id) { return Signal(); }
        public int OnDeviceRemoved(string id) { return Signal(); }
        public int OnDefaultDeviceChanged(int flow, int role, string id) { return Signal(); }
        public int OnPropertyValueChanged(string id, PropertyKey key) { return Signal(); }
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float level, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float level);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
    }
    internal sealed class AudioService : IDisposable, IDeviceSettingsAccess
    {
        private IMMDeviceEnumerator enumerator;
        private EndpointNotifications notifications;
        [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
        public AudioService()
        {
            enumerator = (IMMDeviceEnumerator)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")));
        }
        public void Listen(Action changed)
        {
            notifications = new EndpointNotifications(changed);
            Marshal.ThrowExceptionForHR(enumerator.RegisterEndpointNotificationCallback(notifications));
        }
        public AudioState Read()
        {
            var result = new AudioState();
            for (int flow = 0; flow < 2; flow++)
            {
                IMMDeviceCollection collection = null;
                try
                {
                    Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(flow, 1, out collection));
                    uint count; Marshal.ThrowExceptionForHR(collection.GetCount(out count));
                    for (uint i = 0; i < count; i++)
                    {
                        IMMDevice device = null;
                        try
                        {
                            Marshal.ThrowExceptionForHR(collection.Item(i, out device));
                            string id; Marshal.ThrowExceptionForHR(device.GetId(out id));
                            result.Devices.Add(new Endpoint { Id = id, Name = ReadName(device), Flow = flow });
                        }
                        catch (COMException) { /* Endpoint may disappear during enumeration. */ }
                        finally { Release(device); }
                    }
                }
                finally { Release(collection); }
                for (int role = 0; role < 3; role++)
                {
                    IMMDevice device = null;
                    try
                    {
                        int hr = enumerator.GetDefaultAudioEndpoint(flow, role, out device);
                        if (hr == unchecked((int)0x80070490)) continue; // No endpoint available.
                        Marshal.ThrowExceptionForHR(hr);
                        string id; Marshal.ThrowExceptionForHR(device.GetId(out id));
                        result.Defaults[flow + ":" + role] = id;
                    }
                    finally { Release(device); }
                }
            }
            return result;
        }
        private static string ReadName(IMMDevice device)
        {
            IPropertyStore store = null;
            PropVariant value = new PropVariant();
            try
            {
                Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out store));
                var key = new PropertyKey { Format = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), Id = 14 };
                Marshal.ThrowExceptionForHR(store.GetValue(ref key, out value));
                return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : "音频设备";
            }
            finally { PropVariantClear(ref value); Release(store); }
        }
        public void SetRole(string id, int role)
        {
            object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")));
            try { Marshal.ThrowExceptionForHR(((IPolicyConfig)instance).SetDefaultEndpoint(id, role)); }
            finally { Release(instance); }
        }
        private T WithVolume<T>(string id, Func<IAudioEndpointVolume, T> action)
        {
            IMMDevice device = null; object volume = null;
            try
            {
                Marshal.ThrowExceptionForHR(enumerator.GetDevice(id, out device));
                var iid = typeof(IAudioEndpointVolume).GUID;
                Marshal.ThrowExceptionForHR(device.Activate(ref iid, 23, IntPtr.Zero, out volume));
                return action((IAudioEndpointVolume)volume);
            }
            finally { Release(volume); Release(device); }
        }
        public float ReadVolume(string id)
        {
            return WithVolume(id, volume => { float result; Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out result)); return result; });
        }
        public void SetVolume(string id, float value)
        {
            if (Single.IsNaN(value) || value < 0 || value > 1) throw new ArgumentOutOfRangeException("value");
            WithVolume(id, volume => {
                var context = Guid.Empty;
                Marshal.ThrowExceptionForHR(volume.SetMasterVolumeLevelScalar(value, ref context));
                float actual; Marshal.ThrowExceptionForHR(volume.GetMasterVolumeLevelScalar(out actual));
                if (Math.Abs(actual - value) > 0.011F) throw new InvalidOperationException("音量未能设置到目标值。");
                return 0;
            });
        }
        public SpatialState ReadSpatial(string id) { return SpatialAudio.Read(id); }
        public void SetSpatial(string id, string format) { SpatialAudio.Set(id, format); }
        private static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        public void Dispose()
        {
            if (enumerator == null) return;
            if (notifications != null) enumerator.UnregisterEndpointNotificationCallback(notifications);
            Release(enumerator); enumerator = null; notifications = null;
        }
    }
}
