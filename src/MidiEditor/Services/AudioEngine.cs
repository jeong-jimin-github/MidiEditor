using System.Collections.Concurrent;
using System.IO;
using MeltySynth;
using MidiEditor.Models;
using NAudio.Wave;

namespace MidiEditor.Services;

public sealed class AudioEngine : IDisposable
{
    public const int SampleRate = 44100;

    private WaveOutEvent? _output;
    private SoundFontPlaybackSource? _source;

    public bool IsLoaded => _source is not null;
    public bool IsPlaying => _source?.IsPlaying == true;
    public double CurrentBeat => _source?.CurrentBeat ?? 0;
    public string? SoundFontName { get; private set; }
    public string? SoundFontPath { get; private set; }

    public async Task LoadSoundFontAsync(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(LocalizationService.Get("Error.SoundFontFileMissing"), path);

        var soundFont = await Task.Run(() => new SoundFont(path));
        DisposeOutput();

        var source = new SoundFontPlaybackSource(soundFont, SampleRate);
        var output = new WaveOutEvent
        {
            DesiredLatency = 60,
            NumberOfBuffers = 3
        };

        output.Init(source);
        output.Play();

        _source = source;
        _output = output;
        SoundFontName = Path.GetFileNameWithoutExtension(path);
        SoundFontPath = path;
    }

    public void Start(MidiProject project, double startBeat)
    {
        if (_source is null)
            return;

        _source.Start(PlaybackPlan.Create(project, SampleRate), startBeat);
    }

    public void Stop() => _source?.Stop();

    public void Seek(MidiProject project, double beat)
    {
        if (IsPlaying)
            Start(project, beat);
        else
            _source?.SetPosition(beat);
    }

    public void PreviewNote(int channel, int program, int pitch, int velocity, int bank = 0)
    {
        _source?.PreviewNote(channel, bank, program, pitch, velocity);
    }

    public void PreviewNoteOff(int channel, int pitch) => _source?.PreviewNoteOff(channel, pitch);

    public void Panic() => _source?.Panic();

    public void Unload() => DisposeOutput();

    public void Dispose()
    {
        DisposeOutput();
        GC.SuppressFinalize(this);
    }

    private void DisposeOutput()
    {
        if (_output is not null)
        {
            _output.Stop();
            _output.Dispose();
        }

        _output = null;
        _source = null;
        SoundFontName = null;
        SoundFontPath = null;
    }
}

internal sealed class SoundFontPlaybackSource : ISampleProvider
{
    private readonly Synthesizer _synthesizer;
    private readonly ConcurrentQueue<EngineCommand> _commands = new();
    private readonly int[] _activeSequenceNotes = new int[16 * 128];
    private PlaybackPlan? _plan;
    private long _samplePosition;
    private int _eventIndex;
    private bool _isPlaying;
    private long _currentBeatBits;

    public SoundFontPlaybackSource(SoundFont soundFont, int sampleRate)
    {
        var settings = new SynthesizerSettings(sampleRate)
        {
            MaximumPolyphony = 192,
            EnableReverbAndChorus = true
        };
        _synthesizer = new Synthesizer(soundFont, settings);
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
    }

    public WaveFormat WaveFormat { get; }
    public bool IsPlaying => Volatile.Read(ref _isPlaying);
    public double CurrentBeat => BitConverter.Int64BitsToDouble(Interlocked.Read(ref _currentBeatBits));

    public void Start(PlaybackPlan plan, double startBeat) =>
        _commands.Enqueue(new StartCommand(plan, startBeat));

    public void Stop() => _commands.Enqueue(new StopCommand());

    public void SetPosition(double beat) => _commands.Enqueue(new PositionCommand(beat));

    public void PreviewNote(int channel, int bank, int program, int pitch, int velocity) =>
        _commands.Enqueue(new PreviewCommand(channel, bank, program, pitch, velocity, true));

    public void PreviewNoteOff(int channel, int pitch) =>
        _commands.Enqueue(new PreviewCommand(channel, 0, 0, pitch, 0, false));

    public void Panic() => _commands.Enqueue(new StopCommand());

