using System;
using System.IO;
using System.IO.Compression;
using System.Drawing;
using System.Linq;
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
        private readonly ListView sources;
        private readonly TextBox output, destination, note, password;
        private readonly ComboBox compression;
        private readonly Button create;
        private readonly Label status;
        private readonly Panel advancedPanel;
        private readonly Button advancedToggle;

        internal BuilderForm()
        {
            Text = "DK Self Extractor Builder";
            ClientSize = new Size(695, 552);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 251);
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            AddLabel("파일 / 폴더", 19, 24, 250);
            AddButton("파일 추가", 313, 11, 105, delegate
            {
                using (var dialog = new OpenFileDialog {
                    Title = "파일 추가 (Ctrl/Shift로 여러 파일 선택)",
                    Filter = "모든 파일 (*.*)|*.*", Multiselect = true
                })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        AddSources(dialog.FileNames);
            });
            AddButton("폴더 추가", 423, 11, 105, delegate
            {
                try { AddSources(MultiFolderPicker.Choose(Handle)); }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, "폴더 선택 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
            AddButton("선택 제거", 533, 11, 138, delegate
            {
                foreach (ListViewItem item in sources.SelectedItems.Cast<ListViewItem>().ToArray())
                    sources.Items.Remove(item);
            });

            sources = new ListView {
                Bounds = new Rectangle(24, 54, 647, 211),
                View = View.Details, FullRowSelect = true, MultiSelect = true,
                GridLines = false, HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White, ForeColor = Color.FromArgb(48, 61, 82),
                Font = new Font("Segoe UI", 9.5f)
            };
            sources.Columns.Add("종류", 65);
            sources.Columns.Add("이름", 177);
            sources.Columns.Add("원본 경로", 382);
            Controls.Add(sources);

            AddLabel("저장할 EXE 파일 (미지정 시 자동 생성)", 284);
            output = AddInput(310, 518);
            AddButton("저장 위치", 555, 309, 116, delegate
            {
                string suggested = output.Text.Trim();
                if (suggested.Length == 0 && sources.Items.Count > 0)
                    suggested = DefaultExePath(SourcePaths());
                using (var dialog = new SaveFileDialog {
                    Filter = "실행 파일 (*.exe)|*.exe", FileName = Path.GetFileName(suggested),
                    InitialDirectory = Path.GetDirectoryName(suggested), AddExtension = true
                })
                    if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.FileName;
            });

            AddLabel("압축 해제 기본 경로", 354);
            destination = AddInput(380, 647);

            advancedToggle = AddButton("▸ 고급 설정", 24, 423, 647, delegate
            {
                SetAdvanced(!advancedPanel.Visible);
            });
            advancedToggle.TextAlign = ContentAlignment.MiddleLeft;

            advancedPanel = new Panel {
                Bounds = new Rectangle(24, 465, 647, 201),
                BackColor = BackColor, Visible = false
            };
            Controls.Add(advancedPanel);

            AddLabel("압축률", 4, 0, 225, advancedPanel);
            compression = new ComboBox {
                Location = new Point(0, 31), Size = new Size(233, 30),
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f)
            };
            compression.Items.AddRange(new object[] { "무압축 (속도 우선)", "빠른 압축", "최적 압축 (용량 우선)" });
            compression.SelectedIndex = 2;
            advancedPanel.Controls.Add(compression);

            AddLabel("암호 (선택)", 4, 247, 400, advancedPanel);
            password = AddInput(31, 400, 247, advancedPanel);
            password.UseSystemPasswordChar = true;

            AddLabel("메모 / 안내 문구 (선택)", 78, 0, 340, advancedPanel);
            note = AddInput(105, 647, 0, advancedPanel);
            note.Multiline = true;
            note.ScrollBars = ScrollBars.Vertical;
            note.Height = 85;

            create = AddButton("EXE 생성", 24, 466, 647, delegate { BeginBuild(); });
            create.Height = 43;
            create.BackColor = Color.FromArgb(48, 99, 224);
            create.ForeColor = Color.White;
            create.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);

            status = new Label {
                Text = "", ForeColor = Color.FromArgb(94, 109, 130),
                Bounds = new Rectangle(24, 520, 647, 24), AutoEllipsis = true
            };
            Controls.Add(status);
            SetAdvanced(false);
        }

        private void SetAdvanced(bool expanded)
        {
            advancedPanel.Visible = expanded;
            advancedToggle.Text = expanded ? "▾ 고급 설정" : "▸ 고급 설정";
            create.Top = expanded ? 680 : 466;
            status.Top = create.Bottom + 10;
            ClientSize = new Size(695, status.Bottom + 13);
        }

        private void AddSources(string[] paths)
        {
            if (paths == null || paths.Length == 0) return;
            bool wasEmpty = sources.Items.Count == 0;
            foreach (string path in paths)
            {
                string full = Path.GetFullPath(path);
                if (sources.Items.Cast<ListViewItem>().Any(item =>
                    String.Equals((string)item.Tag, full, StringComparison.OrdinalIgnoreCase))) continue;
                bool folder = Directory.Exists(full);
                if (!folder && !File.Exists(full)) continue;
                string name = Path.GetFileName(full.TrimEnd('\\', '/'));
                var itemToAdd = new ListViewItem(folder ? "폴더" : "파일");
                itemToAdd.SubItems.Add(name);
                itemToAdd.SubItems.Add(full);
                itemToAdd.Tag = full;
                sources.Items.Add(itemToAdd);
            }
            if (wasEmpty && sources.Items.Count > 0)
            {
                if (String.IsNullOrWhiteSpace(output.Text))
                    output.Text = DefaultExePath(SourcePaths());
                if (String.IsNullOrWhiteSpace(destination.Text))
                {
                    string first = (string)sources.Items[0].Tag;
                string folder = Directory.Exists(first) ? first : Path.GetDirectoryName(first);
                    destination.Text = @"%USERPROFILE%\Downloads\" + Path.GetFileName(folder.TrimEnd('\\', '/'));
                }
            }
        }

        private string[] SourcePaths()
        {
            return sources.Items.Cast<ListViewItem>().Select(item => (string)item.Tag).ToArray();
        }

        // When output is blank, name the EXE after the first source folder and
        // place it beside that folder. For a file, use its containing folder.
        internal static string DefaultExePath(string[] paths)
        {
            if (paths == null || paths.Length == 0)
                throw new ArgumentException("추가할 파일 또는 폴더가 없습니다.");
            string first = Path.GetFullPath(paths[0]);
            string folder = Directory.Exists(first) ? first : Path.GetDirectoryName(first);
            string trimmed = folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string parent = Path.GetDirectoryName(trimmed);
            string name = Path.GetFileName(trimmed);
            if (String.IsNullOrEmpty(parent) || String.IsNullOrEmpty(name))
                throw new ArgumentException("기본 EXE 저장 위치를 결정할 수 없습니다. 저장 위치를 직접 지정하세요.");
            return Path.Combine(parent, name + ".exe");
        }

        private void AddLabel(string label, int y, int x = 24, int width = 350, Control parent = null)
        {
            (parent ?? this).Controls.Add(new Label {
                Text = label, Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(64, 79, 105),
                Location = new Point(x, y), Size = new Size(width, 24)
            });
        }

        private TextBox AddInput(int y, int width, int x = 24, Control parent = null)
        {
            var item = new TextBox {
                Location = new Point(x, y), Size = new Size(width, 29),
                Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            (parent ?? this).Controls.Add(item);
            return item;
        }

        private Button AddButton(string text, int x, int y, int width, EventHandler handler)
        {
            var button = new Button {
                Text = text, Bounds = new Rectangle(x, y, width, 32),
                FlatStyle = FlatStyle.Flat, BackColor = Color.White,
                ForeColor = Color.FromArgb(48, 70, 107), Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(213, 220, 232);
            button.Click += handler;
            Controls.Add(button);
            return button;
        }

        private void BeginBuild()
        {
            string[] items = SourcePaths();
            string target = destination.Text.Trim();
            string exe = output.Text.Trim();
            string comment = note.Text;
            string secret = password.Text;
            int level = compression.SelectedIndex;
            if (items.Length == 0 || String.IsNullOrWhiteSpace(target))
            {
                MessageBox.Show(this, "파일 또는 폴더를 추가하고 압축 해제 경로를 확인하세요.", "입력 확인",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                if (exe.Length == 0) exe = DefaultExePath(items);
                exe = Path.GetFullPath(exe);
                if (!exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("출력 파일은 .exe 확장자를 사용해야 합니다.");
                foreach (string item in items)
                {
                    string full = Path.GetFullPath(item);
                    if (String.Equals(exe, full, StringComparison.OrdinalIgnoreCase) ||
                        (Directory.Exists(full) && exe.StartsWith(
                            full.TrimEnd('\\', '/') + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                        throw new ArgumentException("출력 EXE를 선택한 원본 파일 또는 폴더 내부에 저장할 수 없습니다.");
                }
                if (File.Exists(exe) && MessageBox.Show(this, "기존 EXE를 덮어쓰시겠습니까?",
                    "기존 파일", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            }
            catch (Exception error)
            {
                MessageBox.Show(this, error.Message, "경로 오류", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            create.Enabled = false;
            status.Text = "압축 및 EXE 생성 중...";
            Task.Run(() => Build(items, exe, target, comment, secret, level))
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

        private static void Build(string[] sourcePaths, string targetExe, string extractPath,
                                  string memo, string password, int level)
        {
            string zip = Package.TempPath();
            string stub = Package.TempPath();
            string assembled = Package.TempPath();
            try
            {
                CompressionLevel compression = level == 0 ? CompressionLevel.NoCompression
                    : level == 1 ? CompressionLevel.Fastest : CompressionLevel.Optimal;
                Package.CreateZip(sourcePaths, zip, compression);
                using (var embedded = Assembly.GetExecutingAssembly().GetManifestResourceStream("DKSelfExtractor.Stub.exe"))
                {
                    if (embedded == null) throw new InvalidOperationException("압축 해제 실행 파일이 내장되지 않았습니다.");
                    using (var outputStream = new FileStream(stub, FileMode.CreateNew, FileAccess.Write))
                        Package.Copy(embedded, outputStream);
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
