using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace Snowdrift
{
    // Окно «Поддержать автора»: текст, QR-код и ссылка на сбор.
    internal sealed class DonateForm : Form
    {
        public DonateForm()
        {
            Text = L.T("Поддержать автора", "Support the author");
            Icon = Res.AppIcon(new Size(32, 32));
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 10f);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Color.White;
            AutoSize = true;
            AutoSizeMode = AutoSizeMode.GrowAndShrink;
            Padding = new Padding(16);

            var panel = new FlowLayoutPanel();
            panel.FlowDirection = FlowDirection.TopDown;
            panel.AutoSize = true;
            panel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            panel.WrapContents = false;
            Controls.Add(panel);

            var text = new Label();
            text.AutoSize = true;
            text.MaximumSize = new Size(360, 0);
            text.Text = L.T(
                "Сугроб бесплатный. Если снег поднял настроение, можно скинуть автору на новогодний подарок. Любая сумма — спасибо!",
                "Snowdrift is free. If the snow made your day, you can chip in for the author's New Year gift. Thank you!");
            text.Margin = new Padding(0, 0, 0, 12);
            panel.Controls.Add(text);

            string url = Res.Text("donate-url.txt");
            Image qr = null;
            try
            {
                using (var s = Res.Open("donate-qr.png"))
                    if (s != null) qr = new Bitmap(Image.FromStream(s));
            }
            catch (Exception) { qr = null; }

            if (qr != null)
            {
                var pic = new PictureBox();
                pic.Image = qr;
                pic.SizeMode = PictureBoxSizeMode.Zoom;
                pic.Size = new Size(240, 240);
                pic.Margin = new Padding(60, 0, 0, 12);
                panel.Controls.Add(pic);
            }

            if (!string.IsNullOrEmpty(url))
            {
                var link = new LinkLabel();
                link.AutoSize = true;
                link.Text = L.T("Открыть страницу сбора", "Open the donation page");
                link.LinkClicked += delegate { try { Process.Start(url); } catch (Exception) { } };
                link.Margin = new Padding(0, 0, 0, 12);
                panel.Controls.Add(link);
            }
            else if (qr == null)
            {
                var none = new Label();
                none.AutoSize = true;
                none.ForeColor = Color.Gray;
                none.Text = L.T("Ссылка на сбор появится в следующей версии.", "The donation link will be added soon.");
                none.Margin = new Padding(0, 0, 0, 12);
                panel.Controls.Add(none);
            }

            if (!string.IsNullOrEmpty(App.RepoUrl))
            {
                var repo = new LinkLabel();
                repo.AutoSize = true;
                repo.Text = "GitHub";
                repo.LinkClicked += delegate { try { Process.Start(App.RepoUrl); } catch (Exception) { } };
                panel.Controls.Add(repo);
            }

            var ver = new Label();
            ver.AutoSize = true;
            ver.ForeColor = Color.Gray;
            ver.Text = L.T("Сугроб ", "Snowdrift ") + App.Version;
            panel.Controls.Add(ver);

            var ok = new Button();
            ok.Text = L.T("Закрыть", "Close");
            ok.AutoSize = true;
            ok.Click += delegate { Close(); };
            ok.Margin = new Padding(0, 12, 0, 0);
            panel.Controls.Add(ok);
            AcceptButton = ok;
            CancelButton = ok;
        }
    }
}
