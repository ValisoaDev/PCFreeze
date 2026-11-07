using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PCFreeze.Models;

namespace PCFreeze.Engine
{
    public static class ThawSpaceManager
    {
        public static bool CreateOrMountThawSpace(ThawSpaceDriveConfig thawSpace, bool simulationMode, out string message)
        {
            if (simulationMode)
            {
                message = string.Format(
                    "[SIMULATION] Disque virtuel ThawSpace ({0} - {1} Go - {2}) simulé avec succès dans {3}.",
                    thawSpace.DriveLetter,
                    thawSpace.SizeGB,
                    thawSpace.IsHiddenInExplorer ? "Caché dans l'Explorateur" : "Visible dans l'Explorateur",
                    thawSpace.BackingFilePath);
                AuditLogger.Log("SIM-THAWSPACE", message, true);
                return true;
            }

            try
            {
                char letter = thawSpace.DriveLetter.Trim().ToUpperInvariant()[0];
                string vhdxPath = thawSpace.BackingFilePath;
                string parentDir = Path.GetDirectoryName(vhdxPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                long sizeMB = Math.Max(512L, (long)thawSpace.SizeGB * 1024L);
                StringBuilder diskpartScript = new StringBuilder();

                if (!File.Exists(vhdxPath))
                {
                    diskpartScript.AppendFormat("create vdisk file=\"{0}\" maximum={1} type=expandable", vhdxPath, sizeMB).AppendLine();
                    diskpartScript.AppendLine("select vdisk file=\"" + vhdxPath + "\"");
                    diskpartScript.AppendLine("attach vdisk");
                    diskpartScript.AppendLine("create partition primary");
                    diskpartScript.AppendFormat("format fs=ntfs label=\"{0}\" quick", thawSpace.Label ?? "ThawSpace").AppendLine();
                    diskpartScript.AppendFormat("assign letter={0}", letter).AppendLine();
                }
                else
                {
                    diskpartScript.AppendLine("select vdisk file=\"" + vhdxPath + "\"");
                    diskpartScript.AppendLine("attach vdisk");
                    diskpartScript.AppendLine("select partition 1");
                    diskpartScript.AppendFormat("assign letter={0}", letter).AppendLine();
                }

                string tempScript = Path.Combine(Path.GetTempPath(), "pcfreeze_thawspace_" + letter + ".txt");
                File.WriteAllText(tempScript, diskpartScript.ToString(), Encoding.ASCII);

                string dpOut;
                int code = EngineResolver.RunProcessHidden("diskpart.exe", "/s \"" + tempScript + "\"", out dpOut, 90000);
                try { File.Delete(tempScript); } catch { }

                ConfigureExplorerDriveVisibility(letter, thawSpace.IsHiddenInExplorer);

                message = string.Format("ThawSpace {0} ({1} Go) monté (code {2}) : {3}", thawSpace.DriveLetter, thawSpace.SizeGB, code, dpOut);
                AuditLogger.Log("THAWSPACE", message, false);
                return code == 0;
            }
            catch (Exception ex)
            {
                message = "Erreur lors de la création/montage du ThawSpace : " + ex.Message;
                AuditLogger.Log("ERROR", message, false);
                return false;
            }
        }

        public static bool UnmountThawSpace(ThawSpaceDriveConfig thawSpace, bool simulationMode, out string message)
        {
            if (simulationMode)
            {
                message = string.Format("[SIMULATION] ThawSpace {0} démonté avec succès.", thawSpace.DriveLetter);
                AuditLogger.Log("SIM-THAWSPACE", message, true);
                return true;
            }

            if (!File.Exists(thawSpace.BackingFilePath))
            {
                message = "Fichier VHDX inexistant, rien à démonter.";
                return true;
            }

            StringBuilder script = new StringBuilder();
            script.AppendLine("select vdisk file=\"" + thawSpace.BackingFilePath + "\"");
            script.AppendLine("detach vdisk");

            string tempScript = Path.Combine(Path.GetTempPath(), "pcfreeze_detach.txt");
            File.WriteAllText(tempScript, script.ToString(), Encoding.ASCII);

            string dpOut;
            EngineResolver.RunProcessHidden("diskpart.exe", "/s \"" + tempScript + "\"", out dpOut, 30000);
            try { File.Delete(tempScript); } catch { }

            message = "ThawSpace démonté : " + dpOut;
            return true;
        }

        private static void ConfigureExplorerDriveVisibility(char driveLetter, bool hideInExplorer)
        {
            try
            {
                int bitIndex = char.ToUpperInvariant(driveLetter) - 'A';
                if (bitIndex < 0 || bitIndex > 25) return;
                int mask = 1 << bitIndex;

                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer"))
                {
                    if (key != null)
                    {
                        int current = 0;
                        object existing = key.GetValue("NoDrives");
                        if (existing is int) current = (int)existing;

                        if (hideInExplorer)
                            current |= mask;
                        else
                            current &= ~mask;

                        if (current == 0)
                            key.DeleteValue("NoDrives", false);
                        else
                            key.SetValue("NoDrives", current, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }
    }

    public static class TargetDeploymentManager
    {
        [DllImport("user32.dll")]
        public static extern bool BlockInput(bool fBlockIt);

        public static bool InstallOnTargetMachine(PCFreezeConfig config, out string report)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                // 1. Arrêter toute ancienne instance de PCFreeze en cours d'exécution
                string killOut;
                EngineResolver.RunProcessHidden(
                    "taskkill.exe",
                    "/F /IM PCFreeze.exe /FI \"PID ne " + System.Diagnostics.Process.GetCurrentProcess().Id + "\"",
                    out killOut,
                    5000);

                // 2. Nettoyer toute ancienne configuration (ex: ancienne version v1.0 avec ancien mot de passe)
                string progDataDir = @"C:\ProgramData\PCFreeze";
                try
                {
                    if (Directory.Exists(progDataDir))
                    {
                        Directory.Delete(progDataDir, true);
                    }
                    Directory.CreateDirectory(progDataDir);
                    sb.AppendLine("✔ Ancienne configuration purgée et dossier C:\\ProgramData\\PCFreeze réinitialisé.");
                }
                catch { }

                // 3. Accorder les droits de lecture/écriture universels (SID *S-1-5-32-545 = Utilisateurs) sur C:\ProgramData\PCFreeze
                // Indispensable pour que l'agent lancé à l'ouverture de session (HKLM\...\Run) puisse lire/écrire sans blocage UAC !
                string icaclsOut;
                EngineResolver.RunProcessHidden(
                    "icacls.exe",
                    "\"" + progDataDir + "\" /grant *S-1-5-32-545:(OI)(CI)F /T /C /Q",
                    out icaclsOut,
                    15000);

                // 4. Copier le nouvel exécutable v2.0 dans C:\Program Files\PCFreeze\PCFreeze.exe
                string installDir = @"C:\Program Files\PCFreeze";
                if (!Directory.Exists(installDir))
                {
                    Directory.CreateDirectory(installDir);
                }

                string currentExe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                string targetExe = Path.Combine(installDir, "PCFreeze.exe");
                if (!string.Equals(currentExe, targetExe, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(currentExe, targetExe, true);
                    sb.AppendLine("✔ Exécutable PCFreeze v2.0 copié dans " + targetExe);
                }

                // 5. Activer l'état GELÉ immédiatement et initialiser les identifiants de boot
                string currentBootId = EngineResolver.GetCurrentBootIdentifier();
                config.IsInstalledOnTargetMachine = true;
                config.CurrentBootState = BootState.Frozen;
                config.TargetBootStateOnNextReboot = BootState.Frozen;
                config.RemainingThawedRestarts = 0;
                config.LastSystemBootHookId = currentBootId;
                config.LastUserLogonRestoreBootId = currentBootId;
                config.Save(false);

                // 6. Désactiver le Démarrage Rapide Windows (Fast Startup / Hiberboot)
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    using (RegistryKey powerKey = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power", true))
                    {
                        if (powerKey != null)
                        {
                            powerKey.SetValue("HiberbootEnabled", 0, RegistryValueKind.DWord);
                            sb.AppendLine("✔ Démarrage rapide (Hiberboot) désactivé.");
                        }
                    }
                }
                catch { }

                // 7. Enregistrer le démarrage automatique dans HKLM\...\Run ET HKCU\...\Run (invisible dans le Panneau de configuration)
                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    {
                        using (RegistryKey runKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (runKey != null)
                            {
                                runKey.SetValue("PCFreezeTrayAgent", "\"" + targetExe + "\" --tray", RegistryValueKind.String);
                            }
                        }
                        baseKey.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PCFreeze", false);
                    }

                    using (RegistryKey cuRun = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (cuRun != null)
                        {
                            cuRun.SetValue("PCFreezeTrayAgent", "\"" + targetExe + "\" --tray", RegistryValueKind.String);
                        }
                    }
                    sb.AppendLine("✔ Agent de restauration au démarrage enregistré (HKLM & HKCU Run — Invisible dans Applications installées).");
                }
                catch { }

                // 8. Provisionner le moteur et capturer l'image GELÉE de référence (Bureau, Documents, Téléchargements, etc.)
                IFreezeEngine engine = EngineResolver.ResolveEngine(config, false);
                string engineOut;
                engine.ProvisionEngineOnTarget(config, out engineOut);
                sb.AppendLine("✔ Moteur initialisé (" + engine.EngineName + ")");

                string applyOut;
                engine.ApplyStateForNextBoot(config, out applyOut);
                sb.AppendLine(applyOut);

                // 9. Créer les tâches planifiées en autorisant explicitement l'exécution sur batterie (PC portables !)
                string schOut;
                EngineResolver.RunProcessHidden(
                    "schtasks.exe",
                    string.Format("/Create /TN \"PCFreeze_BootService\" /TR \"\\\"{0}\\\" --boot-hook\" /SC ONSTART /RU \"SYSTEM\" /RL HIGHEST /F", targetExe),
                    out schOut);

                EngineResolver.RunProcessHidden(
                    "schtasks.exe",
                    string.Format("/Create /TN \"PCFreeze_TrayAgent\" /TR \"\\\"{0}\\\" --tray\" /SC ONLOGON /RL HIGHEST /F", targetExe),
                    out schOut);

                // Désactiver la restriction "Ne démarrer que sur secteur" (DisallowStartIfOnBatteries) sur les portables
                EngineResolver.RunProcessHidden(
                    "powershell.exe",
                    "-NoProfile -NonInteractive -Command \"$s = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries; Set-ScheduledTask -TaskName 'PCFreeze_BootService' -Settings $s -ErrorAction SilentlyContinue; Set-ScheduledTask -TaskName 'PCFreeze_TrayAgent' -Settings $s -ErrorAction SilentlyContinue\"",
                    out schOut,
                    15000);

                sb.AppendLine("✔ Tâches planifiées configurées (y compris fonctionnement sur batterie).");

                report = sb.ToString();
                AuditLogger.Log("INSTALL", "PCFreeze v2.0 installé avec succès sur la machine cible.\n" + report, false);
                return true;
            }
            catch (Exception ex)
            {
                report = "Erreur lors de l'installation sur la machine cible (vérifiez les droits Administrateur) : " + ex.Message;
                AuditLogger.Log("ERROR", report, false);
                return false;
            }
        }

        public static bool UninstallFromTargetMachine(PCFreezeConfig config, out string report)
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                config.TargetBootStateOnNextReboot = BootState.ThawedPermanent;
                IFreezeEngine engine = EngineResolver.ResolveEngine(config, false);
                string engineOut;
                engine.ApplyStateForNextBoot(config, out engineOut);
                sb.AppendLine("✔ Filtre de gel désarmé : " + engineOut);

                try
                {
                    using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                    {
                        using (RegistryKey runKey = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                        {
                            if (runKey != null)
                            {
                                runKey.DeleteValue("PCFreezeTrayAgent", false);
                            }
                        }
                        baseKey.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PCFreeze", false);
                    }
                    using (RegistryKey cuRun = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                    {
                        if (cuRun != null)
                        {
                            cuRun.DeleteValue("PCFreezeTrayAgent", false);
                        }
                    }
                    sb.AppendLine("✔ Entrées Registre supprimées.");
                }
                catch { }

                string schOut;
                EngineResolver.RunProcessHidden("schtasks.exe", "/Delete /TN \"PCFreeze_BootService\" /F", out schOut);
                EngineResolver.RunProcessHidden("schtasks.exe", "/Delete /TN \"PCFreeze_TrayAgent\" /F", out schOut);
                sb.AppendLine("✔ Démarrage automatique et tâches planifiées supprimés.");

                string killOut;
                EngineResolver.RunProcessHidden(
                    "taskkill.exe",
                    "/F /IM PCFreeze.exe /FI \"PID ne " + System.Diagnostics.Process.GetCurrentProcess().Id + "\"",
                    out killOut,
                    5000);

                config.IsInstalledOnTargetMachine = false;

                try
                {
                    string progDataDir = PCFreezeConfig.GetConfigDirectory(false);
                    if (Directory.Exists(progDataDir))
                    {
                        Directory.Delete(progDataDir, true);
                        sb.AppendLine("✔ Dossier de données C:\\ProgramData\\PCFreeze nettoyé.");
                    }
                }
                catch { }

                try
                {
                    System.Diagnostics.ProcessStartInfo psiClean = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/C ping 127.0.0.1 -n 3 >nul & rmdir /s /q \"C:\\Program Files\\PCFreeze\"",
                        CreateNoWindow = true,
                        UseShellExecute = false
                    };
                    System.Diagnostics.Process.Start(psiClean);
                    sb.AppendLine("✔ Dossier C:\\Program Files\\PCFreeze supprimé.");
                }
                catch { }

                report = sb.ToString();
                return true;
            }
            catch (Exception ex)
            {
                report = "Erreur lors de la désinstallation : " + ex.Message;
                return false;
            }
        }

        public static void ExecuteSystemBootHook()
        {
            PCFreezeConfig config = PCFreezeConfig.Load(false);
            if (!config.IsInstalledOnTargetMachine)
                return;

            string currentBootId = EngineResolver.GetCurrentBootIdentifier();

            // 1. Monter tous les disques virtuels ThawSpace activés
            foreach (ThawSpaceDriveConfig ts in config.ThawSpaces)
            {
                if (ts.IsEnabled)
                {
                    string msg;
                    ThawSpaceManager.CreateOrMountThawSpace(ts, false, out msg);
                }
            }

            // 2. Si le boot système n'a pas encore été traité pour ce démarrage (BootId), exécuter la transition du moteur
            if (!string.Equals(config.LastSystemBootHookId, currentBootId, StringComparison.OrdinalIgnoreCase))
            {
                IFreezeEngine engine = EngineResolver.ResolveEngine(config, false);
                engine.ExecuteBootHook(config);
                config.LastSystemBootHookId = currentBootId;
                config.Save(false);
            }

            // 3. Si nous sommes dans la session interactive de l'utilisateur à l'ouverture de session :
            // restaurer explicitement le Bureau/Documents/Téléchargements de l'utilisateur connecté !
            if (Environment.UserInteractive && !string.Equals(config.LastUserLogonRestoreBootId, currentBootId, StringComparison.OrdinalIgnoreCase))
            {
                if (config.CurrentBootState == BootState.Frozen)
                {
                    string restoreMsg;
                    BaselineSnapshotManager.RestoreBaseline(config, out restoreMsg);
                    EngineResolver.RefreshExplorerDesktop();
                }
                config.LastUserLogonRestoreBootId = currentBootId;
                config.Save(false);
            }

            EngineResolver.MarkCurrentLogonSessionProcessed();
        }
    }
}
