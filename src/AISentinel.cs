using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace RAMBoosterPro
{
    public class AIProcessItem
    {
        public int Pid { get; set; }
        public string FriendlyName { get; set; }
        public string ProcessName { get; set; }
        public long WorkingSet64 { get; set; }
        public long PrivateBytes64 { get; set; }
        public double CpuPercentage { get; set; }
        public string Status { get; set; }
        public Color StatusColor { get; set; }
        public bool IsEcoQoSActive { get; set; }

        public double WorkingSetMB
        {
            get { return WorkingSet64 / (1024.0 * 1024.0); }
        }

        public double PrivateMB
        {
            get { return PrivateBytes64 / (1024.0 * 1024.0); }
        }

        public string WorkingSetFormatted
        {
            get
            {
                if (WorkingSetMB >= 1024.0)
                {
                    return (WorkingSetMB / 1024.0).ToString("F2") + " GB";
                }
                return WorkingSetMB.ToString("F1") + " MB";
            }
        }

        public string PrivateFormatted
        {
            get
            {
                if (PrivateMB >= 1024.0)
                {
                    return (PrivateMB / 1024.0).ToString("F2") + " GB";
                }
                return PrivateMB.ToString("F0") + " MB";
            }
        }

        public string CpuFormatted
        {
            get { return CpuPercentage.ToString("F1") + "%"; }
        }

        public AIProcessItem()
        {
            Status = "Normal";
            StatusColor = Color.FromArgb(0, 230, 118);
        }
    }

    public class AISentinelSnapshot
    {
        public int ActiveCount { get; set; }
        public long TotalBytes { get; set; }
        public string RiskLevel { get; set; }
        public Color RiskColor { get; set; }
        public List<AIProcessItem> Items { get; set; }

        public double TotalMB
        {
            get { return TotalBytes / (1024.0 * 1024.0); }
        }

        public double TotalGB
        {
            get { return TotalBytes / (1024.0 * 1024.0 * 1024.0); }
        }

        public string TotalFormatted
        {
            get
            {
                if (TotalGB >= 1.0)
                {
                    return TotalGB.ToString("F2") + " GB";
                }
                return TotalMB.ToString("F1") + " MB";
            }
        }

        public AISentinelSnapshot()
        {
            Items = new List<AIProcessItem>();
            RiskLevel = "Baixo (< 1.5 GB)";
            RiskColor = Color.FromArgb(0, 230, 118);
        }
    }

    public class AIOptimizationResult
    {
        public int ProcessesOptimized { get; set; }
        public long BytesFreed { get; set; }

        public double FreedMB
        {
            get { return Math.Max(0, BytesFreed / (1024.0 * 1024.0)); }
        }

        public string FreedFormatted
        {
            get
            {
                if (FreedMB >= 1024.0)
                {
                    return (FreedMB / 1024.0).ToString("F2") + " GB";
                }
                return FreedMB.ToString("F1") + " MB";
            }
        }
    }

    internal class CpuMeasurement
    {
        public TimeSpan LastCpuTime { get; set; }
        public DateTime LastSampleTimeUtc { get; set; }
    }

    public static class AISentinel
    {
        // Lista de processos monitorados
        public static readonly string[] TargetProcessNames = new string[] {
            "antigravity",
            "codex",
            "codex-windows-sandbox-service",
            "codex-computer-use-swift",
            "claude",
            "cursor",
            "node",
            "python",
            "language_server"
        };

        private static readonly Dictionary<int, CpuMeasurement> _cpuHistory = new Dictionary<int, CpuMeasurement>();
        private static readonly object _syncLock = new object();

        public static string ResolveFriendlyName(string procName)
        {
            if (string.IsNullOrEmpty(procName)) return "IA / Worker Desconhecido";
            string lower = procName.ToLowerInvariant();

            if (lower == "antigravity") return "Antigravity IDE";
            if (lower == "codex") return "Codex Subagent";
            if (lower == "codex-windows-sandbox-service") return "Codex Sandbox Service";
            if (lower == "codex-computer-use-swift") return "Codex Computer-Use";
            if (lower == "claude") return "Claude Desktop";
            if (lower == "cursor") return "Cursor Editor";
            if (lower == "node") return "OpenCode / Node Engine";
            if (lower == "python") return "Python Extrator / DuckDB";
            if (lower == "language_server") return "Language Server";

            return procName;
        }

        public static AISentinelSnapshot GetSnapshot()
        {
            var snap = new AISentinelSnapshot();
            DateTime nowUtc = DateTime.UtcNow;
            HashSet<int> livePids = new HashSet<int>();

            foreach (var target in TargetProcessNames)
            {
                Process[] procs = null;
                try
                {
                    procs = Process.GetProcessesByName(target);
                }
                catch
                {
                    continue;
                }

                if (procs == null) continue;

                for (int i = 0; i < procs.Length; i++)
                {
                    Process p = procs[i];
                    try
                    {
                        int pid = p.Id;
                        if (pid <= 4) continue;

                        livePids.Add(pid);
                        long ws = p.WorkingSet64;
                        snap.TotalBytes += ws;

                        double cpuPct = CalculateCpu(p, pid, nowUtc);

                        var item = new AIProcessItem();
                        item.Pid = pid;
                        item.ProcessName = p.ProcessName;
                        item.FriendlyName = ResolveFriendlyName(p.ProcessName);
                        item.WorkingSet64 = ws;
                        item.PrivateBytes64 = p.PrivateMemorySize64;
                        item.CpuPercentage = cpuPct;

                        // Determinar Status de Alerta individual (RAM Física + Commit Real)
                        if (item.PrivateMB >= 1500.0)
                        {
                            item.Status = "Vazamento / Commit (> 1.5 GB)";
                            item.StatusColor = Color.FromArgb(255, 82, 82);
                        }
                        else if (item.WorkingSetMB >= 800.0 || item.PrivateMB >= 800.0)
                        {
                            item.Status = "Alerta (> 800 MB)";
                            item.StatusColor = Color.FromArgb(255, 82, 82);
                        }
                        else if (item.WorkingSetMB >= 400.0 || item.PrivateMB >= 400.0)
                        {
                            item.Status = "Atenção (> 400 MB)";
                            item.StatusColor = Color.FromArgb(255, 193, 7);
                        }
                        else
                        {
                            item.Status = "Estável";
                            item.StatusColor = Color.FromArgb(0, 230, 118);
                        }

                        snap.Items.Add(item);
                    }
                    catch
                    {
                        // Processo pode ter sido finalizado durante a enumeração
                    }
                    finally
                    {
                        p.Dispose();
                    }
                }
            }

            // Limpar do histórico PIDs que já morreram
            lock (_syncLock)
            {
                var keysToRemove = new List<int>();
                foreach (var kvp in _cpuHistory)
                {
                    if (!livePids.Contains(kvp.Key))
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }
                for (int k = 0; k < keysToRemove.Count; k++)
                {
                    _cpuHistory.Remove(keysToRemove[k]);
                }
            }

            // Ordenar por consumo de RAM decrescente
            snap.Items.Sort((a, b) => b.WorkingSet64.CompareTo(a.WorkingSet64));
            snap.ActiveCount = snap.Items.Count;

            // Determinar Status de Risco consolidado:
            // Verde (< 1.5 GB), Amarelo (1.5 GB a 3 GB) e Vermelho (> 3 GB - Risco de Travamento)
            double totalGB = snap.TotalGB;
            if (totalGB < 1.5)
            {
                snap.RiskLevel = "SEGURO (< 1.5 GB)";
                snap.RiskColor = Color.FromArgb(0, 230, 118);
            }
            else if (totalGB <= 3.0)
            {
                snap.RiskLevel = "MODERADO (1.5 - 3.0 GB)";
                snap.RiskColor = Color.FromArgb(255, 193, 7);
            }
            else
            {
                snap.RiskLevel = "CRÍTICO (> 3.0 GB - RISCO DE TRAVAMENTO)";
                snap.RiskColor = Color.FromArgb(255, 82, 82);
            }

            return snap;
        }

        private static double CalculateCpu(Process p, int pid, DateTime nowUtc)
        {
            try
            {
                TimeSpan currentCpuTime = p.TotalProcessorTime;
                lock (_syncLock)
                {
                    CpuMeasurement measurement;
                    if (_cpuHistory.TryGetValue(pid, out measurement))
                    {
                        double elapsedMs = (nowUtc - measurement.LastSampleTimeUtc).TotalMilliseconds;
                        double cpuMs = (currentCpuTime - measurement.LastCpuTime).TotalMilliseconds;

                        measurement.LastCpuTime = currentCpuTime;
                        measurement.LastSampleTimeUtc = nowUtc;

                        if (elapsedMs > 200.0)
                        {
                            int coreCount = Environment.ProcessorCount;
                            if (coreCount <= 0) coreCount = 1;
                            double pct = (cpuMs / (elapsedMs * coreCount)) * 100.0;
                            return Math.Max(0.0, Math.Min(100.0, pct));
                        }
                    }
                    else
                    {
                        var newMeas = new CpuMeasurement();
                        newMeas.LastCpuTime = currentCpuTime;
                        newMeas.LastSampleTimeUtc = nowUtc;
                        _cpuHistory[pid] = newMeas;
                    }
                }
            }
            catch
            {
            }
            return 0.0;
        }

        public static bool TrimAIWorkingSet(int pid)
        {
            if (pid <= 4) return false;
            bool ok = MemoryEngine.OptimizeSingleProcess(pid);
            if (ok)
            {
                MemoryEngine.PurgeKernelMemoryFast();
            }
            return ok;
        }

        public static bool SetEcoQoS(int pid, bool enable)
        {
            return MemoryEngine.ApplyEcoQoS(pid, enable);
        }

        public static int ApplyEcoQoSToAll(bool enable)
        {
            int count = 0;
            var snap = GetSnapshot();
            for (int i = 0; i < snap.Items.Count; i++)
            {
                if (SetEcoQoS(snap.Items[i].Pid, enable))
                {
                    count++;
                }
            }
            return count;
        }

        public static AIOptimizationResult TrimAllAIs()
        {
            var result = new AIOptimizationResult();
            var snap = GetSnapshot();

            for (int i = 0; i < snap.Items.Count; i++)
            {
                var item = snap.Items[i];
                try
                {
                    long before = item.WorkingSet64;
                    if (TrimAIWorkingSet(item.Pid))
                    {
                        result.ProcessesOptimized++;
                        try
                        {
                            using (var p = Process.GetProcessById(item.Pid))
                            {
                                long after = p.WorkingSet64;
                                if (before > after)
                                {
                                    result.BytesFreed += (before - after);
                                }
                            }
                        }
                        catch
                        {
                            // Estimativa com base no working set se o processo encerrou
                            if (before > 0) result.BytesFreed += (before / 2);
                        }
                    }
                }
                catch
                {
                }
            }

            MemoryEngine.PurgeKernelMemoryFast();
            return result;
        }

        private static readonly Dictionary<int, DateTime> _lastTrimByPid = new Dictionary<int, DateTime>();
        private static readonly HashSet<int> _containedPids = new HashSet<int>();

        /// <summary>
        /// Sentinela Anti-Travamento v8.0 (Smart Trim CPU-Aware + ProBalance):
        /// - Se o processo estiver usando muita CPU (>= 12%), NÃO faz EmptyWorkingSet cego (para evitar Page Fault Storm);
        ///   em vez disso, aplica EcoQoS + Prioridade Baixa de I/O e purga a Standby List ao redor.
        /// - Só aplica EmptyWorkingSet quando o processo estiver ocioso (< 12% CPU) e respeitando Cooldown de 45s por PID!
        /// </summary>
        public static bool CheckAndAutoTrim(int individualThresholdMB, double totalThresholdGB, out long bytesFreed)
        {
            bytesFreed = 0;
            var snap = GetSnapshot();
            int foregroundPid = (int)NativeMethods.GetForegroundProcessId();
            bool shouldTrimAll = snap.TotalGB >= totalThresholdGB;
            bool didAction = false;
            DateTime now = DateTime.UtcNow;

            for (int i = 0; i < snap.Items.Count; i++)
            {
                var item = snap.Items[i];
                if (item.Pid <= 4) continue;

                // 1. Auto-EcoQoS + Low I/O Priority em workers pesados de background (Python, Node, Codex, Language Server)
                string lowerName = (item.ProcessName ?? "").ToLowerInvariant();
                bool isBackgroundWorker = lowerName.Contains("python") || lowerName.Contains("node") ||
                                          lowerName.Contains("codex") || lowerName.Contains("language_server");

                if (isBackgroundWorker && item.Pid != foregroundPid && !_containedPids.Contains(item.Pid))
                {
                    if (SetEcoQoS(item.Pid, true))
                    {
                        _containedPids.Add(item.Pid);
                    }
                }

                // Nunca despeja Working Set da janela ativa em foco
                if (item.Pid == foregroundPid) continue;

                bool exceedsLimit = shouldTrimAll || item.WorkingSetMB >= individualThresholdMB || item.PrivateMB >= 1500.0;
                if (!exceedsLimit) continue;

                // 2. Proteção Anti-Thrashing (CPU-Aware):
                // Se o processo está em alta atividade de CPU (>= 12%), apenas garante EcoQoS/Low I/O e NÃO arranca as páginas ativas!
                if (item.CpuPercentage >= 12.0)
                {
                    SetEcoQoS(item.Pid, true);
                    continue;
                }

                // 3. Cooldown de 45 segundos por PID para evitar Page Fault loop
                DateTime lastTrim;
                if (_lastTrimByPid.TryGetValue(item.Pid, out lastTrim))
                {
                    if ((now - lastTrim).TotalSeconds < 45.0)
                    {
                        continue;
                    }
                }

                long before = item.WorkingSet64;
                if (MemoryEngine.OptimizeSingleProcess(item.Pid))
                {
                    _lastTrimByPid[item.Pid] = now;
                    didAction = true;
                    try
                    {
                        using (var p = Process.GetProcessById(item.Pid))
                        {
                            long after = p.WorkingSet64;
                            if (before > after) bytesFreed += (before - after);
                        }
                    }
                    catch { }
                }
            }

            if (didAction || shouldTrimAll)
            {
                MemoryEngine.PurgeKernelMemoryFast();
            }

            return didAction;
        }
    }
}
