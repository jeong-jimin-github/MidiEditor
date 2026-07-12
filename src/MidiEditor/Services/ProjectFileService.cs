using System.IO;
using System.Text.Json;
using MidiEditor.Models;

namespace MidiEditor.Services;

public static class ProjectFileService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void Save(string path, MidiProject project)
    {
        var dto = new ProjectDto
        {
            Name = project.Name,
            Tempo = project.Tempo,
            BeatsPerBar = project.BeatsPerBar,
            BeatUnit = project.BeatUnit,
            Bars = project.Bars,
            LoopStartBeat = project.LoopStartBeat,
            LoopEndBeat = project.LoopEndBeat,
            LoopEnabled = project.LoopEnabled,
            SoundFontPath = project.SoundFontPath,
            TempoChanges = project.TempoChanges.Select(change => new TempoChangeDto
            {
                Beat = change.Beat,
                BeatsPerMinute = change.BeatsPerMinute
            }).ToList(),
            Tracks = project.Tracks.Select(track => new TrackDto
            {
                Name = track.Name,
                Kind = track.Kind,
                Channel = track.Channel,
                Program = track.Program,
                Bank = track.Bank,
                Color = track.Color,
                IsMuted = track.IsMuted,
                IsSolo = track.IsSolo,
                Volume = track.Volume,
                Notes = track.Notes.Select(note => new NoteDto
                {
                    StartBeat = note.StartBeat,
                    LengthBeats = note.LengthBeats,
                    Pitch = note.Pitch,
                    Velocity = note.Velocity
                }).ToList()
            }).ToList()
        };

        File.WriteAllText(path, JsonSerializer.Serialize(dto, Options));
    }

    public static MidiProject Load(string path)
    {
        var dto = JsonSerializer.Deserialize<ProjectDto>(File.ReadAllText(path), Options)
                  ?? throw new InvalidDataException("프로젝트 파일을 읽을 수 없습니다.");

        var project = new MidiProject
        {
            Name = dto.Name,
            Tempo = dto.Tempo,
            BeatsPerBar = dto.BeatsPerBar,
            BeatUnit = dto.BeatUnit,
            Bars = dto.Bars,
            LoopStartBeat = dto.LoopStartBeat,
            LoopEndBeat = dto.LoopEndBeat,
            LoopEnabled = dto.LoopEnabled,
            SoundFontPath = dto.SoundFontPath
        };

        foreach (var change in dto.TempoChanges.OrderBy(change => change.Beat))
            project.TempoChanges.Add(new TempoChange { Beat = change.Beat, BeatsPerMinute = change.BeatsPerMinute });

        foreach (var source in dto.Tracks)
        {
            var track = new MidiTrack
            {
                Name = source.Name,
                Kind = source.Kind,
                Channel = source.Kind == TrackKind.Drums ? 9 : source.Channel,
                Program = source.Program,
                Bank = source.Bank,
                Color = source.Color,
                IsMuted = source.IsMuted,
                IsSolo = source.IsSolo,
                Volume = source.Volume
            };
            foreach (var item in source.Notes)
                track.Notes.Add(new MidiNote { StartBeat = item.StartBeat, LengthBeats = item.LengthBeats, Pitch = item.Pitch, Velocity = item.Velocity });
            project.Tracks.Add(track);
        }

        return project;
    }

    private sealed class ProjectDto
    {
        public string Name { get; set; } = "Untitled Groove";
        public double Tempo { get; set; } = 120;
        public int BeatsPerBar { get; set; } = 4;
        public int BeatUnit { get; set; } = 4;
        public int Bars { get; set; } = 16;
        public double LoopStartBeat { get; set; }
        public double LoopEndBeat { get; set; } = 16;
        public bool LoopEnabled { get; set; } = true;
        public string? SoundFontPath { get; set; }
        public List<TempoChangeDto> TempoChanges { get; set; } = [];
        public List<TrackDto> Tracks { get; set; } = [];
    }

    private sealed class TempoChangeDto
    {
        public double Beat { get; set; }
        public double BeatsPerMinute { get; set; } = 120;
    }

    private sealed class TrackDto
    {
        public string Name { get; set; } = "Track";
        public TrackKind Kind { get; set; }
        public int Channel { get; set; }
        public int Program { get; set; }
        public int Bank { get; set; }
        public string Color { get; set; } = "#63D5A7";
        public bool IsMuted { get; set; }
        public bool IsSolo { get; set; }
        public double Volume { get; set; } = 0.9;
        public List<NoteDto> Notes { get; set; } = [];
    }

    private sealed class NoteDto
    {
        public double StartBeat { get; set; }
        public double LengthBeats { get; set; }
        public int Pitch { get; set; }
        public int Velocity { get; set; }
    }
}
