using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MidiEditor.Models;

namespace MidiEditor.Controls;

public sealed class ArrangementView : FrameworkElement
{
    private const double HeaderHeight = 27;
    private MidiProject? _project;
    private MidiTrack? _selectedTrack;
    private double _playheadBeat;
    private double _pixelsPerBeat = 34;
    private double _viewStartBeat;
    private int _firstTrackIndex;
    private double _laneHeight = 37;
    private double _timelineInset = 72;

    public ArrangementView()
    {
        Focusable = true;
        SnapsToDevicePixels = true;
        ClipToBounds = true;
        Cursor = Cursors.Cross;
    }

    public MidiProject? Project
    {
        get => _project;
        set { _project = value; CoerceViewport(); InvalidateVisual(); ViewportChanged?.Invoke(this, EventArgs.Empty); }
    }

    public MidiTrack? SelectedTrack
    {
        get => _selectedTrack;
        set { _selectedTrack = value; InvalidateVisual(); }
    }

    public double PlayheadBeat
    {
        get => _playheadBeat;
        set { _playheadBeat = Math.Max(0, value); InvalidateVisual(); }
    }

    public double PixelsPerBeat
    {
        get => _pixelsPerBeat;
        set
        {
            var clamped = Math.Clamp(value, 12, 110);
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
        get => _laneHeight;
        set
        {
            var clamped = Math.Clamp(value, 24, 72);
            if (Math.Abs(clamped - _laneHeight) < 0.001)
                return;
            _laneHeight = clamped;
            CoerceViewport();
            InvalidateVisual();
            ViewportChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public double TimelineInset
    {
        get => _timelineInset;
        set
        {
            var clamped = Math.Max(0, value);
            if (Math.Abs(clamped - _timelineInset) < 0.001)
                return;
            _timelineInset = clamped;
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
        get => _firstTrackIndex;
        set => SetVerticalOffset(value);
    }

    public double VisibleBeats => Math.Max(0, ActualWidth - TimelineInset) / PixelsPerBeat;
    public int VisibleTracks => Math.Max(1, (int)Math.Floor(Math.Max(0, ActualHeight - HeaderHeight) / _laneHeight));
    public double HorizontalMaximum => Math.Max(0, (_project?.DurationBeats ?? 0) - VisibleBeats);
    public double VerticalMaximum => Math.Max(0, (_project?.Tracks.Count ?? 0) - VisibleTracks);

    public event EventHandler<TrackSelectedEventArgs>? TrackSelected;
    public event EventHandler<SeekRequestedEventArgs>? SeekRequested;
    public event EventHandler? ViewportChanged;

    public void EnsureBeatVisible(double beat)
    {
        var visible = VisibleBeats;
        if (visible <= 0)
            return;
        var center = _viewStartBeat + visible * 0.5;
        if (beat < _viewStartBeat || beat > center)
            SetHorizontalOffset(beat - visible * 0.5);
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
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 22, 29)), null, new Rect(RenderSize));
        if (_project is null)
            return;

        var gridLeft = TimelineInset;
        var width = ActualWidth;
        var visibleBeats = width / PixelsPerBeat;
        var endBeat = _viewStartBeat + visibleBeats;
        var quarterBeatsPerBar = _project.QuarterBeatsPerBar;

        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(26, 31, 40)), null, new Rect(0, 0, width, HeaderHeight));

