using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using MidiEditor.Models;
using NAudio.Wave;

namespace MidiEditor.Services;

public sealed record VoicebankInfo(string Name, string Path)
{
    public override string ToString() => Name;
}

public static class VocalIntegrationService
{
    private const int SampleRate = 44100;

    public static IReadOnlyList<VoicebankInfo> DiscoverVoicebanks(string? rootPath)
    {
        var result = new List<VoicebankInfo>();
        AddVoicebank(result, BundledAssetsService.DefaultVoicebankPath, "PulseGrid Default Voice");

        if (!string.IsNullOrWhiteSpace(rootPath) && Directory.Exists(rootPath))
        {
            if (IsVoicebank(rootPath))
                AddVoicebank(result, rootPath, ReadVoicebankName(rootPath));

            try
            {
                foreach (var directory in Directory.EnumerateDirectories(rootPath))
                    if (IsVoicebank(directory))
                        AddVoicebank(result, directory, ReadVoicebankName(directory));
            }
            catch (UnauthorizedAccessException)
            {
                // Keep any already discovered voicebanks.
            }
        }

        return result
            .GroupBy(item => Path.GetFullPath(item.Path), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderBy(item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    public static string ResolveVoicebankPath(MidiTrack track, VocalToolSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(track.VoicebankPath) && Directory.Exists(track.VoicebankPath))
            return track.VoicebankPath;

        var discovered = DiscoverVoicebanks(settings.VoicebankRootPath);
        return discovered.FirstOrDefault()?.Path
            ?? throw new DirectoryNotFoundException("사용 가능한 OpenUtau/UTAU 보이스뱅크를 찾을 수 없습니다.");
    }

    public static string ExportUst(string path, MidiProject project, MidiTrack track, VocalToolSettings settings)
    {
        if (track.Kind != TrackKind.Vocal)
            throw new InvalidOperationException("UST 내보내기는 Vocal 트랙에서만 사용할 수 있습니다.");

        var voicebank = ResolveVoicebankPath(track, settings);
        var notes = track.Notes.OrderBy(note => note.StartBeat).ThenBy(note => note.Pitch).ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("[#VERSION]");
        builder.AppendLine("UST Version1.2");
        builder.AppendLine("[#SETTING]");
        builder.AppendLine($"Tempo={project.Tempo.ToString("0.###", CultureInfo.InvariantCulture)}");
        builder.AppendLine("Tracks=1");
        builder.AppendLine($"ProjectName={SanitizeUstValue(project.Name)} - {SanitizeUstValue(track.Name)}");
        builder.AppendLine($"VoiceDir={voicebank}");
        builder.AppendLine($"OutFile={Path.ChangeExtension(path, ".wav")}");
        builder.AppendLine($"CacheDir={Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "cache")}");
        if (!string.IsNullOrWhiteSpace(settings.ResamplerPath))
            builder.AppendLine($"Tool1={settings.ResamplerPath}");
        if (!string.IsNullOrWhiteSpace(settings.WavtoolPath))
            builder.AppendLine($"Tool2={settings.WavtoolPath}");
        builder.AppendLine("Mode2=True");

        var index = 0;
        var cursorTick = 0L;
        foreach (var note in notes)
        {
            var startTick = Math.Max(0L, (long)Math.Round(note.StartBeat * 480));
            var noteLength = Math.Max(1L, (long)Math.Round(note.LengthBeats * 480));
            if (startTick > cursorTick)
            {
                AppendUstNote(builder, index++, startTick - cursorTick, "R", 60, 100);
                cursorTick = startTick;
            }

            if (startTick < cursorTick)
                noteLength = Math.Max(1, noteLength - (cursorTick - startTick));

            AppendUstNote(builder, index++, noteLength,
                string.IsNullOrWhiteSpace(note.Lyric) ? "a" : note.Lyric,
                note.Pitch, note.Velocity);
            cursorTick += noteLength;
        }
        builder.AppendLine("[#TRACKEND]");

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(false));
        return path;
    }

    public static string ExportTemporaryUst(MidiProject project, MidiTrack track, VocalToolSettings settings)
    {
        var directory = Path.Combine(Path.GetTempPath(), "PulseGrid", "OpenUtau");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, $"{SanitizeFileName(project.Name)}-{SanitizeFileName(track.Name)}.ust");
        return ExportUst(path, project, track, settings);
    }

    public static string OpenInOpenUtau(MidiProject project, MidiTrack track, VocalToolSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.OpenUtauPath) || !File.Exists(settings.OpenUtauPath))
            throw new FileNotFoundException("보컬 설정에서 OpenUtau 실행 파일 경로를 먼저 지정해 주세요.", settings.OpenUtauPath);

