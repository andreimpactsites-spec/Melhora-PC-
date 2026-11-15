using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading.Tasks;

namespace RAMBoosterPro
{
    public class OptimizationResult
    {
        public ulong InitialUsedBytes { get; set; }
        public ulong FinalUsedBytes { get; set; }

        public long BytesFreed
        {
            get { return (long)InitialUsedBytes - (long)FinalUsedBytes; }
        }

        public double InitialUsagePct { get; set; }
        public double FinalUsagePct { get; set; }
        public int ProcessesOptimized { get; set; }
        public int ProcessesSkipped { get; set; }
        public bool StandbyListPurged { get; set; }
        public bool ModifiedListFlushed { get; set; }
        public TimeSpan Duration { get; set; }

        public string FreedMB
        {
            get { return Math.Max(0, BytesFreed / (1024.0 * 1024.0)).ToString("F1") + " MB"; }
        }

        public string FreedGB
        {
            get { return Math.Max(0, BytesFreed / (1024.0 * 1024.0 * 1024.0)).ToString("F2") + " GB"; }
        }
    }

    public static class MemoryEngine
    {
        public static bool IsAdministrator()
        {
            try
            {
                using (var identity = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool EnablePrivilege(string privilegeName)
        {
            IntPtr tokenHandle = IntPtr.Zero;
            try
            {
                if (!NativeMethods.OpenProcessToken(
                    NativeMethods.GetCurrentProcess(),
                    NativeMethods.TOKEN_ADJUST_PRIVILEGES | NativeMethods.TOKEN_QUERY,
                    out tokenHandle))
                {
                    return false;
                }

                NativeMethods.LUID luid;
                if (!NativeMethods.LookupPrivilegeValue(null, privilegeName, out luid))
                {
                    return false;
                }

                NativeMethods.TOKEN_PRIVILEGES tp = new NativeMethods.TOKEN_PRIVILEGES();
                tp.PrivilegeCount = 1;
                tp.Luid = luid;
                tp.Attributes = NativeMethods.SE_PRIVILEGE_ENABLED;

                return NativeMethods.AdjustTokenPrivileges(
                    tokenHandle,
                    false,
                    ref tp,
                    0,
                    IntPtr.Zero,
                    IntPtr.Zero);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (tokenHandle != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(tokenHandle);
                }
            }
        }

        public static bool PurgeKernelStandbyList()
        {
            try
            {
                EnablePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
                EnablePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);

                int command = (int)NativeMethods.SystemMemoryListCommand.MemoryPurgeStandbyList;
                uint status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation,
                    ref command,
                    sizeof(int));

                return status == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool FlushKernelModifiedList()
        {
            try
            {
                EnablePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
                EnablePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);

                int command = (int)NativeMethods.SystemMemoryListCommand.MemoryFlushModifiedList;
                uint status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation,
                    ref command,
                    sizeof(int));

                return status == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool EmptyKernelWorkingSets()
        {
            try
            {
                EnablePrivilege(NativeMethods.SE_PROFILE_SINGLE_PROCESS_NAME);
                EnablePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);

                int command = (int)NativeMethods.SystemMemoryListCommand.MemoryEmptyWorkingSets;
                uint status = NativeMethods.NtSetSystemInformation(
                    NativeMethods.SystemMemoryListInformation,
                    ref command,
                    sizeof(int));

                return status == 0;
            }
            catch
            {
                return false;
            }
        }

        public static bool OptimizeSingleProcess(int processId)
        {
            if (processId == 0 || processId == 4) return false;

            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_SET_QUOTA | NativeMethods.PROCESS_QUERY_INFORMATION,
                    false,
                    processId);

                if (hProcess == IntPtr.Zero)
                {
                    hProcess = NativeMethods.OpenProcess(0x001F0000 | 0x1000, false, processId);
                }

                if (hProcess != IntPtr.Zero)
                {
                    NativeMethods.EmptyWorkingSet(hProcess);
                    NativeMethods.SetProcessWorkingSetSize(hProcess, (IntPtr)(-1), (IntPtr)(-1));
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                if (hProcess != IntPtr.Zero)
                {
                    NativeMethods.CloseHandle(hProcess);
                }
            }
            return false;
        }

        private static readonly HashSet<string> CriticalShellProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "dwm", "csrss", "smss", "services", "lsass", "audiodg", "RAMBoosterPro", "Ramboster", "Memory Compression"
        };

        public static bool ApplyEcoQoS(int processId, bool enable)
        {
            if (processId <= 4) return false;
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_INFORMATION,
                    false,
                    processId);

