using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PCFreeze.UI
{
    public static class PolarIcons
    {
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        public static Icon CreateStateIcon(bool isFrozen, bool blinkPhaseRed = false, int size = 32)
        {
            using (Bitmap bmp = DrawShieldBitmap(isFrozen, blinkPhaseRed, size))
            {
                IntPtr hIcon = bmp.GetHicon();
                Icon cloned = (Icon)Icon.FromHandle(hIcon).Clone();
                DestroyIcon(hIcon);
                return cloned;
            }
        }

        public static Bitmap DrawShieldBitmap(bool isFrozen, bool blinkPhaseRed, int size)
        {
            Bitmap bmp = new Bitmap(size, size);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                float pad = size * 0.08f;
                float w = size - pad * 2f;
                float h = size - pad * 2f;

                // Fond bouclier / hexagone polaire
                Color topColor = isFrozen
                    ? Color.FromArgb(14, 165, 233)   // Bleu glacier vif
                    : (blinkPhaseRed ? Color.FromArgb(239, 68, 68) : Color.FromArgb(245, 158, 11)); // Rouge / Ambre dégelé
                Color bottomColor = isFrozen
                    ? Color.FromArgb(2, 132, 199)
                    : (blinkPhaseRed ? Color.FromArgb(185, 28, 28) : Color.FromArgb(217, 119, 6));

                using (GraphicsPath path = CreateRoundedRect(pad, pad, w, h, size * 0.22f))
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new PointF(0, 0), new PointF(size, size), topColor, bottomColor))
                using (Pen borderPen = new Pen(Color.FromArgb(220, 255, 255, 255), Math.Max(1.2f, size * 0.05f)))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(borderPen, path);
                }

                // Dessin du flocon de neige à 6 branches au centre
                float cx = size / 2f;
                float cy = size / 2f;
                float radius = size * 0.30f;

                using (Pen snowPen = new Pen(Color.White, Math.Max(1.5f, size * 0.07f)))
                {
                    snowPen.StartCap = LineCap.Round;
                    snowPen.EndCap = LineCap.Round;

                    for (int i = 0; i < 6; i++)
                    {
                        double angle = i * Math.PI / 3.0;
                        float ex = cx + (float)(Math.Cos(angle) * radius);
                        float ey = cy + (float)(Math.Sin(angle) * radius);
                        g.DrawLine(snowPen, cx, cy, ex, ey);

                        // Petites ramifications du cristal
                        float bx = cx + (float)(Math.Cos(angle) * (radius * 0.58f));
                        float by = cy + (float)(Math.Sin(angle) * (radius * 0.58f));
                        double bAngle1 = angle + Math.PI / 6.0;
                        double bAngle2 = angle - Math.PI / 6.0;
                        float bLen = radius * 0.32f;

                        g.DrawLine(snowPen, bx, by,
                            bx + (float)(Math.Cos(bAngle1) * bLen),
                            by + (float)(Math.Sin(bAngle1) * bLen));
                        g.DrawLine(snowPen, bx, by,
                            bx + (float)(Math.Cos(bAngle2) * bLen),
                            by + (float)(Math.Sin(bAngle2) * bLen));
                    }
                }

                // Si DÉGELÉ : croix rouge / barre diagonale de dégel (comme dans Deep Freeze)
                if (!isFrozen && blinkPhaseRed)
                {
                    using (Pen crossPen = new Pen(Color.FromArgb(255, 254, 226, 226), Math.Max(2f, size * 0.10f)))
                    {
                        crossPen.StartCap = LineCap.Round;
                        crossPen.EndCap = LineCap.Round;
                        g.DrawLine(crossPen, size * 0.22f, size * 0.22f, size * 0.78f, size * 0.78f);
                    }
                }
            }
            return bmp;
        }

        private static GraphicsPath CreateRoundedRect(float x, float y, float width, float height, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2f;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>
    /// Écran de verrouillage plein-écran affiché pendant les fenêtres de maintenance
    /// (bloque les interactions utilisateur pendant Windows Update / scripts de maintenance).
    /// </summary>
    public class MaintenanceLockForm : Form
    {
        private Label lblTitle;
        private Label lblSubtitle;
        private Button btnAdminUnlock;

        public MaintenanceLockForm(string scheduleName)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(15, 23, 42);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterScreen;

            Panel centerPanel = new Panel
            {
                Size = new Size(680, 320),
                BackColor = Color.FromArgb(30, 41, 59)
            };

            PictureBox pic = new PictureBox
            {
                Image = PolarIcons.DrawShieldBitmap( false, true, 72),
                Size = new Size(72, 72),
                Location = new Point(304, 28),
                SizeMode = PictureBoxSizeMode.CenterImage
            };

            lblTitle = new Label
            {
                Text = "PCFREEZE — MAINTENANCE SYSTÈME EN COURS",
                Font = new Font("Segoe UI", 16f, FontStyle.Bold),
                ForeColor = Color.FromArgb(56, 189, 248),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 115),
                Size = new Size(640, 40)
            };

            lblSubtitle = new Label
            {
                Text = string.Format(
                    "Tâche active : {0}\r\nLe poste est temporairement dégelé pour l'application des mises à jour.\r\nClavier et souris verrouillés — Ne pas éteindre l'ordinateur.",
                    scheduleName),
                Font = new Font("Segoe UI", 10.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(203, 213, 225),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(30, 160),
                Size = new Size(620, 80)
            };

            btnAdminUnlock = new Button
            {
                Text = "Quitter le mode maintenance (Administrateur)",
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.FromArgb(51, 65, 85),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(320, 38),
                Location = new Point(180, 255)
            };
            btnAdminUnlock.Click += (s, e) => this.Close();

            centerPanel.Controls.Add(pic);
            centerPanel.Controls.Add(lblTitle);
            centerPanel.Controls.Add(lblSubtitle);
            centerPanel.Controls.Add(btnAdminUnlock);

            this.Controls.Add(centerPanel);
            this.Resize += (s, e) =>
            {
                centerPanel.Left = (this.ClientSize.Width - centerPanel.Width) / 2;
                centerPanel.Top = (this.ClientSize.Height - centerPanel.Height) / 2;
            };
        }
    }
}
