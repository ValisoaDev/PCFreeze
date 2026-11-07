using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using PCFreeze.Engine;
using PCFreeze.Models;
using PCFreeze.Security;

namespace PCFreeze.UI
{
    public class MainWindow : Form
    {
        private PCFreezeConfig _config;
        private IFreezeEngine _engine;
        private readonly bool _forceSimulation;

        // En-tête dynamique
        private Panel _headerPanel;
        private PictureBox _picStatusIcon;
        private Label _lblStatusHeadline;
        private Label _lblEngineBadge;
        private Label _lblSafetyBanner;

        // Onglet 1 : Contrôle de démarrage
        private RadioButton _rbBootFrozen;
        private RadioButton _rbBootThawedNext;
        private NumericUpDown _nudThawedRestarts;
        private RadioButton _rbBootThawedPermanent;
        private Label _lblCurrentStateSummary;

        // Onglet 2 : Lecteurs & Overlay
        private ListView _lvVolumes;
        private RadioButton _rbOverlayRam;
        private RadioButton _rbOverlayDisk;
        private NumericUpDown _nudOverlaySizeMB;
        private ProgressBar _pbOverlayUsage;
        private Label _lblOverlayUsageText;

        // Onglet 3 : ThawSpace & Exclusions
        private ComboBox _cbThawLetter;
        private NumericUpDown _nudThawSizeGB;
        private CheckBox _chkThawEnabled;
        private CheckBox _chkThawHidden;
        private ListBox _lstExcludedFolders;

        // Onglet 4 : Maintenance
        private ListView _lvMaintenance;
        private ComboBox _cbMaintDay;
        private TextBox _txtMaintStart;
        private TextBox _txtMaintEnd;
        private CheckBox _chkMaintWinUpdate;
        private CheckBox _chkMaintLockInput;
        private CheckBox _chkMaintShutdown;
        private TextBox _txtMaintCustomCmd;

        // Onglet 5 : Sécurité & OTP
        private TextBox _txtNewPass;
        private TextBox _txtConfirmPass;
        private CheckBox _chkStealthTray;
        private CheckBox _chkBlinkTray;
        private TextBox _txtStationName;
        private TextBox _txtOtpSeed;
        private Label _lblGeneratedOtp;

        // Onglet 6 : Déploiement & Logs
        private TextBox _txtAuditLog;
        private Label _lblInstallStatus;

        public event EventHandler ConfigurationUpdated;

        public MainWindow(PCFreezeConfig config, bool forceSimulation)
        {
            _config = config;
            _forceSimulation = forceSimulation;
            _engine = EngineResolver.ResolveEngine(_config, _forceSimulation);
            InitializeComponent();
            LoadConfigIntoControls();
        }

        private void InitializeComponent()
        {
            this.Text = "PCFreeze Workstation v2.0 — Console de Protection Système (Reboot-to-Restore)";
            this.Size = new Size(920, 690);
            this.MinimumSize = new Size(880, 650);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.ForeColor = Color.FromArgb(241, 245, 249);
            this.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            this.Icon = PolarIcons.CreateStateIcon(_config.CurrentBootState == BootState.Frozen);

            // 1. Bandeau supérieur d'état
            _headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 96,
                BackColor = Color.FromArgb(30, 41, 59)
            };

            _picStatusIcon = new PictureBox
            {
                Size = new Size(64, 64),
                Location = new Point(20, 16),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            _lblStatusHeadline = new Label
            {
                Font = new Font("Segoe UI", 15f, FontStyle.Bold),
                Location = new Point(98, 12),
                AutoSize = true
            };

            _lblEngineBadge = new Label
            {
                Font = new Font("Segoe UI", 9.2f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(100, 42),
                AutoSize = true
            };

            _lblSafetyBanner = new Label
            {
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                Location = new Point(100, 66),
                AutoSize = true
            };

            _headerPanel.Controls.Add(_picStatusIcon);
            _headerPanel.Controls.Add(_lblStatusHeadline);
            _headerPanel.Controls.Add(_lblEngineBadge);
            _headerPanel.Controls.Add(_lblSafetyBanner);

            // 2. Barre de boutons inférieure
            Panel footerPanel = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 62,
                BackColor = Color.FromArgb(30, 41, 59)
            };

            Button btnApplyOnly = CreateStyledButton("Enregistrer la configuration", Color.FromArgb(14, 165, 233), new Point(20, 13), new Size(210, 36));
            btnApplyOnly.Click += OnSaveAndApplyClick;

            Button btnSimulateReboot = CreateStyledButton("🔄 Simuler un Redémarrage (Test)", Color.FromArgb(16, 185, 129), new Point(242, 13), new Size(245, 36));
            btnSimulateReboot.Click += OnSimulateRebootClick;

            Button btnApplyAndReboot = CreateStyledButton("⚡ Appliquer et Redémarrer le PC", Color.FromArgb(234, 88, 12), new Point(500, 13), new Size(235, 36));
            btnApplyAndReboot.Click += OnApplyAndRealRebootClick;

            Button btnClose = CreateStyledButton("Fermer", Color.FromArgb(71, 85, 105), new Point(750, 13), new Size(130, 36));
            btnClose.Click += (s, e) => this.Close();

            footerPanel.Controls.Add(btnApplyOnly);
            footerPanel.Controls.Add(btnSimulateReboot);
            footerPanel.Controls.Add(btnApplyAndReboot);
            footerPanel.Controls.Add(btnClose);

            // 3. Contrôle d'onglets principal (Les 6 onglets de Deep Freeze)
            TabControl tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                Padding = new Point(12, 6)
            };

            tabControl.TabPages.Add(BuildBootControlTab());
            tabControl.TabPages.Add(BuildDrivesAndOverlayTab());
            tabControl.TabPages.Add(BuildThawSpaceAndExclusionsTab());
            tabControl.TabPages.Add(BuildMaintenanceTab());
            tabControl.TabPages.Add(BuildSecurityAndOtpTab());
            tabControl.TabPages.Add(BuildDeployAndLogsTab());

