using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;

namespace PCFreeze.Models
{
    public enum BootState
    {
        Frozen = 0,
        ThawedNextRestarts = 1,
        ThawedPermanent = 2
    }

    public enum OverlayStorageType
    {
        RAM = 0,
        Disk = 1
    }

    public enum FreezeEngineType
    {
        Auto = 0,
        UwfKernel = 1,
        VssSnapshot = 2,
        Simulation = 3
    }

    [Serializable]
    public class VolumeProtectionConfig
    {
        public string DriveLetter { get; set; }
        public string VolumeLabel { get; set; }
        public long TotalSizeGB { get; set; }
        public long FreeSpaceGB { get; set; }
        public bool IsFrozen { get; set; }

        public VolumeProtectionConfig()
        {
            DriveLetter = "C:";
            VolumeLabel = "Système";
            IsFrozen = true;
        }
    }

    [Serializable]
    public class ThawSpaceDriveConfig
    {
        public string DriveLetter { get; set; }
        public int SizeGB { get; set; }
        public string BackingFilePath { get; set; }
        public bool IsEnabled { get; set; }
        public bool IsHiddenInExplorer { get; set; }
        public string Label { get; set; }

        public ThawSpaceDriveConfig()
        {
            DriveLetter = "T:";
            SizeGB = 10;
            BackingFilePath = @"C:\ProgramData\PCFreeze\ThawSpace_T.vhdx";
            IsEnabled = false;
            IsHiddenInExplorer = false;
            Label = "ThawSpace";
        }
    }

    [Serializable]
    public class MaintenanceScheduleItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DayOfWeek Day { get; set; }
        public string StartTime { get; set; } // Format "HH:mm"
        public string EndTime { get; set; }   // Format "HH:mm"
        public bool PerformWindowsUpdate { get; set; }
        public bool LockKeyboardAndMouse { get; set; }
        public bool ShutdownAfterMaintenance { get; set; }
        public string CustomCommand { get; set; }
        public bool IsEnabled { get; set; }

