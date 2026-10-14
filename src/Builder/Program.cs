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
            ClientSize = new Size(695, 508);
            AutoScaleMode = AutoScaleMode.Dpi;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 247, 251);
            Font = new Font("Segoe UI", 10f);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

            AddLabel("항목", 22, 24, 285);
            AddButton("파일", 313, 15, 105, delegate
            {
                using (var dialog = new OpenFileDialog {
                    Title = "파일 선택",
                    Filter = "모든 파일 (*.*)|*.*", Multiselect = true
                })
                    if (dialog.ShowDialog(this) == DialogResult.OK)
                        AddSources(dialog.FileNames);
            });
            AddButton("폴더", 423, 15, 105, delegate
            {
                try { AddSources(MultiFolderPicker.Choose(Handle)); }
                catch (Exception error)
                {
                    MessageBox.Show(this, error.Message, "폴더 선택 실패", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            });
            AddButton("제거", 533, 15, 138, delegate { RemoveSelectedSources(); });

            sources = new ListView {
                Bounds = new Rectangle(24, 57, 647, 166),
                View = View.Details, FullRowSelect = true, MultiSelect = true,
                GridLines = false, HideSelection = false,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White, ForeColor = Color.FromArgb(53, 46, 89),
                Font = new Font("Segoe UI", 9.5f)
            };
            // Explorer file/folder drops are handled both on the list and unused form surface.
            AllowDrop = true;
            sources.AllowDrop = true;
            DragEnter += OnSourceDragEnter;
            DragDrop += OnSourceDragDrop;
            sources.DragEnter += OnSourceDragEnter;
            sources.DragDrop += OnSourceDragDrop;
            sources.KeyDown += OnSourceKeyDown;

            sources.Columns.Add("유형", 65);
            sources.Columns.Add("이름", 177);
            sources.Columns.Add("경로", 382);
            Controls.Add(sources);

            AddLabel("EXE 경로", 244);
            output = AddInput(271, 518);
            AddButton("찾기", 555, 270, 116, delegate
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

            AddLabel("해제 경로", 313);
            destination = AddInput(340, 647);

            advancedToggle = AddButton("▸ 고급", 24, 382, 647, delegate
            {
                SetAdvanced(!advancedPanel.Visible);
            });
            advancedToggle.TextAlign = ContentAlignment.MiddleLeft;
            advancedToggle.BackColor = Color.FromArgb(238, 235, 245);
            advancedToggle.FlatAppearance.MouseOverBackColor = Color.FromArgb(222, 216, 236);

            advancedPanel = new Panel {
                Bounds = new Rectangle(24, 422, 647, 167),
                BackColor = BackColor, Visible = false
            };
            Controls.Add(advancedPanel);

            AddLabel("압축률", 0, 0, 225, advancedPanel);
            compression = new ComboBox {
                Location = new Point(0, 26), Size = new Size(233, 30),
                DropDownStyle = ComboBoxStyle.DropDownList, FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI", 10f)
            };
            compression.Items.AddRange(new object[] { "무압축", "빠름", "최적" });
            compression.SelectedIndex = 2;
            advancedPanel.Controls.Add(compression);

            AddLabel("암호", 0, 247, 400, advancedPanel);
            password = AddInput(26, 400, 247, advancedPanel);
            password.UseSystemPasswordChar = true;

            AddLabel("메모", 66, 0, 340, advancedPanel);
            note = AddInput(90, 647, 0, advancedPanel);
            note.Multiline = true;
            note.ScrollBars = ScrollBars.Vertical;
            note.Height = 76;
            note.AcceptsReturn = true;

            create = AddButton("생성", 24, 424, 647, delegate { BeginBuild(); });
            create.Height = 43;
            create.BackColor = Color.FromArgb(53, 46, 89);
            create.ForeColor = Color.White;
            create.Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold);
            create.FlatAppearance.MouseOverBackColor = Color.FromArgb(68, 58, 109);
            create.FlatAppearance.MouseDownBackColor = Color.FromArgb(43, 36, 74);

            status = new Label {
                Text = "", ForeColor = Color.FromArgb(110, 102, 131),
                Bounds = new Rectangle(24, 475, 647, 24), AutoEllipsis = true
            };
            Controls.Add(status);
            SetAdvanced(false);
        }

        private void SetAdvanced(bool expanded)
        {
            advancedPanel.Visible = expanded;
            advancedToggle.Text = expanded ? "▾ 고급" : "▸ 고급";
            create.Top = expanded ? 594 : 423;
            status.Top = create.Bottom + 9;
            ClientSize = new Size(695, status.Bottom + 12);
        }

        private void OnSourceDragEnter(object sender, DragEventArgs e)
        {
            e.Effect = e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy : DragDropEffects.None;
        }

        private void OnSourceDragDrop(object sender, DragEventArgs e)
        {
            if (e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null) AddSources(paths);
        }

        private void OnSourceKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode != Keys.Delete) return;
            RemoveSelectedSources();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        private void RemoveSelectedSources()
        {
            foreach (ListViewItem item in sources.SelectedItems.Cast<ListViewItem>().ToArray())
                sources.Items.Remove(item);
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
                ForeColor = Color.FromArgb(68, 59, 96),
                Location = new Point(x, y), Size = new Size(width, 24)
            });
        }

        private TextBox AddInput(int y, int width, int x = 24, Control parent = null)
        {
            var item = new TextBox {
                Location = new Point(x, y), Size = new Size(width, 29),
                Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White, ForeColor = Color.FromArgb(53, 46, 89)
            };
            (parent ?? this).Controls.Add(item);
            return item;
        }

        private Button AddButton(string text, int x, int y, int width, EventHandler handler)
        {
            var background = Color.FromArgb(235, 231, 244);
            var hover = Color.FromArgb(218, 211, 236);
            var button = new Button {
                Text = text, Bounds = new Rectangle(x, y, width, 34),
                FlatStyle = FlatStyle.Flat, BackColor = background,
                ForeColor = Color.FromArgb(53, 46, 89), Cursor = Cursors.Hand,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                UseVisualStyleBackColor = false
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(200, 191, 223);
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
