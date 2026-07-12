using System.Collections.ObjectModel;

namespace MidiEditor.Models;

public sealed class MidiProject : ObservableObject
{
    private string _name = "Untitled Groove";
    private double _tempo = 128;
    private int _beatsPerBar = 4;
    private int _beatUnit = 4;
    private int _bars = 16;
    private double _loopStartBeat;
    private double _loopEndBeat = 16;
    private bool _loopEnabled = true;
    private string? _soundFontPath;

    public ObservableCollection<MidiTrack> Tracks { get; } = [];
    public ObservableCollection<TempoChange> TempoChanges { get; } = [];

    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? "Untitled Groove" : value.Trim());
    }

    public double Tempo
    {
        get => _tempo;
        set
        {
            var normalized = Math.Clamp(value, 20, 300);
            if (!SetField(ref _tempo, normalized))
                return;
            var initialChange = TempoChanges.FirstOrDefault(change => Math.Abs(change.Beat) < 0.000001);
            if (initialChange is not null)
                initialChange.BeatsPerMinute = normalized;
        }
    }

    public int BeatsPerBar
    {
        get => _beatsPerBar;
        set
        {
            if (SetField(ref _beatsPerBar, Math.Clamp(value, 1, 32)))
                OnPropertyChanged(nameof(QuarterBeatsPerBar));
        }
    }

    public int BeatUnit
    {
        get => _beatUnit;
        set
        {
            var supported = new[] { 1, 2, 4, 8, 16, 32 };
            var normalized = supported.OrderBy(item => Math.Abs(item - value)).First();
            if (SetField(ref _beatUnit, normalized))
                OnPropertyChanged(nameof(QuarterBeatsPerBar));
        }
    }

    public double QuarterBeatsPerBar => BeatsPerBar * 4.0 / BeatUnit;

    public int Bars
    {
        get => _bars;
        set => SetField(ref _bars, Math.Clamp(value, 1, 999));
    }

    public double LoopStartBeat
    {
        get => _loopStartBeat;
        set => SetField(ref _loopStartBeat, Math.Max(0, value));
    }

    public double LoopEndBeat
    {
        get => _loopEndBeat;
        set => SetField(ref _loopEndBeat, Math.Max(0.25, value));
    }

    public bool LoopEnabled
    {
        get => _loopEnabled;
        set => SetField(ref _loopEnabled, value);
    }

    public string? SoundFontPath
    {
        get => _soundFontPath;
        set => SetField(ref _soundFontPath, value);
    }

    public double DurationBeats => Math.Max(Bars * QuarterBeatsPerBar,
        Tracks.Count == 0 ? 0 : Tracks.Max(track => track.EndBeat));

    public MidiProject Clone()
    {
        var clone = new MidiProject
        {
            Name = Name,
            Tempo = Tempo,
            BeatsPerBar = BeatsPerBar,
            BeatUnit = BeatUnit,
            Bars = Bars,
            LoopStartBeat = LoopStartBeat,
            LoopEndBeat = LoopEndBeat,
            LoopEnabled = LoopEnabled,
            SoundFontPath = SoundFontPath
        };

        foreach (var track in Tracks)
            clone.Tracks.Add(track.Clone());
        foreach (var tempoChange in TempoChanges)
            clone.TempoChanges.Add(tempoChange.Clone());

        return clone;
    }

    public static MidiProject CreateDemo()
    {
        var project = new MidiProject();
        var colors = new[] { "#63D5A7", "#6EA8FE", "#B790F5", "#FF9D66" };

        var keys = new MidiTrack { Name = "Neon Keys", Channel = 0, Program = 4, Color = colors[0] };
        var chords = new[]
        {
            new[] { 60, 64, 67 }, new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 55, 59, 62 }
        };
        for (var bar = 0; bar < 4; bar++)
        {
            foreach (var pitch in chords[bar])
                keys.Notes.Add(new MidiNote { StartBeat = bar * 4, LengthBeats = 3.75, Pitch = pitch, Velocity = 82 + pitch % 9 });
        }

        var bass = new MidiTrack { Name = "Soft Bass", Channel = 1, Program = 38, Color = colors[1] };
        var roots = new[] { 36, 33, 29, 31 };
        for (var bar = 0; bar < 4; bar++)
            for (var beat = 0; beat < 4; beat++)
                bass.Notes.Add(new MidiNote { StartBeat = bar * 4 + beat, LengthBeats = 0.8, Pitch = roots[bar], Velocity = beat == 0 ? 108 : 88 });

        var lead = new MidiTrack { Name = "Air Lead", Channel = 2, Program = 81, Color = colors[2] };
        var melody = new[] { 72, 74, 76, 79, 76, 74, 72, 67, 69, 72, 74, 76, 74, 72, 69, 67 };
        for (var i = 0; i < melody.Length; i++)
            lead.Notes.Add(new MidiNote { StartBeat = i, LengthBeats = 0.72, Pitch = melody[i], Velocity = 72 + (i % 4) * 8 });

        var drums = new MidiTrack { Name = "Studio Drums", Kind = TrackKind.Drums, Channel = 9, Program = 0, Color = colors[3] };
        for (var step = 0; step < 64; step++)
        {
            var beat = step / 4.0;
            if (step % 4 == 0)
                drums.Notes.Add(new MidiNote { StartBeat = beat, LengthBeats = 0.12, Pitch = step % 16 == 0 || step % 16 == 8 ? 36 : 42, Velocity = step % 16 == 0 ? 118 : 88 });
            if (step % 8 == 4)
                drums.Notes.Add(new MidiNote { StartBeat = beat, LengthBeats = 0.12, Pitch = 38, Velocity = 108 });
            if (step % 2 == 0)
                drums.Notes.Add(new MidiNote { StartBeat = beat, LengthBeats = 0.1, Pitch = 42, Velocity = step % 4 == 0 ? 82 : 62 });
        }

        project.Tracks.Add(keys);
        project.Tracks.Add(bass);
        project.Tracks.Add(lead);
        project.Tracks.Add(drums);
        return project;
    }
}
