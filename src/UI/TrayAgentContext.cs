using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PCFreeze.Models;

namespace PCFreeze.UI
{
    public class TrayAgentContext : ApplicationContext
    {
        private readonly PCFreezeConfig _config;
        private readonly bool _forceSimulation;
        private readonly NotifyIcon _notifyIcon;
        private readonly Timer _blinkTimer;
        private readonly HotkeyMessageWindow _hotkeyWindow;
        private bool _blinkPhase;
        private MainWindow _activeMainWindow;
        private bool _isLoginDialogOpen;

        public TrayAgentContext(PCFreezeConfig config, bool forceSimulation, bool openConsoleImmediately)
        {
            _config = config;
            _forceSimulation = forceSimulation;

            ContextMenuStrip menu = BuildTrayContextMenu();

            _notifyIcon = new NotifyIcon
            {
                Icon = PolarIcons.CreateStateIcon(_config.CurrentBootState == BootState.Frozen),
                Text = GetTrayTooltip(),
                Visible = !_config.StealthModeHideTrayIcon,
                ContextMenuStrip = menu
            };

            // Comme dans Deep Freeze : Maj + Double-clic (ou Double-clic simple) ouvre la fenêtre de mot de passe
            _notifyIcon.MouseDoubleClick += (s, e) => PromptLoginAndOpenConsole();

            // Clignotement de l'icône lorsque le poste est Dégelé (exactement comme Deep Freeze)
            _blinkTimer = new Timer { Interval = 900 };
            _blinkTimer.Tick += OnBlinkTimerTick;
            _blinkTimer.Start();

            // Enregistrement du raccourci clavier global secret : Ctrl + Alt + Maj + F6 (Console) et Ctrl + Alt + Maj + F12 (Désinstallation furtive)
            _hotkeyWindow = new HotkeyMessageWindow();
            _hotkeyWindow.HotkeyPressed += (s, e) => PromptLoginAndOpenConsole();
            _hotkeyWindow.UninstallHotkeyPressed += (s, e) => PromptSecretAdminUninstall();
            _hotkeyWindow.RegisterDeepFreezeHotkey();

            if (openConsoleImmediately)
            {
                Timer initialOpenTimer = new Timer { Interval = 150 };
                initialOpenTimer.Tick += (s, e) =>
                {
                    initialOpenTimer.Stop();
                    initialOpenTimer.Dispose();
                    PromptLoginAndOpenConsole();
                };
                initialOpenTimer.Start();
            }
            else if (_config.IsInstalledOnTargetMachine && !_forceSimulation && _config.CurrentBootState == BootState.Frozen)
            {
                // Seconde passe de sécurité 3,5 secondes après l'ouverture de session Windows (au cas où Explorer/OneDrive
                // finit de monter le Bureau 1 ou 2 secondes après le lancement de HKLM\...\Run)
                Timer postLogonCleanupTimer = new Timer { Interval = 3500 };
                postLogonCleanupTimer.Tick += (s, e) =>
                {
                    postLogonCleanupTimer.Stop();
                    postLogonCleanupTimer.Dispose();
                    string restoreMsg;
                    PCFreeze.Engine.BaselineSnapshotManager.RestoreBaseline(_config, out restoreMsg);
                    PCFreeze.Engine.EngineResolver.RefreshExplorerDesktop();
                };
                postLogonCleanupTimer.Start();
            }
        }

        private ContextMenuStrip BuildTrayContextMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem itemOpen = new ToolStripMenuItem("Ouvrir la Console PCFreeze (Ctrl+Alt+Maj+F6)");
            itemOpen.Font = new Font(menu.Font, FontStyle.Bold);
            itemOpen.Click += (s, e) => PromptLoginAndOpenConsole();

            ToolStripMenuItem itemStatus = new ToolStripMenuItem("État : " + _config.CurrentBootState)
            {
                Enabled = false
            };

            ToolStripSeparator sep = new ToolStripSeparator();
            ToolStripMenuItem itemExit = new ToolStripMenuItem("Quitter l'agent (Mode Simulation)");
            itemExit.Click += (s, e) =>
            {
                if (_config.IsInstalledOnTargetMachine && !_forceSimulation)
                {
                    MessageBox.Show(
                        "L'agent système PCFreeze ne peut pas être arrêté par un utilisateur sur une machine cible protégée.",
                        "PCFreeze — Protection Active",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }
                ExitThread();
            };

            menu.Items.Add(itemOpen);
            menu.Items.Add(itemStatus);
            menu.Items.Add(sep);
            menu.Items.Add(itemExit);
            return menu;
        }

        private void OnBlinkTimerTick(object sender, EventArgs e)
        {
            bool isFrozen = _config.CurrentBootState == BootState.Frozen;
            if (!isFrozen && _config.BlinkTrayWhenThawed)
            {
                _blinkPhase = !_blinkPhase;
                Icon oldIcon = _notifyIcon.Icon;
                _notifyIcon.Icon = PolarIcons.CreateStateIcon(false, _blinkPhase);
                if (oldIcon != null) oldIcon.Dispose();
            }
        }

        public void RefreshTrayState()
        {
            bool isFrozen = _config.CurrentBootState == BootState.Frozen;
            Icon oldIcon = _notifyIcon.Icon;
            _notifyIcon.Icon = PolarIcons.CreateStateIcon(isFrozen, !isFrozen);
            if (oldIcon != null) oldIcon.Dispose();

            _notifyIcon.Text = GetTrayTooltip();
            _notifyIcon.Visible = !_config.StealthModeHideTrayIcon;
        }

