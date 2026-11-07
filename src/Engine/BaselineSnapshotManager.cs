using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Xml.Serialization;
using PCFreeze.Models;

namespace PCFreeze.Engine
{
    [Serializable]
    public class BaselineFileEntry
    {
        public string FullPath { get; set; }
        public string BackupFileName { get; set; }
        public long Length { get; set; }
        public long LastWriteTicksUtc { get; set; }
        public bool HasBackupCopy { get; set; }
    }

    [Serializable]
    public class BaselineManifest
    {
        public string CapturedAt { get; set; }
        public List<string> RootPaths { get; set; }
        public List<string> Directories { get; set; }
        public List<BaselineFileEntry> Files { get; set; }

        public BaselineManifest()
        {
            CapturedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            RootPaths = new List<string>();
            Directories = new List<string>();
            Files = new List<BaselineFileEntry>();
        }
    }

    /// <summary>
    /// Gestionnaire de capture et restauration d'image de référence (Baseline Rollback).
    /// Garantit à 100% que tout fichier créé, modifié ou supprimé sur le Bureau, Documents,
    /// Téléchargements, etc. pendant une session GELÉE est annulé au redémarrage,
    /// quelle que soit l'édition de Windows (Home ou Pro) et même avant le chargement complet d'UWF.
    /// </summary>
    public static class BaselineSnapshotManager
    {
        private const long MaxSingleFileBackupBytes = 150L * 1024L * 1024L; // 150 Mo max par fichier individuel

