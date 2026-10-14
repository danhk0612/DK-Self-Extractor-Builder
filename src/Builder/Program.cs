using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace DKSelfExtractor
{
    internal static class BuilderProgram
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new BuilderForm());
        }
    }

    internal sealed class BuilderForm : Form
    {
        private readonly TextBox source, output, destination, note, password;
        private readonly ComboBox compression;
        private readonly Button create;
        private readonly Label status;

        internal BuilderForm()
        {
            Text = "DK Self Extractor Builder";
            ClientSize = new Size(700, 647);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 251);
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            var heading = new Panel { Bounds = new Rectangle(0, 0, 700, 103), BackColor = Color.White };
            Controls.Add(heading);
            heading.Controls.Add(new Label {
                Text = "Self Extractor Builder", Font = new Font("Segoe UI Semibold", 20f, FontStyle.Bold),
                ForeColor = Color.FromArgb(29, 41, 64), Bounds = new Rectangle(30, 19, 620, 38)
            });
            heading.Controls.Add(new Label {
                Text = "폴더를 하나의 자동 압축 해제 EXE로 만듭니다.",
                ForeColor = Color.FromArgb(107, 119, 139), Bounds = new Rectangle(32, 62, 620, 23)
            });

            AddLabel("원본 폴더", 127);
            source = AddInput(158, 510);
            AddButton("찾아보기", 558, 158, 110, delegate {
                using (var dialog = new FolderBrowserDialog { Description = "EXE로 묶을 폴더 선택" })
                {
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                    {
                        source.Text = dialog.SelectedPath;
                        string name = Path.GetFileName(dialog.SelectedPath.TrimEnd('\\'));
                        if (String.IsNullOrWhiteSpace(output.Text))
                            output.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), name + ".exe");
                        if (String.IsNullOrWhiteSpace(destination.Text))
                            destination.Text = @"%USERPROFILE%\Downloads\" + name;
                    }
                }
            });

            AddLabel("저장할 EXE 파일", 204);
            output = AddInput(235, 510);
            AddButton("저장 위치", 558, 235, 110, delegate {
                using (var dialog = new SaveFileDialog { Filter = "실행 파일 (*.exe)|*.exe", FileName = Path.GetFileName(output.Text), AddExtension = true })
                    if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.FileName;
            });

            AddLabel("압축 해제 기본 경로", 281);
            destination = AddInput(312, 638);

            AddLabel("압축률", 360);
            compression = new ComboBox {
                Location = new Point(30, 391), Size = new Size(250, 30),
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f)
            };
            compression.Items.AddRange(new object[] { "무압축 (속도 우선)", "빠른 압축", "최적 압축 (용량 우선)" });
            compression.SelectedIndex = 2;
            Controls.Add(compression);

            AddLabel("암호 (비워두면 사용하지 않음)", 436);
            password = AddInput(467, 300);
            password.UseSystemPasswordChar = true;

            AddLabel("메모 / 안내 문구", 436, 366);
            note = AddInput(467, 302, 366);
            note.Text = "";

            create = AddButton("EXE 생성", 30, 540, 638, delegate { BeginBuild(); });
            create.Height = 45;
            create.BackColor = Color.FromArgb(48, 99, 224);
            create.ForeColor = Color.White;
            create.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);

            status = new Label {
                Text = "압축 파일에 별도 압축 프로그램은 필요하지 않습니다.",
                ForeColor = Color.FromArgb(94, 109, 130),
                Bounds = new Rectangle(30, 600, 638, 28), AutoEllipsis = true
            };
            Controls.Add(status);
        }

        private void AddLabel(string text, int y, int x = 30)
        {
            Controls.Add(new Label {
                Text = text, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(64, 79, 105),
                Location = new Point(x, y), Size = new Size(302, 25)
            });
        }

        private TextBox AddInput(int y, int width, int x = 30)
        {
            var item = new TextBox {
                Location = new Point(x, y), Size = new Size(width, 29),
                Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            Controls.Add(item);
            return item;
        }

        private Button AddButton(string text, int x, int y, int width, EventHandler handler)
        {
            var button = new Button {
                Text = text, Bounds = new Rectangle(x, y, width, 31),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White,
                ForeColor = Color.FromArgb(48, 70, 107),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(213, 220, 232);
            button.Click += handler;
            Controls.Add(button);
            return button;
        }

        private void BeginBuild()
        {
            string src = source.Text.Trim();
            string exe = output.Text.Trim();
            string target = destination.Text.Trim();
            string comment = note.Text;
            string secret = password.Text;
            int level = compression.SelectedIndex;
            if (!Directory.Exists(src) || String.IsNullOrWhiteSpace(exe) || String.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show(this, "원본 폴더, EXE 저장 위치, 압축 해제 경로를 확인하세요.", "입력 확인",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                string fullSource = Path.GetFullPath(src).TrimEnd('\\') + "\\";
                string fullOutput = Path.GetFullPath(exe);
                if (!fullOutput.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("출력 파일은 .exe 확장자를 사용해야 합니다.");
                if (fullOutput.StartsWith(fullSource, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("출력 EXE를 원본 폴더 내부에 저장할 수 없습니다.");
                if (File.Exists(fullOutput) && MessageBox.Show(this, "기존 EXE를 덮어쓰시겠습니까?",
                    "기존 파일", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "경로 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            create.Enabled = false;
            status.Text = "압축 및 EXE 생성 중...";
            Task.Run(() => Build(src, exe, target, comment, secret, level))
                .ContinueWith(task =>
                {
                    create.Enabled = true;
                    if (task.IsFaulted)
                    {
                        status.Text = "생성 실패";
                        MessageBox.Show(this, task.Exception.GetBaseException().Message, "생성 실패",
                                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                    }
                    else
                    {
                        status.Text = "완료: " + exe;
                        MessageBox.Show(this, "자동 압축 해제 EXE를 생성했습니다.\n" + exe, "생성 완료",
                                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private static void Build(string sourcePath, string targetExe, string extractPath,
                                  string memo, string password, int level)
        {
            string zip = Package.TempPath();
            string stub = Package.TempPath();
            string assembled = Package.TempPath();
            try
            {
                CompressionLevel compression = level == 0 ? CompressionLevel.NoCompression
                    : level == 1 ? CompressionLevel.Fastest : CompressionLevel.Optimal;
                Package.CreateZip(sourcePath, zip, compression);
                using (var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("DKSelfExtractor.Stub.exe"))
                {
                    if (embedded == null) throw new InvalidOperationException("압축 해제 실행 파일이 내장되지 않았습니다.");
                    using (var output = new FileStream(stub, FileMode.CreateNew, FileAccess.Write))
                        Package.Copy(embedded, output);
                }
                Package.WriteExe(stub, zip, assembled, Path.GetFileNameWithoutExtension(targetExe), extractPath, memo, password);
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(targetExe)));
                File.Copy(assembled, targetExe, true);
            }
            finally
            {
                if (File.Exists(zip)) File.Delete(zip);
                if (File.Exists(stub)) File.Delete(stub);
                if (File.Exists(assembled)) File.Delete(assembled);
            }
        }
    }
}
