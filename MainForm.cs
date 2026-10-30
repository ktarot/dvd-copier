
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32.SafeHandles;

namespace DvdCopier;

public sealed class MainForm : Form
{
    private readonly ComboBox drives = new()
    {
        Dock = DockStyle.Fill,
        DropDownStyle = ComboBoxStyle.DropDownList
    };

    private readonly Label driveStatus = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Text = "대기 중"
    };

    private readonly TextBox folderPath = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        BorderStyle = BorderStyle.FixedSingle,
        Text = "저장 폴더를 선택하세요"
    };

    private readonly TextBox log = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        Multiline = true,
        ScrollBars = ScrollBars.Vertical,
        BorderStyle = BorderStyle.None
    };

    private readonly LedMeter meter = new();
    private readonly List<Panel> cards = new();
    private readonly List<Button> buttons = new();

    private readonly Button openButton;
    private readonly Button closeButton;
    private readonly Label driveKind = new() { Dock = DockStyle.Fill, Text = "ODD", TextAlign = ContentAlignment.MiddleCenter };
    private readonly Button browseButton;
    private string? selectedDestination;
    private readonly Button startButton;
    private readonly Button stopButton;
    private readonly Button themeButton;

    private readonly System.Windows.For
    public MainForm()
    {
        Text = "ODD Copier v2.0";
        Size = new Size(360, 640);
        MinimumSize = Size;
        MaximumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.None;

        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8, Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 32, 66, 64, 122, 68, 68, 0, 42 })
            root.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100)
                                          : new RowStyle(SizeType.Absolute, height));
        Controls.Add(root);

        root.Controls.Add(Label("ODD COPIER  /  v2.0", 14, true), 0, 0);

        // 1. Optical drive family, drive letter and disc-ready status.
        var odd = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        foreach (int pc in new[] { 28, 29, 43 })
            odd.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, pc));
        odd.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        odd.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        odd.Controls.Add(Label("ODD 종류", 8), 0, 0);
        odd.Controls.Add(Label("경로", 8), 1, 0);
        odd.Controls.Add(Label("디스크", 8), 2, 0);
        odd.Controls.Add(driveKind, 0, 1);
        odd.Controls.Add(drives, 1, 1);
        odd.Controls.Add(driveStatus, 2, 1);
        root.Controls.Add(Card(odd), 0, 1);

        // 2. Destination folder.
        var dest = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
        dest.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 69));
        dest.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 31));
        dest.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        dest.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        dest.Controls.Add(Label("저장 폴더", 8), 0, 0);
        dest.Controls.Add(folderPath, 0, 1);
        browseButton = Button("선택", "neutral", 9);
        dest.Controls.Add(browseButton, 1, 1);
        root.Controls.Add(Card(dest), 0, 2);

        // 3. LED dot-matrix progress bar with percentage embedded.
        meter.Dock = DockStyle.Fill;
        root.Controls.Add(Card(meter), 0, 3);

        // 4. Open / Close
        var tray = TwoColumns();
        openButton = Button("⏏  열기", "open", 14);
        closeButton = Button("⏷  닫기", "close", 14);
        tray.Controls.Add(openButton, 0, 0);
        tray.Controls.Add(closeButton, 1, 0);
        root.Controls.Add(tray, 0, 4);

        // 5. Start / Stop copy
        var copy = TwoColumns();
        startButton = Button("▶  복사 시작", "start", 12);
        stopButton = Button("■  복사 중지", "neutral", 12);
        copy.Controls.Add(startButton, 0, 0);
        copy.Controls.Add(stopButton, 1, 0);
        root.Controls.Add(copy, 0, 5);

        // 6. Status messages
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        status.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        status.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        status.Controls.Add(Label("상태 메시지", 9, true), 0, 0);
        status.Controls.Add(log, 0, 1);
        root.Controls.Add(Card(status), 0, 6);

        // 7. Exit / Theme
        var footer = TwoColumns();
        var exitButton = Button("⏻  종료", "neutral", 10);
        themeButton = Button("☀ Light", "neutral", 10);
        footer.Controls.Add(exitButton, 0, 0);
        footer.Controls.Add(themeButton, 1, 0);
        root.Controls.Add(footer, 0, 7);

        themeButton.Click += (_, _) => { dark = !dark; ApplyTheme(); };
        drives.SelectedIndexChanged += (_, _) => UpdateDriveInfo();
        openButton.Click += async (_, _) => await TrayCommandAsync(true);
        closeButton.Click += async (_, _) => await TrayCommandAsync(false);
        startButton.Click += async (_, _) => await StartFromDiscAsync();
        browseButton.Click += (_, _) =>
        {
            if (copying) return;
            string? path = ChooseDestination();
            if (path is null) return;
            selectedDestination = path;
            folderPath.Text = path;
            AddLog("저장 폴더 선택: " + path);
        };
        stopButton.Click += (_, _) => cancellation?.Cancel();
        exitButton.Click += (_, _) => Close();
        ledTimer.Tick += (_, _) => meter.Pulse();

        FormClosing += (_, e) =>
        {
            if (!copying) return;
            MessageBox.Show("복사 중에는 먼저 복사 중지를 눌러주세요.", "ODD Copier");
            e.Cancel = true;
        };

        ApplyTheme();
        RefreshDrives();
        SetBusy(false);
        AddLog("프로그램 준비 완료");
    }

