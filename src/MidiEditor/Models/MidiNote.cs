namespace MidiEditor.Models;

public sealed class MidiNote : ObservableObject
{
    private double _startBeat;
    private double _lengthBeats = 0.25;
    private int _pitch = 60;
    private int _velocity = 100;
    private bool _isSelected;

    public Guid Id { get; init; } = Guid.NewGuid();

    public double StartBeat
    {
        get => _startBeat;
        set => SetField(ref _startBeat, Math.Max(0, value));
    }

    public double LengthBeats
    {
        get => _lengthBeats;
        set => SetField(ref _lengthBeats, Math.Max(1.0 / 64.0, value));
    }

    public int Pitch
    {
        get => _pitch;
        set => SetField(ref _pitch, Math.Clamp(value, 0, 127));
    }

    public int Velocity
    {
        get => _velocity;
        set => SetField(ref _velocity, Math.Clamp(value, 1, 127));
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public double EndBeat => StartBeat + LengthBeats;

    public MidiNote Clone() => new()
    {
        Id = Id,
        StartBeat = StartBeat,
        LengthBeats = LengthBeats,
        Pitch = Pitch,
        Velocity = Velocity,
        IsSelected = IsSelected
    };
}