    public int Read(float[] buffer, int offset, int count)
    {
        DrainCommands();

        var frames = count / 2;
        var renderedFrames = 0;

        while (renderedFrames < frames)
        {
            DrainCommands();

            if (!_isPlaying || _plan is null)
            {
                Render(buffer, offset + renderedFrames * 2, frames - renderedFrames);
                renderedFrames = frames;
                break;
            }

            ProcessEventsAtCurrentPosition();

            var boundary = _plan.EndSample;
            if (_plan.LoopEnabled)
                boundary = Math.Min(boundary, _plan.LoopEndSample);
            if (_eventIndex < _plan.Events.Count)
                boundary = Math.Min(boundary, _plan.Events[_eventIndex].Sample);

            if (boundary <= _samplePosition)
            {
                if (HandleBoundary())
                    continue;

                // Defensive progress for malformed or rounded event positions.
                boundary = _samplePosition + 1;
            }

            var segmentFrames = (int)Math.Min(frames - renderedFrames, boundary - _samplePosition);
            Render(buffer, offset + renderedFrames * 2, segmentFrames);
            renderedFrames += segmentFrames;
            _samplePosition += segmentFrames;
            UpdateCurrentBeat();

            if (_samplePosition >= boundary)
                HandleBoundary();
        }

        if ((count & 1) != 0)
            buffer[offset + count - 1] = 0;

        return count;
    }

    private void DrainCommands()
    {
        while (_commands.TryDequeue(out var command))
        {
            switch (command)
            {
                case StartCommand start:
                    ResetSynthesizer();
                    _plan = start.Plan;
                    ApplyInitialMessages(start.Plan);
                    var clampedBeat = Math.Clamp(start.StartBeat, 0, start.Plan.EndBeat);
                    if (start.Plan.LoopEnabled && clampedBeat >= start.Plan.LoopEndBeat)
                        clampedBeat = start.Plan.LoopStartBeat;
                    _samplePosition = start.Plan.BeatToSample(clampedBeat);
                    _eventIndex = FindEventIndex(start.Plan.Events, _samplePosition);
                    StartNotesActiveAt(start.Plan, _samplePosition);
                    _isPlaying = true;
                    SetCurrentBeat(clampedBeat);
                    break;

                case StopCommand:
                    _isPlaying = false;
                    ResetSynthesizer();
                    break;

                case PositionCommand position:
                    SetCurrentBeat(Math.Max(0, position.Beat));
                    break;

                case PreviewCommand preview:
                    if (preview.IsNoteOn)
                    {
                        _synthesizer.ProcessMidiMessage(preview.Channel, 0xB0, 0, (preview.Bank >> 7) & 0x7F);
                        _synthesizer.ProcessMidiMessage(preview.Channel, 0xB0, 32, preview.Bank & 0x7F);
                        _synthesizer.ProcessMidiMessage(preview.Channel, 0xC0, preview.Program, 0);
                        _synthesizer.NoteOn(preview.Channel, preview.Pitch, preview.Velocity);
                    }
                    else
                    {
                        // Do not let an audition release cut a sequenced note using the same key/channel.
                        if (_activeSequenceNotes[preview.Channel * 128 + preview.Pitch] == 0)
                            _synthesizer.NoteOff(preview.Channel, preview.Pitch);
                    }
                    break;
            }
        }
    }

    private void ProcessEventsAtCurrentPosition()
    {
        if (_plan is null)
            return;

        while (_eventIndex < _plan.Events.Count && _plan.Events[_eventIndex].Sample <= _samplePosition)
        {
            var midiEvent = _plan.Events[_eventIndex++];
            if (midiEvent.Sample == _samplePosition)
                ProcessScheduledEvent(midiEvent);
        }
    }

    private bool HandleBoundary()
    {
        if (_plan is null)
            return false;

        ProcessEventsAtCurrentPosition();

        if (_plan.LoopEnabled && _samplePosition >= _plan.LoopEndSample)
        {
            ResetSynthesizer();
            ApplyInitialMessages(_plan);
            _samplePosition = _plan.LoopStartSample;
            _eventIndex = FindEventIndex(_plan.Events, _samplePosition);
            StartNotesActiveAt(_plan, _samplePosition);
            UpdateCurrentBeat();
            return true;
        }

        if (_samplePosition >= _plan.EndSample)
        {
            _isPlaying = false;
            ResetSynthesizer();
            SetCurrentBeat(_plan.EndBeat);
            return true;
        }

        return false;
    }

    private void ApplyInitialMessages(PlaybackPlan plan)
    {
        foreach (var message in plan.InitialMessages)
            _synthesizer.ProcessMidiMessage(message.Channel, message.Command, message.Data1, message.Data2);
    }

    private void StartNotesActiveAt(PlaybackPlan plan, long sample)
    {
        foreach (var note in plan.Notes.Where(note => note.OnSample < sample && note.OffSample > sample))
        {
            _activeSequenceNotes[note.Channel * 128 + note.Pitch]++;
            _synthesizer.NoteOn(note.Channel, note.Pitch, note.Velocity);
        }
    }

