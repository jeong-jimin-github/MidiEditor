using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MidiEditor.Models;

namespace MidiEditor.Controls;

public sealed class PianoRollControl : FrameworkElement
{
    private const double KeyboardWidth = 72;
    private const double HeaderHeight = 27;
    private const double VelocityHeight = 112;

    private MidiTrack? _track;
    private MidiProject? _project;
    private double _playheadBeat;
    private double _pixelsPerBeat = 62;
    private double _viewStartBeat;
    private int _topPitch = 84;
    private double _rowHeight = 14;
    private double _snapBeats = 0.25;
    private EditMode _mode;
    private MidiNote? _activeNote;
    private Point _dragOrigin;
    private double _originalStart;
    private double _originalLength;
    private int _originalPitch;
    private int? _previewPitch;
    private bool _erasing;
    private Point _lastErasePoint;
    private NoteEditState[] _editGroup = [];

    public PianoRollControl()
    {
        Focusable = true;
        ClipToBounds = true;
        SnapsToDevicePixels = true;
    }

    public MidiTrack? Track
    {
        get => _track;
        set
        {
            if (!ReferenceEquals(_track, value))
                CancelInteraction();
            _track = value;
            InvalidateVisual();
        }
    }

    public MidiProject? Project
    {
        get => _project;
        set { _project = value; CoerceViewport(); InvalidateVisual(); ViewportChanged?.Invoke(this, EventArgs.Empty); }
    }

    public double PlayheadBeat
    {
        get => _playheadBeat;
        set { _playheadBeat = Math.Max(0, value); InvalidateVisual(); }
    }

    public double SnapBeats
    {
        get => _snapBeats;
        set { _snapBeats = Math.Clamp(value, 1.0 / 32, 4); InvalidateVisual(); }
    }

