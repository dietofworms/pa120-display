// pa120 - minimal open-source driver for the Thermalright Peerless Assassin 120 Digital display.
//
// Shows CPU temp / CPU usage / GPU temp / GPU usage on the cooler's segment display,
// with no vendor software. Zero dependencies: builds with the C# compiler built into Windows.
//
// Protocol credit: raffa0001/Peerless_assassin_and_CLI_UI (MIT), reverse-engineered on Linux.
// CPU temp via PawnIO (https://pawnio.eu, open source, signed) using the same SMN register
// read as LibreHardwareMonitor / Linux k10temp.
//
// License: MIT

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Pa120
{
    // ---------------------------------------------------------------- display layout
    static class Layout
    {
        public const int LedCount = 84;

        // Segment order inside one digit, as indices into the digit's 7-LED block.
        // CPU-side digits are wired f,a,b,g,e,d,c; GPU-side digits are wired in reverse.
        static readonly string CpuOrder = "fabgedc";
        static readonly string GpuOrder = "cdegbaf";

        public static readonly int[] CpuLabel = { 0, 1 };
        public static readonly int[] CpuTemp = { 2, 9, 16 };      // start index of each digit, most significant first
        public const int CpuCelsius = 23, CpuFahrenheit = 24;
        public const int CpuUsageHundredBottom = 25, CpuUsageHundredTop = 26;
        public static readonly int[] CpuUsage = { 27, 34 };
        public const int CpuPercent = 41;

        public const int GpuPercent = 42;
        public static readonly int[] GpuUsage = { 50, 43 };
        public const int GpuUsageHundredTop = 57, GpuUsageHundredBottom = 58;
        public const int GpuCelsius = 59, GpuFahrenheit = 60;
        public static readonly int[] GpuTemp = { 75, 68, 61 };
        public static readonly int[] GpuLabel = { 82, 83 };

        static readonly string[] DigitSegments =
        {
            "abcdef", "bc", "abged", "abgcd", "fgbc", "afgcd", "afgecd", "abc", "abcdefg", "abgfcd"
        };

        // Lights a number right-aligned across the given digits, blanking leading zeros.
        // A negative value draws dashes (sensor unavailable).
        public static void DrawNumber(bool[] leds, int value, int[] digits, bool gpuSide)
        {
            string order = gpuSide ? GpuOrder : CpuOrder;
            for (int i = 0; i < digits.Length; i++)
            {
                int place = digits.Length - 1 - i;
                string segs;
                if (value < 0) segs = "g";
                else
                {
                    int pow = (int)Math.Pow(10, place);
                    if (value < pow && place > 0) continue;
                    segs = DigitSegments[(value / pow) % 10];
                }
                foreach (char s in segs) leds[digits[i] + order.IndexOf(s)] = true;
            }
        }

        public static void DrawUsage(bool[] leds, int value, int[] digits, int hundredTop, int hundredBottom, bool gpuSide)
        {
            if (value >= 100)
            {
                // the "1" is its own pair of segments; the rest keeps its zeros (100 -> 1|00)
                leds[hundredTop] = leds[hundredBottom] = true;
                DrawPadded(leds, value % 100, digits, gpuSide);
            }
            else DrawNumber(leds, value, digits, gpuSide);
        }

        static void DrawPadded(bool[] leds, int value, int[] digits, bool gpuSide)
        {
            string order = gpuSide ? GpuOrder : CpuOrder;
            for (int i = 0; i < digits.Length; i++)
            {
                int pow = (int)Math.Pow(10, digits.Length - 1 - i);
                foreach (char s in DigitSegments[(value / pow) % 10]) leds[digits[i] + order.IndexOf(s)] = true;
            }
        }
    }

    // ---------------------------------------------------------------- HID transport
    sealed class Display : IDisposable
    {
        const ushort Vid = 0x0416, Pid = 0x8001;
        static readonly byte[] Header =
        {
            0xDA, 0xDB, 0xDC, 0xDD, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFC, 0x00, 0x00, 0xFF
        };
        const int Chunk = 64;

        SafeFileHandle _h;
        int _reportLen;

        public static Display Open()
        {
            string path = FindDevicePath();
            if (path == null) return null;
            var h = Native.CreateFile(path, Native.GENERIC_WRITE | Native.GENERIC_READ,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid) return null;

            int reportLen = Chunk + 1;
            IntPtr pre;
            if (Native.HidD_GetPreparsedData(h, out pre))
            {
                Native.HIDP_CAPS caps;
                if (Native.HidP_GetCaps(pre, out caps) == Native.HIDP_STATUS_SUCCESS && caps.OutputReportByteLength > 0)
                    reportLen = caps.OutputReportByteLength;
                Native.HidD_FreePreparsedData(pre);
            }
            return new Display { _h = h, _reportLen = reportLen };
        }

        public bool Send(bool[] leds, uint[] colors)
        {
            var stream = new List<byte>(Header);
            for (int i = 0; i < Layout.LedCount; i++)
            {
                uint c = leds[i] ? colors[i] : 0;
                stream.Add((byte)(c >> 16)); stream.Add((byte)(c >> 8)); stream.Add((byte)c);
            }
            byte[] data = stream.ToArray();
            // Windows wants a leading report-ID byte (0) when the report is Chunk+1 long.
            int offset = _reportLen > Chunk ? 1 : 0;
            for (int pos = 0; pos < data.Length; pos += Chunk)
            {
                var report = new byte[_reportLen];
                Buffer.BlockCopy(data, pos, report, offset, Math.Min(Chunk, data.Length - pos));
                uint written;
                if (!Native.WriteFile(_h, report, (uint)report.Length, out written, IntPtr.Zero)) return false;
            }
            return true;
        }

        public void Dispose() { if (_h != null) _h.Dispose(); }

        static string FindDevicePath()
        {
            Guid hidGuid;
            Native.HidD_GetHidGuid(out hidGuid);
            IntPtr set = Native.SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                Native.DIGCF_PRESENT | Native.DIGCF_DEVICEINTERFACE);
            string want = string.Format("vid_{0:x4}&pid_{1:x4}", Vid, Pid);
            try
            {
                var ifData = new Native.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                for (uint i = 0; Native.SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref ifData); i++)
                {
                    uint needed;
                    Native.SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out needed, IntPtr.Zero);
                    IntPtr buf = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!Native.SetupDiGetDeviceInterfaceDetail(set, ref ifData, buf, needed, out needed, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(buf + 4);
                        if (path != null && path.ToLowerInvariant().Contains(want)) return path;
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { Native.SetupDiDestroyDeviceInfoList(set); }
            return null;
        }
    }

    // ---------------------------------------------------------------- sensors
    sealed class CpuUsage
    {
        ulong _idle, _total;
        public int Read()
        {
            long idle, kernel, user;
            if (!Native.GetSystemTimes(out idle, out kernel, out user)) return -1;
            ulong total = (ulong)(kernel + user); // kernel time includes idle
            ulong dIdle = (ulong)idle - _idle, dTotal = total - _total;
            _idle = (ulong)idle; _total = total;
            if (dTotal == 0) return 0;
            return (int)Math.Round(100.0 * (dTotal - dIdle) / dTotal);
        }
    }

    // AMD Zen CPU temperature (Tctl) via PawnIO's signed AMDFamily17 module.
    sealed class AmdCpuTemp : IDisposable
    {
        const uint IoctlLoad = 0xA1B22084, IoctlExec = 0xA1B22104;
        const uint SmnThmTconCurTmp = 0x00059800;
        SafeFileHandle _h;

        public static AmdCpuTemp Open(string modulePath, out string error)
        {
            error = null;
            if (!File.Exists(modulePath)) { error = "missing " + Path.GetFileName(modulePath); return null; }
            var h = Native.CreateFile(@"\\?\GLOBALROOT\Device\PawnIO", Native.GENERIC_READ | Native.GENERIC_WRITE,
                Native.FILE_SHARE_READ | Native.FILE_SHARE_WRITE, IntPtr.Zero, Native.OPEN_EXISTING, 0, IntPtr.Zero);
            if (h.IsInvalid)
            {
                error = Marshal.GetLastWin32Error() == 5 ? "PawnIO needs admin rights" : "PawnIO driver not installed";
                return null;
            }
            byte[] blob = File.ReadAllBytes(modulePath);
            uint ret;
            if (!Native.DeviceIoControl(h, IoctlLoad, blob, (uint)blob.Length, null, 0, out ret, IntPtr.Zero))
            {
                error = "PawnIO rejected module (error " + Marshal.GetLastWin32Error() + ")";
                h.Dispose();
                return null;
            }
            return new AmdCpuTemp { _h = h };
        }

        public int Read()
        {
            var input = new byte[32 + 8];
            Encoding.ASCII.GetBytes("ioctl_read_smn", 0, 14, input, 0);
            BitConverter.GetBytes((ulong)SmnThmTconCurTmp).CopyTo(input, 32);
            var output = new byte[8];
            uint ret;
            if (!Native.DeviceIoControl(_h, IoctlExec, input, (uint)input.Length, output, 8, out ret, IntPtr.Zero) || ret < 8)
                return -1;
            uint v = BitConverter.ToUInt32(output, 0);
            double t = (v >> 21) * 0.125;
            // RANGE_SEL (bit 19) or TJ_SEL == 3 (bits 17:16) means the reading is offset by 49 C
            if ((v & 0x80000) != 0 || (v & 0x30000) == 0x30000) t -= 49;
            return (int)Math.Round(t);
        }

        public void Dispose() { if (_h != null) _h.Dispose(); }
    }

    sealed class NvidiaGpu
    {
        IntPtr _dev;
        DateTime _retryAt;

        public static NvidiaGpu Open()
        {
            var gpu = new NvidiaGpu();
            return gpu.Init() ? gpu : null;
        }

        bool Init()
        {
            try
            {
                IntPtr dev;
                if (Native.nvmlInit_v2() != 0 || Native.nvmlDeviceGetHandleByIndex_v2(0, out dev) != 0) return false;
                _dev = dev;
                return true;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        // Reads both values; on failure (e.g. driver update) re-initialises NVML at most every 10s.
        public void Read(out int temp, out int usage)
        {
            temp = usage = -1;
            if (_dev == IntPtr.Zero)
            {
                if (DateTime.UtcNow < _retryAt) return;
                _retryAt = DateTime.UtcNow.AddSeconds(10);
                if (!Init()) return;
            }
            uint t;
            Native.NvmlUtilization u;
            if (Native.nvmlDeviceGetTemperature(_dev, 0, out t) != 0 || Native.nvmlDeviceGetUtilizationRates(_dev, out u) != 0)
            {
                _dev = IntPtr.Zero;
                try { Native.nvmlShutdown(); } catch { }
                return;
            }
            temp = (int)t;
            usage = (int)u.Gpu;
        }
    }

    // ---------------------------------------------------------------- colors
    static class Palette
    {
        // cool -> warm gradient keyed on a 0..1 fraction
        static readonly uint[] Stops = { 0x00A0FF, 0x00FFC0, 0x40FF00, 0xFFD000, 0xFF2000 };

        public static uint Gradient(double f)
        {
            f = Math.Max(0, Math.Min(1, f)) * (Stops.Length - 1);
            int i = Math.Min((int)f, Stops.Length - 2);
            return Lerp(Stops[i], Stops[i + 1], f - i);
        }

        public static uint Rainbow(double hue)
        {
            hue = (hue % 1 + 1) % 1 * 6;
            int sector = (int)hue; double x = hue - sector;
            byte up = (byte)(255 * x), down = (byte)(255 * (1 - x));
            switch (sector)
            {
                case 0: return Rgb(255, up, 0);
                case 1: return Rgb(down, 255, 0);
                case 2: return Rgb(0, 255, up);
                case 3: return Rgb(0, down, 255);
                case 4: return Rgb(up, 0, 255);
                default: return Rgb(255, 0, down);
            }
        }

        static uint Lerp(uint a, uint b, double t)
        {
            Func<int, uint> ch = shift => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t) << shift;
            return ch(16) | ch(8) | ch(0);
        }

        static uint Rgb(int r, int g, int b) { return (uint)(r << 16 | g << 8 | b); }
    }

    // ---------------------------------------------------------------- app
    static class Program
    {
        static int Main(string[] args)
        {
            string mode = "temp";       // temp | rainbow | RRGGBB
            int intervalMs = 1000;
            bool test = false, once = false, fahrenheit = false;
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--color": mode = args[++i].ToLowerInvariant(); break;
                    case "--interval": intervalMs = int.Parse(args[++i]); break;
                    case "--fahrenheit": fahrenheit = true; break;
                    case "--test": test = true; break;
                    case "--once": once = true; break;
                    default:
                        Console.WriteLine("usage: pa120 [--color temp|rainbow|RRGGBB] [--interval ms] [--fahrenheit] [--once] [--test]");
                        return 1;
                }
            }

            // The cooler blanks itself a few seconds after frames stop, so two writers would flicker
            // and a crashed one would go dark: allow one instance and keep the loop alive.
            bool firstInstance;
            var mutex = new Mutex(true, @"Global\pa120-display", out firstInstance);
            if (!firstInstance)
            {
                Log("another pa120 instance is already driving the display; exiting");
                return 3;
            }
            Native.SetPriorityClass(Native.GetCurrentProcess(), Native.BELOW_NORMAL_PRIORITY_CLASS);
            if (!once && !test) OpenLogFile();

            Display display = null;
            bool warned = false;
            while ((display = Display.Open()) == null)
            {
                if (once || test) { Log("cooler display (0416:8001) not found"); return 2; }
                if (!warned) Log("cooler display (0416:8001) not found, retrying every 5s");
                warned = true;
                Thread.Sleep(5000);
            }
            Log("display opened");

            if (test) { RunSegmentTest(display); return 0; }

            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            string err;
            AmdCpuTemp cpuTemp = AmdCpuTemp.Open(Path.Combine(exeDir, "AMDFamily17.bin"), out err);
            Log(cpuTemp != null ? "CPU temp: PawnIO OK" : "CPU temp unavailable: " + err);
            NvidiaGpu gpu = NvidiaGpu.Open();
            Log(gpu != null ? "GPU: NVML OK" : "GPU: NVML unavailable");
            var cpuUsage = new CpuUsage();
            cpuUsage.Read();
            Thread.Sleep(200);

            double phase = 0;
            while (true)
            {
                int ct = cpuTemp != null ? cpuTemp.Read() : -1;
                int cu = cpuUsage.Read();
                int gt = -1, gu = -1;
                if (gpu != null) gpu.Read(out gt, out gu);
                if (fahrenheit)
                {
                    if (ct >= 0) ct = ct * 9 / 5 + 32;
                    if (gt >= 0) gt = gt * 9 / 5 + 32;
                }

                var leds = new bool[Layout.LedCount];
                foreach (int l in Layout.CpuLabel) leds[l] = true;
                foreach (int l in Layout.GpuLabel) leds[l] = true;
                leds[fahrenheit ? Layout.CpuFahrenheit : Layout.CpuCelsius] = true;
                leds[fahrenheit ? Layout.GpuFahrenheit : Layout.GpuCelsius] = true;
                leds[Layout.CpuPercent] = leds[Layout.GpuPercent] = true;
                Layout.DrawNumber(leds, Math.Min(ct, 999), Layout.CpuTemp, false);
                Layout.DrawNumber(leds, Math.Min(gt, 999), Layout.GpuTemp, true);
                Layout.DrawUsage(leds, Math.Min(cu, 199), Layout.CpuUsage, Layout.CpuUsageHundredTop, Layout.CpuUsageHundredBottom, false);
                Layout.DrawUsage(leds, Math.Min(gu, 199), Layout.GpuUsage, Layout.GpuUsageHundredTop, Layout.GpuUsageHundredBottom, true);

                var colors = BuildColors(mode, ct, cu, gt, gu, fahrenheit, phase);
                phase += 0.02;

                if (!display.Send(leds, colors))
                {
                    if (once) { Log("write failed"); return 4; }
                    Log("write failed (error " + Marshal.GetLastWin32Error() + "), reconnecting");
                    display.Dispose();
                    while ((display = Display.Open()) == null) Thread.Sleep(2000);
                    Log("display reconnected");
                    continue;
                }
                if (once) break;
                Thread.Sleep(intervalMs);
            }
            GC.KeepAlive(mutex);
            return 0;
        }

        static TextWriter _log;

        // pa120.log next to the exe, so the windowless build can be diagnosed.
        static void OpenLogFile()
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pa120.log");
                _log = TextWriter.Synchronized(new StreamWriter(path, false) { AutoFlush = true });
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        static uint[] BuildColors(string mode, int ct, int cu, int gt, int gu, bool fahrenheit, double phase)
        {
            var c = new uint[Layout.LedCount];
            if (mode == "temp")
            {
                double lo = fahrenheit ? 86 : 30, hi = fahrenheit ? 194 : 90;
                uint cpuT = Palette.Gradient((ct - lo) / (hi - lo)), gpuT = Palette.Gradient((gt - lo) / (hi - lo));
                uint cpuU = Palette.Gradient(cu / 100.0), gpuU = Palette.Gradient(gu / 100.0);
                for (int i = 0; i < c.Length; i++) c[i] = 0xFFFFFF;
                for (int i = 2; i <= 24; i++) c[i] = cpuT;
                for (int i = 25; i <= 41; i++) c[i] = cpuU;
                for (int i = 42; i <= 58; i++) c[i] = gpuU;
                for (int i = 59; i <= 81; i++) c[i] = gpuT;
                foreach (int l in Layout.CpuLabel) c[l] = 0xFF4020;
                foreach (int l in Layout.GpuLabel) c[l] = 0xFF8000;
            }
            else if (mode == "rainbow")
            {
                for (int i = 0; i < c.Length; i++) c[i] = Palette.Rainbow(phase + i / (double)c.Length);
            }
            else
            {
                uint fixedColor = Convert.ToUInt32(mode.TrimStart('#'), 16);
                for (int i = 0; i < c.Length; i++) c[i] = fixedColor;
            }
            return c;
        }

        // Lights each LED index in turn so the layout can be verified by eye.
        static void RunSegmentTest(Display display)
        {
            var colors = new uint[Layout.LedCount];
            for (int i = 0; i < colors.Length; i++) colors[i] = 0xFFFFFF;
            var all = new bool[Layout.LedCount];
            for (int i = 0; i < all.Length; i++) all[i] = true;
            Log("all segments on");
            display.Send(all, colors);
            Thread.Sleep(3000);
            for (int i = 0; i < Layout.LedCount; i++)
            {
                var leds = new bool[Layout.LedCount];
                leds[i] = true;
                Log("LED " + i);
                display.Send(leds, colors);
                Thread.Sleep(400);
            }
            Log("number test: CPU 123 / 45%, GPU 67 / 100%");
            var n = new bool[Layout.LedCount];
            Layout.DrawNumber(n, 123, Layout.CpuTemp, false);
            Layout.DrawUsage(n, 45, Layout.CpuUsage, Layout.CpuUsageHundredTop, Layout.CpuUsageHundredBottom, false);
            Layout.DrawNumber(n, 67, Layout.GpuTemp, true);
            Layout.DrawUsage(n, 100, Layout.GpuUsage, Layout.GpuUsageHundredTop, Layout.GpuUsageHundredBottom, true);
            display.Send(n, colors);
        }

        static void Log(string msg)
        {
            string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + msg;
            Console.WriteLine(line);
            if (_log != null) _log.WriteLine(line);
        }
    }

    // ---------------------------------------------------------------- P/Invoke
    static class Native
    {
        public const uint GENERIC_READ = 0x80000000, GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 1, FILE_SHARE_WRITE = 2, OPEN_EXISTING = 3;
        public const uint DIGCF_PRESENT = 2, DIGCF_DEVICEINTERFACE = 0x10;
        public const int HIDP_STATUS_SUCCESS = 0x00110000;

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct NvmlUtilization { public uint Gpu, Memory; }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr sec, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool WriteFile(SafeFileHandle h, byte[] buf, uint len, out uint written, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool DeviceIoControl(SafeFileHandle h, uint code, byte[] inBuf, uint inLen, byte[] outBuf, uint outLen, out uint ret, IntPtr overlapped);
        [DllImport("kernel32.dll")]
        public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x4000;
        [DllImport("kernel32.dll")] public static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] public static extern bool SetPriorityClass(IntPtr process, uint priority);

        [DllImport("hid.dll")] public static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll")] public static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] public static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] public static extern int HidP_GetCaps(IntPtr data, out HIDP_CAPS caps);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr enumerator, IntPtr parent, uint flags);
        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr devInfo, ref Guid g, uint index, ref SP_DEVICE_INTERFACE_DATA data);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA data, IntPtr detail, uint size, out uint needed, IntPtr devInfo);
        [DllImport("setupapi.dll")] public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

        [DllImport("nvml.dll")] public static extern int nvmlInit_v2();
        [DllImport("nvml.dll")] public static extern int nvmlShutdown();
        [DllImport("nvml.dll")] public static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr dev);
        [DllImport("nvml.dll")] public static extern int nvmlDeviceGetTemperature(IntPtr dev, int sensor, out uint temp);
        [DllImport("nvml.dll")] public static extern int nvmlDeviceGetUtilizationRates(IntPtr dev, out NvmlUtilization util);
    }
}
