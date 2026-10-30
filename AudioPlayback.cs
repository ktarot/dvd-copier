using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using NAudio.Wave;

namespace DvdCopier;

/// <summary>
/// Audio files use NAudio/Windows codecs. Audio-CD tracks use the Windows MCI
/// CD-Audio command set (legacy, may be unsupported by some drives).
/// Only one playback source can be active at a time.
/// </summary>
internal sealed class AudioEngine : IDisposable
{
    private const string CdAlias = "odd_audio_player";
    private AudioFileReader? reader;
    private WaveOutEvent? output;
    private readonly List<string> files = new();
    private readonly List<long> cdStarts = new();
    private readonly List<long> cdLengths = new();
    private string? cdRoot;
    private bool cdOpen;
    private bool paused;
    private bool requestedPlay;
    private int index;

    public bool IsActive => reader != null || cdOpen;
    public bool IsCd => cdOpen;
    public bool IsPlaying => IsActive && requestedPlay && !paused;
    public bool IsPaused => IsActive && paused;
    public int TrackIndex => index;
    public int TrackCount => cdOpen ? cdStarts.Count : files.Count;
    public string CurrentTitle => !IsActive ? "재생할 음악을 선택하세요"
        : cdOpen ? $"Track {index + 1:00}" : Path.GetFileName(files[index]);

    public TimeSpan Duration
    {
        get
        {
            if (reader != null) return reader.TotalTime;
            if (cdOpen && index < cdLengths.Count)
                return TimeSpan.FromMilliseconds(cdLengths[index]);
            return TimeSpan.Zero;
        }
    }

    public TimeSpan Position
    {
        get
        {
            if (reader != null) return reader.CurrentTime;
            if (!cdOpen) return TimeSpan.Zero;
            try
            {
                long absolute = long.Parse(Mci($"status {CdAlias} position"));
                return TimeSpan.FromMilliseconds(
                    Math.Clamp(absolute - cdStarts[index], 0, cdLengths[index]));
            }
            catch { return TimeSpan.Zero; }
        }
    }

    public IReadOnlyList<string> TrackNames =>
        cdOpen
            ? Enumerable.Range(1, cdStarts.Count).Select(n => $"Track {n:00}").ToArray()
            : files.Select(Path.GetFileName).ToArray()!;

    public string? CurrentFile =>
        reader != null && index < files.Count ? files[index] : null;

    public void OpenFiles(IEnumerable<string> paths)
    {
        var selection = paths.Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (selection.Length == 0)
            throw new IOException("선택한 음악 파일을 찾을 수 없습니다.");
        Stop();
        files.AddRange(selection);
        SelectTrack(0);
    }

    public void OpenAudioCd(string drive)
    {
        Stop();
        if (string.IsNullOrWhiteSpace(drive))
            throw new IOException("ODD 드라이브를 선택하세요.");
        string letter = drive[..1].ToUpperInvariant() + ":";
        try
        {
            Mci($"open {letter} type cdaudio alias {CdAlias} wait");
            cdOpen = true;
            cdRoot = drive;
            Mci($"set {CdAlias} time format milliseconds wait");
            int count = int.Parse(Mci($"status {CdAlias} number of tracks"));
            if (count < 1 || count > 99)
                throw new IOException("오디오 CD 트랙을 찾지 못했습니다.");
            for (int track = 1; track <= count; track++)
            {
                string kind = Mci($"status {CdAlias} type track {track}");
                if (!kind.Contains("audio", StringComparison.OrdinalIgnoreCase))
                    throw new NotSupportedException(
                        "데이터 트랙이 섞여 있는 CD는 현재 버전에서 지원하지 않습니다.");
                cdStarts.Add(long.Parse(Mci($"status {CdAlias} position track {track}")));
                cdLengths.Add(long.Parse(Mci($"status {CdAlias} length track {track}")));
            }
            SelectTrack(0);
        }
        catch
        {
            Stop();
            throw;
        }
    }

    public void SelectTrack(int newIndex)
    {
        if (newIndex < 0 || newIndex >= TrackCount) return;
        if (cdOpen)
        {
            Mci($"stop {CdAlias} wait");
            index = newIndex;
            long start = cdStarts[index];
            long end = start + cdLengths[index];
            Mci($"play {CdAlias} from {start} to {end}");
        }
        else
        {
            output?.Stop();
            output?.Dispose();
            reader?.Dispose();
            output = null;
            reader = null;
            index = newIndex;
            try
            {
                reader = new AudioFileReader(files[index]);
                output = new WaveOutEvent();
                output.Init(reader);
                output.Play();
            }
            catch
            {
                output?.Dispose();
                reader?.Dispose();
                output = null;
                reader = null;
                throw;
            }
        }
        paused = false;
        requestedPlay = true;
    }

    public void TogglePause()
    {
        if (!IsActive) return;
        if (cdOpen)
        {
            Mci(paused ? $"resume {CdAlias}" : $"pause {CdAlias} wait");
        }
        else if (output != null)
        {
            if (paused) output.Play();
            else output.Pause();
        }
        paused = !paused;
        requestedPlay = true;
    }

    public void Play()
    {
        if (!IsActive) return;
        if (paused) { TogglePause(); return; }
        if (cdOpen)
        {
            long start = cdStarts[index];
            Mci($"play {CdAlias} from {start} to {start + cdLengths[index]}");
        }
        else if (reader != null && output != null)
        {
            if (reader.Position >= reader.Length) reader.CurrentTime = TimeSpan.Zero;
            output.Play();
        }
        requestedPlay = true;
    }