        public MaintenanceScheduleItem()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Name = "Maintenance Hebdomadaire";
            Day = DayOfWeek.Wednesday;
            StartTime = "02:00";
            EndTime = "04:00";
            PerformWindowsUpdate = true;
            LockKeyboardAndMouse = true;
            ShutdownAfterMaintenance = false;
            CustomCommand = "";
            IsEnabled = false;
        }
    }

    [Serializable]
    public class PCFreezeConfig
    {
        // Contrôle de démarrage (Boot Control)
        public BootState CurrentBootState { get; set; }
        public BootState TargetBootStateOnNextReboot { get; set; }
        public int ThawedRestartsConfigured { get; set; }
        public int RemainingThawedRestarts { get; set; }

        // Moteur & Overlay
        public FreezeEngineType PreferredEngine { get; set; }
        public OverlayStorageType OverlayType { get; set; }
        public int OverlaySizeMB { get; set; }
        public int OverlayWarningThresholdPercent { get; set; }
        public int OverlayCriticalThresholdPercent { get; set; }

        // Sécurité & Mode Furtif
        public string StationName { get; set; }
        public string PasswordHash { get; set; }
        public string PasswordSalt { get; set; }
        public string OtpSecretSeed { get; set; }
        public bool StealthModeHideTrayIcon { get; set; }
        public bool EnableGlobalHotkey { get; set; } // Ctrl+Alt+Shift+F6
        public bool BlinkTrayWhenThawed { get; set; }

        // État d'installation (Sécurité machine de développement vs machine cible)
        public bool IsInstalledOnTargetMachine { get; set; }
        public string ActiveBaselineSnapshotId { get; set; }
        public string LastSystemBootHookId { get; set; }
        public string LastUserLogonRestoreBootId { get; set; }
        public string CapturedDesktopSummary { get; set; }
        public string LastActionTimestamp { get; set; }

        // Listes de configuration
        public List<VolumeProtectionConfig> ProtectedVolumes { get; set; }
        public List<ThawSpaceDriveConfig> ThawSpaces { get; set; }
        public List<string> ExcludedFolderPaths { get; set; }
        public List<string> ExcludedRegistryKeys { get; set; }
        public List<MaintenanceScheduleItem> MaintenanceSchedules { get; set; }

        public PCFreezeConfig()
        {
            CurrentBootState = BootState.ThawedPermanent;
            TargetBootStateOnNextReboot = BootState.Frozen;
            ThawedRestartsConfigured = 1;
            RemainingThawedRestarts = 0;

            PreferredEngine = FreezeEngineType.Auto;
            OverlayType = OverlayStorageType.RAM;
            OverlaySizeMB = 4096;
            OverlayWarningThresholdPercent = 80;
            OverlayCriticalThresholdPercent = 90;

            StationName = Environment.MachineName;
            PasswordHash = ""; // Vide par défaut lors du 1er lancement (comme Deep Freeze)
            PasswordSalt = "";
            OtpSecretSeed = Guid.NewGuid().ToString("N").ToUpperInvariant();
            StealthModeHideTrayIcon = false;
            EnableGlobalHotkey = true;
            BlinkTrayWhenThawed = true;

            IsInstalledOnTargetMachine = false;
            ActiveBaselineSnapshotId = "";
            LastSystemBootHookId = "";
            LastUserLogonRestoreBootId = "";
            CapturedDesktopSummary = "Aucune capture effectuée.";
            LastActionTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

            ProtectedVolumes = new List<VolumeProtectionConfig>();
            ThawSpaces = new List<ThawSpaceDriveConfig>();
            ExcludedFolderPaths = new List<string>();
            ExcludedRegistryKeys = new List<string>
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\PCFreeze",
                @"HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Services\PCFreezeService"
            };
            MaintenanceSchedules = new List<MaintenanceScheduleItem>();
        }

        public static string GetConfigDirectory(bool simulationMode)
        {
            if (simulationMode)
            {
                string localDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "SimulationData");
                if (!Directory.Exists(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }
                return localDir;
            }

            string progData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "PCFreeze");
            if (!Directory.Exists(progData))
            {
                Directory.CreateDirectory(progData);
            }
            return progData;
        }

        public static string GetConfigFilePath(bool simulationMode)
        {
            return Path.Combine(GetConfigDirectory(simulationMode), "pcfreeze_config.xml");
        }

        public static PCFreezeConfig Load(bool forceSimulation)
        {
            string prodPath = GetConfigFilePath(false);
            string simPath = GetConfigFilePath(true);

            string targetPath = (!forceSimulation && File.Exists(prodPath)) ? prodPath : simPath;

            if (File.Exists(targetPath))
            {
                try
                {
                    XmlSerializer serializer = new XmlSerializer(typeof(PCFreezeConfig));
                    using (FileStream fs = new FileStream(targetPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        PCFreezeConfig cfg = (PCFreezeConfig)serializer.Deserialize(fs);
                        EnsureDefaultDrives(cfg);
                        return cfg;
                    }
                }
                catch (Exception ex)
                {
                    AuditLogger.Log("WARN", "Erreur de lecture de la configuration, chargement des valeurs par défaut : " + ex.Message, true);
                }
            }

            PCFreezeConfig defaultConfig = new PCFreezeConfig();
            EnsureDefaultDrives(defaultConfig);
            defaultConfig.ThawSpaces.Add(new ThawSpaceDriveConfig
            {
                DriveLetter = "T:",
                SizeGB = 10,
                Label = "ThawSpace",
                BackingFilePath = Path.Combine(GetConfigDirectory(forceSimulation), "ThawSpace_T.vhdx"),
                IsEnabled = false
            });
            defaultConfig.MaintenanceSchedules.Add(new MaintenanceScheduleItem());
            return defaultConfig;
        }

        public void Save(bool simulationMode)
        {
            LastActionTimestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string path = GetConfigFilePath(simulationMode);
            XmlSerializer serializer = new XmlSerializer(typeof(PCFreezeConfig));
            using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                serializer.Serialize(fs, this);
            }
        }

        private static void EnsureDefaultDrives(PCFreezeConfig cfg)
        {
            if (cfg.ProtectedVolumes != null && cfg.ProtectedVolumes.Count > 0)
                return;

            cfg.ProtectedVolumes = new List<VolumeProtectionConfig>();
            try
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.IsReady && d.DriveType == DriveType.Fixed)
                    {
                        string letter = d.Name.TrimEnd('\\');
                        cfg.ProtectedVolumes.Add(new VolumeProtectionConfig
                        {
                            DriveLetter = letter,
                            VolumeLabel = string.IsNullOrEmpty(d.VolumeLabel) ? "Disque local" : d.VolumeLabel,
                            TotalSizeGB = Math.Max(1, d.TotalSize / (1024L * 1024L * 1024L)),
                            FreeSpaceGB = Math.Max(0, d.AvailableFreeSpace / (1024L * 1024L * 1024L)),
                            IsFrozen = letter.Equals("C:", StringComparison.OrdinalIgnoreCase)
                        });
                    }
                }
            }
            catch
            {
                cfg.ProtectedVolumes.Add(new VolumeProtectionConfig
                {
                    DriveLetter = "C:",
                    VolumeLabel = "Système Windows",
                    TotalSizeGB = 256,
                    FreeSpaceGB = 120,
                    IsFrozen = true
                });
            }
        }
    }

    public static class AuditLogger
    {
        public static void Log(string level, string message, bool simulationMode)
        {
            try
            {
                string logDir = PCFreezeConfig.GetConfigDirectory(simulationMode);
                string logFile = Path.Combine(logDir, "pcfreeze_audit.log");
                string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] [{1}] {2}{3}",
                    DateTime.Now, level, message, Environment.NewLine);
                File.AppendAllText(logFile, line);
            }
            catch
            {
                // Ignore logging errors
            }
        }

        public static string ReadLogs(bool simulationMode)
        {
            try
            {
                string logFile = Path.Combine(PCFreezeConfig.GetConfigDirectory(simulationMode), "pcfreeze_audit.log");
                if (File.Exists(logFile))
                {
                    return File.ReadAllText(logFile);
                }
            }
            catch { }
            return "Aucun événement enregistré pour le moment.";
        }
    }
}
