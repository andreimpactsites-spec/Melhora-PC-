using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace RAMBoosterPro
{
    public class SystemMemorySnapshot
    {
        public ulong TotalPhysicalBytes { get; set; }
        public ulong AvailablePhysicalBytes { get; set; }
        public ulong UsedPhysicalBytes { get; set; }
        public double UsagePercentage { get; set; }

        public ulong TotalPageFileBytes { get; set; }
        public ulong AvailablePageFileBytes { get; set; }
        public ulong UsedPageFileBytes { get; set; }

        public ulong SystemCacheBytes { get; set; }
        public ulong CommitTotalBytes { get; set; }
        public ulong CommitLimitBytes { get; set; }
        public double CommitUsagePercentage { get; set; }

        public uint TotalProcesses { get; set; }
        public uint TotalThreads { get; set; }
        public uint TotalHandles { get; set; }

        public string TotalPhysicalGB
        {
            get { return (TotalPhysicalBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB"; }
        }

        public string AvailablePhysicalGB
        {
            get { return (AvailablePhysicalBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB"; }
        }

        public string UsedPhysicalGB
        {
            get { return (UsedPhysicalBytes / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB"; }
        }

        public string SystemCacheMB
        {
            get { return (SystemCacheBytes / (1024.0 * 1024.0)).ToString("F0") + " MB"; }
        }

        public string CommitSummaryStr
        {
            get
            {
                double totalGb = CommitTotalBytes / (1024.0 * 1024.0 * 1024.0);
                double limitGb = CommitLimitBytes / (1024.0 * 1024.0 * 1024.0);
                if (limitGb <= 0.1) return "Commit N/A";
                return string.Format("Commit: {0:F1}/{1:F1} GB ({2:F0}%)", totalGb, limitGb, CommitUsagePercentage);
            }
        }
    }

    public class ProcessMemoryItem
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public long WorkingSet64 { get; set; }
        public long PrivateMemory64 { get; set; }
        public long DeltaBytes { get; set; }
        public bool IsSuspended { get; set; }
        public string Priority { get; set; }
        public string Category { get; set; }

        public double WorkingSetMB
        {
            get { return WorkingSet64 / (1024.0 * 1024.0); }
        }

        public double PrivateMemoryMB
        {
            get { return PrivateMemory64 / (1024.0 * 1024.0); }
        }

        public double DeltaMB
        {
            get { return DeltaBytes / (1024.0 * 1024.0); }
        }

        public bool IsSuspectedLeak
        {
            get { return DeltaMB >= 150.0; }
        }

        public string WorkingSetStr
        {
            get
            {
                if (DeltaMB >= 50.0)
                {
                    return string.Format("{0:F1} MB (+{1:F0}M)", WorkingSetMB, DeltaMB);
                }
                return WorkingSetMB.ToString("F1") + " MB";
            }
        }

        public string PrivateMemoryStr
        {
            get { return PrivateMemoryMB.ToString("F1") + " MB"; }
        }
    }

    public static class MemoryMetrics
    {
        private static readonly Dictionary<int, long> _baselineWorkingSetByPid = new Dictionary<int, long>();
        private static readonly object _leakLock = new object();

        public static SystemMemorySnapshot GetSnapshot()
        {
            var snapshot = new SystemMemorySnapshot();
            var memStatus = new NativeMethods.MEMORYSTATUSEX();

            if (NativeMethods.GlobalMemoryStatusEx(memStatus))
            {
                snapshot.TotalPhysicalBytes = memStatus.ullTotalPhys;
                snapshot.AvailablePhysicalBytes = memStatus.ullAvailPhys;
                snapshot.UsedPhysicalBytes = memStatus.ullTotalPhys > memStatus.ullAvailPhys ? memStatus.ullTotalPhys - memStatus.ullAvailPhys : 0;
                snapshot.UsagePercentage = memStatus.dwMemoryLoad;

                snapshot.TotalPageFileBytes = memStatus.ullTotalPageFile;
                snapshot.AvailablePageFileBytes = memStatus.ullAvailPageFile;
                snapshot.UsedPageFileBytes = memStatus.ullTotalPageFile > memStatus.ullAvailPageFile ? memStatus.ullTotalPageFile - memStatus.ullAvailPageFile : 0;
            }

            NativeMethods.PERFORMANCE_INFORMATION perf;
            if (NativeMethods.GetPerformanceInfo(out perf, (uint)Marshal.SizeOf(typeof(NativeMethods.PERFORMANCE_INFORMATION))))
            {
                long pageSize = perf.PageSize.ToInt64();
                snapshot.SystemCacheBytes = (ulong)(perf.SystemCache.ToInt64() * pageSize);
                snapshot.CommitTotalBytes = (ulong)(perf.CommitTotal.ToInt64() * pageSize);
                snapshot.CommitLimitBytes = (ulong)(perf.CommitLimit.ToInt64() * pageSize);
                if (snapshot.CommitLimitBytes > 0)
                {
                    snapshot.CommitUsagePercentage = (snapshot.CommitTotalBytes * 100.0) / snapshot.CommitLimitBytes;
                }
                snapshot.TotalProcesses = perf.ProcessCount;
                snapshot.TotalThreads = perf.ThreadCount;
                snapshot.TotalHandles = perf.HandleCount;
            }

            return snapshot;
        }

        public static List<ProcessMemoryItem> GetTopMemoryProcesses(int count)
        {
            var list = new List<ProcessMemoryItem>();
            Process[] processes = Process.GetProcesses();
            var livePids = new HashSet<int>();

            foreach (var p in processes)
            {
                try
                {
                    int pid = p.Id;
                    if (pid == 0 || pid == 4) continue;
                    long ws = p.WorkingSet64;
                    if (ws <= 0) continue;

                    livePids.Add(pid);
                    long delta = 0;
                    lock (_leakLock)
                    {
                        long baseWs;
                        if (_baselineWorkingSetByPid.TryGetValue(pid, out baseWs))
                        {
                            delta = ws - baseWs;
                            if (ws < baseWs) _baselineWorkingSetByPid[pid] = ws;
                        }
                        else
                        {
                            _baselineWorkingSetByPid[pid] = ws;
                        }
                    }

                    string name = p.ProcessName;
                    bool suspended = MemoryEngine.IsProcessSuspended(pid);
                    string cat = suspended ? "❄️ CONGELADO (0% CPU)" : IdentifyCategory(name);

                    list.Add(new ProcessMemoryItem
                    {
                        Id = pid,
                        Name = name,
                        WorkingSet64 = ws,
                        PrivateMemory64 = p.PrivateMemorySize64,
                        DeltaBytes = delta,
                        IsSuspended = suspended,
                        Priority = "Normal",
                        Category = cat
                    });
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }

            lock (_leakLock)
            {
                if (_baselineWorkingSetByPid.Count > 500)
                {
                    var dead = _baselineWorkingSetByPid.Keys.Where(k => !livePids.Contains(k)).ToList();
                    for (int i = 0; i < dead.Count; i++) _baselineWorkingSetByPid.Remove(dead[i]);
                }
            }

            var topList = list.OrderByDescending(x => x.WorkingSet64).Take(count).ToList();
            for (int i = 0; i < topList.Count; i++)
            {
                topList[i].Priority = GetPriorityNativeFast(topList[i].Id);
            }
            return topList;
        }

        private static string GetPriorityNativeFast(int pid)
        {
            IntPtr hProc = IntPtr.Zero;
            try
            {
                hProc = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProc != IntPtr.Zero)
                {
                    uint prio = NativeMethods.GetPriorityClass(hProc);
                    if (prio == NativeMethods.IDLE_PRIORITY_CLASS) return "Idle (Baixa)";
                    if (prio == NativeMethods.BELOW_NORMAL_PRIORITY_CLASS) return "BelowNormal (Eco)";
                    if (prio == NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS) return "AboveNormal (Boost)";
                    if (prio == NativeMethods.HIGH_PRIORITY_CLASS) return "High (Alta)";
                    if (prio == NativeMethods.NORMAL_PRIORITY_CLASS) return "Normal";
                }
            }
            catch { }
            finally
            {
                if (hProc != IntPtr.Zero) NativeMethods.CloseHandle(hProc);
            }
            return "Normal";
        }

        private static string IdentifyCategory(string name)
        {
            string lower = name.ToLowerInvariant();
            if (lower.Contains("chrome") || lower.Contains("msedge") || lower.Contains("brave") ||
                lower.Contains("firefox") || lower.Contains("opera") || lower.Contains("vivaldi"))
                return "Navegador Web";

            if (lower.Contains("webview2") || lower.Contains("msedgewebview2"))
                return "WebView2 / Automação";

            if (lower.Contains("code") || lower.Contains("discord") || lower.Contains("teams") ||
                lower.Contains("slack") || lower.Contains("spotify") || lower.Contains("steam"))
                return "App / Electron";

            if (lower.Contains("devenv") || lower.Contains("rider") || lower.Contains("idea") ||
                lower.Contains("pycharm") || lower.Contains("node") || lower.Contains("python"))
                return "Desenvolvimento / IDE";

            if (lower.Contains("explorer") || lower.Contains("dwm") || lower.Contains("csrss") ||
                lower.Contains("svchost") || lower.Contains("services") || lower.Contains("taskhostw"))
                return "Sistema Windows";

            return "Geral";
        }
    }
}
