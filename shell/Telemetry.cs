using System.Runtime.InteropServices;

namespace SLW3D.Shell;

/// <summary>Live readings for the system panel using standard Windows APIs only:
/// CPU load (split into P-cores/E-cores on hybrid CPUs, or overall CPU otherwise), RAM, and live ready drives.</summary>
sealed class Telemetry : IDisposable
{
    readonly int n = Environment.ProcessorCount;
    readonly List<int> pCpus = new(), eCpus = new();
    long[] prevIdle = Array.Empty<long>(), prevTotal = Array.Empty<long>();
    DateTime lastDrivesAt = DateTime.MinValue;
    List<object> cachedDrives = new();

    public Telemetry()
    {
        ClassifyCores();
        SampleCpu(); // prime the deltas
        RefreshDrives();
    }

    public object Sample()
    {
        var (p, e, overall) = SampleCpu();
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        GlobalMemoryStatusEx(ref mem);
        double ramTotal = mem.ullTotalPhys / 1073741824.0;
        double ramUsed = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;

        if ((DateTime.UtcNow - lastDrivesAt).TotalSeconds >= 4) RefreshDrives();

        return new
        {
            type = "telemetry",
            cpu = Math.Round(overall),
            threads = n,
            p = Math.Round(p),
            e = Math.Round(e),
            pCount = pCpus.Count,
            eCount = eCpus.Count,
            ramUsed = Math.Round(ramUsed, 1),
            ramTotal = Math.Round(ramTotal, 1),
            drives = cachedDrives,
        };
    }

    void RefreshDrives()
    {
        lastDrivesAt = DateTime.UtcNow;
        var list = new List<object>();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType is not (DriveType.Fixed or DriveType.Removable)) continue;
                    if (!d.IsReady || d.TotalSize <= 0) continue;
                    double totalGb = d.TotalSize / 1073741824.0;
                    double freeGb = d.AvailableFreeSpace / 1073741824.0;
                    int usedPct = (int)Math.Clamp(Math.Round(((d.TotalSize - d.AvailableFreeSpace) / (double)d.TotalSize) * 100.0), 0, 100);
                    list.Add(new
                    {
                        name = d.Name.TrimEnd('\\'),
                        freeGb = Math.Round(freeGb, 1),
                        totalGb = Math.Round(totalGb, 1),
                        usedPct,
                        usb = d.DriveType == DriveType.Removable,
                    });
                }
                catch { /* drive unplugged mid-scan or busy */ }
            }
        }
        catch { /* leave list as-is */ }
        cachedDrives = list;
    }

    void ClassifyCores()
    {
        try
        {
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out uint len, IntPtr.Zero, 0);
            IntPtr buf = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetSystemCpuSetInformation(buf, len, out len, IntPtr.Zero, 0)) throw new InvalidOperationException("GetSystemCpuSetInformation failed");
                var cls = new SortedDictionary<int, byte>();
                int off = 0;
                while (off < len)
                {
                    int size = Marshal.ReadInt32(buf, off);
                    if (size <= 0) break;
                    ushort group = (ushort)Marshal.ReadInt16(buf, off + 12);
                    byte lp = Marshal.ReadByte(buf, off + 14);
                    byte eff = Marshal.ReadByte(buf, off + 18);
                    if (group == 0) cls[lp] = eff;
                    off += size;
                }
                byte top = cls.Values.Max();
                foreach (var (lp, eff) in cls) (eff == top ? pCpus : eCpus).Add(lp);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch (Exception ex)
        {
            Log.Write("CPU classification failed, treating all as P-cores: " + ex.Message);
            pCpus.Clear(); eCpus.Clear();
            pCpus.AddRange(Enumerable.Range(0, n));
        }
        Log.Write($"P-core logical CPUs: {Describe(pCpus)}; E-core logical CPUs: {Describe(eCpus)}");
    }

    static string Describe(List<int> l) => l.Count == 0 ? "none" : $"{l.Min()}-{l.Max()} ({l.Count})";

    (double p, double e, double overall) SampleCpu()
    {
        var info = new SPPI[n];
        int status = NtQuerySystemInformation(8 /* SystemProcessorPerformanceInformation */, info, Marshal.SizeOf<SPPI>() * n, out _);
        if (status != 0) return (0, 0, 0);
        bool first = prevIdle.Length != n;
        if (first) { prevIdle = new long[n]; prevTotal = new long[n]; }
        var util = new double[n];
        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            long idle = info[i].Idle, total = info[i].Kernel + info[i].User; // kernel time includes idle
            long dI = idle - prevIdle[i], dT = total - prevTotal[i];
            util[i] = (!first && dT > 0) ? Math.Clamp(1.0 - (double)dI / dT, 0, 1) : 0;
            sum += util[i];
            prevIdle[i] = idle; prevTotal[i] = total;
        }
        double Avg(List<int> l) => l.Count == 0 ? 0 : l.Where(i => i < n).Select(i => util[i]).DefaultIfEmpty(0).Average() * 100;
        return (Avg(pCpus), Avg(eCpus), n > 0 ? (sum / n) * 100.0 : 0);
    }

    public void Dispose() { }

    [StructLayout(LayoutKind.Sequential)] struct SPPI { public long Idle, Kernel, User, Dpc, Interrupt; public uint InterruptCount; }
    [StructLayout(LayoutKind.Sequential)]
    struct MEMORYSTATUSEX { public uint dwLength, dwMemoryLoad; public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual; }

    [DllImport("ntdll.dll")] static extern int NtQuerySystemInformation(int cls, [Out] SPPI[] info, int len, out int ret);
    [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemCpuSetInformation(IntPtr info, uint len, out uint ret, IntPtr process, uint flags);
    [DllImport("kernel32.dll")] static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX m);
}
