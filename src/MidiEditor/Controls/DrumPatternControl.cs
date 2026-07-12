using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MidiEditor.Models;

namespace MidiEditor.Controls;

public sealed class DrumPatternControl : FrameworkElement
{
    private const double LabelWidth = 126;
    private const double HeaderHeight = 28;
    private const double VelocityHeight = 92;

    private static readonly DrumDefinition[] AllDrums =
    [
        new(81, "Open Triangle", "OT"), new(80, "Mute Triangle", "MT"), new(79, "Open Cuica", "OC"),
        new(78, "Mute Cuica", "MC"), new(77, "Low Wood Block", "LW"), new(76, "High Wood Block", "HW"),
        new(75, "Claves", "CL"), new(74, "Long Guiro", "LG"), new(73, "Short Guiro", "SG"),
        new(72, "Long Whistle", "LW"), new(71, "Short Whistle", "SW"), new(70, "Maracas", "MR"),
        new(69, "Cabasa", "CB"), new(68, "Low Agogo", "LA"), new(67, "High Agogo", "HA"),
        new(66, "Low Timbale", "LT"), new(65, "High Timbale", "HT"), new(64, "Low Conga", "LC"),
        new(63, "Open Hi Conga", "OC"), new(62, "Mute Hi Conga", "MC"), new(61, "Low Bongo", "LB"),
        new(60, "High Bongo", "HB"), new(59, "Ride Cymbal 2", "R2"), new(58, "Vibraslap", "VS"),
        new(57, "Crash Cymbal 2", "C2"), new(56, "Cowbell", "CW"), new(55, "Splash Cymbal", "SP"),
        new(54, "Tambourine", "TM"), new(53, "Ride Bell", "RB"), new(52, "Chinese Cymbal", "CC"),
        new(51, "Ride Cymbal 1", "RD"), new(50, "High Tom", "HT"), new(49, "Crash Cymbal 1", "CR"),
        new(48, "Hi-Mid Tom", "HM"), new(47, "Low-Mid Tom", "LM"), new(46, "Open Hi-Hat", "OH"),
        new(45, "Low Tom", "LT"), new(44, "Pedal Hi-Hat", "PH"), new(43, "High Floor Tom", "HF"),
        new(42, "Closed Hi-Hat", "CH"), new(41, "Low Floor Tom", "LF"), new(40, "Electric Snare", "ES"),
        new(39, "Hand Clap", "CP"), new(38, "Acoustic Snare", "SN"), new(37, "Side Stick", "SS"),
        new(36, "Bass Drum 1", "KD"), new(35, "Acoustic Bass Drum", "AK")
    ];

    private MidiTrack? _track;
    private MidiProject? _project;
    private MidiNote? _selectedNote;
    private double _playheadBeat;
    private double _viewStartBeat;
    private double _patternBeats = 16;
    private double _stepBeats = 0.25;
    private bool _paintValue;
    private (int Row, int Step)? _lastPaintCell;
    private bool _editingVelocity;
    private int? _previewPitch;
    private int _firstDrumIndex = 30;
    private double _pixelsPerBeat = 62;
    private double _rowHeight = 25;
    private bool _erasing;
    private Point _lastErasePoint;
    private DrawingGroup? _gridCache;
    private GridCacheKey _gridCacheKey;
    private readonly List<VisibleDrumNote> _visibleNotesBuffer = [];
    private MidiNote[] _notesByStart = [];
    private bool _noteIndexDirty = true;