                if (hProcess != IntPtr.Zero)
                {
                    var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE();
                    state.Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION;
                    state.ControlMask = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                    state.StateMask = enable ? NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0;

                    NativeMethods.SetProcessInformation(
                        hProcess,
                        NativeMethods.ProcessPowerThrottling,
                        ref state,
                        (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESS_POWER_THROTTLING_STATE)));

                    uint priority = enable ? NativeMethods.BELOW_NORMAL_PRIORITY_CLASS : NativeMethods.NORMAL_PRIORITY_CLASS;
                    NativeMethods.SetPriorityClass(hProcess, priority);

                    // Kernel NT: Reduz Prioridade de Disco (I/O) e Prioridade de Páginas de Memória
                    int ioPrio = enable ? NativeMethods.IoPriorityLow : NativeMethods.IoPriorityNormal;
                    NativeMethods.NtSetInformationProcess(
                        hProcess,
                        NativeMethods.ProcessIoPriority,
                        ref ioPrio,
                        sizeof(int));

                    int pagePrio = enable ? NativeMethods.MEMORY_PRIORITY_LOW : NativeMethods.MEMORY_PRIORITY_NORMAL;
                    NativeMethods.NtSetInformationProcess(
                        hProcess,
                        NativeMethods.ProcessPagePriority,
                        ref pagePrio,
                        sizeof(int));

