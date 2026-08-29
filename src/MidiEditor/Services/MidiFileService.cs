using System.IO;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using MidiEditor.Models;

namespace MidiEditor.Services;

public static class MidiFileService
{
    private const short ExportPpq = 480;
    private static readonly string[] Colors = ["#63D5A7", "#6EA8FE", "#B790F5", "#FF9D66", "#F779B8", "#64C9E8", "#E5C76B", "#9DD36D"];

    public static MidiProject Import(string path)
    {
        var file = MidiFile.Read(path, new ReadingSettings
        {
            // A number of hardware sequencers append padding or leave the final event/chunk short.
            // Keep every complete event instead of rejecting the whole song.
            NotEnoughBytesPolicy = NotEnoughBytesPolicy.Ignore,
            InvalidChunkSizePolicy = InvalidChunkSizePolicy.Ignore,
            MissedEndOfTrackPolicy = MissedEndOfTrackPolicy.Ignore
        });
        if (file.TimeDivision is not TicksPerQuarterNoteTimeDivision timeDivision)
            throw new NotSupportedException(LocalizationService.Get("Error.SmpteUnsupported"));

        var ppq = timeDivision.TicksPerQuarterNote;
        // Standard MIDI files without an initial tempo event play at 120 BPM.
        var project = new MidiProject { Name = Path.GetFileNameWithoutExtension(path), Tempo = 120 };
        var chunksWithEvents = file.GetTrackChunks()
            .Select(chunk => (Chunk: chunk, Events: chunk.GetTimedEvents().ToArray()))
            .ToArray();
        var allTimedEvents = chunksWithEvents.SelectMany(item => item.Events).ToArray();

        var tempoEvents = allTimedEvents
            .Where(item => item.Event is SetTempoEvent)
            .OrderBy(item => item.Time)
            .GroupBy(item => item.Time)
            .Select(group => group.Last())
            .ToArray();
        project.TempoChanges.Clear();
        if (tempoEvents.Length == 0 || tempoEvents[0].Time > 0)
            project.TempoChanges.Add(new TempoChange { Beat = 0, BeatsPerMinute = 120 });
        foreach (var timedEvent in tempoEvents)
        {
            var tempoEvent = (SetTempoEvent)timedEvent.Event;
            project.TempoChanges.Add(new TempoChange
            {
                Beat = timedEvent.Time / (double)ppq,
                BeatsPerMinute = 60_000_000.0 / tempoEvent.MicrosecondsPerQuarterNote
            });
        }
        project.Tempo = project.TempoChanges.First(change => Math.Abs(change.Beat) < 0.000001).BeatsPerMinute;

        var signature = allTimedEvents
            .Where(item => item.Time == 0)
            .Select(item => item.Event)
            .OfType<TimeSignatureEvent>()
            .FirstOrDefault();
        if (signature is not null)
        {
            project.BeatsPerBar = signature.Numerator;
            project.BeatUnit = signature.Denominator;
        }

        var colorIndex = 0;
        var trackIndex = 0;
        foreach (var chunkWithEvents in chunksWithEvents)
        {
            trackIndex++;
            var chunk = chunkWithEvents.Chunk;
            var chunkNotes = chunk.GetNotes().ToArray();
            if (chunkNotes.Length == 0)
                continue;

            var baseName = chunk.Events.OfType<SequenceTrackNameEvent>().FirstOrDefault()?.Text;
            foreach (var group in chunkNotes.GroupBy(note => (int)note.Channel))
            {
                var sourceChannel = group.Key;
                var firstNoteTime = group.Min(note => note.Time);
                var program = FindEffectiveProgram(chunkWithEvents.Events, allTimedEvents, sourceChannel, firstNoteTime);
                var bankMsb = FindEffectiveControl(chunkWithEvents.Events, allTimedEvents, sourceChannel, 0, firstNoteTime);
                var bankLsb = FindEffectiveControl(chunkWithEvents.Events, allTimedEvents, sourceChannel, 32, firstNoteTime);
                var suffix = chunkNotes.Select(note => (int)note.Channel).Distinct().Count() > 1 ? $" · Ch {sourceChannel + 1}" : string.Empty;
                var track = new MidiTrack
                {
                    Name = string.IsNullOrWhiteSpace(baseName) ? $"Track {trackIndex}{suffix}" : baseName + suffix,
                    Kind = sourceChannel == 9 ? TrackKind.Drums : TrackKind.Instrument,
                    // MIDI channels are intentionally allowed to be shared by multiple source tracks.
                    // The SMF format has only 16 channels, but it can contain any number of track chunks.
                    Channel = sourceChannel,
                    Program = program,
                    Bank = (bankMsb << 7) | bankLsb,
                    Color = Colors[colorIndex++ % Colors.Length]
                };

                foreach (var note in group)
                {
                    track.Notes.Add(new MidiNote
                    {
                        StartBeat = note.Time / (double)ppq,
                        LengthBeats = Math.Max(1.0 / 64.0, note.Length / (double)ppq),
                        Pitch = note.NoteNumber,
                        Velocity = Math.Max(1, (int)note.Velocity)
                    });
                }
                project.Tracks.Add(track);
            }
        }

        if (project.Tracks.Count == 0)
            project.Tracks.Add(new MidiTrack { Name = "Instrument 1", Color = Colors[0] });

        var endBeat = project.Tracks.Max(track => track.EndBeat);
        project.Bars = Math.Max(4, (int)Math.Ceiling(endBeat / project.QuarterBeatsPerBar));
        project.LoopStartBeat = 0;
        project.LoopEndBeat = Math.Min(project.DurationBeats, Math.Max(project.QuarterBeatsPerBar * 4, endBeat));
        return project;
    }