    public DrumPatternControl()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Hand;
    }

    public MidiTrack? Track
    {
        get => _track;
        set
        {
            if (!ReferenceEquals(_track, value))
                CancelInteraction();
            _track = value;
            _noteIndexDirty = true;
            _selectedNote = null;
            CoerceViewport();
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

    public double StepBeats
    {
        get => _stepBeats;
        set { _stepBeats = Math.Clamp(value, 0.125, 1); InvalidateVisual(); }
    }

    public double PatternBeats
    {
        get => _patternBeats;
        set { _patternBeats = Math.Clamp(value, 4, 4096); CoerceViewport(); InvalidateVisual(); ViewportChanged?.Invoke(this, EventArgs.Empty); }
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
            var clamped = Math.Clamp(value, 8, 48);
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
        get => _firstDrumIndex;
        set => SetVerticalOffset(value);
    }

    public double VisibleBeats => Math.Max(0, ActualWidth - LabelWidth) / PixelsPerBeat;
    public double TimelineInset => LabelWidth;
    public double TimelineBeats => Math.Max(_patternBeats, _project?.DurationBeats ?? 0);
    public double HorizontalMaximum => Math.Max(0, TimelineBeats - VisibleBeats);
    public double VerticalMaximum => Math.Max(0, AllDrums.Length - VisibleDrumCount);
    public int VisibleDrums => VisibleDrumCount;

    public event EventHandler? EditStarted;
    public event EventHandler? EditFinished;
    public event EventHandler<NotePreviewEventArgs>? PreviewNote;
    public event EventHandler<SeekRequestedEventArgs>? SeekRequested;
    public event EventHandler? ViewportChanged;

    private double GridBottom => Math.Max(HeaderHeight + 150, ActualHeight - VelocityHeight);
    private int VisibleDrumCount => Math.Clamp((int)Math.Ceiling(Math.Max(1, GridBottom - HeaderHeight) / RowHeight), 1, AllDrums.Length);
    private double RowHeight => _rowHeight;
    private double StepWidth => StepBeats * PixelsPerBeat;

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
        _lastPaintCell = null;
        _editingVelocity = false;
        _erasing = false;
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
        // A collapsed editor can retain an offset calculated for a zero-sized viewport.
        // Coerce again immediately before drawing when switching to a drum track.
        CoerceViewport();
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(17, 21, 28)), null, new Rect(RenderSize));
        DrawCachedStaticGrid(dc);
        DrawTimelineGrid(dc);
        var visibleNotes = CollectVisibleNotes();
        DrawHits(dc, visibleNotes);
        DrawVelocity(dc, visibleNotes);
        DrawPlayhead(dc);
    }

    private void DrawCachedStaticGrid(DrawingContext dc)
    {
        var key = new GridCacheKey(ActualWidth, ActualHeight, _firstDrumIndex, RowHeight, StepBeats);
        if (_gridCache is null || key != _gridCacheKey)
        {
            var drawing = new DrawingGroup();
            using (var cacheContext = drawing.Open())
                DrawStaticGrid(cacheContext);
            if (drawing.CanFreeze)
                drawing.Freeze();
            _gridCache = drawing;
            _gridCacheKey = key;
        }
        dc.DrawDrawing(_gridCache);
    }

    private void DrawStaticGrid(DrawingContext dc)
    {
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(27, 32, 41)), null, new Rect(0, 0, ActualWidth, HeaderHeight));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(54, 63, 76)), 1), new Point(LabelWidth, 0), new Point(LabelWidth, ActualHeight));

        for (var row = 0; row < VisibleDrumCount; row++)
        {
            var drum = DrumAtRow(row);
            var y = HeaderHeight + row * RowHeight;
            var rowFill = row % 2 == 0 ? Color.FromRgb(21, 26, 34) : Color.FromRgb(18, 23, 30);
            dc.DrawRectangle(new SolidColorBrush(rowFill), null, new Rect(LabelWidth, y, ActualWidth - LabelWidth, RowHeight));
            dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(row % 2 == 0 ? (byte)31 : (byte)27, row % 2 == 0 ? (byte)37 : (byte)33, row % 2 == 0 ? (byte)47 : (byte)42)),
                null, new Rect(0, y, LabelWidth, RowHeight));
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(43, 50, 61)), 0.8), new Point(0, y + RowHeight), new Point(ActualWidth, y + RowHeight));

            var badgeHeight = Math.Max(14, Math.Min(20, RowHeight - 4));
            var badge = new Rect(8, y + Math.Max(2, (RowHeight - badgeHeight) / 2), 25, badgeHeight);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(48, 56, 68)), null, badge, 4, 4);
            var code = DrawingTools.Text(drum.Code, 8, new SolidColorBrush(Color.FromRgb(166, 177, 191)), true);
            dc.DrawText(code, new Point(badge.Left + (badge.Width - code.Width) / 2, badge.Top + (badge.Height - code.Height) / 2));
            var name = DrawingTools.Text(drum.Name, 9.5, new SolidColorBrush(Color.FromRgb(210, 216, 225)), drum.Pitch is 38 or 36);
            dc.DrawText(name, new Point(41, y + Math.Max(2, (RowHeight - name.Height) / 2)));
        }

        var subdivision = (int)Math.Round(1 / StepBeats) * 4;
        var title = DrawingTools.Text($"DRUM GRID  ·  1/{subdivision}", 9, new SolidColorBrush(Color.FromRgb(117, 130, 147)), true);
        dc.DrawText(title, new Point(9, 8));

        var velocityTop = GridBottom;
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(14, 18, 24)), null,
            new Rect(0, velocityTop, ActualWidth, ActualHeight - velocityTop));
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(25, 30, 38)), null,
            new Rect(0, velocityTop, LabelWidth, ActualHeight - velocityTop));
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(56, 66, 79)), 1.2),
            new Point(0, velocityTop), new Point(ActualWidth, velocityTop));
        dc.DrawText(DrawingTools.Text("VELOCITY", 9, new SolidColorBrush(Color.FromRgb(137, 149, 166)), true),
            new Point(10, velocityTop + 9));
    }

    private void DrawTimelineGrid(DrawingContext dc)
    {
        dc.PushClip(new RectangleGeometry(new Rect(LabelWidth, 0, Math.Max(0, ActualWidth - LabelWidth), GridBottom)));
        var visibleEndBeat = _viewStartBeat + VisibleBeats;
        var firstStep = Math.Max(0, (int)Math.Floor(_viewStartBeat / StepBeats));
        var lastStep = Math.Max(firstStep, (int)Math.Ceiling(visibleEndBeat / StepBeats));
        var stepsPerBeat = Math.Max(1, (int)Math.Round(1 / StepBeats));
        for (var step = firstStep; step <= lastStep; step++)
        {
            var beat = step * StepBeats;
            var x = LabelWidth + (beat - _viewStartBeat) * PixelsPerBeat;
            var wholeBeat = step % stepsPerBeat == 0;
            var color = wholeBeat ? Color.FromRgb(54, 62, 74) : Color.FromRgb(39, 45, 55);
            dc.DrawLine(new Pen(new SolidColorBrush(color), wholeBeat ? 1 : 0.65),
                new Point(x, wholeBeat ? 0 : HeaderHeight), new Point(x, GridBottom));
        }

        var quarterBeatsPerBar = _project?.QuarterBeatsPerBar ?? 4;
        var firstBar = Math.Max(0, (int)Math.Floor(_viewStartBeat / quarterBeatsPerBar));
        var lastBar = (int)Math.Ceiling(visibleEndBeat / quarterBeatsPerBar);
        for (var bar = firstBar; bar <= lastBar; bar++)
        {
            var x = LabelWidth + (bar * quarterBeatsPerBar - _viewStartBeat) * PixelsPerBeat;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(76, 86, 101)), 1.4),
                new Point(x, 0), new Point(x, GridBottom));
            if (x >= LabelWidth && x <= ActualWidth)
            {
                var text = DrawingTools.Text((bar + 1).ToString(), 10,
                    new SolidColorBrush(Color.FromRgb(164, 175, 189)), true);
                dc.DrawText(text, new Point(x + 5, 7));
            }
        }
        dc.Pop();
    }

    private IReadOnlyList<VisibleDrumNote> CollectVisibleNotes()
    {
        _visibleNotesBuffer.Clear();
        if (_track is null)
            return _visibleNotesBuffer;

        EnsureNoteIndex();
        var visibleEndBeat = _viewStartBeat + VisibleBeats;
        var firstVisibleIndex = FindFirstNoteAtOrAfter(Math.Max(0, _viewStartBeat - StepBeats));
        for (var noteIndex = firstVisibleIndex; noteIndex < _notesByStart.Length; noteIndex++)
        {
            var note = _notesByStart[noteIndex];
            if (note.StartBeat >= visibleEndBeat)
                break;
            var row = FindVisibleRow(note.Pitch);
            if (row < 0)
                continue;
            var x = LabelWidth + (note.StartBeat - _viewStartBeat) * PixelsPerBeat;
            if (x + StepWidth >= LabelWidth && x <= ActualWidth)
                _visibleNotesBuffer.Add(new VisibleDrumNote(note, row, x));
        }
        return _visibleNotesBuffer;
    }

    private void DrawHits(DrawingContext dc, IReadOnlyList<VisibleDrumNote> visibleNotes)
    {
        if (_track is null)
            return;

        dc.PushClip(new RectangleGeometry(new Rect(LabelWidth, HeaderHeight,
            Math.Max(0, ActualWidth - LabelWidth), Math.Max(0, GridBottom - HeaderHeight))));
        var color = DrawingTools.ParseColor(_track.Color, Color.FromRgb(255, 157, 102));
        foreach (var visibleNote in visibleNotes)
        {
            var note = visibleNote.Note;
            var x = visibleNote.X + 3;
            var y = HeaderHeight + visibleNote.Row * RowHeight + 4;
            var rect = new Rect(x, y, Math.Max(3, StepWidth - 6), Math.Max(3, RowHeight - 8));
            var alpha = (byte)(135 + note.Velocity / 127.0 * 120);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B)),
                note == _selectedNote ? new Pen(new SolidColorBrush(Color.FromRgb(235, 255, 248)), 1.7) : null, rect, 4, 4);

            var meterHeight = Math.Max(2, rect.Height * note.Velocity / 127.0);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(75, 255, 255, 255)), null,
                new Rect(rect.Left + 2, rect.Bottom - meterHeight, Math.Max(1, rect.Width - 4), meterHeight - 1), 2, 2);
        }
        dc.Pop();
    }

    private void DrawVelocity(DrawingContext dc, IReadOnlyList<VisibleDrumNote> visibleNotes)
    {
        var top = GridBottom;
        var selectedText = _selectedNote is null ? "select a step" : $"{DrawingTools.NoteName(_selectedNote.Pitch)}  {_selectedNote.Velocity}";
        dc.DrawText(DrawingTools.Text(selectedText, 9, new SolidColorBrush(Color.FromRgb(86, 97, 113))), new Point(10, top + 26));

        if (_track is null)
            return;
        dc.PushClip(new RectangleGeometry(new Rect(LabelWidth, top,
            Math.Max(0, ActualWidth - LabelWidth), Math.Max(0, ActualHeight - top))));
        var color = DrawingTools.ParseColor(_track.Color, Color.FromRgb(255, 157, 102));
        foreach (var visibleNote in visibleNotes)
        {
            if (visibleNote.Note == _selectedNote)
                continue;
            DrawVelocityBar(dc, visibleNote, color, top, selected: false);
        }
        foreach (var visibleNote in visibleNotes)
        {
            if (visibleNote.Note == _selectedNote)
                DrawVelocityBar(dc, visibleNote, color, top, selected: true);
        }
        dc.Pop();
    }

    private void DrawVelocityBar(DrawingContext dc, VisibleDrumNote visibleNote, Color color, double top, bool selected)
    {
            var note = visibleNote.Note;
            var x = visibleNote.X + Math.Max(2, StepWidth * 0.22);
            var usable = Math.Max(18, ActualHeight - top - 14);
            var height = usable * note.Velocity / 127.0;
            var width = Math.Max(3, StepWidth * 0.56);
            var rect = new Rect(x, ActualHeight - 6 - height, width, height);
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(selected ? (byte)255 : (byte)145, color.R, color.G, color.B)),
                selected ? new Pen(Brushes.White, 1) : null,
                rect, 2, 2);
    }

    private void DrawPlayhead(DrawingContext dc)
    {
        var x = LabelWidth + (_playheadBeat - _viewStartBeat) / StepBeats * StepWidth;
        if (x < LabelWidth || x > ActualWidth)
            return;
        dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(255, 209, 102)), 1.2), new Point(x, 0), new Point(x, ActualHeight));
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        if (_track is null)
            return;

        var point = e.GetPosition(this);
        if (point.Y < HeaderHeight && point.X >= LabelWidth)
        {
            SeekRequested?.Invoke(this, new SeekRequestedEventArgs(PointToBeat(point)));
            return;
        }

        if (point.Y >= GridBottom)
        {
            var note = FindNoteAtStep(PointToStep(point.X));
            if (note is null)
                return;
            _selectedNote = note;
            _editingVelocity = true;
            EditStarted?.Invoke(this, EventArgs.Empty);
            CaptureMouse();
            SetVelocity(note, point.Y);
            InvalidateVisual();
            return;
        }

        if (point.Y < HeaderHeight || point.Y >= GridBottom)
            return;

        var row = Math.Clamp((int)((point.Y - HeaderHeight) / RowHeight), 0, VisibleDrumCount - 1);
        var drum = DrumAtRow(row);
        if (point.X < LabelWidth)
        {
            StartPreview(drum.Pitch, 110);
            _selectedNote = new MidiNote { Pitch = drum.Pitch, Velocity = 110 };
            CaptureMouse();
            return;
        }

        var step = PointToStep(point.X);
        var existing = FindNote(row, step);
        if (existing is not null && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            _selectedNote = existing;
            StartPreview(existing.Pitch, existing.Velocity);
            CaptureMouse();
            InvalidateVisual();
            return;
        }

        EditStarted?.Invoke(this, EventArgs.Empty);
        _paintValue = existing is null;
        ApplyCell(row, step, _paintValue);
        _lastPaintCell = (row, step);
        CaptureMouse();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        if (_track is null)
            return;
        var point = e.GetPosition(this);
        if (point.X < LabelWidth || point.Y < HeaderHeight || point.Y >= GridBottom)
            return;
        Focus();
        EditStarted?.Invoke(this, EventArgs.Empty);
        _erasing = true;
        _lastErasePoint = point;
        CaptureMouse();
        EraseAlong(point, point);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!IsMouseCaptured || _track is null)
            return;
        var point = e.GetPosition(this);
        if (_erasing)
        {
            EraseAlong(_lastErasePoint, point);
            _lastErasePoint = point;
            return;
        }
        if (_editingVelocity && _selectedNote is not null)
        {
            SetVelocity(_selectedNote, point.Y);
            InvalidateVisual();
            return;
        }
        if (_lastPaintCell is null || point.X < LabelWidth || point.Y < HeaderHeight || point.Y >= GridBottom)
            return;
        var row = Math.Clamp((int)((point.Y - HeaderHeight) / RowHeight), 0, VisibleDrumCount - 1);
        var step = PointToStep(point.X);
        if (_lastPaintCell == (row, step))
            return;
        ApplyCell(row, step, _paintValue);
        _lastPaintCell = (row, step);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured)
            return;

        if (_lastPaintCell is not null || _editingVelocity)
            EditFinished?.Invoke(this, EventArgs.Empty);
        StopPreview();
        ReleaseMouseCapture();
        _lastPaintCell = null;
        _editingVelocity = false;
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        if (!_erasing)
            return;
        EraseAlong(_lastErasePoint, e.GetPosition(this));
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
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            SetHorizontalOffset(_viewStartBeat - Math.Sign(e.Delta) * (_project?.QuarterBeatsPerBar ?? 4));
        }
        else
        {
            SetVerticalOffset(_firstDrumIndex - Math.Sign(e.Delta) * 3);
        }
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_track is null)
            return;
        if (e.Key == Key.Delete && _selectedNote is not null && _track.Notes.Contains(_selectedNote))
        {
            EditStarted?.Invoke(this, EventArgs.Empty);
            _track.Notes.Remove(_selectedNote);
            _noteIndexDirty = true;
            _selectedNote = null;
            EditFinished?.Invoke(this, EventArgs.Empty);
            InvalidateVisual();
            e.Handled = true;
        }
    }

    private void ApplyCell(int row, int step, bool enabled)
    {
        if (_track is null || step < 0 || step * StepBeats > TimelineBeats)
            return;
        var existing = FindNote(row, step);
        if (enabled && existing is null)
        {
            var note = new MidiNote
            {
                StartBeat = step * StepBeats,
                LengthBeats = Math.Min(0.12, StepBeats * 0.8),
                Pitch = DrumAtRow(row).Pitch,
                Velocity = step % Math.Max(1, (int)Math.Round(1 / StepBeats)) == 0 ? 112 : 88
            };
            _track.Notes.Add(note);
            _noteIndexDirty = true;
            _selectedNote = note;
            StartPreview(note.Pitch, note.Velocity);
        }
        else if (!enabled && existing is not null)
        {
            _track.Notes.Remove(existing);
            _noteIndexDirty = true;
            if (_selectedNote == existing)
                _selectedNote = null;
        }
        InvalidateVisual();
    }

    private void EraseAlong(Point from, Point to)
    {
        if (_track is null)
            return;
        var distance = Math.Max(Math.Abs(to.X - from.X) / Math.Max(2, StepWidth / 2),
            Math.Abs(to.Y - from.Y) / Math.Max(2, RowHeight / 2));
        var samples = Math.Max(1, (int)Math.Ceiling(distance));
        var changed = false;
        for (var index = 0; index <= samples; index++)
        {
            var amount = index / (double)samples;
            var point = new Point(from.X + (to.X - from.X) * amount, from.Y + (to.Y - from.Y) * amount);
            if (point.X < LabelWidth || point.X > ActualWidth || point.Y < HeaderHeight || point.Y >= GridBottom)
                continue;
            var row = Math.Clamp((int)((point.Y - HeaderHeight) / RowHeight), 0, VisibleDrumCount - 1);
            var existing = FindNote(row, PointToStep(point.X));
            if (existing is null)
                continue;
            _track.Notes.Remove(existing);
            _noteIndexDirty = true;
            if (_selectedNote == existing)
                _selectedNote = null;
            changed = true;
        }
        if (changed)
            InvalidateVisual();
    }

    private MidiNote? FindNote(int row, int step)
    {
        if (_track is null || row < 0 || row >= VisibleDrumCount)
            return null;
        return _track.Notes.FirstOrDefault(note => note.Pitch == DrumAtRow(row).Pitch &&
            (int)Math.Round(note.StartBeat / StepBeats) == step);
    }

    private MidiNote? FindNoteAtStep(int step)
    {
        if (_track is null)
            return null;
        return _track.Notes.Where(note => FindVisibleRow(note.Pitch) >= 0 &&
                                         (int)Math.Round(note.StartBeat / StepBeats) == step)
            .OrderBy(note => note == _selectedNote ? 0 : 1).FirstOrDefault();
    }

    private void SetVelocity(MidiNote note, double y)
    {
        var usable = Math.Max(18, ActualHeight - GridBottom - 14);
        note.Velocity = (int)Math.Round(Math.Clamp((ActualHeight - 6 - y) / usable, 1.0 / 127, 1) * 127);
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

    private int PointToStep(double x) => Math.Max(0, (int)Math.Floor(
        (_viewStartBeat + Math.Max(0, x - LabelWidth) / PixelsPerBeat) / StepBeats));
    private double PointToBeat(Point point) => _viewStartBeat + Math.Max(0, (point.X - LabelWidth) / StepWidth) * StepBeats;

    private DrumDefinition DrumAtRow(int row) => AllDrums[_firstDrumIndex + row];

    private int FindVisibleRow(int pitch)
    {
        var absoluteIndex = pitch is >= 35 and <= 81 ? 81 - pitch : -1;
        return absoluteIndex >= _firstDrumIndex && absoluteIndex < _firstDrumIndex + VisibleDrumCount
            ? absoluteIndex - _firstDrumIndex
            : -1;
    }

    private void EnsureNoteIndex()
    {
        if (!_noteIndexDirty)
            return;
        _notesByStart = _track?.Notes.OrderBy(note => note.StartBeat).ToArray() ?? [];
        _noteIndexDirty = false;
    }

    private int FindFirstNoteAtOrAfter(double beat)
    {
        var low = 0;
        var high = _notesByStart.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (_notesByStart[middle].StartBeat < beat)
                low = middle + 1;
            else
                high = middle;
        }
        return low;
    }

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
        var clamped = Math.Clamp((int)Math.Round(value), 0, (int)VerticalMaximum);
        if (clamped == _firstDrumIndex)
            return;
        _firstDrumIndex = clamped;
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CoerceViewport()
    {
        _viewStartBeat = Math.Clamp(_viewStartBeat, 0, HorizontalMaximum);
        _firstDrumIndex = Math.Clamp(_firstDrumIndex, 0, (int)VerticalMaximum);
    }

    private sealed record DrumDefinition(int Pitch, string Name, string Code);
    private readonly record struct VisibleDrumNote(MidiNote Note, int Row, double X);
    private readonly record struct GridCacheKey(
        double Width,
        double Height,
        int FirstDrumIndex,
        double RowHeight,
        double StepBeats);
}
