using System;
using System.Drawing;
using System.Windows.Forms;
using PCFreeze.Models;
using PCFreeze.Security;

namespace PCFreeze.UI
{
    public class LoginDialog : Form
    {
        private readonly PCFreezeConfig _config;
        private TextBox _txtPassword;
        private Label _lblError;

        public bool IsSecretUninstallRequested { get; private set; }

        public LoginDialog(PCFreezeConfig config)
        {
            _config = config;
            IsSecretUninstallRequested = false;
            InitializeUI();
        }

        private void InitializeUI()
        {
            bool isFrozen = _config.CurrentBootState == BootState.Frozen;

            this.Text = "PCFreeze — Authentification Administrateur";
            this.Size = new Size(460, 315);
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.ForeColor = Color.White;
            this.Icon = PolarIcons.CreateStateIcon(isFrozen);

            // En-tête
            Panel header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 82,
                BackColor = Color.FromArgb(30, 41, 59)
            };

            PictureBox picLogo = new PictureBox
            {
                Image = PolarIcons.DrawShieldBitmap(isFrozen, !isFrozen, 54),
                Size = new Size(54, 54),
                Location = new Point(20, 14),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            Label lblBrand = new Label
            {
                Text = "PCFREEZE WORKSTATION",
                Font = new Font("Segoe UI", 13.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                Location = new Point(86, 14),
                AutoSize = true
            };

            string statusStr = isFrozen
                ? "ÉTAT ACTUEL : GELÉ (PROTÉGÉ)"
                : (_config.CurrentBootState == BootState.ThawedNextRestarts
                    ? "ÉTAT ACTUEL : DÉGELÉ (" + _config.RemainingThawedRestarts + " redémarrage(s) restant(s))"
                    : "ÉTAT ACTUEL : DÉGELÉ (MODIFICATIONS PERMANENTES)");

            Label lblStatus = new Label
            {
                Text = statusStr + "  |  Poste : " + _config.StationName,
                Font = new Font("Segoe UI", 8.8f, FontStyle.Bold),
                ForeColor = isFrozen ? Color.FromArgb(125, 211, 252) : Color.FromArgb(251, 191, 36),
                Location = new Point(88, 44),
                AutoSize = true
            };

            header.Controls.Add(picLogo);
            header.Controls.Add(lblBrand);
            header.Controls.Add(lblStatus);

            // Corps
            Label lblPrompt = new Label
            {
                Text = "Saisissez le mot de passe Administrateur ou un Jeton OTP :",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(226, 232, 240),
                Location = new Point(24, 100),
                AutoSize = true
            };

            _txtPassword = new TextBox
            {
                Location = new Point(24, 128),
                Size = new Size(396, 28),
                Font = new Font("Segoe UI", 11f),
                UseSystemPasswordChar = true,
                BackColor = Color.FromArgb(30, 41, 59),
                ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };

            string hintText = string.IsNullOrEmpty(_config.PasswordHash)
                ? "ℹ Premier lancement : aucun mot de passe défini (cliquez directement sur OK)."
                : "Raccourci d'appel : Ctrl + Alt + Maj + F6 ou Maj + Double-clic sur l'icône.";

            _lblError = new Label
            {
                Text = hintText,
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = string.IsNullOrEmpty(_config.PasswordHash)
                    ? Color.FromArgb(52, 211, 153)
                    : Color.FromArgb(148, 163, 184),
                Location = new Point(24, 165),
                Size = new Size(400, 36)
            };

            Button btnOk = new Button
            {
                Text = "OK",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(14, 165, 233),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(125, 36),
                Location = new Point(165, 215)
            };
            btnOk.FlatAppearance.BorderSize = 0;
            btnOk.Click += OnAuthenticateClick;

            Button btnCancel = new Button
            {
                Text = "Annuler",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(120, 36),
                Location = new Point(300, 215),
                DialogResult = DialogResult.Cancel
            };
            btnCancel.FlatAppearance.BorderSize = 0;

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            this.Controls.Add(header);
            this.Controls.Add(lblPrompt);
            this.Controls.Add(_txtPassword);
            this.Controls.Add(_lblError);
            this.Controls.Add(btnOk);
            this.Controls.Add(btnCancel);

            this.Shown += (s, e) => _txtPassword.Focus();
        }

        private void OnAuthenticateClick(object sender, EventArgs e)
        {
            string rawInput = _txtPassword.Text ?? "";

            // Commande secrète administrateur : "UNINSTALL" ou "UNINSTALL:motdepasse"
            if (rawInput.Equals("UNINSTALL", StringComparison.OrdinalIgnoreCase) ||
                rawInput.StartsWith("UNINSTALL:", StringComparison.OrdinalIgnoreCase))
            {
                string passPart = rawInput.Contains(":")
                    ? rawInput.Substring(rawInput.IndexOf(':') + 1)
                    : "";

                if (CryptoManager.VerifyPasswordOrOtp(_config, passPart))
                {
                    IsSecretUninstallRequested = true;
                    AuditLogger.Log("AUTH-UNINSTALL", "Commande secrète UNINSTALL validée.", !_config.IsInstalledOnTargetMachine);
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                    return;
                }
            }

            if (CryptoManager.VerifyPasswordOrOtp(_config, rawInput))
            {
                AuditLogger.Log("AUTH", "Authentification administrateur réussie.", !_config.IsInstalledOnTargetMachine);
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            else
            {
                AuditLogger.Log("AUTH-FAIL", "Tentative de mot de passe ou jeton OTP invalide.", !_config.IsInstalledOnTargetMachine);
                _lblError.ForeColor = Color.FromArgb(248, 113, 113);
                _lblError.Text = "✖ Mot de passe ou Jeton à usage unique (OTP) incorrect.";
                _txtPassword.SelectAll();
                _txtPassword.Focus();
            }
        }
    }
}