    private void ProcessScheduledEvent(ScheduledMidiEvent midiEvent)
    {
        var noteIndex = midiEvent.Channel * 128 + midiEvent.Data1;
        if (midiEvent.Command == 0x90 && midiEvent.Data2 > 0)
        {
            _activeSequenceNotes[noteIndex]++;
            _synthesizer.ProcessMidiMessage(midiEvent.Channel, midiEvent.Command, midiEvent.Data1, midiEvent.Data2);
            return;
        }

        if (midiEvent.Command == 0x80 || midiEvent.Command == 0x90 && midiEvent.Data2 == 0)
        {
            if (_activeSequenceNotes[noteIndex] > 1)
            {
                _activeSequenceNotes[noteIndex]--;
                return;
            }

            _activeSequenceNotes[noteIndex] = 0;
        }

        _synthesizer.ProcessMidiMessage(midiEvent.Channel, midiEvent.Command, midiEvent.Data1, midiEvent.Data2);
    }

    private void ResetSynthesizer()
    {
        Array.Clear(_activeSequenceNotes);
        _synthesizer.Reset();
    }

    private void Render(float[] buffer, int offset, int frameCount)
    {
        if (frameCount <= 0)
            return;

        _synthesizer.RenderInterleaved(buffer.AsSpan(offset, frameCount * 2));
    }

    private void UpdateCurrentBeat()
    {
        if (_plan is not null)
            SetCurrentBeat(_plan.SampleToBeat(_samplePosition));
    }

    private void SetCurrentBeat(double beat) =>
        Interlocked.Exchange(ref _currentBeatBits, BitConverter.DoubleToInt64Bits(beat));

    private static int FindEventIndex(IReadOnlyList<ScheduledMidiEvent> events, long sample)
    {
        var low = 0;
        var high = events.Count;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (events[middle].Sample < sample)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

    private abstract record EngineCommand;
    private sealed record StartCommand(PlaybackPlan Plan, double StartBeat) : EngineCommand;
    private sealed record StopCommand : EngineCommand;
    private sealed record PositionCommand(double Beat) : EngineCommand;
    private sealed record PreviewCommand(int Channel, int Bank, int Program, int Pitch, int Velocity, bool IsNoteOn) : EngineCommand;
}

internal sealed class PlaybackPlan
{
    private PlaybackPlan(
        IReadOnlyList<ScheduledMidiEvent> events,
        IReadOnlyList<ScheduledNote> notes,
        IReadOnlyList<MidiMessage> initialMessages,
        TempoTimeline timeline,
        double endBeat,
        bool loopEnabled,
        double loopStartBeat,
        double loopEndBeat)
    {
        Events = events;
        Notes = notes;
        InitialMessages = initialMessages;
        Timeline = timeline;
        EndBeat = endBeat;
        EndSample = Math.Max(1, BeatToSample(endBeat));
        LoopEnabled = loopEnabled && loopEndBeat > loopStartBeat;
        LoopStartBeat = loopStartBeat;
        LoopEndBeat = loopEndBeat;
        LoopStartSample = BeatToSample(loopStartBeat);
        LoopEndSample = Math.Min(EndSample, BeatToSample(loopEndBeat));
    }

    public IReadOnlyList<ScheduledMidiEvent> Events { get; }
    public IReadOnlyList<ScheduledNote> Notes { get; }
    public IReadOnlyList<MidiMessage> InitialMessages { get; }
    public TempoTimeline Timeline { get; }
    public double EndBeat { get; }
    public long EndSample { get; }
    public bool LoopEnabled { get; }
    public double LoopStartBeat { get; }
    public double LoopEndBeat { get; }
    public long LoopStartSample { get; }
    public long LoopEndSample { get; }

    public long BeatToSample(double beat) => Timeline.BeatToSample(beat);
    public double SampleToBeat(long sample) => Timeline.SampleToBeat(sample);

