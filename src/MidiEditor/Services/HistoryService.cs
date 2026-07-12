using MidiEditor.Models;

namespace MidiEditor.Services;

public sealed class HistoryService
{
    private readonly Stack<MidiProject> _undo = new();
    private readonly Stack<MidiProject> _redo = new();
    private MidiProject? _pending;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Begin(MidiProject project)
    {
        _pending ??= project.Clone();
    }

    public bool Commit(MidiProject current)
    {
        if (_pending is null)
            return false;

        if (AreEquivalent(_pending, current))
        {
            _pending = null;
            return false;
        }

        _undo.Push(_pending);
        _pending = null;
        _redo.Clear();

        while (_undo.Count > 80)
        {
            var keepNewestFirst = _undo.Take(80).ToArray();
            _undo.Clear();
            for (var index = keepNewestFirst.Length - 1; index >= 0; index--)
                _undo.Push(keepNewestFirst[index]);
        }

        return true;
    }

    public void Cancel() => _pending = null;

    public void InvalidateRedo()
    {
        _pending = null;
        _redo.Clear();
    }

    public MidiProject? Undo(MidiProject current)
    {
        _pending = null;
        if (_undo.Count == 0)
            return null;
        _redo.Push(current.Clone());
        return _undo.Pop();
    }

    public MidiProject? Redo(MidiProject current)
    {
        _pending = null;
        if (_redo.Count == 0)
            return null;
        _undo.Push(current.Clone());
        return _redo.Pop();
    }

    public void Clear()
    {
        _pending = null;
        _undo.Clear();
        _redo.Clear();
    }

    public static bool AreEquivalent(MidiProject left, MidiProject right)
    {
        if (left.Name != right.Name || left.Tempo != right.Tempo || left.BeatsPerBar != right.BeatsPerBar ||
            left.BeatUnit != right.BeatUnit || left.Bars != right.Bars || left.LoopStartBeat != right.LoopStartBeat ||
            left.LoopEndBeat != right.LoopEndBeat || left.LoopEnabled != right.LoopEnabled ||
            left.SoundFontPath != right.SoundFontPath || left.Tracks.Count != right.Tracks.Count ||
            left.TempoChanges.Count != right.TempoChanges.Count)
            return false;

        for (var index = 0; index < left.TempoChanges.Count; index++)
        {
            var a = left.TempoChanges[index];
            var b = right.TempoChanges[index];
            if (a.Beat != b.Beat || a.BeatsPerMinute != b.BeatsPerMinute)
                return false;
        }

        for (var trackIndex = 0; trackIndex < left.Tracks.Count; trackIndex++)
        {
            var a = left.Tracks[trackIndex];
            var b = right.Tracks[trackIndex];
            if (a.Id != b.Id || a.Name != b.Name || a.Kind != b.Kind || a.Channel != b.Channel ||
                a.Program != b.Program || a.Bank != b.Bank || a.Color != b.Color || a.IsMuted != b.IsMuted ||
                a.IsSolo != b.IsSolo || a.Volume != b.Volume || a.Notes.Count != b.Notes.Count)
                return false;

            for (var noteIndex = 0; noteIndex < a.Notes.Count; noteIndex++)
            {
                var x = a.Notes[noteIndex];
                var y = b.Notes[noteIndex];
                if (x.Id != y.Id || x.StartBeat != y.StartBeat || x.LengthBeats != y.LengthBeats ||
                    x.Pitch != y.Pitch || x.Velocity != y.Velocity)
                    return false;
            }
        }

        return true;
    }
}
