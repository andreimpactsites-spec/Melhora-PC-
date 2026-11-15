using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using RAMBoosterPro;

namespace RAMBoosterCore
{
    public class Program
    {
        public const string VERSION = "1.0.0-community";

        public static void Main(string[] args)
        {
            Console.Title = "RAM Booster Core - Kernel NT & AI Process Sentinel v" + VERSION;

            // Auto-elevação UAC se não for Administrador
            if (!IsAdministrator())
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[!] RAM Booster Core requer privilégios de Administrador para acessar a NT Native API.");
                Console.WriteLine("[*] Reiniciando com elevação de privilégios...");
                Console.ResetColor();

                try
                {
                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = Process.GetCurrentProcess().MainModule.FileName,
                        Arguments = string.Join(" ", args),
                        Verb = "runas",
                        UseShellExecute = true
                    };
                    Process.Start(psi);
                    return;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("[X] Não foi possível elevar permissões: " + ex.Message);
                    Console.WriteLine("    Execute o terminal como Administrador.");
                    Console.ResetColor();
                    Console.ReadKey();
                    return;
                }
            }

            // Tratamento de argumentos CLI
            if (args.Length > 0)
            {
                string cmd = args[0].ToLowerInvariant();
                switch (cmd)
                {
                    case "--boost":
                    case "-b":
                        RunDeepBoost();
                        return;

                    case "--trim-ai":
                    case "-a":
                        RunAITrim();
                        return;

                    case "--standby":
                    case "-s":
                        RunStandbyPurge();
                        return;

                    case "--watch":
                    case "-w":
                        RunSentinelWatchMode();
                        return;

                    case "--status":
                        PrintTelemetry();
                        return;

                    case "--help":
                    case "-h":
                    case "/?":
                        PrintHelp();
                        return;

                    default:
                        Console.WriteLine("Comando desconhecido: " + cmd);
                        PrintHelp();
                        return;
                }
            }

