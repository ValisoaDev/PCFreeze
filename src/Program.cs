using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using PCFreeze.Engine;
using PCFreeze.Models;
using PCFreeze.Security;
using PCFreeze.UI;

[assembly: AssemblyTitle("PCFreeze Workstation v2.0")]
[assembly: AssemblyDescription("Logiciel de protection système Reboot-to-Restore (Clone Faronics Deep Freeze)")]
[assembly: AssemblyCompany("PCFreeze Security")]
[assembly: AssemblyProduct("PCFreeze")]
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace PCFreeze
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);
        private const int ATTACH_PARENT_PROCESS = -1;

        [STAThread]
        private static int Main(string[] args)
        {
            bool forceSimulation = false;
            bool trayOnly = false;

            if (args != null && args.Length > 0)
            {
                string firstArg = args[0].ToLowerInvariant();

                // 1. Hook système exécuté au démarrage de Windows (Tâche SYSTEM ou ouverture de session)
                if (firstArg == "--boot-hook" || firstArg == "/boot-hook")
                {
                    TargetDeploymentManager.ExecuteSystemBootHook();
                    return 0;
                }

                // 1b. Désinstallation graphique appelée via la commande secrète Admin
                if (firstArg == "--uninstall-gui" || firstArg == "/uninstall-gui" || firstArg == "--purge")
                {
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    PCFreezeConfig uninstConfig = PCFreezeConfig.Load(false);
                    if (!string.IsNullOrEmpty(uninstConfig.PasswordHash))
                    {
                        using (LoginDialog login = new LoginDialog(uninstConfig))
                        {
                            if (login.ShowDialog() != DialogResult.OK)
                                return 1;
                        }
                    }
                    DialogResult dr = MessageBox.Show(
                        "Voulez-vous désinstaller complètement PCFreeze Workstation de cet ordinateur ?",
                        "PCFreeze — Désinstallation Administrateur",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (dr == DialogResult.Yes)
                    {
                        string report;
                        TargetDeploymentManager.UninstallFromTargetMachine(uninstConfig, out report);
                        MessageBox.Show("PCFreeze a été désinstallé avec succès :\r\n\r\n" + report, "PCFreeze — Désinstallé", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                    return 0;
                }

                // 2. Mode Agent Barre des Tâches au démarrage de session
                if (firstArg == "--tray" || firstArg == "/tray")
                {
                    trayOnly = true;
                }
                else if (firstArg == "--simulate" || firstArg == "/simulate")
                {
                    forceSimulation = true;
                }
                else
                {
                    // 3. Commandes CLI d'administration (équivalent de DFC.exe dans Deep Freeze)
                    return HandleCliCommands(args);
                }
            }

            // Exécuter la restauration au démarrage AVANT le Mutex au cas où une instance se lance à l'ouverture de session
            if (!forceSimulation)
            {
                PCFreezeConfig bootCheckConfig = PCFreezeConfig.Load(false);
                if (bootCheckConfig.IsInstalledOnTargetMachine && !EngineResolver.HasCurrentLogonSessionBeenProcessed())
                {
                    TargetDeploymentManager.ExecuteSystemBootHook();
                }
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, "Local\\PCFreeze_TrayAgent_Singleton_v2", out createdNew))
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                PCFreezeConfig config = PCFreezeConfig.Load(forceSimulation);
                TrayAgentContext ctx = new TrayAgentContext(config, forceSimulation, !trayOnly);
                Application.Run(ctx);
            }

            return 0;
        }

        private static int HandleCliCommands(string[] args)
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            string cmd = args[0].ToLowerInvariant();

            // IMPORTANT : L'installation (--install-target) écrase proprement toute ancienne version
            // sans être bloquée par l'ancien mot de passe d'une installation précédente !
            if (cmd == "/install-target" || cmd == "--install-target")
            {
                PCFreezeConfig freshConfig = new PCFreezeConfig();
                string report;
                bool ok = TargetDeploymentManager.InstallOnTargetMachine(freshConfig, out report);
                Console.WriteLine(report);
                return ok ? 0 : 1;
            }

            PCFreezeConfig config = PCFreezeConfig.Load(false);

            if (cmd == "/status" || cmd == "--status")
            {
                IFreezeEngine engine = EngineResolver.ResolveEngine(config, !config.IsInstalledOnTargetMachine);
                Console.WriteLine();
                Console.WriteLine("=== PCFREEZE v2.0 COMMAND LINE CONTROL (CLI) ===");
                Console.WriteLine("Station             : " + config.StationName);
                Console.WriteLine("Installé sur cible  : " + (config.IsInstalledOnTargetMachine ? "OUI (Production)" : "NON (Mode Simulation)"));
                Console.WriteLine("État actuel         : " + config.CurrentBootState);
                Console.WriteLine("Prochain boot       : " + config.TargetBootStateOnNextReboot);
                Console.WriteLine("Redémarrages dégelés: " + config.RemainingThawedRestarts);
                Console.WriteLine("Moteur actif        : " + engine.EngineName);
                Console.WriteLine("Jeton OTP horaire   : " + CryptoManager.GenerateCurrentOtp(config.StationName, config.OtpSecretSeed));
                Console.WriteLine("================================================");
                return (int)config.CurrentBootState;
            }

            // Vérification du mot de passe si configuré (--password <mdp>)
            string providedPass = "";
            for (int i = 1; i < args.Length - 1; i++)
            {
                if (args[i].Equals("--password", StringComparison.OrdinalIgnoreCase) ||
                    args[i].Equals("/password", StringComparison.OrdinalIgnoreCase))
                {
                    providedPass = args[i + 1];
                }
            }

            if (!CryptoManager.VerifyPasswordOrOtp(config, providedPass))
            {
                Console.WriteLine("[ERREUR] Mot de passe ou jeton OTP requis (--password <mot_de_passe>).");
                return 2;
            }

            IFreezeEngine activeEngine = EngineResolver.ResolveEngine(config, !config.IsInstalledOnTargetMachine);
            string details;

            if (cmd == "/freeze" || cmd == "--freeze")
            {
                config.TargetBootStateOnNextReboot = BootState.Frozen;
                activeEngine.ApplyStateForNextBoot(config, out details);
                Console.WriteLine("[OK] " + details);
                return 0;
            }

            if (cmd == "/thaw" || cmd == "--thaw")
            {
                config.TargetBootStateOnNextReboot = BootState.ThawedPermanent;
                activeEngine.ApplyStateForNextBoot(config, out details);
                Console.WriteLine("[OK] " + details);
                return 0;
            }

            if (cmd == "/thawnext" || cmd == "--thawnext")
            {
                int count = 1;
                if (args.Length > 1) int.TryParse(args[1], out count);
                config.ThawedRestartsConfigured = Math.Max(1, count);
                config.TargetBootStateOnNextReboot = BootState.ThawedNextRestarts;
                activeEngine.ApplyStateForNextBoot(config, out details);
                Console.WriteLine("[OK] " + details);
                return 0;
            }

            if (cmd == "/uninstall-target" || cmd == "--uninstall-target")
            {
                string report;
                bool ok = TargetDeploymentManager.UninstallFromTargetMachine(config, out report);
                Console.WriteLine(report);
                return ok ? 0 : 1;
            }

            Console.WriteLine("Usage : PCFreeze.exe [/status | /freeze | /thaw | /thawnext <N> | /install-target | /uninstall-target | --simulate] [--password <pwd>]");
            return 0;
        }
    }
}