        var ustPath = ExportTemporaryUst(project, track, settings);
        Process.Start(new ProcessStartInfo
        {
            FileName = settings.OpenUtauPath,
            Arguments = $"\"{ustPath}\"",
            WorkingDirectory = Path.GetDirectoryName(settings.OpenUtauPath) ?? Environment.CurrentDirectory,
            UseShellExecute = true
        });
        return ustPath;
    }

    public static Task<string> RenderQuickPreviewAsync(MidiProject project, MidiTrack track, VocalToolSettings settings) =>
        Task.Run(() => RenderQuickPreview(project, track, settings));

    public static string RenderQuickPreview(MidiProject project, MidiTrack track, VocalToolSettings settings)
    {
        if (track.Kind != TrackKind.Vocal)
            throw new InvalidOperationException("빠른 보컬 미리듣기는 Vocal 트랙에서만 사용할 수 있습니다.");
        if (track.Notes.Count == 0)
            throw new InvalidOperationException("렌더할 보컬 노트가 없습니다.");

        var voicebank = ResolveVoicebankPath(track, settings);
        var aliases = LoadAliasMap(voicebank);
        if (aliases.Count == 0)
            throw new InvalidDataException("보이스뱅크에서 oto.ini와 WAV 샘플을 찾지 못했습니다.");

        var endSeconds = track.Notes.Max(note => BeatToSeconds(project, note.EndBeat)) + 0.15;
        var mix = new float[Math.Max(1, (int)Math.Ceiling(endSeconds * SampleRate))];
        var cache = new Dictionary<string, float[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in track.Notes.OrderBy(note => note.StartBeat))
        {
            var samplePath = ResolveAliasSample(aliases, note.Lyric);
            if (samplePath is null)
                continue;
            if (!cache.TryGetValue(samplePath, out var source))
            {
                source = ReadMonoSamples(samplePath);
                cache[samplePath] = source;
            }
            if (source.Length < 2)
                continue;

            var start = Math.Max(0, (int)Math.Round(BeatToSeconds(project, note.StartBeat) * SampleRate));
            var duration = Math.Max(1, (int)Math.Round((BeatToSeconds(project, note.EndBeat) - BeatToSeconds(project, note.StartBeat)) * SampleRate));
            var pitchRatio = Math.Pow(2, (note.Pitch - 60) / 12.0);
            var gain = (float)(track.Volume * (note.Velocity / 127.0) * 0.58);
            var fade = Math.Min((int)(SampleRate * 0.015), Math.Max(1, duration / 4));
            for (var i = 0; i < duration && start + i < mix.Length; i++)
            {
                var position = (i * pitchRatio) % (source.Length - 1);
                var a = (int)position;
                var frac = position - a;
                var value = source[a] * (1 - frac) + source[a + 1] * frac;
                var envelope = Math.Min(1.0, Math.Min((i + 1) / (double)fade, (duration - i) / (double)fade));
                mix[start + i] += (float)(value * gain * envelope);
            }
        }

        var peak = mix.Max(value => Math.Abs(value));
        if (peak > 0.96f)
        {
            var scale = 0.96f / peak;
            for (var i = 0; i < mix.Length; i++)
                mix[i] *= scale;
        }

        var directory = Path.Combine(Path.GetTempPath(), "PulseGrid", "VocalPreview");
        Directory.CreateDirectory(directory);
        var output = Path.Combine(directory, "preview.wav");
        WritePcm16Wave(output, mix);
        return output;
    }

    private static Dictionary<string, string> LoadAliasMap(string voicebankPath)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var otoPath in Directory.EnumerateFiles(voicebankPath, "oto.ini", SearchOption.AllDirectories))
        {
            var folder = Path.GetDirectoryName(otoPath)!;
            foreach (var line in File.ReadLines(otoPath))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith(';'))
                    continue;
                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;
                var wav = line[..eq].Trim();
                var right = line[(eq + 1)..];
                var comma = right.IndexOf(',');
                var alias = (comma >= 0 ? right[..comma] : right).Trim();
                if (string.IsNullOrWhiteSpace(alias))
                    alias = Path.GetFileNameWithoutExtension(wav);
                var full = Path.Combine(folder, wav);
                if (File.Exists(full))
                    result.TryAdd(alias, full);
            }
        }
        return result;
    }

    private static string? ResolveAliasSample(Dictionary<string, string> aliases, string? lyric)
    {
        var key = string.IsNullOrWhiteSpace(lyric) ? "a" : lyric.Trim();
        if (aliases.TryGetValue(key, out var exact))
            return exact;
        var vowel = key.ToLowerInvariant().LastOrDefault(ch => "aiueo".Contains(ch));
        if (vowel != default && aliases.TryGetValue(vowel.ToString(), out var fallback))
            return fallback;
        return aliases.Values.FirstOrDefault();
    }

    private static float[] ReadMonoSamples(string path)
    {
        using var reader = new AudioFileReader(path);
        var channels = Math.Max(1, reader.WaveFormat.Channels);
        var buffer = new float[SampleRate * channels];
        var mono = new List<float>();
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < read; i += channels)
            {
                var sum = 0f;
                var count = Math.Min(channels, read - i);
                for (var ch = 0; ch < count; ch++)
                    sum += buffer[i + ch];
                mono.Add(sum / count);
            }
        }
        return mono.ToArray();
    }

    private static double BeatToSeconds(MidiProject project, double targetBeat)
    {
        targetBeat = Math.Max(0, targetBeat);
        var changes = project.TempoChanges
            .Where(change => change.Beat > 0 && change.Beat < targetBeat)
            .OrderBy(change => change.Beat)
            .ToArray();
        var tempo = project.Tempo;
        var previousBeat = 0.0;
        var seconds = 0.0;
        foreach (var change in changes)
        {
            seconds += (change.Beat - previousBeat) * 60.0 / tempo;
            tempo = change.BeatsPerMinute;
            previousBeat = change.Beat;
        }
        seconds += (targetBeat - previousBeat) * 60.0 / tempo;
        return seconds;
    }

    private static void WritePcm16Wave(string path, float[] samples)
    {
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false);
        var dataBytes = samples.Length * 2;
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (var sample in samples)
            writer.Write((short)Math.Clamp((int)Math.Round(sample * 32767), short.MinValue, short.MaxValue));
    }

    private static void AppendUstNote(StringBuilder builder, int index, long length, string lyric, int pitch, int velocity)
    {
        builder.AppendLine($"[#{index:0000}]");
        builder.AppendLine($"Length={Math.Max(1, length)}");
        builder.AppendLine($"Lyric={SanitizeUstValue(lyric)}");
        builder.AppendLine($"NoteNum={Math.Clamp(pitch, 0, 127)}");
        builder.AppendLine($"Intensity={Math.Clamp(velocity, 1, 127)}");
    }

    private static bool IsVoicebank(string path)
    {
        if (!Directory.Exists(path))
            return false;
        if (File.Exists(Path.Combine(path, "character.txt")))
            return true;
        try
        {
            return Directory.EnumerateFiles(path, "oto.ini", SearchOption.AllDirectories).Any();
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static void AddVoicebank(List<VoicebankInfo> result, string path, string name)
    {
        if (Directory.Exists(path) && IsVoicebank(path))
            result.Add(new VoicebankInfo(string.IsNullOrWhiteSpace(name) ? Path.GetFileName(path) : name, path));
    }

    private static string ReadVoicebankName(string path)
    {
        try
        {
            var character = Path.Combine(path, "character.txt");
            if (File.Exists(character))
            {
                foreach (var line in File.ReadLines(character))
                {
                    if (line.StartsWith("name=", StringComparison.OrdinalIgnoreCase))
                        return line[5..].Trim();
                }
            }
        }
        catch { }
        return Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    private static string SanitizeUstValue(string value) => value.Replace("\r", " ").Replace("\n", " ");
    private static string SanitizeFileName(string value)
    {
        foreach (var character in Path.GetInvalidFileNameChars())
            value = value.Replace(character, '_');
        return string.IsNullOrWhiteSpace(value) ? "Vocal" : value;
    }
}

public sealed class VocalPreviewPlayer : IDisposable
{
    private WaveOutEvent? _output;
    private AudioFileReader? _reader;

    public void Play(string path)
    {
        Stop();
        _reader = new AudioFileReader(path);
        _output = new WaveOutEvent();
        _output.Init(_reader);
        _output.Play();
    }

    public void Stop()
    {
        _output?.Stop();
        _output?.Dispose();
        _reader?.Dispose();
        _output = null;
        _reader = null;
    }

    public void Dispose() => Stop();
}