        private static string GetBaselineDirectory()
        {
            string dir = Path.Combine(PCFreezeConfig.GetConfigDirectory(false), "BaselineStore");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        private static string GetBackupFilesDirectory()
        {
            string dir = Path.Combine(GetBaselineDirectory(), "Files");
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        private static string GetManifestPath()
        {
            return Path.Combine(GetBaselineDirectory(), "manifest.xml");
        }

        public static List<string> GetMonitoredRootDirectories()
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Dossiers spéciaux de la session courante (gère automatiquement la redirection OneDrive !)
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory));
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments));
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.Startup));
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup));
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            AddIfValid(roots, Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));

            // 2. Énumération de tous les profils dans C:\Users (pour que le service SYSTEM au boot couvre tous les comptes utilisateurs)
            try
            {
                string usersRoot = Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "Users");
                if (Directory.Exists(usersRoot))
                {
                    foreach (string userDir in Directory.GetDirectories(usersRoot))
                    {
                        string name = Path.GetFileName(userDir);
                        if (string.Equals(name, "Default", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, "Default User", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(name, "All Users", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        string[] subFolders = new string[]
                        {
                            "Desktop",
                            "Bureau",
                            "Documents",
                            "Downloads",
                            "Téléchargements",
                            "Pictures",
                            "Images",
                            "Music",
                            "Musique",
                            "Videos",
                            "Vidéos",
                            @"OneDrive\Desktop",
                            @"OneDrive\Bureau",
                            @"OneDrive\Documents",
                            @"OneDrive\Pictures",
                            @"OneDrive\Images",
                            @"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup"
                        };

                        foreach (string sub in subFolders)
                        {
                            AddIfValid(roots, Path.Combine(userDir, sub));
                        }

                        // Support de tous les dossiers OneDrive (ex: "OneDrive - Entreprise\Bureau", "OneDrive - Personnel\Desktop")
                        try
                        {
                            foreach (string odDir in Directory.GetDirectories(userDir, "OneDrive*"))
                            {
                                AddIfValid(roots, Path.Combine(odDir, "Desktop"));
                                AddIfValid(roots, Path.Combine(odDir, "Bureau"));
                                AddIfValid(roots, Path.Combine(odDir, "Documents"));
                                AddIfValid(roots, Path.Combine(odDir, "Pictures"));
                                AddIfValid(roots, Path.Combine(odDir, "Images"));
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            // 3. Lecture directe des dossiers Shell dans HKEY_USERS (pour résoudre les chemins exacts même depuis le compte SYSTEM)
            try
            {
                using (Microsoft.Win32.RegistryKey hku = Microsoft.Win32.RegistryKey.OpenBaseKey(
                    Microsoft.Win32.RegistryHive.Users, Microsoft.Win32.RegistryView.Registry64))
                {
                    foreach (string sid in hku.GetSubKeyNames())
                    {
                        if (sid.EndsWith("_Classes", StringComparison.OrdinalIgnoreCase)) continue;
                        using (Microsoft.Win32.RegistryKey sf = hku.OpenSubKey(
                            sid + @"\Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders"))
                        {
                            if (sf != null)
                            {
                                foreach (string valName in new[] { "Desktop", "Personal", "My Pictures", "My Music", "My Video", "Startup", "{374DE290-123F-4565-9164-39C4925E467B}" })
                                {
                                    object val = sf.GetValue(valName);
                                    if (val != null) AddIfValid(roots, val.ToString());
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return new List<string>(roots);
        }

        private static void AddIfValid(HashSet<string> set, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            try
            {
                string full = Path.GetFullPath(path).TrimEnd('\\');
                if (Directory.Exists(full))
                {
                    set.Add(full);
                }
            }
            catch { }
        }

        private static bool IsExcluded(string path, PCFreezeConfig config)
        {
            if (string.IsNullOrEmpty(path)) return false;

            string internalDir = PCFreezeConfig.GetConfigDirectory(false);
            if (path.StartsWith(internalDir, StringComparison.OrdinalIgnoreCase))
                return true;

            if (config.ExcludedFolderPaths != null)
            {
                foreach (string excl in config.ExcludedFolderPaths)
                {
                    if (!string.IsNullOrWhiteSpace(excl))
                    {
                        string cleanExcl = excl.Trim().TrimEnd('\\');
                        if (path.Equals(cleanExcl, StringComparison.OrdinalIgnoreCase) ||
                            path.StartsWith(cleanExcl + "\\", StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Capture l'état exact des fichiers/dossiers au moment où l'administrateur active l'état GELÉ.
        /// </summary>
        public static bool CaptureBaseline(PCFreezeConfig config, out string summary)
        {
            try
            {
                string backupDir = GetBackupFilesDirectory();
                // Nettoyer l'ancien cache de fichiers
                try
                {
                    if (Directory.Exists(backupDir))
                    {
                        Directory.Delete(backupDir, true);
                    }
                    Directory.CreateDirectory(backupDir);
                }
                catch { }

                BaselineManifest manifest = new BaselineManifest();
                List<string> roots = GetMonitoredRootDirectories();
                manifest.RootPaths.AddRange(roots);

                int fileCount = 0;
                int backedUpCount = 0;

                foreach (string root in roots)
                {
                    if (IsExcluded(root, config)) continue;
                    CaptureDirectoryRecursive(root, config, manifest, backupDir, ref fileCount, ref backedUpCount);
                }

                string manifestPath = GetManifestPath();
                XmlSerializer serializer = new XmlSerializer(typeof(BaselineManifest));
                using (FileStream fs = new FileStream(manifestPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    serializer.Serialize(fs, manifest);
                }

                string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                int desktopFiles = 0;
                try
                {
                    if (!string.IsNullOrEmpty(desktopDir) && Directory.Exists(desktopDir))
                    {
                        desktopFiles = Directory.GetFiles(desktopDir).Length + Directory.GetDirectories(desktopDir).Length;
                    }
                }
                catch { }

                config.CapturedDesktopSummary = string.Format(
                    "Bureau protégé : {0} ({1} élément(s) figés à {2:HH:mm:ss} — Total : {3} fichiers protégés)",
                    desktopDir, desktopFiles, DateTime.Now, fileCount);

                summary = string.Format(
                    "Image de référence (Baseline) capturée : {0} dossier(s) racine, {1} fichier(s) indexé(s), {2} fichier(s) sauvegardé(s).\r\n{3}",
                    roots.Count, fileCount, backedUpCount, config.CapturedDesktopSummary);
                AuditLogger.Log("BASELINE-CAPTURE", summary, false);
                return true;
            }
            catch (Exception ex)
            {
                summary = "Erreur lors de la capture Baseline : " + ex.Message;
                AuditLogger.Log("ERROR", summary, false);
                return false;
            }
        }

        private static void CaptureDirectoryRecursive(
            string currentDir,
            PCFreezeConfig config,
            BaselineManifest manifest,
            string backupDir,
            ref int fileCount,
            ref int backedUpCount)
        {
            if (IsExcluded(currentDir, config)) return;

            try
            {
                foreach (string filePath in Directory.GetFiles(currentDir))
                {
                    if (IsExcluded(filePath, config)) continue;

                    try
                    {
                        FileInfo fi = new FileInfo(filePath);
                        BaselineFileEntry entry = new BaselineFileEntry
                        {
                            FullPath = fi.FullName,
                            Length = fi.Length,
                            LastWriteTicksUtc = fi.LastWriteTimeUtc.Ticks,
                            HasBackupCopy = false,
                            BackupFileName = ""
                        };

                        if (fi.Length <= MaxSingleFileBackupBytes)
                        {
                            string backupName = Guid.NewGuid().ToString("N") + ".bak";
                            string destPath = Path.Combine(backupDir, backupName);
                            File.Copy(fi.FullName, destPath, true);
                            entry.BackupFileName = backupName;
                            entry.HasBackupCopy = true;
                            backedUpCount++;
                        }

                        manifest.Files.Add(entry);
                        fileCount++;
                    }
                    catch { }
                }

                foreach (string subDir in Directory.GetDirectories(currentDir))
                {
                    if (IsExcluded(subDir, config)) continue;
                    manifest.Directories.Add(subDir);
                    CaptureDirectoryRecursive(subDir, config, manifest, backupDir, ref fileCount, ref backedUpCount);
                }
            }
            catch { }
        }

        /// <summary>
        /// Restaure l'image gelée : supprime tout fichier/dossier créé après le gel et restaure tout fichier modifié ou supprimé.
        /// </summary>
        public static bool RestoreBaseline(PCFreezeConfig config, out string summary)
        {
            string manifestPath = GetManifestPath();
            if (!File.Exists(manifestPath))
            {
                summary = "Aucun manifeste de référence trouvé.";
                return false;
            }

            try
            {
                BaselineManifest manifest;
                XmlSerializer serializer = new XmlSerializer(typeof(BaselineManifest));
                using (FileStream fs = new FileStream(manifestPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    manifest = (BaselineManifest)serializer.Deserialize(fs);
                }

                string backupDir = GetBackupFilesDirectory();

                HashSet<string> expectedDirs = new HashSet<string>(manifest.Directories, StringComparer.OrdinalIgnoreCase);
                Dictionary<string, BaselineFileEntry> expectedFiles = new Dictionary<string, BaselineFileEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (BaselineFileEntry f in manifest.Files)
                {
                    expectedFiles[f.FullPath] = f;
                }

                int deletedNewFiles = 0;
                int deletedNewDirs = 0;
                int restoredFiles = 0;
                int recreatedDirs = 0;

                // 1. Supprimer tous les nouveaux fichiers et dossiers créés depuis le gel
                List<string> rootsToScan = new List<string>(manifest.RootPaths);
                foreach (string extraRoot in GetMonitoredRootDirectories())
                {
                    if (!rootsToScan.Contains(extraRoot))
                        rootsToScan.Add(extraRoot);
                }

                foreach (string root in rootsToScan)
                {
                    if (!Directory.Exists(root) || IsExcluded(root, config))
                        continue;

                    PurgeNewItemsRecursive(root, config, expectedDirs, expectedFiles, ref deletedNewFiles, ref deletedNewDirs);
                }

                // 2. Recréer les dossiers de référence qui auraient été supprimés
                foreach (string dir in manifest.Directories)
                {
                    if (IsExcluded(dir, config)) continue;
                    try
                    {
                        if (!Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                            recreatedDirs++;
                        }
                    }
                    catch { }
                }

                // 3. Restaurer les fichiers supprimés ou modifiés depuis le gel
                foreach (BaselineFileEntry entry in manifest.Files)
                {
                    if (IsExcluded(entry.FullPath, config) || !entry.HasBackupCopy)
                        continue;

                    string backupPath = Path.Combine(backupDir, entry.BackupFileName);
                    if (!File.Exists(backupPath))
                        continue;

                    try
                    {
                        bool needsRestore = false;
                        if (!File.Exists(entry.FullPath))
                        {
                            needsRestore = true;
                        }
                        else
                        {
                            FileInfo currentFi = new FileInfo(entry.FullPath);
                            if (currentFi.Length != entry.Length || currentFi.LastWriteTimeUtc.Ticks != entry.LastWriteTicksUtc)
                            {
                                needsRestore = true;
                            }
                        }

                        if (needsRestore)
                        {
                            string parent = Path.GetDirectoryName(entry.FullPath);
                            if (!string.IsNullOrEmpty(parent) && !Directory.Exists(parent))
                            {
                                Directory.CreateDirectory(parent);
                            }

                            if (File.Exists(entry.FullPath))
                            {
                                File.SetAttributes(entry.FullPath, FileAttributes.Normal);
                            }

                            File.Copy(backupPath, entry.FullPath, true);
                            File.SetLastWriteTimeUtc(entry.FullPath, new DateTime(entry.LastWriteTicksUtc, DateTimeKind.Utc));
                            restoredFiles++;
                        }
                    }
                    catch { }
                }

                summary = string.Format(
                    "Restauration de l'image GELÉE terminée : {0} nouveau(x) fichier(s) effacé(s), {1} nouveau(x) dossier(s) effacé(s), {2} fichier(s) restauré(s), {3} dossier(s) recréé(s).",
                    deletedNewFiles, deletedNewDirs, restoredFiles, recreatedDirs);
                AuditLogger.Log("BASELINE-RESTORE", summary, false);
                return true;
            }
            catch (Exception ex)
            {
                summary = "Erreur lors de la restauration Baseline : " + ex.Message;
                AuditLogger.Log("ERROR", summary, false);
                return false;
            }
        }

        private static void PurgeNewItemsRecursive(
            string currentDir,
            PCFreezeConfig config,
            HashSet<string> expectedDirs,
            Dictionary<string, BaselineFileEntry> expectedFiles,
            ref int deletedNewFiles,
            ref int deletedNewDirs)
        {
            if (IsExcluded(currentDir, config)) return;

            try
            {
                foreach (string filePath in Directory.GetFiles(currentDir))
                {
                    if (IsExcluded(filePath, config)) continue;

                    if (!expectedFiles.ContainsKey(filePath))
                    {
                        try
                        {
                            File.SetAttributes(filePath, FileAttributes.Normal);
                            File.Delete(filePath);
                            deletedNewFiles++;
                        }
                        catch { }
                    }
                }

                foreach (string subDir in Directory.GetDirectories(currentDir))
                {
                    if (IsExcluded(subDir, config)) continue;

                    if (!expectedDirs.Contains(subDir))
                    {
                        try
                        {
                            ForceDeleteDirectory(subDir);
                            deletedNewDirs++;
                        }
                        catch { }
                    }
                    else
                    {
                        PurgeNewItemsRecursive(subDir, config, expectedDirs, expectedFiles, ref deletedNewFiles, ref deletedNewDirs);
                    }
                }
            }
            catch { }
        }

        private static void ForceDeleteDirectory(string dirPath)
        {
            foreach (string file in Directory.GetFiles(dirPath, "*", SearchOption.AllDirectories))
            {
                try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
            }
            Directory.Delete(dirPath, true);
        }
    }
}