            // Menu Interativo
            RunInteractiveMenu();
        }

        private static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static void PrintBanner()
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(@"  _____            __  __   ____                  _             ");
            Console.WriteLine(@" |  __ \   /\     |  \/  | |  _ \                | |            ");
            Console.WriteLine(@" | |__) | /  \    | \  / | | |_) | ___   ___  ___| |_ ___ _ __ ");
            Console.WriteLine(@" |  _  / / /\ \   | |\/| | |  _ < / _ \ / _ \/ __| __/ _ \ '__|");
            Console.WriteLine(@" | | \ \/ ____ \  | |  | | | |_) | (_) | (_) \__ \ ||  __/ |   ");
            Console.WriteLine(@" |_|  \_\_/    \_\_|  |_| |____/ \___/ \___/|___/\__\___|_|   ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine(" Kernel NT & AI Process Sentinel • Open Source Community Edition v" + VERSION);
            Console.WriteLine(" ========================================================================");
            Console.ResetColor();
        }

        private static void PrintTelemetry()
        {
            var snap = MemoryMetrics.GetSnapshot();
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine(string.Format(" [+] RAM Física: {0} Total | {1} Em Uso ({2:F1}%) | {3} Livre",
                snap.TotalPhysicalGB, snap.UsedPhysicalGB, snap.UsagePercentage, snap.AvailablePhysicalGB));

            Console.WriteLine(string.Format(" [+] Cache do Kernel: {0} (Standby List & File Cache)", snap.SystemCacheMB));
            Console.WriteLine(string.Format(" [+] {0}", snap.CommitSummaryStr));
            Console.ResetColor();
        }

        private static void RunDeepBoost()
        {
            PrintBanner();
            Console.WriteLine("[*] Executando Turbo Boost no Kernel NT...");
            var res = MemoryEngine.DeepKernelCleanAsync(msg => Console.WriteLine("    -> " + msg)).Result;

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(string.Format("\n[OK] Turbo Boost Concluído! Memória recuperada: {0}", res.FreedMB));
            Console.WriteLine(string.Format("     Processos Otimizados: {0} | Standby Purge: {1}", res.ProcessesOptimized, res.StandbyListPurged ? "Sim" : "Não"));
            Console.ResetColor();
        }

        private static void RunStandbyPurge()
        {
            Console.WriteLine("[*] Purgando Standby List do Kernel...");
            MemoryEngine.PurgeKernelMemoryFast();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[OK] Kernel Standby List descarregada com sucesso.");
            Console.ResetColor();
        }

        private static void RunAITrim()
        {
            PrintBanner();
            Console.WriteLine("[*] Varrendo processos de IA (Claude, Cursor, Node, Python, Antigravity, etc.)...");
            var snap = AISentinel.GetSnapshot();

            if (snap.Items.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[!] Nenhum processo conhecido de IA em execução no momento.");
                Console.ResetColor();
                return;
            }

            Console.WriteLine(string.Format("[i] Encontradas {0} instâncias de IA/Workers consumindo {1:F1} MB no total.",
                snap.Items.Count, snap.TotalMB));

            var aiRes = AISentinel.TrimAllAIs();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(string.Format("[OK] Otimização concluída! Liberados {0:F1} MB em {1} processos de IA.",
                aiRes.FreedMB, aiRes.ProcessesOptimized));
            Console.ResetColor();
        }

        private static void RunSentinelWatchMode()
        {
            PrintBanner();
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[*] Modo Sentinela Ativo (Pressione Ctrl + C para sair)...");
            Console.ResetColor();

            while (true)
            {
                var snap = MemoryMetrics.GetSnapshot();
                var aiSnap = AISentinel.GetSnapshot();

                Console.Title = string.Format("RAM: {0:F0}% | IAs: {1} procs ({2:F0} MB) | Sentinel Watch",
                    snap.UsagePercentage, aiSnap.Items.Count, aiSnap.TotalMB);

                long bytesFreed = 0;
                if (AISentinel.CheckAndAutoTrim(800, 2.5, out bytesFreed))
                {
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine(string.Format("[AUTO-SENTINEL {0:HH:mm:ss}] Descarregada memória ociosa de IA: {1:F1} MB liberados.",
                        DateTime.Now, bytesFreed / (1024.0 * 1024.0)));
                    Console.ResetColor();
                }

                if (snap.UsagePercentage > 85)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(string.Format("[ALERTA {0:HH:mm:ss}] Carga de RAM crítica ({1:F1}%). Executando Auto-Purge de Standby...",
                        DateTime.Now, snap.UsagePercentage));
                    MemoryEngine.PurgeKernelMemoryFast();
                    Console.ResetColor();
                }

                Thread.Sleep(3000);
            }
        }

        private static void RunInteractiveMenu()
        {
            while (true)
            {
                Console.Clear();
                PrintBanner();
                PrintTelemetry();
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine(" MENU DE OPERAÇÕES:");
                Console.WriteLine(" [1] ⚡ Turbo Boost Completo (Kernel Standby + Working Sets)");
                Console.WriteLine(" [2] 🤖 Otimizar Apenas IAs e Workers (Node, Python, Claude, Cursor)");
                Console.WriteLine(" [3] 🧹 Purgar Kernel Standby List (Sem tocar em processos)");
                Console.WriteLine(" [4] 🛡️ Iniciar Modo Sentinela Contínuo (Watch Mode)");
                Console.WriteLine(" [5] 🔄 Atualizar Telemetria");
                Console.WriteLine(" [0] Sair");
                Console.ResetColor();
                Console.Write("\n Escolha uma opção: ");

                var key = Console.ReadKey(true);
                Console.WriteLine();

                switch (key.KeyChar)
                {
                    case '1':
                        RunDeepBoost();
                        Console.WriteLine("\nPressione qualquer tecla para continuar...");
                        Console.ReadKey();
                        break;
                    case '2':
                        RunAITrim();
                        Console.WriteLine("\nPressione qualquer tecla para continuar...");
                        Console.ReadKey();
                        break;
                    case '3':
                        RunStandbyPurge();
                        Console.WriteLine("\nPressione qualquer tecla para continuar...");
                        Console.ReadKey();
                        break;
                    case '4':
                        RunSentinelWatchMode();
                        break;
                    case '5':
                        break;
                    case '0':
                        return;
                }
            }
        }

        private static void PrintHelp()
        {
            PrintBanner();
            Console.WriteLine("Uso: rambooster [OPÇÕES]\n");
            Console.WriteLine("Opções:");
            Console.WriteLine("  --boost, -b     Executa o Turbo Boost no Kernel NT e esvazia working sets.");
            Console.WriteLine("  --trim-ai, -a   Otimiza a memória dos processos de IA (Claude, Node, Python, etc.).");
            Console.WriteLine("  --standby, -s   Purga apenas a Standby List e o cache do Kernel.");
            Console.WriteLine("  --watch, -w     Inicia o monitoramento contínuo em background (Sentinela).");
            Console.WriteLine("  --status        Exibe as métricas de memória do sistema.");
            Console.WriteLine("  --help, -h      Exibe esta tela de ajuda.\n");
        }
    }
}
