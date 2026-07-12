namespace MidiEditor.Models;

public sealed class TempoChange : ObservableObject
{
    private double _beat;
    private double _beatsPerMinute = 120;

    public double Beat
    {
        get => _beat;
        set => SetField(ref _beat, Math.Max(0, value));
    }

    public double BeatsPerMinute
    {
        get => _beatsPerMinute;
        set => SetField(ref _beatsPerMinute, Math.Clamp(value, 20, 300));
    }

    public TempoChange Clone() => new() { Beat = Beat, BeatsPerMinute = BeatsPerMinute };
}

