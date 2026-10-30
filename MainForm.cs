
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

    // Optional compact player underneath the existing copy controls.
    private readonly AudioEngine audio = new();
    private LyricsDocument? lyrics;
    private bool playerExpanded;
    private readonly System.Windows.Forms.Timer audioTimer = new() { Interval = 500 };
    private readonly Label trackLabel = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, Text = "음악을 선택하세요"
    };
    private readonly Label lyricLabel = new()
    {
        Dock = DockStyle.Fill, AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft, Text = "가사 정보 없음"
    };
    private readonly Label playTimeLabel = new()
    {
        Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
        Text = "00:00 / 00:00"
    };
    private readonly Button audioToggle;
    private readonly Button audioCdButton;
    private readonly Button audioFileButton;
    private readonly Button audioPauseButton;
    private readonly Button audioStopButton;
    private readonly Button audioPrevButton;
    private readonly Button audioNextButton;
    private readonly Button audioListButton;
    private readonly Panel audioDetail = new() { Dock = DockStyle.Fill, Padding = new Padding(2) };
    private TableLayoutPanel rootLayout = null!;


    private readonly System.Windows.Forms.Timer ledTimer = new()
    {
        Interval = 450
    };

    private CancellationTokenSource? cancellation;
    private bool copying;
    private bool dark = true;

    public MainForm()
    {
        Text = "ODD Copier v2.1";
        Size = new Size(360, 640);
        MinimumSize = Size;
        MaximumSize = Size;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9);
        AutoScaleMode = AutoScaleMode.None;

        rootLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 9, Padding = new Padding(8)
        };
        rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foreach (int height in new[] { 28, 60, 58, 108, 58, 58, 34, 0, 38 })
            rootLayout.RowStyles.Add(height == 0 ? new RowStyle(SizeType.Percent, 100)
                                                 : new RowStyle(SizeType.Absolute, height));
        var root = rootLayout;
        Controls.Add(root);

        root.Controls.Add(Label("ODD COPIER  /  v2.1", 14, true), 0, 0);

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

        // Expandable player — copy controls remain visible in the same window.
        var audioArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        audioArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        audioArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        audioToggle = Button("♫  음악 재생  ▾", "neutral", 9);
        audioArea.Controls.Add(audioToggle, 0, 0);
        audioArea.Controls.Add(audioDetail, 0, 1);
        root.Controls.Add(Card(audioArea), 0, 6);
        audioDetail.Visible = false;

        var playerLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5
        };
        playerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        playerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        playerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        playerLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        playerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        audioDetail.Controls.Add(playerLayout);

        var sources = TwoColumns();
        audioCdButton = Button("CD 재생", "neutral", 8);
        audioFileButton = Button("파일 열기", "neutral", 8);
        sources.Controls.Add(audioCdButton, 0, 0);
        sources.Controls.Add(audioFileButton, 1, 0);
        playerLayout.Controls.Add(sources, 0, 0);

        var transport = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        for (int c = 0; c < 4; c++)
            transport.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        audioPrevButton = Button("⏮", "neutral", 10);
        audioPauseButton = Button("▶/Ⅱ", "neutral", 9);
        audioNextButton = Button("⏭", "neutral", 10);
        audioStopButton = Button("■", "neutral", 10);
        transport.Controls.Add(audioPrevButton, 0, 0);
        transport.Controls.Add(audioPauseButton, 1, 0);
        transport.Controls.Add(audioNextButton, 2, 0);
        transport.Controls.Add(audioStopButton, 3, 0);
        playerLayout.Controls.Add(transport, 0, 1);

        var now = TwoColumns(72);
        now.Controls.Add(trackLabel, 0, 0);
        audioListButton = Button("목록", "neutral", 8);
        now.Controls.Add(audioListButton, 1, 0);
        playerLayout.Controls.Add(now, 0, 2);
        playerLayout.Controls.Add(lyricLabel, 0, 3);
        playerLayout.Controls.Add(playTimeLabel, 0, 4);

        // 6. Status messages
        var status = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        status.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        status.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        status.Controls.Add(Label("상태 메시지", 9, true), 0, 0);
        status.Controls.Add(log, 0, 1);
        root.Controls.Add(Card(status), 0, 7);

        // 7. Exit / Theme
        var footer = TwoColumns();
        var exitButton = Button("⏻  종료", "neutral", 10);
        themeButton = Button("☀ Light", "neutral", 10);
        footer.Controls.Add(exitButton, 0, 0);
        footer.Controls.Add(themeButton, 1, 0);
        root.Controls.Add(footer, 0, 8);

        themeButton.Click += (_, _) => { dark = !dark; ApplyTheme(); };
        audioToggle.Click += (_, _) => SetPlayerExpanded(!playerExpanded);
        audioFileButton.Click += (_, _) => OpenAudioFiles();
        audioCdButton.Click += (_, _) => OpenAudioCd();
        audioPauseButton.Click += (_, _) => RunAudioAction(() =>
        {
            if (audio.IsPaused) audio.TogglePause();
            else if (audio.IsPlaying) audio.TogglePause();
            else audio.Play();
        });
        audioStopButton.Click += (_, _) => StopAudio();
        audioPrevButton.Click += (_, _) => RunAudioAction(() => audio.Previous());
        audioNextButton.Click += (_, _) => RunAudioAction(() => audio.Next());
        audioListButton.Click += (_, _) => ShowTracks();
        audioTimer.Tick += (_, _) => UpdateAudioProgress();
        meter.MouseClick += (_, e) =>
        {
            if (audio.IsActive && e.Y >= meter.Height - 40 && !copying)
                RunAudioAction(() => audio.SeekFraction((double)e.X / Math.Max(1, meter.Width)));
        };
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
            if (copying)
            {
                MessageBox.Show("복사 중에는 먼저 복사 중지를 눌러주세요.", "ODD Copier");
                e.Cancel = true;
            }
            else
            {
                audioTimer.Stop();
                audio.Dispose();
            }
        };

        ApplyTheme();
        RefreshDrives();
        SetBusy(false);
        AddLog("프로그램 준비 완료");
    }

    // ---------- Optional audio player ----------

    private void SetPlayerExpanded(bool expand)
    {
        playerExpanded = expand;
        audioDetail.Visible = expand;
        audioToggle.Text = expand ? "♫  음악 재생  ▴" : "♫  음악 재생  ▾";
        rootLayout.RowStyles[6].Height = expand ? 158 : 34;
        rootLayout.RowStyles[3].Height = expand ? 96 : 108;
        rootLayout.PerformLayout();
    }

    private void OpenAudioFiles()
    {
        if (copying) return;
        using var picker = new OpenFileDialog
        {
            Title = "PC 또는 ODD의 음악 파일 선택",
            Filter = "오디오 파일|*.mp3;*.m4a;*.wav;*.wma;*.flac;*.aac;*.aif;*.aiff|모든 파일|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        RunAudioAction(() => audio.OpenFiles(picker.FileNames));
    }

    private void OpenAudioCd()
    {
        if (copying) return;
        string? root = SelectedDrive();
        if (root is null)
        {
            MessageBox.Show("ODD 드라이브를 선택하세요.");
            return;
        }
        RunAudioAction(() => audio.OpenAudioCd(root));
    }

    private void RunAudioAction(Action action)
    {
        if (copying) return;
        try
        {
            action();
            RefreshAudioTrack();
            UpdateAudioButtons();
            if (audio.IsActive)
            {
                audioTimer.Start();
                SetPlayerExpanded(true);
            }
        }
        catch (Exception ex)
        {
            StopAudio();
            AddLog("음악 재생 오류: " + ex.Message);
            MessageBox.Show(ex.Message, "음악 재생 오류",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void RefreshAudioTrack()
    {
        trackLabel.Text = audio.CurrentTitle;
        lyrics = LyricsDocument.FromAudioFile(audio.CurrentFile);
        lyricLabel.Text = lyrics?.LineAt(audio.Position) ?? "가사 정보 없음";
        AddLog(audio.IsActive ? "음악 선택: " + audio.CurrentTitle : "음악 재생 종료");
    }

    private void UpdateAudioButtons()
    {
        bool enabled = audio.IsActive;
        if (IsDisposed) return;
        audioPauseButton.Enabled = enabled && !copying;
        audioStopButton.Enabled = enabled && !copying;
        audioPrevButton.Enabled = enabled && !copying && audio.TrackIndex > 0;
        audioNextButton.Enabled = enabled && !copying &&
                                  audio.TrackIndex + 1 < audio.TrackCount;
        audioListButton.Enabled = enabled && !copying;
        audioCdButton.Enabled = !copying;
        audioFileButton.Enabled = !copying;
        startButton.Enabled = !copying && !enabled;
        drives.Enabled = !copying && !enabled;
        browseButton.Enabled = !copying;
        audioPauseButton.Text = audio.IsPlaying ? "Ⅱ" : "▶";
    }

    private void StopAudio()
    {
        audioTimer.Stop();
        audio.Stop();
        lyrics = null;
        trackLabel.Text = "음악을 선택하세요";
        lyricLabel.Text = "가사 정보 없음";
        playTimeLabel.Text = "00:00 / 00:00";
        if (!copying)
        {
            meter.Value = 0;
            meter.Running = false;
        }
        UpdateAudioButtons();
    }

    private void UpdateAudioProgress()
    {
        if (!audio.IsActive || copying) return;
        try
        {
            if (audio.Update()) RefreshAudioTrack();
            var position = audio.Position;
            var length = audio.Duration;
            int progress = length.TotalMilliseconds <= 0 ? 0 :
                (int)Math.Clamp(position.TotalMilliseconds * 100 / length.TotalMilliseconds, 0, 100);
            meter.Value = progress;
            meter.Running = audio.IsPlaying;
            if (audio.IsPlaying) meter.Pulse();
            lyricLabel.Text = lyrics?.LineAt(position) ?? "가사 정보 없음";
            playTimeLabel.Text = position.ToString(@"mm\:ss") + " / " + length.ToString(@"mm\:ss");
            audioPauseButton.Text = audio.IsPlaying ? "Ⅱ" : "▶";
            if (!audio.IsPlaying && !audio.IsPaused) meter.Running = false;
        }
        catch (Exception ex)
        {
            AddLog("재생 상태 확인 실패: " + ex.Message);
            StopAudio();
        }
    }

    private void ShowTracks()
    {
        if (!audio.IsActive) return;
        using var chooser = new Form
        {
            Text = "트랙 목록",
            StartPosition = FormStartPosition.CenterParent,
            ClientSize = new Size(320, 350)
        };
        var list = new ListBox { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 10) };
        list.Items.AddRange(audio.TrackNames.Cast<object>().ToArray());
        list.SelectedIndex = audio.TrackIndex;
        chooser.Controls.Add(list);
        list.DoubleClick += (_, _) =>
        {
            if (list.SelectedIndex >= 0)
            {
                int idx = list.SelectedIndex;
                chooser.Close();
                RunAudioAction(() => audio.SelectTrack(idx));
            }
        };
        chooser.ShowDialog(this);
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
        audioDetail.BackColor = surface;
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
        audioToggle.Enabled = !busy;
        UpdateAudioButtons();
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
        if (audio.IsActive) StopAudio();

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
        if (audio.IsActive)
        {
            MessageBox.Show("음악 재생을 중지한 뒤 복사를 시작하세요.");
            return;
        }

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
            Text = "ODD Copier v2.1";
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

// ---------- Retro LED dot matrix progress meter ----------

public sealed class LedMeter : Control
{
    private int value;
    private bool phase;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => value;
        set { this.value = Math.Clamp(value, 0, 100); Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Running { get; set; }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Dark { get; set; } = true;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['0'] = new[] { "11111", "10001", "10011", "10101", "11001", "10001", "11111" },
        ['1'] = new[] { "00100", "01100", "00100", "00100", "00100", "00100", "01110" },
        ['2'] = new[] { "11111", "00001", "00001", "11111", "10000", "10000", "11111" },
        ['3'] = new[] { "11111", "00001", "00001", "01111", "00001", "00001", "11111" },
        ['4'] = new[] { "10001", "10001", "10001", "11111", "00001", "00001", "00001" },
        ['5'] = new[] { "11111", "10000", "10000", "11111", "00001", "00001", "11111" },
        ['6'] = new[] { "11111", "10000", "10000", "11111", "10001", "10001", "11111" },
        ['7'] = new[] { "11111", "00001", "00010", "00100", "01000", "01000", "01000" },
        ['8'] = new[] { "11111", "10001", "10001", "11111", "10001", "10001", "11111" },
        ['9'] = new[] { "11111", "10001", "10001", "11111", "00001", "00001", "11111" },
        ['%'] = new[] { "11001", "11010", "00100", "00100", "01000", "10110", "00110" }
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
        Graphics g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        Color bg = Dark ? Color.FromArgb(13, 24, 29) : Color.FromArgb(225, 237, 237);
        Color off = Dark ? Color.FromArgb(37, 61, 58) : Color.FromArgb(181, 208, 201);
        Color on = Dark ? Color.FromArgb(68, 222, 169) : Color.FromArgb(16, 130, 104);
        g.Clear(bg);

        // 5x7 LED dot matrix glyphs for the numeric percentage.
        string str = value.ToString() + "%";
        const int dot = 4, pitch = 6, stride = 38;
        int startX = Math.Max(2, (Width - str.Length * stride) / 2);
        int startY = Math.Max(4, (Height - 90) / 2);
        using var dimBrush = new SolidBrush(off);
        using var litBrush = new SolidBrush(on);

        for (int n = 0; n < str.Length; n++)
        {
            if (!Glyphs.TryGetValue(str[n], out var rows)) continue;
            int gx = startX + n * stride;
            for (int y = 0; y < 7; y++)
            for (int x = 0; x < 5; x++)
                g.FillEllipse(rows[y][x] == '1' ? litBrush : dimBrush,
                    gx + x * pitch, startY + y * pitch, dot, dot);
        }

        // Ten LED sections, each covering 10%; current section's brightness
        // represents 1% increments, and it pulses during copying.
        int gap = 3, pad = 7;
        int width = Math.Max(10, (Width - 2 * pad - 9 * gap) / 10);
        int y0 = Height - 34;
        int full = value / 10;
        float fraction = value % 10 / 10f;

        for (int block = 0; block < 10; block++)
        {
            float intensity = block < full ? 1f
                : block == full && value < 100 ? fraction : 0f;
            if (Running && phase && block == full && value < 100)
                intensity = 1f - intensity;

            Color c = Mix(off, on, intensity);
            using var brush = new SolidBrush(c);
            int x0 = pad + block * (width + gap);
            for (int r = 0; r < 3; r++)
            for (int col = 0; col < 4; col++)
            {
                int dx = x0 + (col * (width - 5)) / 4;
                g.FillEllipse(brush, dx, y0 + r * 7, 4, 4);
            }
        }
    }

    private static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }
}
