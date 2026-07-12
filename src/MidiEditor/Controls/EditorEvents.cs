namespace MidiEditor.Controls;

public sealed class TrackSelectedEventArgs(int trackIndex) : EventArgs
{
    public int TrackIndex { get; } = trackIndex;
}

public sealed class SeekRequestedEventArgs(double beat) : EventArgs
{
    public double Beat { get; } = Math.Max(0, beat);
}

public sealed class NotePreviewEventArgs(int pitch, int velocity, bool isNoteOn) : EventArgs
{
    public int Pitch { get; } = Math.Clamp(pitch, 0, 127);
    public int Velocity { get; } = Math.Clamp(velocity, 0, 127);
    public bool IsNoteOn { get; } = isNoteOn;
}