                    return true;
                }
            }
            catch { }
            finally
            {
                if (hProcess != IntPtr.Zero) NativeMethods.CloseHandle(hProcess);
            }
            return false;
        }

        public static bool BoostProcessForGamingOrFocus(int processId)
        {
            if (processId <= 4) return false;
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = NativeMethods.OpenProcess(
                    NativeMethods.PROCESS_SET_INFORMATION | NativeMethods.PROCESS_QUERY_INFORMATION,
                    false,
                    processId);

                if (hProcess != IntPtr.Zero)
                {
                    // Desativa qualquer throttling de energia (EcoQoS)
                    var state = new NativeMethods.PROCESS_POWER_THROTTLING_STATE();
                    state.Version = NativeMethods.PROCESS_POWER_THROTTLING_CURRENT_VERSION;
                    state.ControlMask = NativeMethods.PROCESS_POWER_THROTTLING_EXECUTION_SPEED;
                    state.StateMask = 0;

                    NativeMethods.SetProcessInformation(
                        hProcess,
                        NativeMethods.ProcessPowerThrottling,
                        ref state,
                        (uint)Marshal.SizeOf(typeof(NativeMethods.PROCESS_POWER_THROTTLING_STATE)));

                    NativeMethods.SetPriorityClass(hProcess, NativeMethods.ABOVE_NORMAL_PRIORITY_CLASS);

                    int ioPrio = NativeMethods.IoPriorityNormal;
                    NativeMethods.NtSetInformationProcess(hProcess, NativeMethods.ProcessIoPriority, ref ioPrio, sizeof(int));

                    int pagePrio = NativeMethods.MEMORY_PRIORITY_NORMAL;
                    NativeMethods.NtSetInformationProcess(hProcess, NativeMethods.ProcessPagePriority, ref pagePrio, sizeof(int));
                    return true;
                }
            }
            catch { }
            finally
            {
                if (hProcess != IntPtr.Zero) NativeMethods.CloseHandle(hProcess);
            }
            return false;
        }

        public static double SetUltraLowTimerResolution(bool enable)
        {
            try
            {
                uint minRes, maxRes, curRes;
                if (NativeMethods.NtQueryTimerResolution(out minRes, out maxRes, out curRes) == 0)
                {
                    uint target = enable ? maxRes : minRes;
                    uint newRes;
                    if (NativeMethods.NtSetTimerResolution(target, enable, out newRes) == 0)
                    {
                        return newRes / 10000.0; // Converte de 100ns para milissegundos (ex: 0.5ms)
                    }
                    return curRes / 10000.0;
                }
            }
            catch { }
            return 1.0;
        }

        public static bool FlushSystemFileCache()
        {
            try
            {
                EnablePrivilege(NativeMethods.SE_INCREASE_QUOTA_NAME);
                return NativeMethods.SetSystemFileCacheSize(new IntPtr(-1), new IntPtr(-1), 0);
            }
            catch
            {
                return false;
            }
        }

        private static readonly HashSet<int> _suspendedPids = new HashSet<int>();

        public static bool IsProcessSuspended(int processId)
        {
            return _suspendedPids.Contains(processId);
        }

        public static bool SuspendProcess(int processId)
        {
            if (processId <= 4) return false;
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, processId);
                if (hProcess != IntPtr.Zero)
                {
                    if (NativeMethods.NtSuspendProcess(hProcess) == 0)
                    {
                        _suspendedPids.Add(processId);
                        OptimizeSingleProcess(processId);
                        return true;
                    }
                }
            }
            catch { }
            finally
            {
                if (hProcess != IntPtr.Zero) NativeMethods.CloseHandle(hProcess);
            }
            return false;
        }

        public static bool ResumeProcess(int processId)
        {
            if (processId <= 4) return false;
            IntPtr hProcess = IntPtr.Zero;
            try
            {
                hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_SUSPEND_RESUME, false, processId);
                if (hProcess != IntPtr.Zero)
                {
                    if (NativeMethods.NtResumeProcess(hProcess) == 0)
                    {
                        _suspendedPids.Remove(processId);
                        return true;
                    }
                }
            }
            catch { }
            finally
            {
                if (hProcess != IntPtr.Zero) NativeMethods.CloseHandle(hProcess);
            }
            return false;
        }

        public static double CalculateSystemHealthScore(SystemMemorySnapshot snap, out string diagnosis)
        {
            double score = 100.0;
            var issues = new List<string>();

            // Penalidade por carga de RAM física acima de 55%
            if (snap.UsagePercentage > 55.0)
            {
                double pen = (snap.UsagePercentage - 55.0) * 0.75;
                score -= pen;
                if (snap.UsagePercentage >= 75.0) issues.Add("RAM Alta");
            }

            // Penalidade por pressão de Commit Charge acima de 45%
            if (snap.CommitUsagePercentage > 45.0)
            {
                double pen = (snap.CommitUsagePercentage - 45.0) * 0.55;
                score -= pen;
                if (snap.CommitUsagePercentage >= 65.0) issues.Add("Commit Elevado");
            }

            // Penalidade por Cache/Standby inchado (> 2.5 GB)
            double cacheGB = snap.SystemCacheBytes / (1024.0 * 1024.0 * 1024.0);
            if (cacheGB > 2.5)
            {
                score -= Math.Min(12.0, (cacheGB - 2.5) * 3.0);
                issues.Add("Standby Cheia");
            }

            // Bônus de Sistema Otimizado
            score = Math.Max(15.0, Math.Min(100.0, score));

            if (score >= 88.0) diagnosis = "EXCELENTE (Sistema Destravado)";
            else if (score >= 70.0) diagnosis = issues.Count > 0 ? "BOM (" + string.Join(", ", issues.ToArray()) + ")" : "ESTÁVEL (Pronto p/ Turbo)";
            else diagnosis = "ATENÇÃO (" + (issues.Count > 0 ? string.Join(", ", issues.ToArray()) : "Otimização Recomendada") + ")";

            return score;
        }

        public static void PurgeKernelMemoryFast()
        {
            try
            {
                if (IsAdministrator())
                {
                    FlushSystemFileCache();
                    FlushKernelModifiedList();
                    PurgeKernelStandbyList();
                }
            }
            catch { }
        }

        public static Task<OptimizationResult> QuickCleanAsync(Action<string> logProgress)
        {
            return Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                var before = MemoryMetrics.GetSnapshot();
                var result = new OptimizationResult();
                result.InitialUsedBytes = before.UsedPhysicalBytes;
                result.InitialUsagePct = before.UsagePercentage;

                int foregroundPid = (int)NativeMethods.GetForegroundProcessId();
                Process[] processes = Process.GetProcesses();
                int optimized = 0;
                int skipped = 0;

                if (logProgress != null)
                {
                    logProgress(string.Format("Iniciando limpeza inteligente em {0} processos (Protegendo janela ativa PID {1})...", processes.Length, foregroundPid));
                }

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id <= 4 || p.Id == foregroundPid || CriticalShellProcesses.Contains(p.ProcessName))
                        {
                            skipped++;
                            continue;
                        }

                        if (OptimizeSingleProcess(p.Id))
                        {
                            optimized++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }

                TrimCurrentProcessMemory();

                // Purga Standby List e System File Cache do Kernel do Windows para liberar RAM real!
                if (IsAdministrator())
                {
                    FlushSystemFileCache();
                    result.ModifiedListFlushed = FlushKernelModifiedList();
                    result.StandbyListPurged = PurgeKernelStandbyList();
                }

                stopwatch.Stop();
                var after = MemoryMetrics.GetSnapshot();
                result.FinalUsedBytes = after.UsedPhysicalBytes;
                result.FinalUsagePct = after.UsagePercentage;
                result.ProcessesOptimized = optimized;
                result.ProcessesSkipped = skipped;
                result.Duration = stopwatch.Elapsed;

                if (logProgress != null)
                {
                    logProgress(string.Format("Otimização rápida concluída! {0} processos otimizados em {1:F0}ms.",
                        optimized, result.Duration.TotalMilliseconds));
                }
                return result;
            });
        }

        public static Task<OptimizationResult> DeepKernelCleanAsync(Action<string> logProgress)
        {
            return Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                var before = MemoryMetrics.GetSnapshot();
                var result = new OptimizationResult();
                result.InitialUsedBytes = before.UsedPhysicalBytes;
                result.InitialUsagePct = before.UsagePercentage;

                int foregroundPid = (int)NativeMethods.GetForegroundProcessId();
                if (logProgress != null) logProgress("⚡ Ativando privilégios de Administrador, File Cache Flush e Kernel NT...");
                bool isAdmin = IsAdministrator();

                if (isAdmin)
                {
                    FlushSystemFileCache();
                    if (logProgress != null) logProgress("1/4 Gravando páginas modificadas pendentes (MemoryFlushModifiedList)...");
                    result.ModifiedListFlushed = FlushKernelModifiedList();

                    if (logProgress != null) logProgress("2/4 Purgando Standby List do Kernel do Windows (MemoryPurgeStandbyList)...");
                    result.StandbyListPurged = PurgeKernelStandbyList();

                    if (logProgress != null) logProgress("3/4 Descarregando Working Sets do Kernel (MemoryEmptyWorkingSets)...");
                    EmptyKernelWorkingSets();
                }
                else
                {
                    if (logProgress != null) logProgress("⚠️ Aviso: Execute como Administrador para liberação completa da Standby List em nível de Kernel.");
                }

                if (logProgress != null) logProgress("4/4 Esvaziando Working Sets de processos em background (protegendo janela ativa)...");
                Process[] processes = Process.GetProcesses();
                int optimized = 0;
                int skipped = 0;

                foreach (var p in processes)
                {
                    try
                    {
                        if (p.Id <= 4 || p.Id == foregroundPid || CriticalShellProcesses.Contains(p.ProcessName))
                        {
                            skipped++;
                            continue;
                        }

                        if (OptimizeSingleProcess(p.Id))
                        {
                            optimized++;
                        }
                        else
                        {
                            skipped++;
                        }
                    }
                    catch
                    {
                        skipped++;
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }

                if (isAdmin)
                {
                    PurgeKernelStandbyList();
                }

                TrimCurrentProcessMemory();

                stopwatch.Stop();
                var after = MemoryMetrics.GetSnapshot();
                result.FinalUsedBytes = after.UsedPhysicalBytes;
                result.FinalUsagePct = after.UsagePercentage;
                result.ProcessesOptimized = optimized;
                result.ProcessesSkipped = skipped;
                result.Duration = stopwatch.Elapsed;

                if (logProgress != null)
                {
                    logProgress(string.Format("🔥 Turbo Clean concluído! Liberados {0} ({1}) em {2:F0}ms.",
                        result.FreedMB, result.FreedGB, result.Duration.TotalMilliseconds));
                }
                return result;
            });
        }

        public static Task<OptimizationResult> OptimizeBrowsersAndWebViewsAsync(Action<string> logProgress)
        {
            return Task.Run(() =>
            {
                var stopwatch = Stopwatch.StartNew();
                var before = MemoryMetrics.GetSnapshot();
                var result = new OptimizationResult();
                result.InitialUsedBytes = before.UsedPhysicalBytes;
                result.InitialUsagePct = before.UsagePercentage;

                string[] targetNames = new string[] {
                    "msedgewebview2", "chrome", "msedge", "brave", "firefox", "opera",
                    "vivaldi", "code", "discord", "slack", "teams", "spotify"
                };

                int optimized = 0;
                int skipped = 0;
                if (logProgress != null) logProgress("Identificando instâncias de navegadores, WebView2 e apps Electron...");

                foreach (var target in targetNames)
                {
                    Process[] procs = Process.GetProcessesByName(target);
                    foreach (var p in procs)
                    {
                        try
                        {
                            if (OptimizeSingleProcess(p.Id))
                            {
                                optimized++;
                            }
                            else
                            {
                                skipped++;
                            }
                        }
                        catch
                        {
                            skipped++;
                        }
                        finally
                        {
                            p.Dispose();
                        }
                    }
                }

                TrimCurrentProcessMemory();

                stopwatch.Stop();
                var after = MemoryMetrics.GetSnapshot();
                result.FinalUsedBytes = after.UsedPhysicalBytes;
                result.FinalUsagePct = after.UsagePercentage;
                result.ProcessesOptimized = optimized;
                result.ProcessesSkipped = skipped;
                result.Duration = stopwatch.Elapsed;

                if (logProgress != null)
                {
                    logProgress(string.Format("🌐 Otimizador Web: {0} processos de navegadores/WebView2 desinchados!", optimized));
                }
                return result;
            });
        }

        public static void TrimCurrentProcessMemory()
        {
            try
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                NativeMethods.SetProcessWorkingSetSize(NativeMethods.GetCurrentProcess(), (IntPtr)(-1), (IntPtr)(-1));
            }
            catch
            {
            }
        }
    }
}
