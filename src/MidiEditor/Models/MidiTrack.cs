using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace MidiEditor.Models;

public enum TrackKind
{
    Instrument,
    Drums
}

public sealed class MidiTrack : ObservableObject
{
    private string _name = "Instrument";
    private TrackKind _kind;
    private int _channel;
    private int _program;
    private int _bank;
    private string _color = "#63D5A7";
    private bool _isMuted;
    private bool _isSolo;
    private double _volume = 0.9;

    public MidiTrack()
    {
        Notes.CollectionChanged += NotesOnCollectionChanged;
    }

    public Guid Id { get; init; } = Guid.NewGuid();
    public ObservableCollection<MidiNote> Notes { get; } = [];

    public string Name
    {
        get => _name;
        set => SetField(ref _name, string.IsNullOrWhiteSpace(value) ? "Untitled" : value.Trim());
    }

    public TrackKind Kind
    {
        get => _kind;
        set
        {
            if (SetField(ref _kind, value))
                OnPropertyChanged(nameof(KindLabel));
        }
    }

    public string KindLabel => Kind == TrackKind.Drums ? "DRUMS" : $"CH {Channel + 1:00}";

    public int Channel
    {
        get => _channel;
        set
        {
            if (SetField(ref _channel, Math.Clamp(value, 0, 15)))
                OnPropertyChanged(nameof(KindLabel));
        }
    }

    public int Program
    {
        get => _program;
        set
        {
            if (SetField(ref _program, Math.Clamp(value, 0, 127)))
                OnPropertyChanged(nameof(ProgramLabel));
        }
    }

    public int Bank
    {
        get => _bank;
        set => SetField(ref _bank, Math.Clamp(value, 0, 16383));
    }

    public string ProgramLabel => Kind == TrackKind.Drums ? "GM Drum Kit" : GeneralMidiPrograms.GetName(Program);

    public string Color
    {
        get => _color;
        set => SetField(ref _color, value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set => SetField(ref _isMuted, value);
    }

    public bool IsSolo
    {
        get => _isSolo;
        set => SetField(ref _isSolo, value);
    }

    public double Volume
    {
        get => _volume;
        set => SetField(ref _volume, Math.Clamp(value, 0, 1));
    }

    public double EndBeat => Notes.Count == 0 ? 0 : Notes.Max(note => note.EndBeat);

    public MidiTrack Clone()
    {
        var clone = new MidiTrack
        {
            Id = Id,
            Name = Name,
            Kind = Kind,
            Channel = Channel,
            Program = Program,
            Bank = Bank,
            Color = Color,
            IsMuted = IsMuted,
            IsSolo = IsSolo,
            Volume = Volume
        };

        foreach (var note in Notes)
            clone.Notes.Add(note.Clone());

        return clone;
    }

    private void NotesOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) =>
        OnPropertyChanged(nameof(EndBeat));
}

public static class GeneralMidiPrograms
{
    private static readonly string[] Names =
    [
        "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano",
        "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavinet",
        "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
        "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
        "Acoustic Guitar (nylon)", "Acoustic Guitar (steel)", "Electric Guitar (jazz)", "Electric Guitar (clean)",
        "Electric Guitar (muted)", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics",
        "Acoustic Bass", "Electric Bass (finger)", "Electric Bass (pick)", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
        "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
        "String Ensemble 1", "String Ensemble 2", "SynthStrings 1", "SynthStrings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
        "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "SynthBrass 1", "SynthBrass 2",
        "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
        "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
        "Lead 1 (square)", "Lead 2 (sawtooth)", "Lead 3 (calliope)", "Lead 4 (chiff)", "Lead 5 (charang)", "Lead 6 (voice)", "Lead 7 (fifths)", "Lead 8 (bass + lead)",
        "Pad 1 (new age)", "Pad 2 (warm)", "Pad 3 (polysynth)", "Pad 4 (choir)", "Pad 5 (bowed)", "Pad 6 (metallic)", "Pad 7 (halo)", "Pad 8 (sweep)",
        "FX 1 (rain)", "FX 2 (soundtrack)", "FX 3 (crystal)", "FX 4 (atmosphere)", "FX 5 (brightness)", "FX 6 (goblins)", "FX 7 (echoes)", "FX 8 (sci-fi)",
        "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bag pipe", "Fiddle", "Shanai",
        "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
        "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot"
    ];

    public static IReadOnlyList<string> All => Names;
    public static string GetName(int program) => Names[Math.Clamp(program, 0, 127)];
}