    public void StopPlayback()
    {
        if (!IsActive) return;
        if (cdOpen) Mci($"stop {CdAlias} wait");
        else output?.Stop();
        paused = false;
        requestedPlay = false;
    }

    public void Next() => SelectTrack(Math.Min(index + 1, TrackCount - 1));
    public void Previous() => SelectTrack(Math.Max(index - 1, 0));

    public void SeekFraction(double fraction)
    {
        if (!IsActive || Duration.TotalMilliseconds <= 0) return;
        fraction = Math.Clamp(fraction, 0, 1);
        if (cdOpen)
        {
            long start = cdStarts[index] + (long)(cdLengths[index] * fraction);
            long end = cdStarts[index] + cdLengths[index];
            Mci($"play {CdAlias} from {start} to {end}");
        }
        else if (reader != null && output != null)
        {
            reader.CurrentTime = TimeSpan.FromTicks((long)(reader.TotalTime.Ticks * fraction));
            output.Play();
        }
        paused = false;
        requestedPlay = true;
    }

    /// <summary>Returns true when a track has just advanced automatically.</summary>
    public bool Update()
    {
        if (!IsPlaying) return false;
        bool completed;
        if (cdOpen)
        {
            try
            {
                completed = Mci($"status {CdAlias} mode")
                    .Equals("stopped", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
        else
        {
            completed = output?.PlaybackState == PlaybackState.Stopped
                && reader != null && reader.TotalTime > TimeSpan.Zero
                && reader.CurrentTime >= reader.TotalTime - TimeSpan.FromMilliseconds(150);
        }
        if (!completed) return false;
        if (index + 1 < TrackCount)
            SelectTrack(index + 1);
        else
            StopPlayback();
        return true;
    }

    public void Stop()
    {
        requestedPlay = false;
        paused = false;
        try { output?.Stop(); } catch { }
        output?.Dispose();
        reader?.Dispose();
        output = null;
        reader = null;
        if (cdOpen)
        {
            try { Mci($"stop {CdAlias}"); } catch { }
            try { Mci($"close {CdAlias} wait"); } catch { }
        }
        cdOpen = false;
        cdRoot = null;
        files.Clear();
        cdStarts.Clear();
        cdLengths.Clear();
        index = 0;
    }

    public void Dispose() => Stop();

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciSendStringW")]
    private static extern uint MciSendString(
        string command, StringBuilder output, uint outputLength, IntPtr callback);

    [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "mciGetErrorStringW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MciGetErrorString(
        uint error, StringBuilder text, uint textLength);

    private static string Mci(string command)
    {
        var output = new StringBuilder(256);
        uint result = MciSendString(command, output, (uint)output.Capacity, IntPtr.Zero);
        if (result == 0) return output.ToString().Trim();
        var error = new StringBuilder(256);
        MciGetErrorString(result, error, (uint)error.Capacity);
        throw new IOException(
            $"음악 CD 제어 실패 ({result}): {error}\n" +
            "일부 최신 ODD/Windows 조합은 레거시 CD 재생 명령을 지원하지 않습니다.");
    }
}

/// <summary>Local sidecar LRC/TXT lyrics only; no online lyrics retrieval.</summary>
internal sealed class LyricsDocument
{
    private readonly List<(TimeSpan Time, string Line)> cues = new();
    private readonly string plain;
    public bool IsTimed => cues.Count > 0;
    public bool HasContent => cues.Count > 0 || !string.IsNullOrWhiteSpace(plain);

    private LyricsDocument(string content, bool timed)
    {
        plain = timed ? "" : content.Trim();
        if (!timed) return;
        foreach (string line in content.Split('\n'))
        {
            var matches = System.Text.RegularExpressions.Regex.Matches(
                line, @"\[(\d{1,2}):(\d{2})(?:\.(\d{1,3}))?\]");
            if (matches.Count == 0) continue;
            string lyric = System.Text.RegularExpressions.Regex.Replace(
                line, @"\[\d{1,2}:\d{2}(?:\.\d{1,3})?\]", "").Trim();
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                int min = int.Parse(m.Groups[1].Value);
                int sec = int.Parse(m.Groups[2].Value);
                if (sec > 59) continue;
                string fraction = m.Groups[3].Value.PadRight(3, '0');
                int ms = fraction.Length == 0 ? 0 : int.Parse(fraction);
                cues.Add((TimeSpan.FromMinutes(min) + TimeSpan.FromSeconds(sec)
                    + TimeSpan.FromMilliseconds(ms), lyric));
            }
        }
        cues.Sort((a, b) => a.Time.CompareTo(b.Time));
    }

    public static LyricsDocument? FromAudioFile(string? audioPath)
    {
        if (string.IsNullOrWhiteSpace(audioPath)) return null;
        foreach (string ext in new[] { ".lrc", ".txt" })
        {
            string candidate = Path.ChangeExtension(audioPath, ext);
            if (!File.Exists(candidate)) continue;
            try
            {
                var doc = new LyricsDocument(File.ReadAllText(candidate), ext == ".lrc");
                if (doc.HasContent) return doc;
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return null;
    }

    public string LineAt(TimeSpan position)
    {
        if (!IsTimed) return plain;
        int hit = -1;
        for (int i = 0; i < cues.Count; i++)
        {
            if (cues[i].Time > position) break;
            hit = i;
        }
        return hit < 0 ? "♪" : cues[hit].Line;
    }
}
