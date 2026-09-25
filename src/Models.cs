// Copyright (C) 2026 EthenGod
// SPDX-License-Identifier: GPL-3.0-only
// This file is part of AudioSwitch. See LICENSE and NOTICE.txt.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AudioSwitch
{
    public sealed class Endpoint
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public int Flow { get; set; }
    }

    public sealed class AudioState
    {
        public List<Endpoint> Devices { get; set; }
        // Keys are flow:role; role 0 = console, 1 = multimedia, 2 = communications.
        public Dictionary<string, string> Defaults { get; set; }
        public AudioState() { Devices = new List<Endpoint>(); Defaults = new Dictionary<string, string>(); }
        public string Default(int flow, int role)
        {
            string value;
            return Defaults.TryGetValue(flow + ":" + role, out value) ? value : null;
        }
    }

    public sealed class Arrival
    {
        public string Token { get; set; }
        public int Flow { get; set; }
        public List<Endpoint> NewDevices { get; set; }
        public Dictionary<string, string> PreviousDefaults { get; set; }
        public string PreviousName { get; set; }
        public List<Endpoint> DisconnectedDevices { get; set; }
        public bool IsDisconnection { get { return DisconnectedDevices.Count > 0; } }
        public Arrival() { DisconnectedDevices = new List<Endpoint>(); }
    }

    // Pure state machine: no COM, windows, timers, or writes to system settings.
    public sealed class ArrivalTracker
    {
        public AudioState Current { get; private set; }
        public List<Arrival> Pending { get; private set; }
        public ArrivalTracker(AudioState initial) { Current = initial; Pending = new List<Arrival>(); }

        public bool Update(AudioState next, bool ask, bool askDisconnect = false, bool includeCommunications = true)
        {
            var previousIds = new HashSet<string>(Current.Devices.Select(d => d.Id));
            var activeIds = new HashSet<string>(next.Devices.Select(d => d.Id));
            foreach (var pending in Pending)
            {
                pending.NewDevices.RemoveAll(d => !activeIds.Contains(d.Id));
                pending.DisconnectedDevices.RemoveAll(d => activeIds.Contains(d.Id));
                if (pending.IsDisconnection) pending.PreviousName = String.Join("、", pending.DisconnectedDevices.Select(d => d.Name));
            }
            Pending.RemoveAll(p => p.NewDevices.Count == 0 && !p.IsDisconnection);
            bool added = false;
            if (askDisconnect)
            {
                foreach (int flow in new[] { 0, 1 })
                {
                    var roles = includeCommunications ? new[] { 0, 1, 2 } : new[] { 0, 1 };
                    var lost = Current.Devices.Where(d => d.Flow == flow && !activeIds.Contains(d.Id)
                        && roles.Any(role => Current.Default(flow, role) == d.Id)).ToList();
                    if (lost.Count == 0) continue;
                    var existing = Pending.FirstOrDefault(p => p.Flow == flow);
                    var pending = new Arrival {
                        Token = Guid.NewGuid().ToString("N"), Flow = flow,
                        NewDevices = existing == null ? new List<Endpoint>() : existing.NewDevices,
                        PreviousDefaults = new Dictionary<string, string>(Current.Defaults),
                        DisconnectedDevices = lost,
                        PreviousName = String.Join("、", lost.Select(d => d.Name))
                    };
                    Pending.RemoveAll(p => p.Flow == flow);
                    Pending.Add(pending);
                    added = true;
                }
            }
            if (ask)
            {
                foreach (var group in next.Devices.Where(d => !previousIds.Contains(d.Id)).GroupBy(d => d.Flow))
                {
                    var pending = Pending.FirstOrDefault(p => p.Flow == group.Key);
                    if (pending == null)
                    {
                        var oldId = Current.Default(group.Key, 1);
                        var old = Current.Devices.FirstOrDefault(d => d.Id == oldId);
                        pending = new Arrival {
                            Token = Guid.NewGuid().ToString("N"), Flow = group.Key,
                            NewDevices = new List<Endpoint>(),
                            PreviousDefaults = new Dictionary<string, string>(Current.Defaults),
                            PreviousName = old == null ? "之前没有可用的默认设备" : old.Name
                        };
                        Pending.Add(pending);
                    }
                    pending.NewDevices.AddRange(group);
                    added = true;
                }
            }
            Current = next;
            return added;
        }

        public void Dismiss(string token) { Pending.RemoveAll(p => p.Token == token); }
    }

    public sealed class Preferences
    {
        public bool DarkMode { get; set; }
        public bool AskOnConnect { get; set; }
        public bool IncludeCommunications { get; set; }
        public Dictionary<string, DeviceProfile> DeviceProfiles { get; set; }
        public bool UseDevicePriority { get; set; }
        public List<Endpoint> DeviceOrder { get; set; }
        public Dictionary<string, DeviceRule> DeviceRules { get; set; }
        public Preferences() { AskOnConnect = true; IncludeCommunications = true; UseDevicePriority = true; DeviceProfiles = new Dictionary<string, DeviceProfile>(); DeviceOrder = new List<Endpoint>(); DeviceRules = new Dictionary<string, DeviceRule>(); }
    }

    public enum DeviceRule { Normal = 0, AcceptSystem = 1, SwitchOnConnect = 2 }

    public sealed class Request
    {
        public string Action { get; set; }
        public string DeviceId { get; set; }
        public string Token { get; set; }
        public bool Value { get; set; }
        public DeviceProfile Profile { get; set; }
        public int Flow { get; set; }
        public List<string> DeviceOrder { get; set; }
        public DeviceRule? DeviceRule { get; set; }
        public string Message { get; set; }
        public string ConfigurationJson { get; set; }
    }

    public sealed class Reply
    {
        public string Error { get; set; }
        public string Warning { get; set; }
        public string ConfigurationJson { get; set; }
        public string BackupPath { get; set; }
        public AudioState State { get; set; }
        public List<Arrival> Pending { get; set; }
        public Preferences Preferences { get; set; }
        public int BackendPid { get; set; }
        public int FrontendPid { get; set; }
        public int PromptPid { get; set; }
        public bool DolbyApplying { get; set; }
        public DeviceSettingsInfo DeviceSettings { get; set; }
    }

    public sealed class DeviceProfile
    {
        public DolbyProfile Dolby { get; set; }
        public int? Volume { get; set; }
        // null = leave unchanged; empty = off; otherwise a Windows spatial format GUID.
        public string SpatialFormat { get; set; }
    }
    public sealed class SpatialOption
    {
        public string Id { get; set; }
        public string Name { get; set; }
    }
    public sealed class SpatialState
    {
        public bool Supported { get; set; }
        public string CurrentFormat { get; set; }
        public List<SpatialOption> Options { get; set; }
    }
    public sealed class DeviceSettingsInfo
    {
        public Endpoint Device { get; set; }
        public DeviceProfile Profile { get; set; }
        public int? CurrentVolume { get; set; }
        public string VolumeError { get; set; }
        public SpatialState Spatial { get; set; }
        public string SpatialError { get; set; }
        public DeviceRule DeviceRule { get; set; }
    }
}
