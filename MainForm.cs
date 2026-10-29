
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
    private readonly Button checkButton;
    private readonly Button startButton;
    private readonly Button stopButton;
    private readonly Button themeButton;

    private readonly System.Windows.Forms.Timer ledTimer = new()
    {
        Interval = 450
    };

    private CancellationTokenSource? cancellation;
    private bool copying;
    private bool dark = true;

    public MainForm()
    {
        Text = "DVD Copier v2.0";
        ClientSize = new Size(880, 850);
        MinimumSize = new Size(760, 780);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 11);
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 8,
            Padding = new Padding(18)
        };

        root.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 100));

        foreach (int height in new[]
        {
            54, 84, 152, 82, 104, 0, 112, 62
        })
        {
            root.RowStyles.Add(
                height == 0
                ? new RowStyle(SizeType.Percent, 100)
                : new RowStyle(SizeType.Absolute, height));
        }

        Controls.Add(root);

        // Header
        var header = TwoColumns(78);
        var title = Label("DVD COPIER  /  v2.0", 19, true);
        themeButton = Button("☀ Light", "neutral", 11);

        header.Controls.Add(title, 0, 0);
        header.Controls.Add(themeButton, 1, 0);
        root.Controls.Add(header, 0, 0);

        themeButton.Click += (_, _) =>
        {
            dark = !dark;
            ApplyTheme();
        };

        // Drive selector
        var driveRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1
        };

        driveRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 22));
        driveRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 51));
        driveRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 27));

        driveRow.Controls.Add(Label("DVD 드라이브"), 0, 0);
        driveRow.Controls.Add(drives, 1, 0);
        driveRow.Controls.Add(driveStatus, 2, 0);

        root.Controls.Add(Card(driveRow), 0, 1);

        // Large remote buttons
        var trayRow = TwoColumns();
        openButton = Button("⏏  DVD 열기", "open", 21);
        closeButton = Button("⏷  DVD 닫기", "close", 21);

        trayRow.Controls.Add(openButton, 0, 0);
        trayRow.Controls.Add(closeButton, 1, 0);
        root.Controls.Add(trayRow, 0, 2);

        // Secondary buttons
        var actionRow = TwoColumns();
        checkButton = Button("◎  디스크 확인", "neutral", 15);
        startButton = Button("▶  복사 시작", "start", 15);

        actionRow.Controls.Add(checkButton, 0, 0);
        actionRow.Controls.Add(startButton, 1, 0);
        root.Controls.Add(actionRow, 0, 3);

        // Destination folder
        var folderRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };

        folderRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 77));
        folderRow.ColumnStyles.Add(
            new ColumnStyle(SizeType.Percent, 23));
        folderRow.RowStyles.Add(
            new RowStyle(SizeType.Percent, 40));
        folderRow.RowStyles.Add(
            new RowStyle(SizeType.Percent, 60));

        var browse = Button("폴더 선택", "neutral", 11);

        folderRow.Controls.Add(Label("저장 폴더"), 0, 0);
        folderRow.Controls.Add(folderPath, 0, 1);
        folderRow.Controls.Add(browse, 1, 1);

        root.Controls.Add(Card(folderRow), 0, 4);

        // Retro LED display
        meter.Dock = DockStyle.Fill;
        root.Controls.Add(Card(meter), 0, 5);

        // Status log
        var statusPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };

        statusPanel.RowStyles.Add(
            new RowStyle(SizeType.Absolute, 28));
        statusPanel.RowStyles.Add(
            new RowStyle(SizeType.Percent, 100));

        statusPanel.Controls.Add(Label("상태 메시지"), 0, 0);
        statusPanel.Controls.Add(log, 0, 1);
        root.Controls.Add(Card(statusPanel), 0, 6);

        // Footer
        var footer = TwoColumns();
        stopButton = Button("■  복사 중지", "neutral", 13);
        var exitButton = Button("⏻  종료", "neutral", 13);

        footer.Controls.Add(stopButton, 0, 0);
        footer.Controls.Add(exitButton, 1, 0);
        root.Controls.Add(footer, 0, 7);

        // Events
        openButton.Click += async (_, _) =>
            await TrayCommandAsync(true);

        closeButton.Click += async (_, _) =>
            await TrayCommandAsync(false);

        checkButton.Click += async (_, _) =>
            await StartFromDiscAsync();

        startButton.Click += async (_, _) =>
            await StartFromDiscAsync();

        browse.Click += async (_, _) =>
            await StartFromDiscAsync();

        stopButton.Click += (_, _) =>
            cancellation?.Cancel();

        exitButton.Click += (_, _) => Close();

        ledTimer.Tick += (_, _) => meter.Pulse();

        FormClosing += (_, e) =>
        {
            if (copying)
            {
                MessageBox.Show(
                    "복사 중에는 먼저 복사 중지를 눌러주세요.",
                    "DVD Copier");
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
            Margin = new Padding(4),
            Padding = new Padding(12)
        };

        panel.Controls.Add(content);
        cards.Add(panel);
        return panel;
    }

    private void ApplyTheme()
    {
        Color background = dark
            ? Color.FromArgb(17, 24, 39)
            : Color.FromArgb(241, 245, 249);

        Color surface = dark
            ? Color.FromArgb(31, 41, 55)
            : Color.White;

        Color foreground = dark
            ? Color.FromArgb(242, 245, 250)
            : Color.FromArgb(25, 35, 50);

        BackColor = background;
        ForeColor = foreground;

        foreach (var card in cards)
            card.BackColor = surface;

        foreach (var button in buttons)
        {
            string role = button.Tag?.ToString() ?? "";

            button.BackColor = role switch
            {
                "open" => Color.FromArgb(37, 99, 235),
                "close" => Color.FromArgb(220, 65, 72),
                "start" => Color.FromArgb(22, 145, 82),
                _ => dark
                    ? Color.FromArgb(51, 65, 85)
                    : Color.FromArgb(220, 229, 238)
            };

            button.ForeColor = role == "neutral"
                ? foreground
                : Color.White;
        }

        SetLabelColors(this, foreground);

        drives.BackColor = surface;
        drives.ForeColor = foreground;

        folderPath.BackColor = surface;
        folderPath.ForeColor = foreground;

        log.BackColor = surface;
        log.ForeColor = foreground;

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
        checkButton.Enabled = !busy;
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
            driveStatus.Text = "DVD 드라이브 없음";
    }

    private string? SelectedDrive()
    {
        return drives.SelectedItem?.ToString();
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
                driveStatus.Text = "디스크 준비됨";
                await StartFromDiscAsync();
            }
            else
            {
                driveStatus.Text = "디스크 확인 필요";
                AddLog("디스크 인식 후 '디스크 확인'을 누르세요.");
            }
        }
        catch (Exception ex)
        {
            AddLog("드라이브 제어 실패: " + ex.Message);

            MessageBox.Show(
                "드라이브 명령을 실행하지 못했습니다.\n" +
                "닫기 기능을 지원하지 않으면 직접 트레이를 닫고 " +
                "'디스크 확인'을 누르세요.\n\n" + ex.Message,
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
            Text = "DVD_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
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
            MessageBox.Show("DVD 드라이브를 선택하세요.");
            return;
        }

        bool ready = await Task.Run(() => DiscReady(source));

        if (!ready)
        {
            driveStatus.Text = "디스크 없음";
            MessageBox.Show("DVD를 삽입한 후 디스크를 확인하세요.");
            return;
        }

        driveStatus.Text = "디스크 준비됨";

        string? destination = ChooseDestination();
        if (destination == null) return;

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
                Text = $"DVD Copier - {meter.Value}%";
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
            Text = "DVD Copier v2.0";
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

    public int Value
    {
        get => value;
        set
        {
            this.value = Math.Clamp(value, 0, 100);
            Invalidate();
        }
    }

    public bool Running { get; set; }
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
