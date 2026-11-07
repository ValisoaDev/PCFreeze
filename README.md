# ❄️ PCFreeze Workstation — Clone Natif de Faronics Deep Freeze

**PCFreeze** est une solution complète de protection système **« Reboot-to-Restore »** inspirée de [Faronics Deep Freeze](https://www.faronics.com/assets/DeepFreeze_fr.pdf), compilée en un exécutable Windows 64-bit natif autonome (`PCFreeze.exe`) **sans aucune dépendance externe à installer sur l'ordinateur cible**.

---

## 🕵️‍♂️ AIDE-MÉMOIRE ADMINISTRATEUR (Raccourcis Secrets & Désinstallation Cachée)

Pour empêcher tout utilisateur de désinstaller ou contourner la protection, **PCFreeze est 100 % invisible dans le Panneau de configuration Windows (`Applications installées`)**.

### 🔓 1. Ouvrir la Console d'Administration PCFreeze
- **Raccourci clavier secret** : **`Ctrl + Alt + Maj + F6`** *(fonctionne même si l'icône de la barre des tâches est masquée en Mode Furtif)*.
- **Souris** : **Double-clic** sur l'icône polaire dans la barre d'état système (System Tray).
- **Premier lancement** : Aucun mot de passe n'est défini par défaut (laissez le champ vide et cliquez sur **OK**, puis définissez votre mot de passe dans l'onglet **5. Mot de passe & Mode Furtif**).

---

### 🗑️ 2. Les 4 Moyens Cachés pour Désinstaller PCFreeze (Réservés à l'Admin)

#### Méthode Secrète A — Le Raccourci Clavier Furtif : `Ctrl + Alt + Maj + F12`
- Appuyez simultanément sur **`Ctrl + Alt + Maj + F12`** depuis n'importe où sous Windows.
- Saisissez votre mot de passe Administrateur (ou un jeton OTP) si un mot de passe est actif.
- Confirmez : PCFreeze désarme le gel et supprime intégralement ses fichiers (`C:\Program Files\PCFreeze`, `C:\ProgramData\PCFreeze`), ses tâches planifiées et ses clés de registre.

#### Méthode Secrète B — Le Mot-Clé `UNINSTALL` dans la boîte de mot de passe (`Ctrl + Alt + Maj + F6`)
Ouvrez la fenêtre d'authentification habituelle (`Ctrl + Alt + Maj + F6`) et tapez directement dans le champ de mot de passe :
- **`UNINSTALL`** *(si aucun mot de passe n'est défini)*
- **`UNINSTALL:VotreMotDePasse`** *(si un mot de passe est défini, ex: `UNINSTALL:Secret123`)*
- **`UNINSTALL:XXXX-XXXX`** *(avec un jeton OTP horaire)*
Cliquez sur **OK** : la désinstallation complète démarre immédiatement.

#### Méthode Secrète C — Commande directe via `Win + R` (Exécuter) ou CMD Admin
- Désinstallation graphique (demande le mot de passe si défini) :
  ```cmd
  "C:\Program Files\PCFreeze\PCFreeze.exe" --uninstall-gui
  ```
- Désinstallation silencieuse en ligne de commande :
  ```cmd
  "C:\Program Files\PCFreeze\PCFreeze.exe" --uninstall-target --password VotreMotDePasse
  ```

#### Méthode D — Depuis la Console PCFreeze
- Ouvrez la console (`Ctrl + Alt + Maj + F6`) $\rightarrow$ **Onglet 6 (Déploiement Cible & Logs)** $\rightarrow$ bouton **« Désinstaller le service de ce poste »** *(nécessite d'être en état Dégelé)*.

---

## 🛡️ Sécurité maximale pour votre ordinateur de développement

- Tant que le logiciel n'a pas été explicitement installé en tant que service sur l'ordinateur cible via `2-Installer-Sur-Ordinateur-Cible.bat`, il s'exécute automatiquement en **Mode Simulation (Bac à sable sécurisé)**.
- Vous pouvez lancer `dist\1-Tester-Mode-Simulation-Sans-Risque.bat` sur votre PC de développement pour tester les 6 onglets sans qu'aucune modification ne soit faite à vos disques ou à votre démarrage.

---

## ✨ Correspondance 1:1 avec Faronics Deep Freeze

| Fonctionnalité Faronics Deep Freeze | Implémentation dans **PCFreeze** |
| :--- | :--- |
| **3 États de contrôle de démarrage (Boot Control)** | **1. Démarrer Gelé** (restauration 100 % à chaque reboot)<br>**2. Démarrer Dégelé sur les `N` prochains redémarrages** (décrémente automatiquement à chaque boot puis regèle à `0` en capturant la nouvelle image)<br>**3. Démarrer Dégelé permanent** |
| **Double Protection (Noyau UWF + Image Baseline)** | • Capture et restaure automatiquement l'image exacte des dossiers utilisateurs (`Bureau`, `OneDrive\Bureau`, `Documents`, `Téléchargements`, `Images`, `Musique`, `Vidéos`, `Startup`) via `BaselineSnapshotManager`.<br>• Sur **Windows Pro / Enterprise / Education** : pilote également le filtre secteur noyau Microsoft **UWF (`uwfvol.sys`)** avec Overlay en **RAM** ou sur **Disque**.<br>• Désactive automatiquement le *Démarrage Rapide (Hiberboot)* Windows pour garantir la restauration à chaque arrêt/redémarrage. |
| **ThawSpace (Disque virtuel persistant)** | Création et montage automatique au boot d'un disque virtuel `.vhdx` persistant (ex: lecteur `T:` de 1 à 500 Go), avec option **Visible** ou **Caché dans l'Explorateur**. |
| **Data Igloo / Dossiers exclus du gel** | Exclusion directe de dossiers (`Mes Documents`, `Bureau`, dossiers personnalisés) et de clés de Registre pour conserver les données choisies même lorsque `C:` est gelé. |
| **Mode Furtif (Stealth Mode)** | • Appel via **`Ctrl + Alt + Maj + F6`**.<br>• Option **Mode Furtif** pour masquer complètement l'icône de la barre des tâches.<br>• **Clignotement rouge/ambre** de l'icône quand le PC est Dégelé. |
| **Mot de passe & Jetons à Usage Unique (OTP)** | • Mot de passe administrateur haché en **PBKDF2-SHA256** (50 000 itérations + sel aléatoire 256 bits).<br>• **Générateur de Jeton OTP Horaire (`XXXX-XXXX`)** basé sur `HMAC-SHA256` pour débloquer ou désinstaller un poste cible à distance sans divulguer le mot de passe maître. |
| **Fenêtres de Maintenance & Windows Update** | Planification par jour et plage horaire avec exécution automatique de **Windows Update**, script personnalisé, et **verrouillage plein écran Clavier/Souris** pendant la maintenance. |
| **Contrôle en ligne de commande (`DFC.exe`)** | Interface CLI complète intégrée à `PCFreeze.exe` (`/status`, `/freeze`, `/thaw`, `/thawnext <N>`). |

---

## 📦 Structure du projet

```text
d:\PCFreeze\
├── build.ps1                                     # Script de compilation C# 64-bit natif (csc.exe)
├── README.md                                     # Documentation & Aide-mémoire des raccourcis secrets
├── dist\                                         # Dossier prêt à copier sur clé USB / réseau vers le PC cible
│   ├── PCFreeze.exe                              # Exécutable autonome unique (GUI + Agent Tray + Service Boot + CLI)
│   ├── 1-Tester-Mode-Simulation-Sans-Risque.bat  # Pour tester l'interface sans rien toucher au système
│   └── 2-Installer-Sur-Ordinateur-Cible.bat      # À exécuter en Admin UNIQUEMENT sur l'ordinateur cible
└── src\
    ├── Program.cs                                # Point d'entrée (GUI, Tray, Boot-Hook, CLI DFC)
    ├── Models\PCFreezeConfig.cs                  # Configuration XML, états de démarrage et AuditLogger
    ├── Security\CryptoManager.cs                 # Hachage PBKDF2-SHA256 & Générateur de Jetons OTP horaires
    ├── Engine\BaselineSnapshotManager.cs         # Capture & restauration garantie des profils/Bureau/OneDrive
    ├── Engine\FreezeEngines.cs                   # Moteurs UWF Kernel, VSS Snapshot et Simulation
    ├── Engine\ThawSpaceAndDeployManager.cs       # Gestionnaire VHDX ThawSpace & Déploiement furtif
    └── UI\
        ├── PolarIcons.cs                         # Rendu vectoriel GDI+ des icônes Gelé/Dégelé & Écran de verrouillage
        ├── LoginDialog.cs                        # Boîte d'authentification Admin / OTP & mot-clé secret UNINSTALL
        ├── MainWindow.cs                         # Console d'administration à 6 onglets
        └── TrayAgentContext.cs                   # Agent System Tray + Raccourcis Ctrl+Alt+Maj+F6 et Ctrl+Alt+Maj+F12
```