    public static void Export(string path, MidiProject project)
    {
        var conductor = new TrackChunk();
        using (var manager = new TimedObjectsManager(conductor.Events, ObjectType.TimedEvent))
        {
            manager.Objects.Add(new TimedEvent(new SequenceTrackNameEvent("Conductor"), 0));
            manager.Objects.Add(new TimedEvent(new TimeSignatureEvent((byte)project.BeatsPerBar, (byte)project.BeatUnit), 0));

            var tempoChanges = project.TempoChanges
                .OrderBy(change => change.Beat)
                .GroupBy(change => change.Beat)
                .Select(group => group.Last())
                .ToList();
            if (tempoChanges.Count == 0 || tempoChanges[0].Beat > 0.000001)
                tempoChanges.Insert(0, new TempoChange { Beat = 0, BeatsPerMinute = project.Tempo });
            foreach (var change in tempoChanges)
            {
                var time = (long)Math.Round(change.Beat * ExportPpq);
                var microseconds = (int)Math.Round(60_000_000.0 / change.BeatsPerMinute);
                manager.Objects.Add(new TimedEvent(new SetTempoEvent(microseconds), time));
            }
        }

        var chunks = new List<TrackChunk> { conductor };
        foreach (var source in project.Tracks)
        {
            var chunk = new TrackChunk();
            using (var manager = new TimedObjectsManager(chunk.Events, ObjectType.Note | ObjectType.TimedEvent))
            {
                manager.Objects.Add(new TimedEvent(new SequenceTrackNameEvent(source.Name), 0));
                var channel = (FourBitNumber)(source.Kind == TrackKind.Drums ? 9 : source.Channel);

                manager.Objects.Add(new TimedEvent(new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)((source.Bank >> 7) & 0x7F)) { Channel = channel }, 0));
                manager.Objects.Add(new TimedEvent(new ControlChangeEvent((SevenBitNumber)32, (SevenBitNumber)(source.Bank & 0x7F)) { Channel = channel }, 0));
                manager.Objects.Add(new TimedEvent(new ProgramChangeEvent((SevenBitNumber)source.Program) { Channel = channel }, 0));

                foreach (var sourceNote in source.Notes)
                {
                    var time = (long)Math.Round(sourceNote.StartBeat * ExportPpq);
                    var length = Math.Max(1, (long)Math.Round(sourceNote.LengthBeats * ExportPpq));
                    manager.Objects.Add(new Note((SevenBitNumber)sourceNote.Pitch, length, time)
                    {
                        Channel = channel,
                        Velocity = (SevenBitNumber)sourceNote.Velocity,
                        OffVelocity = (SevenBitNumber)0
                    });
                }
            }
            chunks.Add(chunk);
        }

        var file = new MidiFile(chunks.ToArray())
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(ExportPpq)
        };
        file.Write(path, overwriteFile: true, format: MidiFileFormat.MultiTrack);
    }

    private static int FindEffectiveProgram(
        IReadOnlyList<TimedEvent> localEvents,
        IReadOnlyList<TimedEvent> globalEvents,
        int channel,
        long atTime)
    {
        var effective = globalEvents
            .Where(item => item.Time <= atTime && item.Event is ProgramChangeEvent program && (int)program.Channel == channel)
            .OrderBy(item => item.Time)
            .ThenBy(item => localEvents.Contains(item) ? 1 : 0)
            .LastOrDefault()?.Event as ProgramChangeEvent;
        return effective is not null ? effective.ProgramNumber : 0;
    }

    private static int FindEffectiveControl(
        IReadOnlyList<TimedEvent> localEvents,
        IReadOnlyList<TimedEvent> globalEvents,
        int channel,
        int controlNumber,
        long atTime)
    {
        var effective = globalEvents
            .Where(item => item.Time <= atTime && item.Event is ControlChangeEvent control &&
                           (int)control.Channel == channel && (int)control.ControlNumber == controlNumber)
            .OrderBy(item => item.Time)
            .ThenBy(item => localEvents.Contains(item) ? 1 : 0)
            .LastOrDefault()?.Event as ControlChangeEvent;
        return effective is not null ? effective.ControlValue : 0;
    }
}