            this.Controls.Add(tabControl);
            this.Controls.Add(footerPanel);
            this.Controls.Add(_headerPanel);
        }

        private TabPage BuildBootControlTab()
        {
            TabPage page = CreateDarkTabPage("1. Contrôle de démarrage");

            GroupBox grpBoot = CreateGroupBox("État au prochain redémarrage (Boot Control — Principe Deep Freeze)", new Point(20, 18), new Size(840, 290));

            _rbBootFrozen = new RadioButton
            {
                Text = "❄️  Démarrer Gelé (Boot Frozen)",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(28, 38),
                AutoSize = true
            };
            Label lblFrozenDesc = new Label
            {
                Text = "Protège intégralement le système. Toute modification effectuée pendant la session (logiciels installés,\r\nfichiers supprimés, virus, malwares, modifications du Registre) est effacée à 100% au redémarrage.",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(54, 65),
                Size = new Size(760, 42)
            };

            _rbBootThawedNext = new RadioButton
            {
                Text = "🔄  Démarrer Dégelé sur les",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 191, 36),
                Location = new Point(28, 122),
                AutoSize = true
            };

            _nudThawedRestarts = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 99,
                Value = 1,
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                Location = new Point(275, 120),
                Size = new Size(65, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };

            Label lblNextRestartsSuffix = new Label
            {
                Text = "prochains redémarrages (puis regeler automatiquement)",
                Font = new Font("Segoe UI", 10.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 191, 36),
                Location = new Point(348, 123),
                AutoSize = true
            };

            Label lblThawedNextDesc = new Label
            {
                Text = "Idéal pour installer un logiciel ou appliquer des mises à jour Windows : le poste reste dégelé pendant N\r\nredémarrages puis repasse automatiquement à l'état GELÉ sans intervention humaine.",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(54, 152),
                Size = new Size(760, 42)
            };

            _rbBootThawedPermanent = new RadioButton
            {
                Text = "🔥  Démarrer Dégelé en permanence (Boot Thawed)",
                Font = new Font("Segoe UI", 11f, FontStyle.Bold),
                ForeColor = Color.FromArgb(248, 113, 113),
                Location = new Point(28, 206),
                AutoSize = true
            };

            Label lblThawedPermDesc = new Label
            {
                Text = "Désactive la protection jusqu'à ce que l'administrateur regèle manuellement le poste. Toutes les modifications\r\neffectuées sont enregistrées de façon permanente sur le disque.",
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(54, 234),
                Size = new Size(760, 42)
            };

            grpBoot.Controls.Add(_rbBootFrozen);
            grpBoot.Controls.Add(lblFrozenDesc);
            grpBoot.Controls.Add(_rbBootThawedNext);
            grpBoot.Controls.Add(_nudThawedRestarts);
            grpBoot.Controls.Add(lblNextRestartsSuffix);
            grpBoot.Controls.Add(lblThawedNextDesc);
            grpBoot.Controls.Add(_rbBootThawedPermanent);
            grpBoot.Controls.Add(lblThawedPermDesc);

            GroupBox grpSummary = CreateGroupBox("Diagnostic en temps réel de la station", new Point(20, 322), new Size(840, 145));
            _lblCurrentStateSummary = new Label
            {
                Font = new Font("Consolas", 9.8f, FontStyle.Regular),
                ForeColor = Color.FromArgb(125, 211, 252),
                Location = new Point(22, 30),
                Size = new Size(795, 100)
            };
            grpSummary.Controls.Add(_lblCurrentStateSummary);

