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
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi;
            BackColor = Color.FromArgb(248, 250, 253);
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int top = 23;
            if (!String.IsNullOrWhiteSpace(info.Note))
            {
                AddLabel("안내 문구", top);
                var memo = new TextBox {
                    Text = info.Note,
                    Bounds = new Rectangle(25, top + 29, 512, 83),
                    ReadOnly = true, Multiline = true, ScrollBars = ScrollBars.Vertical,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.White, ForeColor = Color.FromArgb(56, 72, 97),
                    Font = new Font("Segoe UI", 9.5f),
                    TabStop = false
                };
                Controls.Add(memo);
                top += 128;
            }

            AddLabel("압축 해제 위치", top);
            destination = new TextBox {
                Bounds = new Rectangle(25, top + 29, 392, 29),
                BorderStyle = BorderStyle.FixedSingle,
                Text = Environment.ExpandEnvironmentVariables(info.Destination),
                Font = new Font("Segoe UI", 10f)
            };
            Controls.Add(destination);
            AddButton("찾아보기", 425, top + 27, 112, 33, delegate {
                using (var dialog = new FolderBrowserDialog {
                    Description = "압축 해제 폴더 선택", SelectedPath = destination.Text
                })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        destination.Text = dialog.SelectedPath;
            }, false);

            overwrite = new CheckBox {
                Text = "기존 파일 덮어쓰기", Checked = true, AutoSize = true,
                Location = new Point(25, top + 74),
                ForeColor = Color.FromArgb(56, 72, 97)
            };
            Controls.Add(overwrite);

            int next = top + 110;
            if (info.Encrypted)
            {
                AddLabel("암호", next);
                password = new TextBox {
                    Bounds = new Rectangle(25, next + 27, 512, 29),
                    BorderStyle = BorderStyle.FixedSingle,
                    UseSystemPasswordChar = true, Font = new Font("Segoe UI", 10f)
                };
                Controls.Add(password);
                next += 70;
            }

            extract = AddButton("압축 해제", 25, next, 512, 43, delegate {
                BeginExtract();
            }, true);
            progress = new ProgressBar {
                Bounds = new Rectangle(25, next + 54, 512, 7),
                Minimum = 0, Maximum = 100, Style = ProgressBarStyle.Continuous
            };
            Controls.Add(progress);
            status = new Label {
                Text = "준비됨", Bounds = new Rectangle(25, next + 70, 512, 24),
                ForeColor = Color.FromArgb(103, 119, 140), AutoEllipsis = true
            };
            Controls.Add(status);
            ClientSize = new Size(563, status.Bottom + 12);
        }

        private void AddLabel(string content, int y)
        {
            Controls.Add(new Label {
                Text = content, Bounds = new Rectangle(25, y, 512, 25),
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(64, 79, 105)
            });
        }

        private Button AddButton(string text, int x, int y, int width, int height,
                                 EventHandler handler, bool primary)
        {
            var button = new Button {
                Text = text, Bounds = new Rectangle(x, y, width, height),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(48, 99, 224) : Color.FromArgb(234, 240, 252),
                ForeColor = primary ? Color.White : Color.FromArgb(39, 72, 130),
                Font = new Font("Segoe UI Semibold", primary ? 11f : 9.5f, FontStyle.Bold),
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = primary ? Color.FromArgb(39, 84, 199) : Color.FromArgb(217, 229, 251);
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(29, 70, 181) : Color.FromArgb(198, 216, 246);
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