        if (_project.LoopEnabled)
        {
            var loopX = gridLeft + (_project.LoopStartBeat - _viewStartBeat) * PixelsPerBeat;
            var loopWidth = (_project.LoopEndBeat - _project.LoopStartBeat) * PixelsPerBeat;
            dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(55, 99, 213, 167)), null,
                new Rect(loopX, 0, loopWidth, 4));
        }

        var firstBeat = Math.Max(0, (int)Math.Floor(_viewStartBeat));
        var lastBeat = (int)Math.Ceiling(endBeat);
        for (var beat = firstBeat; beat <= lastBeat; beat++)
        {
            var x = gridLeft + (beat - _viewStartBeat) * PixelsPerBeat;
            var pen = new Pen(new SolidColorBrush(Color.FromRgb(42, 48, 59)), 0.7);
            dc.DrawLine(pen, new Point(x, HeaderHeight - 7), new Point(x, ActualHeight));
        }

        var firstBar = Math.Max(0, (int)Math.Floor(_viewStartBeat / quarterBeatsPerBar));
        var lastBar = (int)Math.Ceiling(endBeat / quarterBeatsPerBar);
        for (var bar = firstBar; bar <= lastBar; bar++)
        {
            var barBeat = bar * quarterBeatsPerBar;
            var x = gridLeft + (barBeat - _viewStartBeat) * PixelsPerBeat;
            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(66, 75, 89)), 1.2),
                new Point(x, 0), new Point(x, ActualHeight));
            if (x >= 0)
            {
                var label = DrawingTools.Text((bar + 1).ToString(), 10,
                    new SolidColorBrush(Color.FromRgb(157, 167, 182)), true);
                dc.DrawText(label, new Point(x + 5, 7));
            }
        }

        var visibleTrackCount = VisibleTracks;
        _firstTrackIndex = Math.Clamp(_firstTrackIndex, 0, Math.Max(0, _project.Tracks.Count - visibleTrackCount));
        for (var index = _firstTrackIndex; index < _project.Tracks.Count; index++)
        {
            var track = _project.Tracks[index];
            var y = HeaderHeight + (index - _firstTrackIndex) * _laneHeight;
            if (y > ActualHeight)
                break;

            if (track == SelectedTrack)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(36, 99, 213, 167)), null, new Rect(0, y, width, _laneHeight));
            else if ((index & 1) == 1)
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)), null, new Rect(0, y, width, _laneHeight));

            dc.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(46, 53, 64)), 1),
                new Point(0, y + _laneHeight), new Point(width, y + _laneHeight));

            var color = DrawingTools.ParseColor(track.Color, Color.FromRgb(99, 213, 167));
            // The timeline origin is inset to align with the piano keyboard, but the
            // playlist's actual visible boundary is still the full control width.
            dc.PushClip(new RectangleGeometry(new Rect(0, y, width, _laneHeight)));
            foreach (var note in track.Notes)
            {
                var x = gridLeft + (note.StartBeat - _viewStartBeat) * PixelsPerBeat;
                var noteWidth = Math.Max(2, note.LengthBeats * PixelsPerBeat);
                // Keep drawing a partially visible note and remove it only after its
                // rendered rectangle has completely left the timeline viewport.
                if (x + noteWidth <= 0 || x >= width)
                    continue;
                var normalizedPitch = track.Kind == TrackKind.Drums
                    ? Math.Clamp((note.Pitch - 35) / 46.0, 0, 1)
                    : Math.Clamp((note.Pitch - 24) / 84.0, 0, 1);
                var noteY = y + 5 + (1 - normalizedPitch) * (_laneHeight - 12);
                var brush = new SolidColorBrush(Color.FromArgb(track.IsMuted ? (byte)65 : (byte)205, color.R, color.G, color.B));
                dc.DrawRoundedRectangle(brush, null, new Rect(x, noteY, noteWidth, 3.2), 1.4, 1.4);
            }
            dc.Pop();
        }

        var playheadX = gridLeft + (_playheadBeat - _viewStartBeat) * PixelsPerBeat;
        if (playheadX is >= 0 and <= 10000)
        {
            var playheadBrush = new SolidColorBrush(Color.FromRgb(255, 209, 102));
            dc.DrawLine(new Pen(playheadBrush, 1.4), new Point(playheadX, 0), new Point(playheadX, ActualHeight));
            dc.DrawGeometry(playheadBrush, null, new StreamGeometry
            {
                FillRule = FillRule.Nonzero
            }.WithTriangle(new Point(playheadX - 5, 0), new Point(playheadX + 5, 0), new Point(playheadX, 8)));
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        var point = e.GetPosition(this);
        var beat = Math.Max(0, _viewStartBeat + Math.Max(0, point.X - TimelineInset) / PixelsPerBeat);
        SeekRequested?.Invoke(this, new SeekRequestedEventArgs(beat));

        if (_project is not null && point.Y >= HeaderHeight)
        {
            var index = (int)((point.Y - HeaderHeight) / _laneHeight);
            index += _firstTrackIndex;
            if (index >= 0 && index < _project.Tracks.Count)
                TrackSelected?.Invoke(this, new TrackSelectedEventArgs(index));
        }
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) != 0)
        {
            var timelineX = Math.Max(0, e.GetPosition(this).X - TimelineInset);
            var anchor = _viewStartBeat + timelineX / PixelsPerBeat;
            PixelsPerBeat *= e.Delta > 0 ? 1.12 : 1 / 1.12;
            SetHorizontalOffset(anchor - timelineX / PixelsPerBeat);
        }
        else if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            SetHorizontalOffset(_viewStartBeat - Math.Sign(e.Delta) * 2);
        }
        else if (_project is not null)
        {
            SetVerticalOffset(_firstTrackIndex - Math.Sign(e.Delta));
        }
        e.Handled = true;
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
        if (clamped == _firstTrackIndex)
            return;
        _firstTrackIndex = clamped;
        InvalidateVisual();
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    private void CoerceViewport()
    {
        _viewStartBeat = Math.Clamp(_viewStartBeat, 0, HorizontalMaximum);
        _firstTrackIndex = Math.Clamp(_firstTrackIndex, 0, (int)VerticalMaximum);
    }
}

internal static class GeometryExtensions
{
    public static StreamGeometry WithTriangle(this StreamGeometry geometry, Point a, Point b, Point c)
    {
        using var context = geometry.Open();
        context.BeginFigure(a, true, true);
        context.LineTo(b, true, false);
        context.LineTo(c, true, false);
        geometry.Freeze();
        return geometry;
    }
}
