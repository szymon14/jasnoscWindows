using System;
using System.Collections.Generic;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace Jasnosc
{
    // Niezmienna migawka stanu monitora przekazywana do UI.
    class DisplayInfo
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int Brightness;
        public readonly bool Available;

        public DisplayInfo(string id, string name, int brightness, bool available)
        {
            Id = id;
            Name = name;
            Brightness = brightness;
            Available = available;
        }
    }

    abstract class Display : IDisposable
    {
        public string Id;
        public string Name;
        public int Brightness;
        public bool Available = true;

        public abstract bool Set(int percent);
        public virtual void Dispose() { }

        public DisplayInfo Snapshot()
        {
            return new DisplayInfo(Id, Name, Brightness, Available);
        }
    }

    // Wbudowany ekran laptopa - sterowany przez WMI.
    class WmiDisplay : Display
    {
        readonly ManagementObject methods;

        public WmiDisplay(ManagementObject methods)
        {
            this.methods = methods;
        }

        public override bool Set(int percent)
        {
            try
            {
                ManagementBaseObject args = methods.GetMethodParameters("WmiSetBrightness");
                args["Timeout"] = (uint)1;
                args["Brightness"] = (byte)percent;
                methods.InvokeMethod("WmiSetBrightness", args, null);
                Brightness = percent;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public override void Dispose()
        {
            methods.Dispose();
        }
    }

    // Monitor zewnętrzny - sterowany przez DDC/CI (dxva2.dll).
    class DdcDisplay : Display
    {
        // Specyfikacja DDC/CI wymaga przerwy między komendami, inaczej monitor gubi polecenia.
        const int MinGapMs = 50;

        IntPtr handle;
        readonly uint min, max;
        int lastCommand;

        public DdcDisplay(IntPtr handle, uint min, uint max)
        {
            this.handle = handle;
            this.min = min;
            this.max = max;
            lastCommand = Environment.TickCount;
        }

        public override bool Set(int percent)
        {
            if (handle == IntPtr.Zero) return false;
            uint value = min + (uint)Math.Round((max - min) * percent / 100.0);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                int wait = MinGapMs - (Environment.TickCount - lastCommand);
                if (wait > 0) Thread.Sleep(wait);
                bool ok = Native.SetMonitorBrightness(handle, value);
                lastCommand = Environment.TickCount;
                if (ok)
                {
                    Brightness = percent;
                    return true;
                }
                Thread.Sleep(100);
            }
            return false;
        }

        public override void Dispose()
        {
            if (handle != IntPtr.Zero)
            {
                Native.DestroyPhysicalMonitor(handle);
                handle = IntPtr.Zero;
            }
        }
    }

    static class DisplayScanner
    {
        public static List<Display> Scan()
        {
            var result = new List<Display>();
            Dictionary<string, string> friendly = ReadFriendlyNames();
            var internalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ScanWmi(result, friendly, internalIds);
            ScanDdc(result, friendly, internalIds);

            MakeNamesUnique(result);
            return result;
        }

        static void ScanWmi(List<Display> result, Dictionary<string, string> friendly, HashSet<string> internalIds)
        {
            try
            {
                var methodsByInstance = new Dictionary<string, ManagementObject>(StringComparer.OrdinalIgnoreCase);
                using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT * FROM WmiMonitorBrightnessMethods"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                        methodsByInstance[NormalizeInstance((string)mo["InstanceName"])] = mo;
                }

                using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT InstanceName, CurrentBrightness FROM WmiMonitorBrightness"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string id = NormalizeInstance((string)mo["InstanceName"]);
                        ManagementObject methods;
                        if (!methodsByInstance.TryGetValue(id, out methods)) continue;
                        methodsByInstance.Remove(id);

                        internalIds.Add(id);
                        result.Add(new WmiDisplay(methods)
                        {
                            Id = id,
                            Name = "Wbudowany ekran",
                            Brightness = Convert.ToInt32(mo["CurrentBrightness"]),
                        });
                        mo.Dispose();
                    }
                }

                foreach (ManagementObject unused in methodsByInstance.Values) unused.Dispose();
            }
            catch (ManagementException)
            {
                // Brak wbudowanego ekranu (np. komputer stacjonarny) - to normalne.
            }
        }

        static void ScanDdc(List<Display> result, Dictionary<string, string> friendly, HashSet<string> internalIds)
        {
            var hMonitors = new List<IntPtr>();
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
                delegate(IntPtr h, IntPtr hdc, IntPtr rect, IntPtr data) { hMonitors.Add(h); return true; },
                IntPtr.Zero);

            foreach (IntPtr hMonitor in hMonitors)
            {
                var info = new Native.MONITORINFOEX();
                info.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(hMonitor, ref info)) continue;

                List<string> deviceIds = ActiveMonitorDeviceIds(info.szDevice);

                uint count;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out count) || count == 0) continue;
                var physical = new Native.PHYSICAL_MONITOR[count];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, physical)) continue;

                for (int i = 0; i < physical.Length; i++)
                {
                    string id = i < deviceIds.Count ? deviceIds[i] : info.szDevice + "#" + i;
                    IntPtr handle = physical[i].hPhysicalMonitor;

                    // Wbudowany ekran obsługuje już WMI, a DDC na nim tylko zawiesza się na timeoutach.
                    if (internalIds.Contains(id))
                    {
                        Native.DestroyPhysicalMonitor(handle);
                        continue;
                    }

                    string name;
                    if (!friendly.TryGetValue(id, out name))
                    {
                        name = physical[i].szPhysicalMonitorDescription;
                        if (string.IsNullOrEmpty(name)) name = "Monitor";
                    }

                    uint min = 0, cur = 0, max = 0;
                    bool ok = false;
                    for (int attempt = 0; attempt < 3 && !ok; attempt++)
                    {
                        if (attempt > 0) Thread.Sleep(150);
                        ok = Native.GetMonitorBrightness(handle, out min, out cur, out max);
                    }

                    if (ok && max > min)
                    {
                        result.Add(new DdcDisplay(handle, min, max)
                        {
                            Id = id,
                            Name = name,
                            Brightness = (int)Math.Round((cur - min) * 100.0 / (max - min)),
                        });
                    }
                    else
                    {
                        Native.DestroyPhysicalMonitor(handle);
                        result.Add(new DdcDisplay(IntPtr.Zero, 0, 100)
                        {
                            Id = id,
                            Name = name,
                            Available = false,
                        });
                    }
                }
            }
        }

        // Identyfikatory aktywnych monitorów podłączonych do danego wyjścia, w formacie WMI (DISPLAY\XXX\YYY).
        static List<string> ActiveMonitorDeviceIds(string adapterDevice)
        {
            var ids = new List<string>();
            var dd = new Native.DISPLAY_DEVICE();
            dd.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
            for (uint i = 0; Native.EnumDisplayDevices(adapterDevice, i, ref dd, Native.EDD_GET_DEVICE_INTERFACE_NAME); i++)
            {
                if ((dd.StateFlags & Native.DISPLAY_DEVICE_ACTIVE) != 0)
                    ids.Add(InterfaceNameToInstance(dd.DeviceID));
                dd.cb = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
            }
            return ids;
        }

        // \\?\DISPLAY#GSM5B7F#5&abc&0&UID4352#{guid}  ->  DISPLAY\GSM5B7F\5&ABC&0&UID4352
        static string InterfaceNameToInstance(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return "";
            string[] parts = deviceId.Split('#');
            if (parts.Length < 3) return deviceId.ToUpperInvariant();
            return ("DISPLAY\\" + parts[1] + "\\" + parts[2]).ToUpperInvariant();
        }

        // DISPLAY\GSM5B7F\5&abc&0&UID4352_0  ->  DISPLAY\GSM5B7F\5&ABC&0&UID4352
        static string NormalizeInstance(string instanceName)
        {
            if (instanceName == null) return "";
            return Regex.Replace(instanceName, @"_\d+$", "").ToUpperInvariant();
        }

        static Dictionary<string, string> ReadFriendlyNames()
        {
            var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var searcher = new ManagementObjectSearcher("root\\WMI", "SELECT InstanceName, UserFriendlyName FROM WmiMonitorID"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        string name = DecodeWmiString(mo["UserFriendlyName"]);
                        if (name.Length > 0) names[NormalizeInstance((string)mo["InstanceName"])] = name;
                        mo.Dispose();
                    }
                }
            }
            catch (ManagementException) { }
            return names;
        }

        static string DecodeWmiString(object value)
        {
            var chars = value as Array;
            if (chars == null) return "";
            var sb = new StringBuilder();
            foreach (object c in chars)
            {
                int code = Convert.ToInt32(c);
                if (code == 0) break;
                sb.Append((char)code);
            }
            return sb.ToString().Trim();
        }

        static void MakeNamesUnique(List<Display> displays)
        {
            var seen = new Dictionary<string, int>();
            foreach (Display d in displays)
            {
                int n;
                seen.TryGetValue(d.Name, out n);
                seen[d.Name] = n + 1;
                if (n > 0) d.Name = d.Name + " (" + (n + 1) + ")";
            }
        }
    }
}
