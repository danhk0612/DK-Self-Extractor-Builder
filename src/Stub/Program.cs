using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
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
        private readonly CheckBox overwrite, openAfterExtract;
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
            BackColor = Color.FromArgb(250, 249, 252);
            Font = new Font("Segoe UI", 9f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            int top = 10;
            if (!String.IsNullOrWhiteSpace(info.Note))
            {
                AddLabel("메모", top);
                var memo = new TextBox {
                    Text = info.Note,
                    Bounds = new Rectangle(14, top + 19, 382, 55),
                    ReadOnly = true, Multiline = true,
                    ScrollBars = ScrollBars.Vertical,
                    BorderStyle = BorderStyle.FixedSingle,
                    BackColor = Color.White, ForeColor = Color.FromArgb(53, 46, 89),
                    Font = new Font("Segoe UI", 9f),
                    TabStop = false
                };
                Controls.Add(memo);
                top += 82;
            }

            AddLabel("해제 경로", top);
            destination = new TextBox {
                Bounds = new Rectangle(14, top + 19, 302, 25),
                BorderStyle = BorderStyle.FixedSingle,
                Text = Environment.ExpandEnvironmentVariables(info.Destination),
                BackColor = Color.White, ForeColor = Color.FromArgb(53, 46, 89),
                Font = new Font("Segoe UI", 9f)
            };
            Controls.Add(destination);
            AddButton("찾기", 323, top + 18, 73, 27, delegate {
                using (var dialog = new FolderBrowserDialog {
                    Description = "폴더 선택", SelectedPath = destination.Text
                })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        destination.Text = dialog.SelectedPath;
            }, false);
            top += 55;

            if (info.Encrypted)
            {
                AddLabel("암호", top);
                password = new TextBox {
                    Bounds = new Rectangle(14, top + 19, 382, 25),
                    BorderStyle = BorderStyle.FixedSingle,
                    UseSystemPasswordChar = true,
                    BackColor = Color.White, ForeColor = Color.FromArgb(53, 46, 89),
                    Font = new Font("Segoe UI", 9f)
                };
                Controls.Add(password);
                top += 50;
            }

            overwrite = new CheckBox {
                Text = "덮어쓰기", Checked = true,
                AutoSize = true, Location = new Point(14, top + 2),
                ForeColor = Color.FromArgb(53, 46, 89)
            };
            Controls.Add(overwrite);
            openAfterExtract = new CheckBox {
                Text = "해제 후 열기", Checked = true,
                AutoSize = true, Location = new Point(125, top + 2),
                ForeColor = Color.FromArgb(53, 46, 89)
            };
            Controls.Add(openAfterExtract);

            int footer = top + 32;
            status = new Label {
                Text = "준비됨",
                Bounds = new Rectangle(14, footer + 5, 257, 20),
                ForeColor = Color.FromArgb(100, 94, 115),
                AutoEllipsis = true
            };
            Controls.Add(status);
            extract = AddButton("해제", 280, footer, 116, 29,
                                delegate { BeginExtract(); }, true);

            progress = new ProgressBar {
                Bounds = new Rectangle(14, footer + 35, 382, 5),
                Minimum = 0, Maximum = 100,
                Style = ProgressBarStyle.Continuous, Visible = false
            };
            Controls.Add(progress);
            ClientSize = new Size(410, footer + 46);
        }

        private void AddLabel(string content, int y)
        {
            Controls.Add(new Label {
                Text = content, Bounds = new Rectangle(14, y, 382, 18),
                Font = new Font("Segoe UI Semibold", 9f, FontStyle.Bold),
                ForeColor = Color.FromArgb(53, 46, 89)
            });
        }

        private Button AddButton(string text, int x, int y, int width, int height,
                                 EventHandler handler, bool primary)
        {
            var button = new Button {
                Text = text, Bounds = new Rectangle(x, y, width, height),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Color.FromArgb(53, 46, 89)
                                    : Color.FromArgb(244, 242, 248),
                ForeColor = primary ? Color.White : Color.FromArgb(53, 46, 89),
                Font = new Font("Segoe UI", 9f),
                Cursor = Cursors.Hand, UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = primary ? 0 : 1;
            button.FlatAppearance.BorderColor = Color.FromArgb(217, 213, 227);
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(68, 58, 109) : Color.FromArgb(230, 225, 240);
            button.FlatAppearance.MouseDownBackColor = primary
                ? Color.FromArgb(43, 36, 74) : Color.FromArgb(211, 204, 228);
            button.Click += handler;
            Controls.Add(button);
            return button;
        }

        internal static void RunPostCommand(PackageInfo info, string destination)
        {
            if (String.IsNullOrWhiteSpace(info.PostCommand)) return;

            string scriptPath = Path.ChangeExtension(Package.TempPath(),
                                                      info.CommandShell == 1 ? ".cmd" : ".ps1");
            try
            {
                File.WriteAllText(scriptPath, info.PostCommand,
                                  info.CommandShell == 1 ? Encoding.Default : new UTF8Encoding(true));
                string executable = info.CommandShell == 1
                    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe")
                    : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
                                   "WindowsPowerShell", "v1.0", "powershell.exe");
                string arguments = info.CommandShell == 1
                    ? "/D /S /C \"\"" + scriptPath + "\"\""
                    : "-NoProfile -ExecutionPolicy Bypass -File \"" + scriptPath + "\"";
                var start = new ProcessStartInfo {
                    FileName = executable,
                    Arguments = arguments,
                    WorkingDirectory = Path.GetFullPath(Environment.ExpandEnvironmentVariables(destination)),
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    WindowStyle = ProcessWindowStyle.Normal
                };
                using (var process = Process.Start(start))
                {
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new InvalidOperationException("작업 후 명령줄 실패 (종료 코드 " + process.ExitCode + ")");
                }
            }
            finally
            {
                if (File.Exists(scriptPath)) File.Delete(scriptPath);
            }
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
            progress.Value = 0;
            progress.Visible = true;
            status.Text = "압축 데이터 확인 중...";
            bool replace = overwrite.Checked;
            bool openFolder = openAfterExtract.Checked;
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
                if (!String.IsNullOrWhiteSpace(package.PostCommand))
                {
                    if (IsHandleCreated && !IsDisposed)
                        BeginInvoke(new Action(() => status.Text = "작업 후 명령줄 실행 중..."));
                    RunPostCommand(package, path);
                }
            }).ContinueWith(task =>
            {
                extract.Enabled = true;
                progress.Visible = false;
                if (task.IsFaulted)
                {
                    string error = task.Exception.GetBaseException().Message;
                    status.Text = error.StartsWith("작업 후 명령줄", StringComparison.Ordinal)
                        ? "작업 후 명령줄 실패" : "압축 해제 실패";
                    MessageBox.Show(this, error, status.Text,
                                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    progress.Value = 100;
                    status.Text = "압축 해제 완료";
                    MessageBox.Show(this, "파일 압축 해제가 완료되었습니다.", "완료",
                                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                    if (openFolder)
                        Process.Start(new ProcessStartInfo {
                            FileName = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)),
                            UseShellExecute = true
                        });
                }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
