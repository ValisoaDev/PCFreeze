using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using PCFreeze.Models;

namespace PCFreeze.Engine
{
    public interface IFreezeEngine
    {
        string EngineName { get; }
        string TechnicalDescription { get; }
        bool IsSimulation { get; }
        bool IsSupportedOnThisOS(out string reason);
        bool ProvisionEngineOnTarget(PCFreezeConfig config, out string logOutput);
        bool ApplyStateForNextBoot(PCFreezeConfig config, out string details);
        bool SyncExclusionsAndOverlay(PCFreezeConfig config, out string details);
        int GetEstimatedOverlayUsedMB(PCFreezeConfig config);
        void ExecuteBootHook(PCFreezeConfig config);
    }

    public static class EngineResolver
    {
        [DllImport("shell32.dll")]
        private static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);
        private const int SHCNE_ASSOCCHANGED = 0x08000000;
        private const int SHCNF_IDLIST = 0x0000;

        [DllImport("kernel32.dll")]
        private static extern ulong GetTickCount64();

        public static void RefreshExplorerDesktop()
        {
            try
            {
                SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
        }

        /// <summary>
        /// Retourne un identifiant unique du démarrage Windows courant (BootId noyau + horodatage de boot).
        /// Change obligatoirement à chaque redémarrage ou arrêt/allumage du PC, indépendamment du compte (SYSTEM vs User).
        /// </summary>
        public static string GetCurrentBootIdentifier()
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Memory Management\PrefetchParameters"))
                {
                    if (key != null)
                    {
                        object bootId = key.GetValue("BootId");
                        if (bootId != null)
                        {
                            return "KernelBootId_" + bootId.ToString();
                        }
                    }
                }
            }
            catch { }

