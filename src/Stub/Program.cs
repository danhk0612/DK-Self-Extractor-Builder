using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DKSelfExtractor
{
    internal static class ExtractorProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            try { Application.Run(new ExtractorForm(Package.Read(Application.ExecutablePath))); }
            catch (Exception error)
            {
                MessageBox.Show("이 파일은 실행할 수 없습니다.\n" + error.Message,
                                "DK Self Extractor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class ExtractorForm : Form
    {
        private readonly PackageInfo package;
        private readonly TextBox destination, password;
        private readonly CheckBox overwrite;
        private readonly Button extract;
        private readonly Label status;
        private readonly ProgressBar progress;

        internal ExtractorForm(PackageInfo info)
        {
            package = info;
            Text = String.IsNullOrWhiteSpace(info.Name) ? "DK Self Extractor" : info.Name;
            ClientSize = new Size(565, info.Encrypted ? 452 : 393);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 251);
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var header = new Panel { Bounds = new Rectangle(0, 0, 565, 90), BackColor = Color.White };
            Controls.Add(header);
            header.Controls.Add(new Label {
                Text = "파일 압축 해제", Font = new Font("Segoe UI Semibold", 19f, FontStyle.Bold),
                ForeColor = Color.FromArgb(29, 41, 64), Bounds = new Rectangle(26, 15, 510, 40)
            });
            header.Controls.Add(new Label {
                Text = "원하는 폴더를 확인한 후 압축 해제를 시작하세요.",
                ForeColor = Color.FromArgb(107, 119, 139), Bounds = new Rectangle(28, 58, 520, 24)
            });

            if (!String.IsNullOrEmpty(info.Note))
            {
                var memo = new Label {
                    Text = info.Note, ForeColor = Color.FromArgb(64, 79, 105),
                    Bounds = new Rectangle(28, 101, 509, 43), AutoEllipsis = true
                };
                Controls.Add(memo);
            }

            int top = 153;
            AddLabel("압축 해제 위치", top);
            destination = new TextBox {
                Bounds = new Rectangle(28, top + 28, 396, 28), BorderStyle = BorderStyle.FixedSingle,
                Text = Environment.ExpandEnvironmentVariables(info.Destination), Font = new Font("Segoe UI", 10f)
            };
            Controls.Add(destination);
            AddButton("찾아보기", 432, top + 26, 106, 32, delegate {
                using (var dialog = new FolderBrowserDialog { Description = "압축 해제 폴더 선택", SelectedPath = destination.Text })
                    if (dialog.ShowDialog(this) == DialogResult.OK) destination.Text = dialog.SelectedPath;
            }, false);

            overwrite = new CheckBox {
                Text = "기존 파일 덮어쓰기", Checked = true, AutoSize = true,
                Location = new Point(28, 224), ForeColor = Color.FromArgb(64, 79, 105)
            };
            Controls.Add(overwrite);

            if (info.Encrypted)
            {
                AddLabel("암호", 262);
                password = new TextBox {
                    Bounds = new Rectangle(28, 292, 510, 29), BorderStyle = BorderStyle.FixedSingle,
                    UseSystemPasswordChar = true, Font = new Font("Segoe UI", 10f)
                };
                Controls.Add(password);
            }

            int start = info.Encrypted ? 336 : 276;
            extract = AddButton("압축 해제", 28, start, 510, 43, delegate { BeginExtract(); }, true);
            progress = new ProgressBar {
                Bounds = new Rectangle(28, start + 54, 510, 8), Minimum = 0, Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };
            Controls.Add(progress);
            status = new Label {
                Text = "준비됨", Bounds = new Rectangle(28, start + 71, 510, 24),
                ForeColor = Color.FromArgb(107, 119, 139), AutoEllipsis = true
            };
            Controls.Add(status);
        }

        private void AddLabel(string content, int y)
        {
            Controls.Add(new Label {
                Text = content, Bounds = new Rectangle(28, y, 500, 25),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(64, 79, 105)
            });
        }

        private Button AddButton(string text, int x, int y, int width, int height,
                                 EventHandler handler, bool primary)
        {
            var button = new Button {
                Text = text, Bounds = new Rectangle(x, y, width, height),
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Color.FromArgb(48, 99, 224) : Color.White,
                ForeColor = primary ? Color.White : Color.FromArgb(48, 70, 107),
                Font = primary ? new Font("Segoe UI Semibold", 11f, FontStyle.Bold) : Font,
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(213, 220, 232);
            button.Click += handler;
            Controls.Add(button);
            return button;
        }

        private void BeginExtract()
        {
            string path = destination.Text.Trim();
            if (String.IsNullOrWhiteSpace(path))
            {
                MessageBox.Show(this, "압축 해제 폴더를 입력하세요.", "입력 확인",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            string secret = password == null ? "" : password.Text;
            if (package.Encrypted && secret.Length == 0)
            {
                MessageBox.Show(this, "암호를 입력하세요.", "암호 필요",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            extract.Enabled = false;
            status.Text = "압축 데이터 확인 중...";
            bool replace = overwrite.Checked;
            Task.Run(() =>
            {
                string temporary = Package.UnpackPayload(Application.ExecutablePath, package, secret);
                try
                {
                    Package.Extract(temporary, path, replace, (done, total) =>
                    {
                        int pct = total == 0 ? 100 : (int)((long)done * 100 / total);
                        if (IsHandleCreated && !IsDisposed)
                            BeginInvoke(new Action(() => { progress.Value = pct; status.Text = "압축 해제 중... " + pct + "%"; }));
                    });
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }).ContinueWith(task =>
            {
                extract.Enabled = true;
                if (task.IsFaulted)
                {
                    status.Text = "압축 해제 실패";
                    MessageBox.Show(this, task.Exception.GetBaseException().Message, "압축 해제 실패",
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    progress.Value = 100;
                    status.Text = "압축 해제 완료";
                    MessageBox.Show(this, "파일 압축 해제가 완료되었습니다.", "완료",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