            page.Controls.Add(grpBoot);
            page.Controls.Add(grpSummary);
            return page;
        }

        private TabPage BuildDrivesAndOverlayTab()
        {
            TabPage page = CreateDarkTabPage("2. Disques & Overlay");

            GroupBox grpDrives = CreateGroupBox("Volumes locaux à geler (Frozen Drives)", new Point(20, 18), new Size(840, 215));

            _lvVolumes = new ListView
            {
                Location = new Point(20, 30),
                Size = new Size(800, 165),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular)
            };
            _lvVolumes.Columns.Add("Geler (Protéger)", 135);
            _lvVolumes.Columns.Add("Lecteur", 90);
            _lvVolumes.Columns.Add("Nom du volume", 220);
            _lvVolumes.Columns.Add("Capacité totale", 150);
            _lvVolumes.Columns.Add("Espace libre", 150);

            grpDrives.Controls.Add(_lvVolumes);

            GroupBox grpOverlay = CreateGroupBox("Configuration du Cache Overlay UWF (Stockage des écritures temporaires)", new Point(20, 248), new Size(840, 220));

            _rbOverlayRam = new RadioButton
            {
                Text = "Overlay en Mémoire Vive (RAM) — Ultra-rapide, 0 écriture sur le SSD (Recommandé)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(24, 34),
                AutoSize = true
            };

            _rbOverlayDisk = new RadioButton
            {
                Text = "Overlay sur Disque — Permet des sessions très lourdes (jusqu'à 64 Go de modifications temporaires)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(24, 66),
                AutoSize = true
            };

            Label lblOverlaySize = new Label
            {
                Text = "Taille maximale de l'Overlay (Mo) :",
                Location = new Point(24, 108),
                AutoSize = true
            };

            _nudOverlaySizeMB = new NumericUpDown
            {
                Minimum = 512,
                Maximum = 65536,
                Increment = 512,
                Value = 4096,
                Location = new Point(255, 105),
                Size = new Size(110, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };

            Label lblUsageTitle = new Label
            {
                Text = "Occupation actuelle de l'Overlay :",
                Location = new Point(24, 152),
                AutoSize = true
            };

            _pbOverlayUsage = new ProgressBar
            {
                Location = new Point(255, 150),
                Size = new Size(360, 24),
                Minimum = 0,
                Maximum = 100
            };

            _lblOverlayUsageText = new Label
            {
                Text = "0 Mo / 4096 Mo (0%)",
                Location = new Point(628, 152),
                AutoSize = true,
                ForeColor = Color.FromArgb(125, 211, 252)
            };

            grpOverlay.Controls.Add(_rbOverlayRam);
            grpOverlay.Controls.Add(_rbOverlayDisk);
            grpOverlay.Controls.Add(lblOverlaySize);
            grpOverlay.Controls.Add(_nudOverlaySizeMB);
            grpOverlay.Controls.Add(lblUsageTitle);
            grpOverlay.Controls.Add(_pbOverlayUsage);
            grpOverlay.Controls.Add(_lblOverlayUsageText);

            page.Controls.Add(grpDrives);
            page.Controls.Add(grpOverlay);
            return page;
        }

        private TabPage BuildThawSpaceAndExclusionsTab()
        {
            TabPage page = CreateDarkTabPage("3. ThawSpace & Exclusions");

            // Partie 1 : Disque Virtuel ThawSpace (.vhdx)
            GroupBox grpThaw = CreateGroupBox("ThawSpace — Disque Virtuel Persistant (comme dans Deep Freeze)", new Point(20, 18), new Size(840, 185));

            _chkThawEnabled = new CheckBox
            {
                Text = "Activer et monter automatiquement le disque virtuel ThawSpace au démarrage",
                Font = new Font("Segoe UI", 9.8f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(24, 32),
                AutoSize = true
            };

            Label lblLetter = new Label
            {
                Text = "Lettre du lecteur :",
                Location = new Point(24, 72),
                AutoSize = true
            };

            _cbThawLetter = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(150, 68),
                Size = new Size(80, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };
            foreach (string l in new[] { "T:", "U:", "V:", "W:", "X:", "Y:", "Z:" })
                _cbThawLetter.Items.Add(l);
            _cbThawLetter.SelectedIndex = 0;

            Label lblSize = new Label
            {
                Text = "Taille du ThawSpace (Go) :",
                Location = new Point(260, 72),
                AutoSize = true
            };

            _nudThawSizeGB = new NumericUpDown
            {
                Minimum = 1,
                Maximum = 500,
                Value = 10,
                Location = new Point(440, 68),
                Size = new Size(80, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };

            _chkThawHidden = new CheckBox
            {
                Text = "Masquer ce lecteur dans l'Explorateur Windows (Mode Furtif)",
                Location = new Point(545, 70),
                AutoSize = true
            };

            Button btnMountNow = CreateStyledButton("Monter / Tester le ThawSpace maintenant", Color.FromArgb(14, 165, 233), new Point(24, 118), new Size(310, 36));
            btnMountNow.Click += OnMountThawSpaceClick;

            Button btnUnmountNow = CreateStyledButton("Démonter le ThawSpace", Color.FromArgb(71, 85, 105), new Point(350, 118), new Size(210, 36));
            btnUnmountNow.Click += OnUnmountThawSpaceClick;

            grpThaw.Controls.Add(_chkThawEnabled);
            grpThaw.Controls.Add(lblLetter);
            grpThaw.Controls.Add(_cbThawLetter);
            grpThaw.Controls.Add(lblSize);
            grpThaw.Controls.Add(_nudThawSizeGB);
            grpThaw.Controls.Add(_chkThawHidden);
            grpThaw.Controls.Add(btnMountNow);
            grpThaw.Controls.Add(btnUnmountNow);

            // Partie 2 : Exclusions de dossiers (Data Igloo / UWF File Exclusions)
            GroupBox grpExcl = CreateGroupBox("Dossiers exclus du gel (Data Igloo — Les fichiers dans ces dossiers sont conservés)", new Point(20, 215), new Size(840, 255));

            _lstExcludedFolders = new ListBox
            {
                Location = new Point(24, 32),
                Size = new Size(570, 198),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f)
            };

            Button btnAddDocs = CreateStyledButton("+ Mes Documents", Color.FromArgb(30, 64, 175), new Point(612, 32), new Size(205, 34));
            btnAddDocs.Click += (s, e) => AddExcludedPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

            Button btnAddDesktop = CreateStyledButton("+ Bureau (Desktop)", Color.FromArgb(30, 64, 175), new Point(612, 74), new Size(205, 34));
            btnAddDesktop.Click += (s, e) => AddExcludedPath(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory));

            Button btnAddBrowse = CreateStyledButton("+ Parcourir un dossier...", Color.FromArgb(14, 165, 233), new Point(612, 116), new Size(205, 34));
            btnAddBrowse.Click += OnBrowseExcludedFolderClick;

            Button btnRemoveExcl = CreateStyledButton("Supprimer la sélection", Color.FromArgb(185, 28, 28), new Point(612, 194), new Size(205, 34));
            btnRemoveExcl.Click += (s, e) =>
            {
                if (_lstExcludedFolders.SelectedIndex >= 0)
                    _lstExcludedFolders.Items.RemoveAt(_lstExcludedFolders.SelectedIndex);
            };

            grpExcl.Controls.Add(_lstExcludedFolders);
            grpExcl.Controls.Add(btnAddDocs);
            grpExcl.Controls.Add(btnAddDesktop);
            grpExcl.Controls.Add(btnAddBrowse);
            grpExcl.Controls.Add(btnRemoveExcl);

            page.Controls.Add(grpThaw);
            page.Controls.Add(grpExcl);
            return page;
        }

        private TabPage BuildMaintenanceTab()
        {
            TabPage page = CreateDarkTabPage("4. Maintenance & Windows Update");

            GroupBox grpList = CreateGroupBox("Fenêtres de maintenance planifiées (Dégel auto -> Mises à jour -> Regel)", new Point(20, 18), new Size(840, 210));

            _lvMaintenance = new ListView
            {
                Location = new Point(20, 30),
                Size = new Size(800, 160),
                View = View.Details,
                CheckBoxes = true,
                FullRowSelect = true,
                GridLines = true,
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };
            _lvMaintenance.Columns.Add("Actif", 70);
            _lvMaintenance.Columns.Add("Nom", 180);
            _lvMaintenance.Columns.Add("Jour", 110);
            _lvMaintenance.Columns.Add("Horaire", 120);
            _lvMaintenance.Columns.Add("Windows Update", 120);
            _lvMaintenance.Columns.Add("Verrou Clavier/Souris", 150);

            grpList.Controls.Add(_lvMaintenance);

            GroupBox grpAdd = CreateGroupBox("Ajouter une fenêtre de maintenance ou tester le verrouillage", new Point(20, 240), new Size(840, 230));

            Label lblDay = new Label { Text = "Jour :", Location = new Point(24, 36), AutoSize = true };
            _cbMaintDay = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(80, 32),
                Size = new Size(140, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };
            foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek)))
                _cbMaintDay.Items.Add(d);
            _cbMaintDay.SelectedIndex = 3;

            Label lblStart = new Label { Text = "Début (HH:mm) :", Location = new Point(245, 36), AutoSize = true };
            _txtMaintStart = new TextBox { Text = "02:00", Location = new Point(365, 33), Size = new Size(70, 26), BackColor = Color.FromArgb(15, 23, 42), ForeColor = Color.White };

            Label lblEnd = new Label { Text = "Fin (HH:mm) :", Location = new Point(455, 36), AutoSize = true };
            _txtMaintEnd = new TextBox { Text = "04:00", Location = new Point(555, 33), Size = new Size(70, 26), BackColor = Color.FromArgb(15, 23, 42), ForeColor = Color.White };

            _chkMaintWinUpdate = new CheckBox { Text = "Exécuter Windows Update automatiquement", Checked = true, Location = new Point(24, 78), AutoSize = true };
            _chkMaintLockInput = new CheckBox { Text = "Verrouiller Clavier & Souris pendant la maintenance", Checked = true, Location = new Point(365, 78), AutoSize = true };
            _chkMaintShutdown = new CheckBox { Text = "Éteindre le PC une fois la maintenance terminée", Checked = false, Location = new Point(24, 110), AutoSize = true };

            Label lblCmd = new Label { Text = "Script / Commande batch post-update :", Location = new Point(24, 145), AutoSize = true };
            _txtMaintCustomCmd = new TextBox
            {
                Text = "winget upgrade --all --silent",
                Location = new Point(290, 142),
                Size = new Size(520, 26),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };

            Button btnAddMaint = CreateStyledButton("+ Ajouter cette planification", Color.FromArgb(14, 165, 233), new Point(24, 180), new Size(240, 36));
            btnAddMaint.Click += OnAddMaintenanceClick;

            Button btnRemoveMaint = CreateStyledButton("Supprimer la sélection", Color.FromArgb(185, 28, 28), new Point(280, 180), new Size(200, 36));
            btnRemoveMaint.Click += OnRemoveMaintenanceClick;

            Button btnPreviewLock = CreateStyledButton("🔒 Tester l'écran de verrouillage Maintenance", Color.FromArgb(124, 58, 237), new Point(500, 180), new Size(310, 36));
            btnPreviewLock.Click += (s, e) =>
            {
                using (MaintenanceLockForm lockForm = new MaintenanceLockForm("Aperçu de la fenêtre de maintenance"))
                {
                    lockForm.ShowDialog(this);
                }
            };

            grpAdd.Controls.Add(lblDay);
            grpAdd.Controls.Add(_cbMaintDay);
            grpAdd.Controls.Add(lblStart);
            grpAdd.Controls.Add(_txtMaintStart);
            grpAdd.Controls.Add(lblEnd);
            grpAdd.Controls.Add(_txtMaintEnd);
            grpAdd.Controls.Add(_chkMaintWinUpdate);
            grpAdd.Controls.Add(_chkMaintLockInput);
            grpAdd.Controls.Add(_chkMaintShutdown);
            grpAdd.Controls.Add(lblCmd);
            grpAdd.Controls.Add(_txtMaintCustomCmd);
            grpAdd.Controls.Add(btnAddMaint);
            grpAdd.Controls.Add(btnRemoveMaint);
            grpAdd.Controls.Add(btnPreviewLock);

            page.Controls.Add(grpList);
            page.Controls.Add(grpAdd);
            return page;
        }

        private TabPage BuildSecurityAndOtpTab()
        {
            TabPage page = CreateDarkTabPage("5. Mot de passe & Mode Furtif");

            // Mot de passe & Mode Furtif
            GroupBox grpPass = CreateGroupBox("Mot de passe Administrateur & Mode Furtif (Stealth Mode)", new Point(20, 18), new Size(840, 215));

            Label lblPass1 = new Label { Text = "Nouveau mot de passe :", Location = new Point(24, 38), AutoSize = true };
            _txtNewPass = new TextBox
            {
                UseSystemPasswordChar = true,
                Location = new Point(220, 34),
                Size = new Size(250, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };

            Label lblPass2 = new Label { Text = "Confirmer le mot de passe :", Location = new Point(24, 76), AutoSize = true };
            _txtConfirmPass = new TextBox
            {
                UseSystemPasswordChar = true,
                Location = new Point(220, 72),
                Size = new Size(250, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };

            Button btnSavePass = CreateStyledButton("Mettre à jour le mot de passe", Color.FromArgb(14, 165, 233), new Point(495, 50), new Size(240, 38));
            btnSavePass.Click += OnUpdatePasswordClick;

            _chkStealthTray = new CheckBox
            {
                Text = "Activer le Mode Furtif : masquer l'icône de la barre des tâches (accès uniquement via Ctrl + Alt + Maj + F6)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(251, 191, 36),
                Location = new Point(24, 125),
                AutoSize = true
            };

            _chkBlinkTray = new CheckBox
            {
                Text = "Faire clignoter l'icône en rouge/ambre lorsque le PC est DÉGELÉ (comme Faronics Deep Freeze)",
                Location = new Point(24, 160),
                AutoSize = true
            };

            grpPass.Controls.Add(lblPass1);
            grpPass.Controls.Add(_txtNewPass);
            grpPass.Controls.Add(lblPass2);
            grpPass.Controls.Add(_txtConfirmPass);
            grpPass.Controls.Add(btnSavePass);
            grpPass.Controls.Add(_chkStealthTray);
            grpPass.Controls.Add(_chkBlinkTray);

            // Générateur de Jeton à Usage Unique (OTP)
            GroupBox grpOtp = CreateGroupBox("Générateur de Jetons à Usage Unique (One-Time Password / Dépannage à distance)", new Point(20, 248), new Size(840, 220));

            Label lblStation = new Label { Text = "Nom de la station cible :", Location = new Point(24, 38), AutoSize = true };
            _txtStationName = new TextBox
            {
                Location = new Point(220, 34),
                Size = new Size(250, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White
            };

            Label lblSeed = new Label { Text = "Clé secrète OTP (Seed) :", Location = new Point(24, 78), AutoSize = true };
            _txtOtpSeed = new TextBox
            {
                Location = new Point(220, 74),
                Size = new Size(360, 28),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.White,
                Font = new Font("Consolas", 9.5f)
            };

            Button btnRegenOtp = CreateStyledButton("Calculer le Jeton OTP Horaire", Color.FromArgb(16, 185, 129), new Point(600, 70), new Size(215, 34));
            btnRegenOtp.Click += (s, e) => RefreshOtpPreview();

            _lblGeneratedOtp = new Label
            {
                Text = "CODE OTP ACTUEL : ----",
                Font = new Font("Consolas", 15f, FontStyle.Bold),
                ForeColor = Color.FromArgb(52, 211, 153),
                Location = new Point(24, 125),
                AutoSize = true
            };

            Label lblOtpExplain = new Label
            {
                Text = "Ce code à usage unique permet d'ouvrir PCFreeze sur le poste cible sans divulguer le mot de passe maître.\r\nIl change automatiquement toutes les heures (tolérance +/- 1h).",
                ForeColor = Color.FromArgb(148, 163, 184),
                Location = new Point(24, 162),
                Size = new Size(780, 42)
            };

            grpOtp.Controls.Add(lblStation);
            grpOtp.Controls.Add(_txtStationName);
            grpOtp.Controls.Add(lblSeed);
            grpOtp.Controls.Add(_txtOtpSeed);
            grpOtp.Controls.Add(btnRegenOtp);
            grpOtp.Controls.Add(_lblGeneratedOtp);
            grpOtp.Controls.Add(lblOtpExplain);

            page.Controls.Add(grpPass);
            page.Controls.Add(grpOtp);
            return page;
        }

        private TabPage BuildDeployAndLogsTab()
        {
            TabPage page = CreateDarkTabPage("6. Déploiement Cible & Logs");

            GroupBox grpDeploy = CreateGroupBox("Installation du Service PCFreeze sur l'Ordinateur Cible", new Point(20, 18), new Size(840, 175));

            _lblInstallStatus = new Label
            {
                Location = new Point(24, 30),
                Size = new Size(790, 75),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(226, 232, 240)
            };

            Button btnInstallTarget = CreateStyledButton("🚀 Installer PCFreeze sur CE poste (Machine Cible uniquement)", Color.FromArgb(220, 38, 38), new Point(24, 118), new Size(430, 38));
            btnInstallTarget.Click += OnInstallOnTargetClick;

            Button btnUninstallTarget = CreateStyledButton("Désinstaller le service de ce poste", Color.FromArgb(71, 85, 105), new Point(470, 118), new Size(260, 38));
            btnUninstallTarget.Click += OnUninstallFromTargetClick;

            grpDeploy.Controls.Add(_lblInstallStatus);
            grpDeploy.Controls.Add(btnInstallTarget);
            grpDeploy.Controls.Add(btnUninstallTarget);

            GroupBox grpLogs = CreateGroupBox("Journal d'audit système (pcfreeze_audit.log)", new Point(20, 205), new Size(840, 265));

            _txtAuditLog = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(20, 30),
                Size = new Size(800, 180),
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(125, 211, 252),
                Font = new Font("Consolas", 9f)
            };

            Button btnRefreshLogs = CreateStyledButton("Actualiser le journal", Color.FromArgb(51, 65, 85), new Point(20, 218), new Size(180, 32));
            btnRefreshLogs.Click += (s, e) => _txtAuditLog.Text = AuditLogger.ReadLogs(_engine.IsSimulation);

            grpLogs.Controls.Add(_txtAuditLog);
            grpLogs.Controls.Add(btnRefreshLogs);

            page.Controls.Add(grpDeploy);
            page.Controls.Add(grpLogs);
            return page;
        }

        private void LoadConfigIntoControls()
        {
            _engine = EngineResolver.ResolveEngine(_config, _forceSimulation);
            bool isFrozen = _config.CurrentBootState == BootState.Frozen;

            this.Icon = PolarIcons.CreateStateIcon(isFrozen);
            _picStatusIcon.Image = PolarIcons.DrawShieldBitmap(isFrozen, !isFrozen, 64);

            if (isFrozen)
            {
                _lblStatusHeadline.Text = "ÉTAT ACTUEL : GELÉ (SYSTÈME PROTÉGÉ À 100%)";
                _lblStatusHeadline.ForeColor = Color.FromArgb(56, 189, 248);
            }
            else if (_config.CurrentBootState == BootState.ThawedNextRestarts)
            {
                _lblStatusHeadline.Text = string.Format("ÉTAT ACTUEL : DÉGELÉ TEMPORAIRE ({0} redémarrage(s) restant(s))", _config.RemainingThawedRestarts);
                _lblStatusHeadline.ForeColor = Color.FromArgb(251, 191, 36);
            }
            else
            {
                _lblStatusHeadline.Text = "ÉTAT ACTUEL : DÉGELÉ (MODIFICATIONS PERMANENTES)";
                _lblStatusHeadline.ForeColor = Color.FromArgb(248, 113, 113);
            }

            _lblEngineBadge.Text = "Moteur actif : " + _engine.EngineName + "  |  Édition OS détectée : Windows " + EngineResolver.GetWindowsEditionId();

            if (_engine.IsSimulation)
            {
                _lblSafetyBanner.Text = "🛡️ SÉCURITÉ PC DE DÉVELOPPEMENT ACTIVE : Mode Simulation (Aucune modification disque/boot sur cet ordinateur)";
                _lblSafetyBanner.ForeColor = Color.FromArgb(52, 211, 153);
            }
            else
            {
                _lblSafetyBanner.Text = "⚡ MODE MACHINE CIBLE ACTIF : Les règles de gel/dégel sont appliquées directement au système.";
                _lblSafetyBanner.ForeColor = Color.FromArgb(251, 191, 36);
            }

            // Onglet 1
            _rbBootFrozen.Checked = _config.TargetBootStateOnNextReboot == BootState.Frozen;
            _rbBootThawedNext.Checked = _config.TargetBootStateOnNextReboot == BootState.ThawedNextRestarts;
            _rbBootThawedPermanent.Checked = _config.TargetBootStateOnNextReboot == BootState.ThawedPermanent;
            _nudThawedRestarts.Value = Math.Max(1, Math.Min(99, _config.ThawedRestartsConfigured));

            _lblCurrentStateSummary.Text = string.Format(
                "• Station                : {0}  (PCFreeze v2.0)\r\n" +
                "• État de la session     : {1}\r\n" +
                "• Prochain redémarrage   : {2}\r\n" +
                "• Image Baseline figée   : {3}\r\n" +
                "• Dernière action        : {4}",
                _config.StationName,
                _config.CurrentBootState,
                _config.TargetBootStateOnNextReboot == BootState.ThawedNextRestarts
                    ? "Dégelé pour " + _config.ThawedRestartsConfigured + " redémarrage(s)"
                    : _config.TargetBootStateOnNextReboot.ToString(),
                string.IsNullOrEmpty(_config.CapturedDesktopSummary) ? _engine.TechnicalDescription : _config.CapturedDesktopSummary,
                _config.LastActionTimestamp);

            // Onglet 2 : Volumes
            _lvVolumes.Items.Clear();
            foreach (VolumeProtectionConfig vol in _config.ProtectedVolumes)
            {
                ListViewItem item = new ListViewItem("");
                item.Checked = vol.IsFrozen;
                item.SubItems.Add(vol.DriveLetter);
                item.SubItems.Add(vol.VolumeLabel);
                item.SubItems.Add(vol.TotalSizeGB + " Go");
                item.SubItems.Add(vol.FreeSpaceGB + " Go");
                item.Tag = vol;
                _lvVolumes.Items.Add(item);
            }

            _rbOverlayRam.Checked = _config.OverlayType == OverlayStorageType.RAM;
            _rbOverlayDisk.Checked = _config.OverlayType == OverlayStorageType.Disk;
            _nudOverlaySizeMB.Value = Math.Max(512, Math.Min(65536, _config.OverlaySizeMB));

            int usedMB = _engine.GetEstimatedOverlayUsedMB(_config);
            int pct = Math.Min(100, (usedMB * 100) / Math.Max(1, _config.OverlaySizeMB));
            _pbOverlayUsage.Value = pct;
            _lblOverlayUsageText.Text = string.Format("{0} Mo / {1} Mo ({2}%)", usedMB, _config.OverlaySizeMB, pct);

            // Onglet 3 : ThawSpace & Exclusions
            if (_config.ThawSpaces.Count > 0)
            {
                ThawSpaceDriveConfig ts = _config.ThawSpaces[0];
                _chkThawEnabled.Checked = ts.IsEnabled;
                _cbThawLetter.SelectedItem = ts.DriveLetter;
                _nudThawSizeGB.Value = Math.Max(1, Math.Min(500, ts.SizeGB));
                _chkThawHidden.Checked = ts.IsHiddenInExplorer;
            }

            _lstExcludedFolders.Items.Clear();
            foreach (string path in _config.ExcludedFolderPaths)
            {
                _lstExcludedFolders.Items.Add(path);
            }

            // Onglet 4 : Maintenance
            RefreshMaintenanceList();

            // Onglet 5 : Sécurité & OTP
            _chkStealthTray.Checked = _config.StealthModeHideTrayIcon;
            _chkBlinkTray.Checked = _config.BlinkTrayWhenThawed;
            _txtStationName.Text = _config.StationName;
            _txtOtpSeed.Text = _config.OtpSecretSeed;
            RefreshOtpPreview();

            // Onglet 6 : Déploiement & Logs
            _lblInstallStatus.Text = _config.IsInstalledOnTargetMachine
                ? "État d'installation : INSTALLÉ EN MODE PRODUCTION SUR CE POSTE.\r\nLes tâches système PCFreeze_BootService et PCFreeze_TrayAgent sont actives."
                : "État d'installation : NON INSTALLÉ (Mode Simulation sécurisé actif).\r\nVous pouvez tester toute l'interface sur cet ordinateur sans aucun risque. Pour protéger l'ordinateur cible, copiez PCFreeze.exe dessus et cliquez sur le bouton d'installation ci-dessous.";
            _txtAuditLog.Text = AuditLogger.ReadLogs(_engine.IsSimulation);
        }

        private void CollectControlsIntoConfig()
        {
            if (_rbBootFrozen.Checked)
                _config.TargetBootStateOnNextReboot = BootState.Frozen;
            else if (_rbBootThawedNext.Checked)
                _config.TargetBootStateOnNextReboot = BootState.ThawedNextRestarts;
            else
                _config.TargetBootStateOnNextReboot = BootState.ThawedPermanent;

            _config.ThawedRestartsConfigured = (int)_nudThawedRestarts.Value;

            foreach (ListViewItem item in _lvVolumes.Items)
            {
                VolumeProtectionConfig vol = item.Tag as VolumeProtectionConfig;
                if (vol != null)
                {
                    vol.IsFrozen = item.Checked;
                }
            }

            _config.OverlayType = _rbOverlayRam.Checked ? OverlayStorageType.RAM : OverlayStorageType.Disk;
            _config.OverlaySizeMB = (int)_nudOverlaySizeMB.Value;

            if (_config.ThawSpaces.Count == 0)
                _config.ThawSpaces.Add(new ThawSpaceDriveConfig());

            ThawSpaceDriveConfig ts = _config.ThawSpaces[0];
            ts.IsEnabled = _chkThawEnabled.Checked;
            ts.DriveLetter = (_cbThawLetter.SelectedItem ?? "T:").ToString();
            ts.SizeGB = (int)_nudThawSizeGB.Value;
            ts.IsHiddenInExplorer = _chkThawHidden.Checked;
            ts.BackingFilePath = Path.Combine(
                PCFreezeConfig.GetConfigDirectory(_engine.IsSimulation),
                "ThawSpace_" + ts.DriveLetter[0] + ".vhdx");

            _config.ExcludedFolderPaths.Clear();
            foreach (object obj in _lstExcludedFolders.Items)
            {
                _config.ExcludedFolderPaths.Add(obj.ToString());
            }

            _config.StealthModeHideTrayIcon = _chkStealthTray.Checked;
            _config.BlinkTrayWhenThawed = _chkBlinkTray.Checked;
            _config.StationName = _txtStationName.Text.Trim();
            _config.OtpSecretSeed = _txtOtpSeed.Text.Trim();
        }

        private void OnSaveAndApplyClick(object sender, EventArgs e)
        {
            CollectControlsIntoConfig();
            string syncDetails;
            _engine.SyncExclusionsAndOverlay(_config, out syncDetails);

            string bootDetails;
            _engine.ApplyStateForNextBoot(_config, out bootDetails);

            _config.Save(_engine.IsSimulation);
            LoadConfigIntoControls();
            if (ConfigurationUpdated != null) ConfigurationUpdated(this, EventArgs.Empty);

            MessageBox.Show(
                this,
                "Configuration enregistrée avec succès !\r\n\r\n" + bootDetails,
                "PCFreeze — Configuration appliquée",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnSimulateRebootClick(object sender, EventArgs e)
        {
            CollectControlsIntoConfig();
            _engine.ExecuteBootHook(_config);
            LoadConfigIntoControls();
            if (ConfigurationUpdated != null) ConfigurationUpdated(this, EventArgs.Empty);

            MessageBox.Show(
                this,
                string.Format(
                    "Redémarrage simulé exécuté !\r\n\r\nÉtat actuel après redémarrage : {0}{1}",
                    _config.CurrentBootState,
                    _config.CurrentBootState == BootState.ThawedNextRestarts
                        ? " (" + _config.RemainingThawedRestarts + " redémarrage(s) dégelé(s) restant(s))"
                        : ""),
                "PCFreeze — Simulation de Redémarrage",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void OnApplyAndRealRebootClick(object sender, EventArgs e)
        {
            CollectControlsIntoConfig();
            if (_engine.IsSimulation)
            {
                MessageBox.Show(
                    this,
                    "Vous êtes actuellement en Mode Simulation (Sécurité PC de développement).\r\n\r\nLe vrai redémarrage Windows est bloqué pour ne pas interrompre votre travail sur ce PC. Utilisez le bouton « Simuler un Redémarrage (Test) » pour tester la transition d'état !",
                    "PCFreeze — Sécurité Mode Simulation",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            DialogResult dr = MessageBox.Show(
                this,
                "Voulez-vous appliquer le nouvel état (" + _config.TargetBootStateOnNextReboot + ") et redémarrer Windows immédiatement ?",
                "PCFreeze — Confirmation de redémarrage",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (dr == DialogResult.Yes)
            {
                string details;
                _engine.ApplyStateForNextBoot(_config, out details);
                string outReboot;
                EngineResolver.RunProcessHidden("shutdown.exe", "/r /t 5 /c \"PCFreeze : Application du changement d'état Gelé/Dégelé...\"", out outReboot);
                this.Close();
            }
        }

        private void OnMountThawSpaceClick(object sender, EventArgs e)
        {
            CollectControlsIntoConfig();
            string msg;
            ThawSpaceManager.CreateOrMountThawSpace(_config.ThawSpaces[0], _engine.IsSimulation, out msg);
            LoadConfigIntoControls();
            MessageBox.Show(this, msg, "PCFreeze — ThawSpace", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnUnmountThawSpaceClick(object sender, EventArgs e)
        {
            CollectControlsIntoConfig();
            string msg;
            ThawSpaceManager.UnmountThawSpace(_config.ThawSpaces[0], _engine.IsSimulation, out msg);
            LoadConfigIntoControls();
            MessageBox.Show(this, msg, "PCFreeze — ThawSpace", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void AddExcludedPath(string folderPath)
        {
            if (string.IsNullOrWhiteSpace(folderPath)) return;
            foreach (object item in _lstExcludedFolders.Items)
            {
                if (string.Equals(item.ToString(), folderPath, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            _lstExcludedFolders.Items.Add(folderPath);
        }

        private void OnBrowseExcludedFolderClick(object sender, EventArgs e)
        {
            using (FolderBrowserDialog fbd = new FolderBrowserDialog())
            {
                fbd.Description = "Sélectionnez un dossier à exclure du gel (son contenu sera conservé après chaque redémarrage) :";
                if (fbd.ShowDialog(this) == DialogResult.OK)
                {
                    AddExcludedPath(fbd.SelectedPath);
                }
            }
        }

        private void RefreshMaintenanceList()
        {
            _lvMaintenance.Items.Clear();
            foreach (MaintenanceScheduleItem m in _config.MaintenanceSchedules)
            {
                ListViewItem item = new ListViewItem("");
                item.Checked = m.IsEnabled;
                item.SubItems.Add(m.Name);
                item.SubItems.Add(m.Day.ToString());
                item.SubItems.Add(m.StartTime + " -> " + m.EndTime);
                item.SubItems.Add(m.PerformWindowsUpdate ? "Oui" : "Non");
                item.SubItems.Add(m.LockKeyboardAndMouse ? "Verrouillé" : "Libre");
                item.Tag = m;
                _lvMaintenance.Items.Add(item);
            }
        }

        private void OnAddMaintenanceClick(object sender, EventArgs e)
        {
            MaintenanceScheduleItem item = new MaintenanceScheduleItem
            {
                Name = "Maintenance " + _cbMaintDay.SelectedItem,
                Day = (DayOfWeek)_cbMaintDay.SelectedItem,
                StartTime = _txtMaintStart.Text.Trim(),
                EndTime = _txtMaintEnd.Text.Trim(),
                PerformWindowsUpdate = _chkMaintWinUpdate.Checked,
                LockKeyboardAndMouse = _chkMaintLockInput.Checked,
                ShutdownAfterMaintenance = _chkMaintShutdown.Checked,
                CustomCommand = _txtMaintCustomCmd.Text.Trim(),
                IsEnabled = true
            };
            _config.MaintenanceSchedules.Add(item);
            RefreshMaintenanceList();
        }

        private void OnRemoveMaintenanceClick(object sender, EventArgs e)
        {
            if (_lvMaintenance.SelectedItems.Count > 0)
            {
                MaintenanceScheduleItem item = _lvMaintenance.SelectedItems[0].Tag as MaintenanceScheduleItem;
                if (item != null)
                {
                    _config.MaintenanceSchedules.Remove(item);
                    RefreshMaintenanceList();
                }
            }
        }

        private void OnUpdatePasswordClick(object sender, EventArgs e)
        {
            if (_txtNewPass.Text != _txtConfirmPass.Text)
            {
                MessageBox.Show(this, "Les deux mots de passe ne correspondent pas.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            CryptoManager.SetPassword(_config, _txtNewPass.Text);
            _config.Save(_engine.IsSimulation);
            _txtNewPass.Text = "";
            _txtConfirmPass.Text = "";

            MessageBox.Show(
                this,
                string.IsNullOrEmpty(_config.PasswordHash)
                    ? "Le mot de passe a été effacé (connexion sans mot de passe)."
                    : "Nouveau mot de passe administrateur chiffré (PBKDF2-SHA256) enregistré avec succès !",
                "PCFreeze — Sécurité",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        private void RefreshOtpPreview()
        {
            string otp = CryptoManager.GenerateCurrentOtp(_txtStationName.Text, _txtOtpSeed.Text);
            _lblGeneratedOtp.Text = string.Format("CODE OTP HORAIRE ACTUEL :  {0}   (Valide pour {1})", otp, _txtStationName.Text);
        }

        private void OnInstallOnTargetClick(object sender, EventArgs e)
        {
            DialogResult confirm = MessageBox.Show(
                this,
                "⚠️ ATTENTION : Cette action va installer PCFreeze en tant que service système actif sur CET ordinateur (activation de UWF/VSS et des tâches de démarrage).\r\n\r\n" +
                "Ne cliquez sur OUI que si vous êtes sur l'ORDINATEUR CIBLE à protéger !\r\n" +
                "Êtes-vous certain d'être sur l'ordinateur cible ?",
                "PCFreeze — Confirmation d'installation sur Machine Cible",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (confirm != DialogResult.Yes)
                return;

            CollectControlsIntoConfig();
            string report;
            bool ok = TargetDeploymentManager.InstallOnTargetMachine(_config, out report);
            LoadConfigIntoControls();
            MessageBox.Show(this, report, ok ? "Installation réussie" : "Erreur d'installation", MessageBoxButtons.OK, ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);
        }

        private void OnUninstallFromTargetClick(object sender, EventArgs e)
        {
            if (_config.CurrentBootState == BootState.Frozen && !_engine.IsSimulation)
            {
                MessageBox.Show(
                    this,
                    "Comme dans Deep Freeze, il est impossible de désinstaller PCFreeze pendant que le poste est GELÉ.\r\nPassez d'abord en mode DÉGELÉ et redémarrez.",
                    "PCFreeze — Poste Gelé",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string report;
            TargetDeploymentManager.UninstallFromTargetMachine(_config, out report);
            LoadConfigIntoControls();
            MessageBox.Show(this, report, "PCFreeze — Désinstallation", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static TabPage CreateDarkTabPage(string title)
        {
            return new TabPage(title)
            {
                BackColor = Color.FromArgb(15, 23, 42),
                ForeColor = Color.FromArgb(241, 245, 249)
            };
        }

        private static GroupBox CreateGroupBox(string title, Point location, Size size)
        {
            return new GroupBox
            {
                Text = "  " + title + "  ",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(125, 211, 252),
                BackColor = Color.FromArgb(30, 41, 59),
                Location = location,
                Size = size
            };
        }

        private static Button CreateStyledButton(string text, Color backColor, Point location, Size size)
        {
            Button btn = new Button
            {
                Text = text,
                BackColor = backColor,
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 9.2f, FontStyle.Bold),
                Location = location,
                Size = size,
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
    }
}