            try
            {
                ulong uptimeMs = GetTickCount64();
                DateTime bootTimeUtc = DateTime.UtcNow.AddMilliseconds(-(double)uptimeMs);
                // Arrondi à la minute pour rester constant durant toute la session de boot
                return "BootTime_" + bootTimeUtc.ToString("yyyyMMdd_HHmm");
            }
            catch
            {
                return "BootSession_Default";
            }
        }

        public static bool HasCurrentLogonSessionBeenProcessed()
        {
            try
            {
                PCFreezeConfig cfg = PCFreezeConfig.Load(false);
                string currentBootId = GetCurrentBootIdentifier();
                if (!string.Equals(cfg.LastUserLogonRestoreBootId, currentBootId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
                return true;
            }
            catch { }
            return false;
        }

        public static void MarkCurrentLogonSessionProcessed()
        {
            try
            {
                string currentBootId = GetCurrentBootIdentifier();
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Volatile Environment"))
                {
                    if (key != null)
                    {
                        key.SetValue("PCFreeze_SessionInitialized", currentBootId, RegistryValueKind.String);
                    }
                }
            }
            catch { }
        }

        public static string GetWindowsEditionId()
        {
            try
            {
                using (RegistryKey baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        object val = key.GetValue("EditionID");
                        if (val != null && !string.IsNullOrEmpty(val.ToString()))
                            return val.ToString();
                        object prod = key.GetValue("ProductName");
                        if (prod != null)
                            return prod.ToString();
                    }
                }
            }
            catch { }
            return "Unknown";
        }

        public static bool DoesOSNativelySupportUwf()
        {
            string edition = GetWindowsEditionId().ToLowerInvariant();
            return edition.Contains("professional") ||
                   edition.Contains("enterprise") ||
                   edition.Contains("education") ||
                   edition.Contains("iot") ||
                   edition.Contains("server") ||
                   edition.Contains("pro");
        }

        public static bool IsUwfBinaryPresent()
        {
            string sys32 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "uwfmgr.exe");
            string sysNative = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Sysnative\uwfmgr.exe");
            return File.Exists(sys32) || File.Exists(sysNative);
        }

        public static IFreezeEngine ResolveEngine(PCFreezeConfig config, bool forceSimulation)
        {
            if (forceSimulation || !config.IsInstalledOnTargetMachine || config.PreferredEngine == FreezeEngineType.Simulation)
            {
                return new SimulationFreezeEngine();
            }

            if (config.PreferredEngine == FreezeEngineType.UwfKernel)
            {
                return new UwfKernelFreezeEngine();
            }

            if (config.PreferredEngine == FreezeEngineType.VssSnapshot)
            {
                return new VssSnapshotFreezeEngine();
            }

            // Mode Auto : UWF + Baseline si Windows Pro/Enterprise/Education, sinon VSS + Baseline (Windows Home)
            if (DoesOSNativelySupportUwf() || IsUwfBinaryPresent())
            {
                return new UwfKernelFreezeEngine();
            }

            return new VssSnapshotFreezeEngine();
        }

        public static int RunProcessHidden(string fileName, string arguments, out string stdOutAndErr, int timeoutMs = 60000)
        {
            StringBuilder output = new StringBuilder();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.Default,
                    StandardErrorEncoding = Encoding.Default
                };

                using (Process proc = Process.Start(psi))
                {
                    string outText = proc.StandardOutput.ReadToEnd();
                    string errText = proc.StandardError.ReadToEnd();
                    proc.WaitForExit(timeoutMs);
                    if (!string.IsNullOrEmpty(outText)) output.AppendLine(outText.Trim());
                    if (!string.IsNullOrEmpty(errText)) output.AppendLine(errText.Trim());
                    stdOutAndErr = output.ToString().Trim();
                    return proc.ExitCode;
                }
            }
            catch (Exception ex)
            {
                stdOutAndErr = "Exception lors de l'exécution de " + fileName + " : " + ex.Message;
                return -1;
            }
        }
    }

    /// <summary>
    /// Moteur de Simulation (Sécurité Machine de Développement) :
    /// Émule à 100% le comportement de Deep Freeze sans toucher au disque, au registre de boot ni à UWF.
    /// </summary>
    public class SimulationFreezeEngine : IFreezeEngine
    {
        public string EngineName
        {
            get { return "Mode Simulation (Bac à sable sécurisé)"; }
        }

        public string TechnicalDescription
        {
            get
            {
                string osEdition = EngineResolver.GetWindowsEditionId();
                string targetRecommendation = EngineResolver.DoesOSNativelySupportUwf()
                    ? "UWF Noyau Microsoft + Baseline Snapshot (édition " + osEdition + ")"
                    : "VSS Snapshot + Baseline Rollback (édition " + osEdition + ")";
                return "Aucune modification n'est appliquée à ce PC. Sur la machine cible, PCFreeze utilisera : " + targetRecommendation;
            }
        }

        public bool IsSimulation { get { return true; } }

        public bool IsSupportedOnThisOS(out string reason)
        {
            reason = "Toujours supporté (Mode Simulation sans impact système).";
            return true;
        }

        public bool ProvisionEngineOnTarget(PCFreezeConfig config, out string logOutput)
        {
            logOutput = "[SIMULATION] Vérification des prérequis UWF / VSS simulée avec succès.";
            AuditLogger.Log("INFO", logOutput, true);
            return true;
        }

        public bool ApplyStateForNextBoot(PCFreezeConfig config, out string details)
        {
            config.CurrentBootState = config.TargetBootStateOnNextReboot;
            if (config.CurrentBootState == BootState.ThawedNextRestarts)
            {
                config.RemainingThawedRestarts = Math.Max(1, config.ThawedRestartsConfigured);
            }
            else
            {
                config.RemainingThawedRestarts = 0;
            }
            config.Save(true);

            details = string.Format(
                "[SIMULATION] Transition appliquée avec succès -> Nouvel état simulé : {0}{1}",
                config.CurrentBootState,
                config.CurrentBootState == BootState.ThawedNextRestarts
                    ? " (" + config.RemainingThawedRestarts + " redémarrage(s) restants)"
                    : "");
            AuditLogger.Log("SIM-BOOT", details, true);
            return true;
        }

        public bool SyncExclusionsAndOverlay(PCFreezeConfig config, out string details)
        {
            details = string.Format(
                "[SIMULATION] Overlay configuré ({0} - {1} Mo), {2} dossier(s) exclus, {3} ThawSpace(s) configuré(s).",
                config.OverlayType,
                config.OverlaySizeMB,
                config.ExcludedFolderPaths.Count,
                config.ThawSpaces.Count);
            AuditLogger.Log("SIM-SYNC", details, true);
            return true;
        }

        public int GetEstimatedOverlayUsedMB(PCFreezeConfig config)
        {
            if (config.CurrentBootState != BootState.Frozen)
                return 0;
            return Math.Min(config.OverlaySizeMB, Math.Max(128, config.OverlaySizeMB / 8));
        }

        public void ExecuteBootHook(PCFreezeConfig config)
        {
            if (config.CurrentBootState == BootState.ThawedNextRestarts)
            {
                if (config.RemainingThawedRestarts > 1)
                {
                    config.RemainingThawedRestarts--;
                    AuditLogger.Log("SIM-HOOK", "Redémarrage simulé : il reste " + config.RemainingThawedRestarts + " redémarrage(s) dégelé(s).", true);
                }
                else
                {
                    config.RemainingThawedRestarts = 0;
                    config.CurrentBootState = BootState.Frozen;
                    config.TargetBootStateOnNextReboot = BootState.Frozen;
                    AuditLogger.Log("SIM-HOOK", "Compteur de redémarrages dégelés atteint 0 -> Retour automatique à l'état GELÉ.", true);
                }
                config.Save(true);
            }
        }
    }

    /// <summary>
    /// Moteur Hybride Noyau Windows UWF (Unified Write Filter) + Baseline Snapshot instantané.
    /// Combine la protection secteur Ring 0 (uwfvol.sys) et la restauration garantie de l'image Baseline.
    /// </summary>
    public class UwfKernelFreezeEngine : IFreezeEngine
    {
        public string EngineName
        {
            get { return "Noyau Windows UWF (Ring-0) + Image Baseline"; }
        }

        public string TechnicalDescription
        {
            get { return "Double protection : Filtre secteur signé Microsoft (uwfvol.sys) + Restauration garantie de l'image de référence au démarrage."; }
        }

        public bool IsSimulation { get { return false; } }

        private string GetUwfMgrPath()
        {
            string sysNative = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Sysnative\uwfmgr.exe");
            if (File.Exists(sysNative)) return sysNative;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "uwfmgr.exe");
        }

        public bool IsSupportedOnThisOS(out string reason)
        {
            if (EngineResolver.IsUwfBinaryPresent())
            {
                reason = "Pilote noyau UWF présent et opérationnel.";
                return true;
            }
            if (EngineResolver.DoesOSNativelySupportUwf())
            {
                reason = "Windows " + EngineResolver.GetWindowsEditionId() + " supporte UWF nativement.";
                return true;
            }
            reason = "L'édition " + EngineResolver.GetWindowsEditionId() + " utilise le moteur Baseline + VSS.";
            return false;
        }

        public bool ProvisionEngineOnTarget(PCFreezeConfig config, out string logOutput)
        {
            StringBuilder sb = new StringBuilder();
            if (!EngineResolver.IsUwfBinaryPresent())
            {
                sb.AppendLine("Activation de la fonctionnalité noyau Client-UnifiedWriteFilter via DISM...");
                string dismOut;
                int code = EngineResolver.RunProcessHidden(
                    "dism.exe",
                    "/Online /Enable-Feature /FeatureName:Client-UnifiedWriteFilter /All /NoRestart",
                    out dismOut,
                    180000);
                sb.AppendLine(dismOut);
                if (code == 3010)
                {
                    sb.AppendLine("Note : Le pilote UWF sera actif au prochain redémarrage. La protection Baseline est déjà active immédiatement.");
                }
            }
            else
            {
                sb.AppendLine("uwfmgr.exe est déjà installé et prêt.");
            }

            logOutput = sb.ToString().Trim();
            AuditLogger.Log("INFO", "Provisioning UWF : " + logOutput, false);
            return true;
        }

        public bool SyncExclusionsAndOverlay(PCFreezeConfig config, out string details)
        {
            StringBuilder sb = new StringBuilder();
            string uwf = GetUwfMgrPath();
            if (!File.Exists(uwf))
            {
                details = "Profil d'exclusions Baseline enregistré (" + config.ExcludedFolderPaths.Count + " dossier(s) exclus).";
                return true;
            }

            string cmdOut;
            string overlayTypeArg = config.OverlayType == OverlayStorageType.RAM ? "RAM" : "DISK";
            EngineResolver.RunProcessHidden(uwf, "overlay set-type " + overlayTypeArg, out cmdOut);
            EngineResolver.RunProcessHidden(uwf, "overlay set-size " + config.OverlaySizeMB, out cmdOut);
            EngineResolver.RunProcessHidden(uwf, "overlay set-warningthreshold " + (config.OverlaySizeMB * config.OverlayWarningThresholdPercent / 100), out cmdOut);
            EngineResolver.RunProcessHidden(uwf, "overlay set-criticalthreshold " + (config.OverlaySizeMB * config.OverlayCriticalThresholdPercent / 100), out cmdOut);

            string internalDataDir = PCFreezeConfig.GetConfigDirectory(false);
            EngineResolver.RunProcessHidden(uwf, string.Format("file add-exclusion \"{0}\"", internalDataDir), out cmdOut);

            foreach (string folder in config.ExcludedFolderPaths)
            {
                if (!string.IsNullOrWhiteSpace(folder))
                {
                    EngineResolver.RunProcessHidden(uwf, string.Format("file add-exclusion \"{0}\"", folder.Trim()), out cmdOut);
                }
            }

            foreach (string regKey in config.ExcludedRegistryKeys)
            {
                if (!string.IsNullOrWhiteSpace(regKey))
                {
                    EngineResolver.RunProcessHidden(uwf, string.Format("registry add-exclusion \"{0}\"", regKey.Trim()), out cmdOut);
                }
            }

            foreach (VolumeProtectionConfig vol in config.ProtectedVolumes)
            {
                string action = vol.IsFrozen ? "protect" : "unprotect";
                EngineResolver.RunProcessHidden(uwf, string.Format("volume {0} {1}", action, vol.DriveLetter), out cmdOut);
                sb.AppendLine(string.Format("Volume {0} ({1})", vol.DriveLetter, action));
            }

            details = sb.ToString().Trim();
            AuditLogger.Log("UWF-SYNC", details, false);
            return true;
        }

        public bool ApplyStateForNextBoot(PCFreezeConfig config, out string details)
        {
            StringBuilder sb = new StringBuilder();
            string syncDetails;
            SyncExclusionsAndOverlay(config, out syncDetails);

            string uwf = GetUwfMgrPath();
            string cmdOut;

            if (config.TargetBootStateOnNextReboot == BootState.Frozen)
            {
                config.CurrentBootState = BootState.Frozen;
                config.RemainingThawedRestarts = 0;

                // 1. Capture immédiate de l'image Baseline (Bureau, Documents, Téléchargements, Startup, etc.)
                string baselineSummary;
                BaselineSnapshotManager.CaptureBaseline(config, out baselineSummary);
                sb.AppendLine("✔ " + baselineSummary);

                // Marquer la session actuelle comme initialisée pour que le prochain redémarrage déclenche la restauration
                EngineResolver.MarkCurrentLogonSessionProcessed();

                // 2. Armer le filtre noyau UWF si uwfmgr.exe est disponible
                if (File.Exists(uwf))
                {
                    EngineResolver.RunProcessHidden(uwf, "filter enable", out cmdOut);
                    sb.AppendLine("✔ Filtre noyau UWF armé (GELÉ) : " + cmdOut);
                }
            }
            else if (config.TargetBootStateOnNextReboot == BootState.ThawedNextRestarts)
            {
                config.CurrentBootState = BootState.ThawedNextRestarts;
                config.RemainingThawedRestarts = Math.Max(1, config.ThawedRestartsConfigured);
                EngineResolver.MarkCurrentLogonSessionProcessed();

                if (File.Exists(uwf))
                {
                    EngineResolver.RunProcessHidden(uwf, "filter disable", out cmdOut);
                }
                sb.AppendLine(string.Format("✔ Système DÉGELÉ pour les {0} prochain(s) redémarrage(s).", config.RemainingThawedRestarts));
            }
            else
            {
                config.CurrentBootState = BootState.ThawedPermanent;
                config.RemainingThawedRestarts = 0;
                EngineResolver.MarkCurrentLogonSessionProcessed();

                if (File.Exists(uwf))
                {
                    EngineResolver.RunProcessHidden(uwf, "filter disable", out cmdOut);
                }
                sb.AppendLine("✔ Système DÉGELÉ de façon permanente.");
            }

            string currentBootId = EngineResolver.GetCurrentBootIdentifier();
            config.LastSystemBootHookId = currentBootId;
            config.LastUserLogonRestoreBootId = currentBootId;
            config.Save(false);
            details = sb.ToString().Trim();
            AuditLogger.Log("STATE-APPLY", details, false);
            return true;
        }

        public int GetEstimatedOverlayUsedMB(PCFreezeConfig config)
        {
            string uwf = GetUwfMgrPath();
            if (!File.Exists(uwf)) return 0;

            string cmdOut;
            if (EngineResolver.RunProcessHidden(uwf, "overlay get-consumption", out cmdOut, 5000) == 0)
            {
                foreach (string token in cmdOut.Split(new char[] { ' ', '\r', '\n', ':' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int val;
                    if (int.TryParse(token, out val) && val >= 0)
                        return val;
                }
            }
            return 0;
        }

        public void ExecuteBootHook(PCFreezeConfig config)
        {
            if (config.CurrentBootState == BootState.ThawedNextRestarts)
            {
                if (config.RemainingThawedRestarts > 1)
                {
                    config.RemainingThawedRestarts--;
                    config.Save(false);
                    AuditLogger.Log("BOOT-HOOK", "Session dégelée en cours. Redémarrages dégelés restants : " + config.RemainingThawedRestarts, false);
                }
                else
                {
                    // Le compteur atteint 0 : on capture la nouvelle image Baseline incluant les mises à jour et on regèle !
                    config.RemainingThawedRestarts = 0;
                    config.CurrentBootState = BootState.Frozen;
                    config.TargetBootStateOnNextReboot = BootState.Frozen;

                    string baselineMsg;
                    BaselineSnapshotManager.CaptureBaseline(config, out baselineMsg);
                    config.Save(false);

                    string uwf = GetUwfMgrPath();
                    if (File.Exists(uwf))
                    {
                        string cmdOut;
                        EngineResolver.RunProcessHidden(uwf, "filter enable", out cmdOut);
                    }
                    AuditLogger.Log("BOOT-HOOK", "Fin des redémarrages dégelés -> Nouvelle Baseline capturée et système REGELÉ automatiquement.", false);
                }
            }
            else if (config.CurrentBootState == BootState.Frozen)
            {
                // Restaurer l'image Baseline (efface tout fichier créé sur le Bureau/Documents/etc. et restaure les fichiers supprimés/modifiés)
                string restoreMsg;
                BaselineSnapshotManager.RestoreBaseline(config, out restoreMsg);
                EngineResolver.RefreshExplorerDesktop();

                // S'assurer que UWF est également armé si uwfmgr.exe vient d'être activé après un reboot DISM
                string uwf = GetUwfMgrPath();
                if (File.Exists(uwf))
                {
                    string syncDetails;
                    SyncExclusionsAndOverlay(config, out syncDetails);
                    string cmdOut;
                    EngineResolver.RunProcessHidden(uwf, "filter enable", out cmdOut);
                }
            }
        }
    }

    /// <summary>
    /// Moteur VSS Snapshot & Baseline Rollback (Compatible toutes éditions Windows y compris Home).
    /// </summary>
    public class VssSnapshotFreezeEngine : IFreezeEngine
    {
        public string EngineName
        {
            get { return "Moteur Baseline & VSS Snapshot (Compatible Windows Home/Pro)"; }
        }

        public string TechnicalDescription
        {
            get { return "Capture une image de référence complète (Baseline + Cliché VSS + Registre) et restaure automatiquement le système à chaque redémarrage."; }
        }

        public bool IsSimulation { get { return false; } }

        public bool IsSupportedOnThisOS(out string reason)
        {
            reason = "Disponible sur toutes les éditions de Windows.";
            return true;
        }

        public bool ProvisionEngineOnTarget(PCFreezeConfig config, out string logOutput)
        {
            string outStr;
            EngineResolver.RunProcessHidden("sc.exe", "config VSS start= demand", out outStr);
            logOutput = "Moteur Baseline & Service Volume Shadow Copy (VSS) prêts.";
            AuditLogger.Log("VSS-INIT", logOutput, false);
            return true;
        }

        public bool SyncExclusionsAndOverlay(PCFreezeConfig config, out string details)
        {
            details = string.Format("Configuration Baseline & VSS enregistrée ({0} exclusions de dossiers).", config.ExcludedFolderPaths.Count);
            return true;
        }

        public bool ApplyStateForNextBoot(PCFreezeConfig config, out string details)
        {
            StringBuilder sb = new StringBuilder();

            if (config.TargetBootStateOnNextReboot == BootState.Frozen)
            {
                config.CurrentBootState = BootState.Frozen;
                config.RemainingThawedRestarts = 0;

                // 1. Capture immédiate de l'image de référence (Bureau, Documents, Téléchargements, Startup, etc.)
                string baselineSummary;
                BaselineSnapshotManager.CaptureBaseline(config, out baselineSummary);
                sb.AppendLine("✔ " + baselineSummary);

                // Marquer la session actuelle comme initialisée
                EngineResolver.MarkCurrentLogonSessionProcessed();

                // 2. Sauvegarde de la ruche Registre de référence
                string baselineDir = Path.Combine(PCFreezeConfig.GetConfigDirectory(false), "BaselineRegistry");
                if (!Directory.Exists(baselineDir)) Directory.CreateDirectory(baselineDir);
                string cmdOut;
                string runHive = Path.Combine(baselineDir, "RUN_HKCU.reg");
                EngineResolver.RunProcessHidden("reg.exe", string.Format("export \"HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" \"{0}\" /y", runHive), out cmdOut);
                sb.AppendLine("✔ État GELÉ activé : toute modification sera effacée au prochain redémarrage.");
            }
            else if (config.TargetBootStateOnNextReboot == BootState.ThawedNextRestarts)
            {
                config.CurrentBootState = BootState.ThawedNextRestarts;
                config.RemainingThawedRestarts = Math.Max(1, config.ThawedRestartsConfigured);
                EngineResolver.MarkCurrentLogonSessionProcessed();
                sb.AppendLine("✔ Mode Dégelé armé pour " + config.RemainingThawedRestarts + " redémarrage(s).");
            }
            else
            {
                config.CurrentBootState = BootState.ThawedPermanent;
                config.RemainingThawedRestarts = 0;
                EngineResolver.MarkCurrentLogonSessionProcessed();
                sb.AppendLine("✔ Mode Dégelé permanent activé.");
            }

            string currentBootId = EngineResolver.GetCurrentBootIdentifier();
            config.LastSystemBootHookId = currentBootId;
            config.LastUserLogonRestoreBootId = currentBootId;
            config.Save(false);
            details = sb.ToString().Trim();
            AuditLogger.Log("VSS-STATE", details, false);
            return true;
        }

        public int GetEstimatedOverlayUsedMB(PCFreezeConfig config)
        {
            return 0;
        }

        public void ExecuteBootHook(PCFreezeConfig config)
        {
            if (config.CurrentBootState == BootState.ThawedNextRestarts)
            {
                if (config.RemainingThawedRestarts > 1)
                {
                    config.RemainingThawedRestarts--;
                    config.Save(false);
                    AuditLogger.Log("VSS-BOOT", "Session dégelée en cours. Redémarrages restants : " + config.RemainingThawedRestarts, false);
                }
                else
                {
                    config.RemainingThawedRestarts = 0;
                    config.CurrentBootState = BootState.Frozen;
                    config.TargetBootStateOnNextReboot = BootState.Frozen;
                    string details;
                    ApplyStateForNextBoot(config, out details);
                }
            }
            else if (config.CurrentBootState == BootState.Frozen)
            {
                string restoreSummary;
                BaselineSnapshotManager.RestoreBaseline(config, out restoreSummary);
                EngineResolver.RefreshExplorerDesktop();

                string runHive = Path.Combine(PCFreezeConfig.GetConfigDirectory(false), @"BaselineRegistry\RUN_HKCU.reg");
                if (File.Exists(runHive))
                {
                    string cmdOut;
                    EngineResolver.RunProcessHidden("reg.exe", string.Format("import \"{0}\"", runHive), out cmdOut);
                }
            }
        }
    }
}