    public static PlaybackPlan Create(MidiProject project, int sampleRate)
    {
        var events = new List<ScheduledMidiEvent>();
        var notes = new List<ScheduledNote>();
        var pendingNotes = new List<PendingNote>();
        var setup = new List<MidiMessage>();
        var hasSolo = project.Tracks.Any(track => track.IsSolo);
        var timeline = TempoTimeline.Create(project, sampleRate);

        foreach (var track in project.Tracks)
        {
            if (track.IsMuted || hasSolo && !track.IsSolo)
                continue;

            var channel = track.Kind == TrackKind.Drums ? 9 : track.Channel;
            setup.Add(new MidiMessage(channel, 0xB0, 0, (track.Bank >> 7) & 0x7F));
            setup.Add(new MidiMessage(channel, 0xB0, 32, track.Bank & 0x7F));
            setup.Add(new MidiMessage(channel, 0xC0, track.Program, 0));
            setup.Add(new MidiMessage(channel, 0xB0, 7, (int)Math.Round(track.Volume * 127)));

            foreach (var note in track.Notes)
                pendingNotes.Add(new PendingNote(note.StartBeat, note.EndBeat, channel, note.Pitch, note.Velocity));
        }

        // MIDI NoteOff addresses channel+pitch, not an individual voice. Retriggering the same
        // key therefore truncates the previous note at the next NoteOn to avoid stuck/extended voices.
        foreach (var pitchGroup in pendingNotes.GroupBy(note => (note.Channel, note.Pitch)))
        {
            var ordered = pitchGroup.GroupBy(note => note.StartBeat)
                .Select(group => group.OrderByDescending(note => note.Velocity).First())
                .OrderBy(note => note.StartBeat)
                .ToArray();
            for (var index = 0; index < ordered.Length; index++)
            {
                var note = ordered[index];
                var effectiveEndBeat = note.EndBeat;
                if (index + 1 < ordered.Length)
                    effectiveEndBeat = Math.Min(effectiveEndBeat, ordered[index + 1].StartBeat);
                var onSample = timeline.BeatToSample(note.StartBeat);
                var offSample = timeline.BeatToSample(Math.Max(note.StartBeat + 1.0 / 960.0, effectiveEndBeat));
                events.Add(new ScheduledMidiEvent(onSample, 2, note.Channel, 0x90, note.Pitch, note.Velocity));
                offSample = Math.Max(onSample + 1, offSample);
                events.Add(new ScheduledMidiEvent(offSample, 1, note.Channel, 0x80, note.Pitch, 0));
                notes.Add(new ScheduledNote(onSample, offSample, note.Channel, note.Pitch, note.Velocity));
            }
        }

        var sorted = events.OrderBy(item => item.Sample).ThenBy(item => item.Priority).ToArray();
        var endBeat = Math.Max(0.25, project.DurationBeats);
        var loopEnd = Math.Clamp(project.LoopEndBeat, 0.25, endBeat);
        var loopStart = Math.Clamp(project.LoopStartBeat, 0, Math.Max(0, loopEnd - 0.25));
        return new PlaybackPlan(sorted, notes, setup, timeline, endBeat,
            project.LoopEnabled, loopStart, loopEnd);
    }

    private readonly record struct PendingNote(double StartBeat, double EndBeat, int Channel, int Pitch, int Velocity);
}

internal sealed class TempoTimeline
{
    private readonly TempoSegment[] _segments;

    private TempoTimeline(int sampleRate, TempoSegment[] segments)
    {
        SampleRate = sampleRate;
        _segments = segments;
    }

    public int SampleRate { get; }

    public long BeatToSample(double beat)
    {
        beat = Math.Max(0, beat);
        var segment = _segments[0];
        foreach (var candidate in _segments)
        {
            if (candidate.Beat > beat)
                break;
            segment = candidate;
        }
        return (long)Math.Round(segment.StartSample + (beat - segment.Beat) * SampleRate * 60.0 / segment.BeatsPerMinute);
    }

    public double SampleToBeat(long sample)
    {
        sample = Math.Max(0, sample);
        var segment = _segments[0];
        foreach (var candidate in _segments)
        {
            if (candidate.StartSample > sample)
                break;
            segment = candidate;
        }
        return segment.Beat + (sample - segment.StartSample) * segment.BeatsPerMinute / (SampleRate * 60.0);
    }

    public static TempoTimeline Create(MidiProject project, int sampleRate)
    {
        var points = project.TempoChanges
            .Where(change => change.Beat >= 0)
            .OrderBy(change => change.Beat)
            .GroupBy(change => change.Beat)
            .Select(group => group.Last())
            .Select(change => (change.Beat, change.BeatsPerMinute))
            .ToList();
        if (points.Count == 0 || points[0].Beat > 0.000001)
            points.Insert(0, (0, project.Tempo));

        var segments = new List<TempoSegment>(points.Count);
        var startSample = 0.0;
        for (var index = 0; index < points.Count; index++)
        {
            if (index > 0)
            {
                var previous = points[index - 1];
                startSample += (points[index].Beat - previous.Beat) * sampleRate * 60.0 / previous.BeatsPerMinute;
            }
            segments.Add(new TempoSegment(points[index].Beat, points[index].BeatsPerMinute, startSample));
        }
        return new TempoTimeline(sampleRate, segments.ToArray());
    }

    private readonly record struct TempoSegment(double Beat, double BeatsPerMinute, double StartSample);
}

internal readonly record struct ScheduledMidiEvent(long Sample, int Priority, int Channel, int Command, int Data1, int Data2);
internal readonly record struct ScheduledNote(long OnSample, long OffSample, int Channel, int Pitch, int Velocity);
internal readonly record struct MidiMessage(int Channel, int Command, int Data1, int Data2);