");
                e.Cancel = true;
            }
        };

        ApplyTheme();
        RefreshDrives();
        SetBusy(false);
        AddLog("프로그램 준비 완료");
    }

    // ---------- UI helpers ----------

    private static TableLayoutPanel TwoColumns(int left = 50)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1
        };

        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, left));
        panel.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100 - left));

        return panel;
    }

    private static Label Label(
        string text, float size = 11, bool bold = false)
    {
        return new Label
        {
            Text = text,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font(
                "Segoe UI", size,
                bold ? FontStyle.Bold : FontStyle.Regular)
        };
    }

    private Button Button(
        string text, string role, float fontSize)
    {
        var button = new Button
        {
            Text = text,
            Tag = role,
            Dock = DockStyle.Fill,
            Margin = new Padding(4),
            FlatStyle = FlatStyle.Flat,
            Font = new Font(
                "Segoe UI", fontSize, FontStyle.Bold),
            Cursor = Cursors.Hand
        };

        button.FlatAppearance.BorderSize = 0;
        buttons.Add(button);
        return button;
    }

    private Panel Card(Control content)
    {
        var panel = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(3),
            Padding = new Padding(7)
        };

        panel.Controls.Add(content);
        cards.Add(panel);
        return panel;
    }


    private void ApplyTheme()
    {
        Color bg = dark ? Color.FromArgb(17, 24, 30) : Color.FromArgb(247, 249, 250);
        Color surface = dark ? Color.FromArgb(30, 40, 48) : Color.White;
        Color fg = dark ? Color.FromArgb(236, 241, 243) : Color.FromArgb(31, 44, 54);
        Color neutral = dark ? Color.FromArgb(45, 59, 68) : Color.FromArgb(229, 236, 240);

        BackColor = bg;
        ForeColor = fg;
        foreach (var card in cards) card.BackColor = surface;
        foreach (var button in buttons)
        {
            bool primary = (button.Tag?.ToString() ?? "") == "start";
            button.BackColor = primary ? Color.FromArgb(25, 133, 116) : neutral;
            button.ForeColor = primary ? Color.White : fg;
            button.FlatAppearance.MouseOverBackColor = primary
                ? Color.FromArgb(31, 158, 138)
                : (dark ? Color.FromArgb(60, 78, 87) : Color.FromArgb(214, 226, 231));
        }
        SetLabelColors(this, fg);
        drives.BackColor = surface;
        drives.ForeColor = fg;
        folderPath.BackColor = surface;
        folderPath.ForeColor = fg;
        log.BackColor = surface;
        log.ForeColor = fg;
        meter.Dark = dark;
        themeButton.Text = dark ? "☀ Light" : "☾ Dark";
        Invalidate(true);
    }

    private static void SetLabelColors(
        Control parent, Color color)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Label label)
                label.ForeColor = color;

            SetLabelColors(child, color);
        }
    }

    private void AddLog(string message)
    {
        log.AppendText(
            $"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
    }

    private void SetBusy(bool busy)
    {
        copying = busy;

        openButton.Enabled = !busy;
        closeButton.Enabled = !busy;
        browseButton.Enabled = !busy;
        startButton.Enabled = !busy;
        drives.Enabled = !busy;
        stopButton.Enabled = busy;
    }

    // ---------- DVD drive ----------

    private void RefreshDrives()
    {
        drives.Items.Clear();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType == DriveType.CDRom)
                drives.Items.Add(drive.Name);
        }

        if (drives.Items.Count > 0)
            drives.SelectedIndex = 0;
        else
            driveStatus.Text = "ODD 드라이브 없음";
    }

    private string? SelectedDrive()
    {
        return drives.SelectedItem?.ToString();
    }


    private void UpdateDriveInfo()
    {
        string? root = SelectedDrive();
        if (root is null) return;
        driveStatus.Text = DiscReady(root) ? "준비됨" : "디스크 없음";
        driveKind.Text = "ODD";

        // Win32_CDROMDrive.Name describes the device family, not inserted media.
        _ = Task.Run(() => DetectDriveKind(root)).ContinueWith(task =>
        {
            if (IsDisposed || !IsHandleCreated) return;
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && SelectedDrive() == root)
                        driveKind.Text = task.IsCompletedSuccessfully
                            ? task.Result : "ODD";
                }));
            }
            catch (InvalidOperationException) { }
        });
    }

    private static string DetectDriveKind(string root)
    {
        try
        {
            Type? type = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (type is null) return "ODD";
            dynamic locator = Activator.CreateInstance(type)!;
            dynamic wmi = locator.ConnectServer(".", @"root\cimv2");
            foreach (dynamic device in wmi.ExecQuery("SELECT Drive, Name FROM Win32_CDROMDrive"))
            {
                string letter = Convert.ToString(device.Drive) ?? "";
                if (letter.Length == 0 ||
                    char.ToUpperInvariant(letter[0]) != char.ToUpperInvariant(root[0]))
                    continue;

                string name = (Convert.ToString(device.Name) ?? "").ToUpperInvariant();
                if (name.Contains("BLU") || name.Contains("BD-") ||
                    name.Contains("BDR") || name.Contains("BD ROM"))
                    return "BD";
                if (name.Contains("DVD")) return "DVD";
                if (name.Contains("CD")) return "CD";
                return "ODD";
            }
        }
        catch { /* WMI unavailable; use ODD fallback. */ }
        return "ODD";
    }

    private static bool DiscReady(string root)
    {
        try
        {
            return new DriveInfo(root).IsReady;
        }
        catch
        {
            return false;
        }
    }

    private async Task TrayCommandAsync(bool eject)
    {
        if (copying) return;

        string? root = SelectedDrive();

        if (root == null)
        {
            MessageBox.Show("DVD 드라이브가 없습니다.");
            return;
        }

        openButton.Enabled = false;
        closeButton.Enabled = false;

        try
        {
            await Task.Run(() => DriveHardware.Tray(root, eject));

            AddLog(eject ? "DVD 트레이 열림" : "DVD 트레이 닫기 명령");

            if (eject)
            {
                driveStatus.Text = "트레이 열림";
                return;
            }

            driveStatus.Text = "디스크 인식 중";

            bool ready = await Task.Run(() =>
            {
                for (int i = 0; i < 30; i++)
                {
                    if (DiscReady(root)) return true;
                    Thread.Sleep(500);
                }
                return false;
            });

            if (ready)
            {
                driveStatus.Text = "준비됨";
                AddLog("디스크 준비 완료. 복사 시작 버튼을 누르세요.");
            }
            else
            {
                driveStatus.Text = "디스크 확인 필요";
                AddLog("디스크 인식 후 복사 시작 버튼을 누르세요.");
            }
        }
        catch (Exception ex)
        {
            AddLog("드라이브 제어 실패: " + ex.Message);

            MessageBox.Show(
                "드라이브 명령을 실행하지 못했습니다.\n" +
                "닫기 기능을 지원하지 않으면 직접 트레이를 닫고 " +
                "'복사 시작'을 누르세요.\n\n" + ex.Message,
                "DVD 드라이브");
        }
        finally
        {
            if (!copying)
            {
                openButton.Enabled = true;
                closeButton.Enabled = true;
            }
        }
    }

    // ---------- Destination selection ----------

    private static string? AskFolderName()
    {
        using var dialog = new Form
        {
            Text = "새 폴더 만들기",
            ClientSize = new Size(420, 150),
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false
        };

        var input = new TextBox
        {
            Left = 20,
            Top = 22,
            Width = 380,
            Text = "ODD_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
        };

        var ok = new Button
        {
            Text = "확인",
            Left = 210,
            Top = 85,
            Width = 90,
            DialogResult = DialogResult.OK
        };

        var cancel = new Button
        {
            Text = "취소",
            Left = 310,
            Top = 85,
            Width = 90,
            DialogResult = DialogResult.Cancel
        };

        dialog.Controls.AddRange(new Control[]
        {
            input, ok, cancel
        });

        dialog.AcceptButton = ok;
        dialog.CancelButton = cancel;

        return dialog.ShowDialog() == DialogResult.OK
            ? input.Text.Trim()
            : null;
    }

    private string? ChooseDestination()
    {
        while (true)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "DVD 데이터를 저장할 폴더를 선택하세요",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return null;

            string destination = dialog.SelectedPath;

            var choice = MessageBox.Show(
                "새 폴더를 생성하시겠습니까?\n\n" +
                "예: 새 폴더 생성\n" +
                "아니오: 선택한 폴더에 직접 복사\n" +
                "취소: 폴더 다시 선택",
                "저장 방식",
                MessageBoxButtons.YesNoCancel,
                MessageBoxIcon.Question);

            if (choice == DialogResult.Cancel)
                continue;

            if (choice == DialogResult.Yes)
            {
                string? name = AskFolderName();

                if (string.IsNullOrWhiteSpace(name))
                    continue;

                if (name == "." || name == ".." ||
                    name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    name.EndsWith('.') || name.EndsWith(' '))
                {
                    MessageBox.Show("사용할 수 없는 폴더 이름입니다.");
                    continue;
                }

                destination = Path.Combine(destination, name);

                if (Directory.Exists(destination) ||
                    File.Exists(destination))
                {
                    MessageBox.Show("이미 존재하는 폴더 이름입니다.");
                    continue;
                }
            }

            var confirm = MessageBox.Show(
                $"선택한 폴더로 복사할까요?\n\n{destination}",
                "복사 위치 확인",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
                return destination;
        }
    }

    // ---------- Copy ----------

    private async Task StartFromDiscAsync()
    {
        if (copying) return;

        string? source = SelectedDrive();

        if (source == null)
        {
            MessageBox.Show("ODD 드라이브를 선택하세요.");
            return;
        }

        bool ready = await Task.Run(() => DiscReady(source));

        if (!ready)
        {
            driveStatus.Text = "디스크 없음";
            MessageBox.Show("디스크를 삽입한 후 복사 시작을 누르세요.");
            return;
        }

        driveStatus.Text = "디스크 준비됨";

        string? destination = selectedDestination;
        if (destination is null)
        {
            destination = ChooseDestination();
            if (destination is null) return;
            selectedDestination = destination;
        }

        folderPath.Text = destination;
        await CopyDiscAsync(source, destination);
    }

    private sealed record CopyPlan(
        FileInfo[] Files,
        string[] Directories,
        long TotalBytes);

    private sealed record CopyProgress(
        double Percent,
        int CompletedFiles,
        int TotalFiles,
        string FileName);

    private static CopyPlan BuildPlan(
        string source, string destination)
    {
        var directories = Directory.GetDirectories(
            source, "*", SearchOption.AllDirectories);

        var files = Directory.GetFiles(
            source, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .ToArray();

        foreach (string directory in directories)
        {
            string relative = Path.GetRelativePath(source, directory);
            string target = Path.Combine(destination, relative);

            if (File.Exists(target))
                throw new IOException(
                    "폴더와 이름이 충돌하는 파일: " + target);
        }

        foreach (var file in files)
        {
            string relative = Path.GetRelativePath(
                source, file.FullName);

            string target = Path.Combine(destination, relative);

            if (File.Exists(target) || Directory.Exists(target))
                throw new IOException(
                    "동일한 이름의 파일이 이미 존재합니다:\n" + target);
        }

        long total = files.Sum(file => file.Length);

        return new CopyPlan(files, directories, total);
    }

    private async Task CopyDiscAsync(
        string source, string destination)
    {
        SetBusy(true);
        cancellation = new CancellationTokenSource();

        meter.Value = 0;
        meter.Running = true;
        ledTimer.Start();

        driveStatus.Text = "복사 중";
        AddLog("복사 준비: " + source);

        var token = cancellation.Token;

        var progress = new Progress<CopyProgress>(p =>
        {
            meter.Value = (int)Math.Clamp(p.Percent, 0, 100);

            driveStatus.Text = $"복사 중 {meter.Value}%";

            if (p.CompletedFiles > 0)
            {
                Text = $"ODD Copier - {meter.Value}%";
            }
        });

        try
        {
            AddLog("디스크 파일 검사 중");

            var plan = await Task.Run(
                () => BuildPlan(source, destination), token);

            AddLog(
                $"파일 {plan.Files.Length}개 / " +
                $"{plan.TotalBytes / 1048576.0:N1} MB");

            // Check available destination space.
            string driveRoot = Path.GetPathRoot(destination)!;
            long freeSpace = new DriveInfo(driveRoot).AvailableFreeSpace;

            if (freeSpace < plan.TotalBytes)
                throw new IOException(
                    "저장 드라이브의 여유 공간이 부족합니다.");

            await Task.Run(async () =>
            {
                Directory.CreateDirectory(destination);

                foreach (string directory in plan.Directories)
                {
                    token.ThrowIfCancellationRequested();

                    string relative =
                        Path.GetRelativePath(source, directory);

                    Directory.CreateDirectory(
                        Path.Combine(destination, relative));
                }

                long copiedBytes = 0;
                int copiedFiles = 0;
                var clock = Stopwatch.StartNew();
                long lastUpdate = 0;

                byte[] buffer = new byte[262144];

                foreach (var file in plan.Files)
                {
                    token.ThrowIfCancellationRequested();

                    string relative =
                        Path.GetRelativePath(source, file.FullName);

                    string target = Path.Combine(
                        destination, relative);

                    Directory.CreateDirectory(
                        Path.GetDirectoryName(target)!);

                    // Temporary file prevents incomplete final files.
                    string partial = target + ".dvd-copier-partial";

                    try
                    {
                        await using (var input = new FileStream(
                            file.FullName,
                            FileMode.Open,
                            FileAccess.Read,
                            FileShare.Read,
                            buffer.Length,
                            useAsync: true))
                        await using (var output = new FileStream(
                            partial,
                            FileMode.CreateNew,
                            FileAccess.Write,
                            FileShare.None,
                            buffer.Length,
                            useAsync: true))
                        {
                            int count;

                            while ((count = await input.ReadAsync(
                                buffer.AsMemory(), token)) > 0)
                            {
                                await output.WriteAsync(
                                    buffer.AsMemory(0, count), token);

                                copiedBytes += count;

                                if (clock.ElapsedMilliseconds -
                                    lastUpdate >= 100)
                                {
                                    double percent =
                                        plan.TotalBytes == 0
                                        ? 0
                                        : copiedBytes * 100.0 /
                                          plan.TotalBytes;

                                    ((IProgress<CopyProgress>)progress)
                                        .Report(new CopyProgress(
                                            percent,
                                            copiedFiles,
                                            plan.Files.Length,
                                            file.Name));

                                    lastUpdate = clock.ElapsedMilliseconds;
                                }
                            }
                        }

                        File.Move(partial, target);
                        File.SetLastWriteTimeUtc(
                            target, file.LastWriteTimeUtc);

                        copiedFiles++;

                        double done =
                            plan.TotalBytes == 0
                            ? copiedFiles * 100.0 /
                              Math.Max(1, plan.Files.Length)
                            : copiedBytes * 100.0 /
                              plan.TotalBytes;

                        ((IProgress<CopyProgress>)progress)
                            .Report(new CopyProgress(
                                done,
                                copiedFiles,
                                plan.Files.Length,
                                file.Name));
                    }
                    finally
                    {
                        // Delete only unfinished temporary files.
                        if (File.Exists(partial))
                        {
                            try { File.Delete(partial); }
                            catch { /* Preserve original error. */ }
                        }
                    }
                }
            }, token);

            meter.Value = 100;
            driveStatus.Text = "복사 완료";

            AddLog("복사 완료: " + destination);

            var next = MessageBox.Show(
                "DVD 복사가 완료되었습니다.\n\n" +
                "다음 DVD를 복사하시겠습니까?",
                "복사 완료",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);

            if (next == DialogResult.Yes)
            {
                AddLog("다음 DVD를 준비하세요.");
            }
            else
            {
                AddLog("작업 완료");
            }
        }
        catch (OperationCanceledException)
        {
            driveStatus.Text = "복사 중지";
            AddLog("사용자가 복사를 중지했습니다.");
            MessageBox.Show(
                "복사가 중지되었습니다.\n" +
                "이미 복사된 파일은 저장 폴더에 남아 있습니다.");
        }
        catch (Exception ex)
        {
            driveStatus.Text = "복사 오류";
            AddLog("오류: " + ex.Message);

            MessageBox.Show(
                "복사 중 문제가 발생했습니다.\n\n" + ex.Message,
                "복사 오류",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            ledTimer.Stop();
            meter.Running = false;

            cancellation.Dispose();
            cancellation = null;

            SetBusy(false);
            Text = "ODD Copier v2.0";
        }
    }
}

// ---------- Windows DVD hardware control ----------

internal static class DriveHardware
{
    private const uint GenericRead = 0x80000000;
    private const uint ShareReadWrite = 3;
    private const uint OpenExisting = 3;

    private const uint EjectMedia = 0x002D4808;
    private const uint LoadMedia = 0x002D480C;

    [DllImport(
        "kernel32.dll",
        CharSet = CharSet.Unicode,
        SetLastError = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flags,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeviceIoControl(
        SafeFileHandle handle,
        uint controlCode,
        IntPtr input,
        uint inputSize,
        IntPtr output,
        uint outputSize,
        out uint bytesReturned,
        IntPtr overlapped);

    public static void Tray(string root, bool eject)
    {
        string path = @"\\.\" + char.ToUpperInvariant(root[0]) + ":";

        using var handle = CreateFile(
            path,
            GenericRead,
            ShareReadWrite,
            IntPtr.Zero,
            OpenExisting,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "DVD 드라이브에 접근할 수 없습니다.");

        uint command = eject ? EjectMedia : LoadMedia;

        bool ok = DeviceIoControl(
            handle,
            command,
            IntPtr.Zero,
            0,
            IntPtr.Zero,
            0,
            out _,
            IntPtr.Zero);

        if (!ok)
            throw new Win32Exception(
                Marshal.GetLastWin32Error(),
                "드라이브가 명령을 지원하지 않거나 사용 중입니다.");
    }
}

// ---------- Retro 7-segment LED meter ----------

public sealed class LedMeter : Control
{
    private int value;
    private bool phase;


[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
public int Value
{
    get => value;
    set
    {
        this.value = Math.Clamp(value, 0, 100);
        Invalidate();
    }
}

[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
public bool Running { get; set; }

[DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
public bool Dark { get; set; } = true;

    private static readonly int[] DigitMasks =
    {
        0x3F, 0x06, 0x5B, 0x4F, 0x66,
        0x6D, 0x7D, 0x07, 0x7F, 0x6F
    };

    public LedMeter()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
    }

    public void Pulse()
    {
        phase = !phase;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var g = e.Graphics;
        g.Clear(Dark
            ? Color.FromArgb(11, 20, 27)
            : Color.FromArgb(222, 233, 238));

        Color off = Dark
            ? Color.FromArgb(35, 55, 53)
            : Color.FromArgb(180, 200, 195);

        Color on = Color.FromArgb(51, 237, 122);

        // 7-segment progress number.
        string number = value.ToString();

        int digitW = 32;
        int digitH = 55;
        int gap = 8;

        int totalW = number.Length * (digitW + gap) + 42;
        int startX = (Width - totalW) / 2;
        int digitY = Math.Max(6, (Height - 100) / 2 - 8);

        for (int i = 0; i < number.Length; i++)
        {
            DrawDigit(
                g,
                number[i] - '0',
                startX + i * (digitW + gap),
                digitY,
                digitW,
                digitH,
                on,
                off);
        }

        using (var font = new Font(
            "Consolas", 24, FontStyle.Bold))
        using (var brush = new SolidBrush(on))
        {
            g.DrawString(
                "%",
                font,
                brush,
                startX + number.Length * (digitW + gap),
                digitY + 12);
        }

        // Ten LED blocks: each block represents 10%.
        int margin = 12;
        int blockGap = 6;
        int blockWidth =
            Math.Max(3, (Width - margin * 2 -
                blockGap * 9) / 10);

        int blockHeight = 30;
        int blockY = Height - 45;

        int fullBlocks = value / 10;
        float partialBrightness = (value % 10) / 10f;

        for (int i = 0; i < 10; i++)
        {
            float intensity;

            if (i < fullBlocks)
            {
                intensity = 1f;
            }
            else if (i == fullBlocks && value < 100)
            {
                intensity = partialBrightness;

                // Invert the active block while copying.
                if (Running && phase)
                    intensity = 1f - intensity;
            }
            else
            {
                intensity = 0f;
            }

            Color current = Mix(off, on, intensity);

            using var brush = new SolidBrush(current);

            g.FillRectangle(
                brush,
                margin + i * (blockWidth + blockGap),
                blockY,
                blockWidth,
                blockHeight);
        }
    }

    private static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    private static void DrawDigit(
        Graphics g,
        int number,
        int x,
        int y,
        int width,
        int height,
        Color on,
        Color off)
    {
        int t = 5;
        int half = height / 2;

        Rectangle[] segments =
        {
            // a: top
            new(x + t, y, width - 2*t, t),

            // b: upper right
            new(x + width - t, y + t, t, half - t),

            // c: lower right
            new(x + width - t, y + half, t, half - t),

            // d: bottom
            new(x + t, y + height - t, width - 2*t, t),

            // e: lower left
            new(x, y + half, t, half - t),

            // f: upper left
            new(x, y + t, t, half - t),

            // g: middle
            new(x + t, y + half - t/2, width - 2*t, t)
        };

        int mask = DigitMasks[Math.Clamp(number, 0, 9)];

        for (int i = 0; i < 7; i++)
        {
            bool lit = (mask & (1 << i)) != 0;

            using var brush = new SolidBrush(lit ? on : off);
            g.FillRectangle(brush, segments[i]);
        }
    }
}