        private string GetTrayTooltip()
        {
            string state = _config.CurrentBootState == BootState.Frozen
                ? "GELÉ (Protégé)"
                : (_config.CurrentBootState == BootState.ThawedNextRestarts
                    ? "DÉGELÉ (" + _config.RemainingThawedRestarts + " boot(s))"
                    : "DÉGELÉ");
            string mode = (!_config.IsInstalledOnTargetMachine || _forceSimulation) ? " [Simulation]" : "";
            string text = "PCFreeze : " + state + mode + "\nCtrl+Alt+Maj+F6 pour ouvrir";
            return text.Length > 63 ? text.Substring(0, 63) : text;
        }

        public void PromptLoginAndOpenConsole()
        {
            if (_activeMainWindow != null && !_activeMainWindow.IsDisposed)
            {
                _activeMainWindow.WindowState = FormWindowState.Normal;
                _activeMainWindow.BringToFront();
                _activeMainWindow.Activate();
                return;
            }

            if (_isLoginDialogOpen)
                return;

            _isLoginDialogOpen = true;
            try
            {
                using (LoginDialog login = new LoginDialog(_config))
                {
                    if (login.ShowDialog() == DialogResult.OK)
                    {
                        if (login.IsSecretUninstallRequested)
                        {
                            ExecuteConfirmedUninstall();
                            return;
                        }

                        _activeMainWindow = new MainWindow(_config, _forceSimulation);
                        _activeMainWindow.ConfigurationUpdated += (s, e) => RefreshTrayState();
                        _activeMainWindow.FormClosed += (s, e) =>
                        {
                            RefreshTrayState();
                            // Si l'utilisateur est sur son PC de développement (non installé en service),
                            // fermer la fenêtre principale arrête proprement le processus pour ne rien laisser tourner en tâche de fond.
                            if (!_config.IsInstalledOnTargetMachine)
                            {
                                ExitThread();
                            }
                        };
                        _activeMainWindow.Show();
                    }
                    else if (!_config.IsInstalledOnTargetMachine)
                    {
                        ExitThread();
                    }
                }
            }
            finally
            {
                _isLoginDialogOpen = false;
            }
        }

        public void PromptSecretAdminUninstall()
        {
            if (_isLoginDialogOpen)
                return;

            _isLoginDialogOpen = true;
            try
            {
                if (!string.IsNullOrEmpty(_config.PasswordHash))
                {
                    using (LoginDialog login = new LoginDialog(_config))
                    {
                        login.Text = "PCFreeze — Désinstallation Furtive Administrateur (Ctrl+Alt+Maj+F12)";
                        if (login.ShowDialog() != DialogResult.OK)
                            return;
                    }
                }
                ExecuteConfirmedUninstall();
            }
            finally
            {
                _isLoginDialogOpen = false;
            }
        }

        private void ExecuteConfirmedUninstall()
        {
            DialogResult dr = MessageBox.Show(
                "🔓 ACCÈS FURTIF ADMINISTRATEUR VALIDÉ\r\n\r\nVoulez-vous désarmer le gel et désinstaller intégralement PCFreeze de cet ordinateur ?",
                "PCFreeze — Désinstallation Secrète Administrateur",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (dr == DialogResult.Yes)
            {
                string report;
                PCFreeze.Engine.TargetDeploymentManager.UninstallFromTargetMachine(_config, out report);
                MessageBox.Show(
                    "PCFreeze a été intégralement désinstallé de ce poste :\r\n\r\n" + report,
                    "PCFreeze — Désinstallation Terminée",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                ExitThread();
            }
        }

        protected override void ExitThreadCore()
        {
            _blinkTimer.Stop();
            _blinkTimer.Dispose();
            _hotkeyWindow.Dispose();
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            base.ExitThreadCore();
        }
    }

    /// <summary>
    /// Fenêtre Win32 invisible dédiée à la capture des raccourcis globaux :
    /// - Ctrl + Alt + Maj + F6  : Console d'administration
    /// - Ctrl + Alt + Maj + F12 : Désinstallation secrète administrateur
    /// </summary>
    internal class HotkeyMessageWindow : NativeWindow, IDisposable
    {
        private const int WM_HOTKEY = 0x0312;
        private const int HOTKEY_ID = 0xDF06;
        private const int HOTKEY_UNINSTALL_ID = 0xDF12;
        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CONTROL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint VK_F6 = 0x75;
        private const uint VK_F12 = 0x7B;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event EventHandler HotkeyPressed;
        public event EventHandler UninstallHotkeyPressed;

        public HotkeyMessageWindow()
        {
            CreateHandle(new CreateParams());
        }

        public void RegisterDeepFreezeHotkey()
        {
            RegisterHotKey(this.Handle, HOTKEY_ID, MOD_CONTROL | MOD_ALT | MOD_SHIFT, VK_F6);
            RegisterHotKey(this.Handle, HOTKEY_UNINSTALL_ID, MOD_CONTROL | MOD_ALT | MOD_SHIFT, VK_F12);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id == HOTKEY_ID && HotkeyPressed != null)
                {
                    HotkeyPressed(this, EventArgs.Empty);
                }
                else if (id == HOTKEY_UNINSTALL_ID && UninstallHotkeyPressed != null)
                {
                    UninstallHotkeyPressed(this, EventArgs.Empty);
                }
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            UnregisterHotKey(this.Handle, HOTKEY_ID);
            UnregisterHotKey(this.Handle, HOTKEY_UNINSTALL_ID);
            DestroyHandle();
        }
    }
}