    public double PixelsPerBeat
    {
        get => _pixelsPerBeat;
        set
        {
            var clamped = Math.Clamp(value, 24, 220);
            if (Math.Abs(clamped - _pixelsPerBeat) < 0.001)
                return;
            _pixelsPerBeat = clamped;
            CoerceViewport();
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double VerticalZoom
    {
        get => _rowHeight;
        set
        {
            var clamped = Math.Clamp(value, 8, 36);
            if (Math.Abs(clamped - _rowHeight) < 0.001)
                return;
            _rowHeight = clamped;
            CoerceViewport();
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double HorizontalOffset
    {
        get => _viewStartBeat;
        set => SetHorizontalOffset(value);
    }

    public double VerticalOffset
    {
        get => 127 - _topPitch;
        set => SetVerticalOffset(value);
    }

    public double VisibleBeats => Math.Max(0, ActualWidth - KeyboardWidth) / PixelsPerBeat;
    public double TimelineInset => KeyboardWidth;
    public int VisiblePitchCount => Math.Clamp((int)Math.Ceiling(Math.Max(1, GridBottom - HeaderHeight) / RowHeight), 1, 128);
    public double HorizontalMaximum => Math.Max(0, (_project?.DurationBeats ?? 0) - VisibleBeats);
    public double VerticalMaximum => Math.Max(0, 128 - VisiblePitchCount);

    public event EventHandler? EditStarted;
    public event EventHandler? EditFinished;
    public event EventHandler<NotePreviewEventArgs>? PreviewNote;
    public event EventHandler<SeekRequestedEventArgs>? SeekRequested;
    public event EventHandler? SelectionChanged;
    public event EventHandler? ViewportChanged;

    private double GridBottom => Math.Max(HeaderHeight + 40, ActualHeight - VelocityHeight);
    private double RowHeight => _rowHeight;

    public void EnsureBeatVisible(double beat)
    {
        var visible = VisibleBeats;
        if (visible <= 0)
            return;
        var center = _viewStartBeat + visible * 0.5;
        if (beat < _viewStartBeat || beat > center)
            SetHorizontalOffset(beat - visible * 0.5);
    }

    public void CancelInteraction()
    {
        StopPreview();
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        _mode = EditMode.None;
        _activeNote = null;
        _editGroup = [];
        _erasing = false;
        Cursor = Cursors.Cross;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        CoerceViewport();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 21, 28)), null, new Rect(RenderSize));
        DrawHeaderAndGrid(dc);
        DrawKeyboard(dc);
        DrawNotes(dc);
        DrawVelocity(dc);
        DrawPlayhead(dc);
    }

    private void DrawHeaderAndGrid(DrawingContext dc)
    {
        var gridWidth = Math.Max(0, ActualWidth - KeyboardWidth);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(27, 32, 41)), null, new Rect(KeyboardWidth, 0, gridWidth, HeaderHeight));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(20, 24, 32)), null,
            new Rect(KeyboardWidth, HeaderHeight, gridWidth, Math.Max(0, GridBottom - HeaderHeight)));

        for (var row = 0; row < VisiblePitchCount; row++)
        {
            var pitch = _topPitch - row;
            var y = HeaderHeight + row * RowHeight;
            if (DrawingTools.IsBlackKey(pitch))
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(16, 20, 27)), null, new Rect(KeyboardWidth, y, gridWidth, RowHeight));
            if (pitch % 12 == 0)
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(48, 57, 69)), 1), new Point(KeyboardWidth, y), new Point(ActualWidth, y));
            else
                dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(36, 42, 52)), 0.6), new Point(KeyboardWidth, y), new Point(ActualWidth, y));
        }

        var quarterBeatsPerBar = _project?.QuarterBeatsPerBar ?? 4;
        var subdivisions = Math.Max(1, (int)Math.Round(1 / SnapBeats));
        var firstStep = (int)Math.Floor(_viewStartBeat / SnapBeats);
        var lastStep = (int)Math.Ceiling((_viewStartBeat + gridWidth / PixelsPerBeat) / SnapBeats);
        for (var step = firstStep; step <= lastStep; step++)
        {
            var beat = step * SnapBeats;
            var x = BeatToX(beat);
            var wholeBeat = Math.Abs(beat - Math.Round(beat)) < 0.0001;
            var color = wholeBeat ? Color.FromRgb(51, 59, 71) : Color.FromRgb(39, 45, 55);
            var thickness = wholeBeat ? 0.9 : 0.55;
            dc.DrawLine(new Pen(new SolidColorBrush(color), thickness), new Point(x, wholeBeat ? 0 : HeaderHeight), new Point(x, ActualHeight));
        }

        var visibleEndBeat = _viewStartBeat + gridWidth / PixelsPerBeat;
        var firstBar = Math.Max(0, (int)Math.Floor(_viewStartBeat / quarterBeatsPerBar));
        var lastBar = (int)Math.Ceiling(visibleEndBeat / quarterBeatsPerBar);
        for (var bar = firstBar; bar <= lastBar; bar++)
        {
            var x = BeatToX(bar * quarterBeatsPerBar);
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(72, 82, 97)), 1.25),
                new Point(x, 0), new Point(x, ActualHeight));
            if (x >= KeyboardWidth)
            {
                var label = DrawingTools.Text((bar + 1).ToString(), 10,
                    new SolidColorBrush(Color.FromRgb(166, 176, 190)), true);
                dc.DrawText(label, new Point(x + 5, 7));
            }
        }

        var snapLabel = DrawingTools.Text($"SNAP  1/{Math.Max(1, subdivisions * 4)}", 9,
            new SolidColorBrush(Color.FromRgb(112, 124, 142)), true);
        dc.DrawText(snapLabel, new Point(8, 8));
    }

    private void DrawKeyboard(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(28, 33, 42)), null, new Rect(0, HeaderHeight, KeyboardWidth, GridBottom - HeaderHeight));
        for (var row = 0; row < VisiblePitchCount; row++)
        {
            var pitch = _topPitch - row;
            var y = HeaderHeight + row * RowHeight;
            var black = DrawingTools.IsBlackKey(pitch);
            var fill = black ? Color.FromRgb(26, 31, 39) : Color.FromRgb(199, 204, 211);
            var keyWidth = black ? KeyboardWidth * 0.62 : KeyboardWidth;
            dc.DrawRectangle(new SolidColorBrush(fill), new Pen(new SolidColorBrush(Color.FromRgb(48, 54, 64)), 0.7),
                new Rect(0, y, keyWidth, RowHeight));
            if (pitch % 12 == 0)
            {
                var text = DrawingTools.Text(DrawingTools.NoteName(pitch), 9,
                    black ? Brushes.White : new SolidColorBrush(Color.FromRgb(55, 61, 71)), true);
                dc.DrawText(text, new Point(black ? 4 : KeyboardWidth - text.Width - 5, y + Math.Max(0, (RowHeight - text.Height) / 2)));
            }
        }
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(54, 63, 76)), 1), new Point(KeyboardWidth, 0), new Point(KeyboardWidth, ActualHeight));
    }

    private void DrawNotes(DrawingContext dc)
    {
        if (_track is null)
            return;

        dc.PushClip(new RectangleGeometry(new Rect(KeyboardWidth, HeaderHeight,
            Math.Max(0, ActualWidth - KeyboardWidth), Math.Max(0, GridBottom - HeaderHeight))));
        var baseColor = DrawingTools.ParseColor(_track.Color, Color.FromRgb(99, 213, 167));
        foreach (var note in _track.Notes.OrderBy(note => note.IsSelected))
        {
            var rect = NoteRect(note);
            if (rect.Right < KeyboardWidth || rect.Left > ActualWidth || rect.Bottom < HeaderHeight || rect.Top > GridBottom)
                continue;

            var strength = 0.58 + note.Velocity / 127.0 * 0.42;
            var color = Color.FromRgb((byte)(baseColor.R * strength), (byte)(baseColor.G * strength), (byte)(baseColor.B * strength));
            var fill = new SolidColorBrush(color);
            var border = note.IsSelected
                ? new Pen(new SolidColorBrush(Color.FromRgb(225, 255, 244)), 1.7)
                : new Pen(new SolidColorBrush(Color.FromArgb(180, 15, 22, 27)), 0.8);
            dc.DrawRoundedRectangle(fill, border, rect, 3, 3);
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)), null,
                new Rect(rect.Left + 2, rect.Top + 2, Math.Max(0, rect.Width - 4), 1));

            if (rect.Width > 42 && RowHeight > 12)
            {
                var noteLabel = _track.Kind == TrackKind.Vocal && !string.IsNullOrWhiteSpace(note.Lyric)
                    ? $"{note.Lyric}  ·  {DrawingTools.NoteName(note.Pitch)}"
                    : $"{DrawingTools.NoteName(note.Pitch)}  {note.Velocity}";
                var text = DrawingTools.Text(noteLabel, 9,
                    new SolidColorBrush(Color.FromArgb(220, 12, 22, 20)), true);
                dc.PushClip(new RectangleGeometry(rect));
                dc.DrawText(text, new Point(rect.Left + 5, rect.Top + Math.Max(1, (rect.Height - text.Height) / 2)));
                dc.Pop();
            }

            if (note.IsSelected)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(180, 240, 255, 250)), null,
                    new Rect(rect.Right - 4, rect.Top + 2, 2, Math.Max(2, rect.Height - 4)));
        }
        dc.Pop();
    }

    private void DrawVelocity(DrawingContext dc)
    {
        var top = GridBottom;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 18, 24)), null, new Rect(0, top, ActualWidth, Math.Max(0, ActualHeight - top)));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(56, 66, 79)), 1.2), new Point(0, top), new Point(ActualWidth, top));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 30, 38)), null, new Rect(0, top, KeyboardWidth, ActualHeight - top));
        var title = DrawingTools.Text("VELOCITY", 9, new SolidColorBrush(Color.FromRgb(137, 149, 166)), true);
        dc.DrawText(title, new Point(9, top + 9));
        var hint = DrawingTools.Text("drag bars", 9, new SolidColorBrush(Color.FromRgb(81, 91, 106)));
        dc.DrawText(hint, new Point(9, top + 25));

        if (_track is null)
            return;
        dc.PushClip(new RectangleGeometry(new Rect(KeyboardWidth, top,
            Math.Max(0, ActualWidth - KeyboardWidth), Math.Max(0, ActualHeight - top))));
        var color = DrawingTools.ParseColor(_track.Color, Color.FromRgb(99, 213, 167));
        foreach (var note in _track.Notes.OrderBy(note => note.IsSelected))
        {
            var x = BeatToX(note.StartBeat) + 1;
            var usable = Math.Max(20, ActualHeight - top - 18);
            var height = note.Velocity / 127.0 * usable;
            var width = Math.Clamp(note.LengthBeats * PixelsPerBeat - 3, 4, 12);
            if (x + width <= KeyboardWidth || x >= ActualWidth)
                continue;
            var brush = new SolidColorBrush(Color.FromArgb(note.IsSelected ? (byte)255 : (byte)165, color.R, color.G, color.B));
            dc.DrawRoundedRectangle(brush, note.IsSelected ? new Pen(Brushes.White, 1) : null,
                new Rect(x, ActualHeight - 8 - height, width, height), 2, 2);
        }
        dc.Pop();
    }

    private void DrawPlayhead(DrawingContext dc)
    {
        var x = BeatToX(_playheadBeat);
        if (x < KeyboardWidth || x > ActualWidth)
            return;
        var brush = new SolidColorBrush(Color.FromRgb(255, 209, 102));
        dc.DrawLine(new Pen(brush, 1.2), new Point(x, 0), new Point(x, ActualHeight));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_track is null)
            return;

        var point = e.GetPosition(this);
        if (point.Y < HeaderHeight)
        {
            SeekRequested?.Invoke(this, new SeekRequestedEventArgs(XToBeat(point.X)));
            return;
        }

        if (point.Y >= GridBottom)
        {
            var note = FindVelocityNote(point.X);
            if (note is null)
                return;
            if (!note.IsSelected)
                SelectOnly(note);
            BeginEdit(EditMode.Velocity, note, point);
            SetSelectedVelocities(point.Y);
            return;
        }

        var pitch = YToPitch(point.Y);
        if (point.X < KeyboardWidth)
        {
            _mode = EditMode.KeyboardPreview;
            _activeNote = new MidiNote { Pitch = pitch, Velocity = 100 };
            CaptureMouse();
            StartPreview(pitch, 100);
            return;
        }

        var hit = HitTestNote(point);
        if (hit is not null)
        {
            if (!hit.IsSelected)
                SelectOnly(hit);
            var rect = NoteRect(hit);
            BeginEdit(point.X >= rect.Right - 7 ? EditMode.Resize : EditMode.Move, hit, point);
            StartPreview(hit.Pitch, hit.Velocity);
        }
        else
        {
            SelectOnly(null);
            var start = Snap(XToBeat(point.X));
            var note = new MidiNote { StartBeat = start, LengthBeats = SnapBeats, Pitch = pitch, Velocity = 100, Lyric = _track.Kind == TrackKind.Vocal ? "a" : string.Empty, IsSelected = true };
            EditStarted?.Invoke(this, EventArgs.Empty);
            _track.Notes.Add(note);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            _activeNote = note;
            _mode = EditMode.Draw;
            _dragOrigin = point;
            _originalStart = start;
            _originalLength = note.LengthBeats;
            CaptureMouse();
            StartPreview(pitch, note.Velocity);
            InvalidateVisual();
        }
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (_track is null)
            return;
        Focus();
        EditStarted?.Invoke(this, EventArgs.Empty);
        _erasing = true;
        _lastErasePoint = e.GetPosition(this);
        CaptureMouse();
        EraseNotesAlong(_lastErasePoint, _lastErasePoint);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (_erasing && IsMouseCaptured)
        {
            EraseNotesAlong(_lastErasePoint, point);
            _lastErasePoint = point;
            return;
        }
        if (!IsMouseCaptured || _activeNote is null)
        {
            Cursor = HitTestNote(point) is { } hit && point.X >= NoteRect(hit).Right - 7 ? Cursors.SizeWE : Cursors.Cross;
            return;
        }

        switch (_mode)
        {
            case EditMode.Draw:
            case EditMode.Resize:
                {
                    var end = Snap(XToBeat(point.X));
                    _activeNote.LengthBeats = Math.Max(SnapBeats, end - _activeNote.StartBeat + (_mode == EditMode.Draw ? SnapBeats : 0));
                    break;
                }
            case EditMode.Move:
                {
                    var deltaBeat = Snap((point.X - _dragOrigin.X) / PixelsPerBeat);
                    var deltaPitch = (int)Math.Round((_dragOrigin.Y - point.Y) / RowHeight);
                    if (_editGroup.Length > 0)
                    {
                        deltaBeat = Math.Max(deltaBeat, -_editGroup.Min(item => item.StartBeat));
                        deltaPitch = Math.Clamp(deltaPitch,
                            -_editGroup.Min(item => item.Pitch),
                            127 - _editGroup.Max(item => item.Pitch));
                        foreach (var item in _editGroup)
                        {
                            item.Note.StartBeat = item.StartBeat + deltaBeat;
                            item.Note.Pitch = item.Pitch + deltaPitch;
                        }
                    }
                    else
                    {
                        _activeNote.StartBeat = Math.Max(0, _originalStart + deltaBeat);
                        _activeNote.Pitch = Math.Clamp(_originalPitch + deltaPitch, 0, 127);
                    }
                    break;
                }
            case EditMode.Velocity:
                SetSelectedVelocities(point.Y);
                break;
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured)
            return;

        StopPreview();
        if (_mode is not EditMode.KeyboardPreview and not EditMode.None)
            EditFinished?.Invoke(this, EventArgs.Empty);

        ReleaseMouseCapture();
        _mode = EditMode.None;
        _activeNote = null;
        _editGroup = [];
        Cursor = Cursors.Cross;
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (!_erasing)
            return;
        EraseNotesAlong(_lastErasePoint, e.GetPosition(this));
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        _erasing = false;
        EditFinished?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var x = e.GetPosition(this).X;
            var anchor = XToBeat(x);
            PixelsPerBeat *= e.Delta > 0 ? 1.12 : 1 / 1.12;
            SetHorizontalOffset(anchor - Math.Max(0, x - KeyboardWidth) / PixelsPerBeat);
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            SetHorizontalOffset(_viewStartBeat - Math.Sign(e.Delta) * 2);
        }
        else
        {
            SetVerticalOffset(VerticalOffset - Math.Sign(e.Delta) * 3);
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_track is null)
            return;

        if (e.Key == Key.Delete)
        {
            var selected = _track.Notes.Where(note => note.IsSelected).ToArray();
            if (selected.Length == 0)
                return;
            EditStarted?.Invoke(this, EventArgs.Empty);
            foreach (var note in selected)
                _track.Notes.Remove(note);
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            EditFinished?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            foreach (var note in _track.Notes)
                note.IsSelected = true;
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.D && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            var selected = _track.Notes.Where(note => note.IsSelected).ToArray();
            if (selected.Length == 0)
                return;
            EditStarted?.Invoke(this, EventArgs.Empty);
            foreach (var note in selected)
            {
                note.IsSelected = false;
                var clone = new MidiNote
                {
                    IsSelected = true,
                    StartBeat = note.StartBeat + SnapBeats,
                    LengthBeats = note.LengthBeats,
                    Pitch = note.Pitch,
                    Velocity = note.Velocity,
                    Lyric = note.Lyric
                };
                _track.Notes.Add(clone);
            }
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            EditFinished?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
        else if (e.Key == Key.Q)
        {
            var selected = _track.Notes.Where(note => note.IsSelected).ToArray();
            if (selected.Length == 0)
                return;
            EditStarted?.Invoke(this, EventArgs.Empty);
            foreach (var note in selected)
                note.StartBeat = Snap(note.StartBeat);
            EditFinished?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void BeginEdit(EditMode mode, MidiNote note, Point point)
    {
        EditStarted?.Invoke(this, EventArgs.Empty);
        _mode = mode;
        _activeNote = note;
        _dragOrigin = point;
        _originalStart = note.StartBeat;
        _originalLength = note.LengthBeats;
        _originalPitch = note.Pitch;
        _editGroup = _track?.Notes
            .Where(item => item.IsSelected)
            .Select(item => new NoteEditState(item, item.StartBeat, item.Pitch, item.Velocity))
            .ToArray() ?? [];
        if (_editGroup.Length == 0)
            _editGroup = [new NoteEditState(note, note.StartBeat, note.Pitch, note.Velocity)];
        CaptureMouse();
    }

    private void SetSelectedVelocities(double y)
    {
        var usable = Math.Max(20, ActualHeight - GridBottom - 18);
        var normalized = (ActualHeight - 8 - y) / usable;
        var targetVelocity = (int)Math.Round(Math.Clamp(normalized, 1.0 / 127, 1) * 127);
        var activeVelocity = _editGroup.FirstOrDefault(item => item.Note == _activeNote).Velocity;
        if (activeVelocity <= 0)
            activeVelocity = _activeNote?.Velocity ?? targetVelocity;
        var delta = targetVelocity - activeVelocity;
        foreach (var item in _editGroup)
            item.Note.Velocity = Math.Clamp(item.Velocity + delta, 1, 127);
    }

    private void StartPreview(int pitch, int velocity)
    {
        StopPreview();
        _previewPitch = pitch;
        PreviewNote?.Invoke(this, new NotePreviewEventArgs(pitch, velocity, true));
    }

    private void StopPreview()
    {
        if (_previewPitch is not { } pitch)
            return;
        PreviewNote?.Invoke(this, new NotePreviewEventArgs(pitch, 0, false));
        _previewPitch = null;
    }

    private MidiNote? FindVelocityNote(double x) => _track?.Notes
        .Where(note => Math.Abs(BeatToX(note.StartBeat) - x) <= 10)
        .OrderBy(note => note.IsSelected ? 0 : 1)
        .ThenBy(note => Math.Abs(BeatToX(note.StartBeat) - x))
        .FirstOrDefault();

    private MidiNote? HitTestNote(Point point) => _track?.Notes.LastOrDefault(note => NoteRect(note).Contains(point));

    private void EraseNotesAlong(Point from, Point to)
    {
        if (_track is null)
            return;
        var distance = Math.Sqrt(Math.Pow(to.X - from.X, 2) + Math.Pow(to.Y - from.Y, 2));
        var samples = Math.Max(1, (int)Math.Ceiling(distance / 2));
        var changed = false;
        for (var index = 0; index <= samples; index++)
        {
            var amount = index / (double)samples;
            var point = new Point(from.X + (to.X - from.X) * amount, from.Y + (to.Y - from.Y) * amount);
            MidiNote? hit;
            while ((hit = HitTestNote(point)) is not null)
            {
                _track.Notes.Remove(hit);
                changed = true;
            }
        }
        if (changed)
        {
            SelectionChanged?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
        }
    }

    private Rect NoteRect(MidiNote note)
    {
        var y = HeaderHeight + (_topPitch - note.Pitch) * RowHeight + 1.5;
        return new Rect(BeatToX(note.StartBeat) + 1, y,
            Math.Max(5, note.LengthBeats * PixelsPerBeat - 2), Math.Max(4, RowHeight - 3));
    }

    private void SelectOnly(MidiNote? selected)
    {
        if (_track is null)
            return;
        var changed = false;
        foreach (var note in _track.Notes)
        {
            var isSelected = note == selected;
            changed |= note.IsSelected != isSelected;
            note.IsSelected = isSelected;
        }
        if (changed)
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    private double BeatToX(double beat) => KeyboardWidth + (beat - _viewStartBeat) * PixelsPerBeat;
    private double XToBeat(double x) => Math.Max(0, _viewStartBeat + (x - KeyboardWidth) / PixelsPerBeat);
    private int YToPitch(double y) => Math.Clamp(_topPitch - (int)((y - HeaderHeight) / RowHeight), 0, 127);
    private double Snap(double beat) => (Keyboard.Modifiers & ModifierKeys.Alt) != 0
        ? Math.Round(beat * 64) / 64.0
        : Math.Round(beat / SnapBeats) * SnapBeats;

    private void SetHorizontalOffset(double value)
    {
        var clamped = Math.Clamp(value, 0, HorizontalMaximum);
        if (Math.Abs(clamped - _viewStartBeat) < 0.0001)
            return;
        _viewStartBeat = clamped;
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SetVerticalOffset(double value)
    {
        var offset = Math.Clamp((int)Math.Round(value), 0, (int)VerticalMaximum);
        var topPitch = 127 - offset;
        if (topPitch == _topPitch)
            return;
        _topPitch = topPitch;
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CoerceViewport()
    {
        _viewStartBeat = Math.Clamp(_viewStartBeat, 0, HorizontalMaximum);
        var offset = Math.Clamp(127 - _topPitch, 0, (int)VerticalMaximum);
        _topPitch = 127 - offset;
    }

    private enum EditMode { None, Draw, Move, Resize, Velocity, KeyboardPreview }
    private readonly record struct NoteEditState(MidiNote Note, double StartBeat, int Pitch, int Velocity);
}
