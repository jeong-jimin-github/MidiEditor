using MidiEditor.Models;
using MidiEditor.Services;
using Melanchall.DryWetMidi.Common;
using Melanchall.DryWetMidi.Core;
using Melanchall.DryWetMidi.Interaction;
using Xunit;

namespace MidiEditor.Tests;

public sealed class ProjectAndMidiTests
{
    [Fact]
    public void AppSettings_RemembersLastSoundFontPath()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"pulsegrid-settings-{Guid.NewGuid():N}.json");
        var soundFontPath = @"C:\Sounds\Remembered.sf2";
        try
        {
            AppSettingsService.SaveLastSoundFontPath(soundFontPath, settingsPath);
            Assert.Equal(soundFontPath, AppSettingsService.LoadLastSoundFontPath(settingsPath));
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void ProjectFile_RoundTripsTracksNotesAndSoundFontSetting()
    {
        var source = MidiProject.CreateDemo();
        source.SoundFontPath = @"C:\Sounds\Studio.sf2";
        source.Tracks[0].Notes[0].Velocity = 117;
        var path = Path.Combine(Path.GetTempPath(), $"pulsegrid-{Guid.NewGuid():N}.pulsegrid");

        try
        {
            ProjectFileService.Save(path, source);
            var loaded = ProjectFileService.Load(path);

            Assert.Equal(source.Name, loaded.Name);
            Assert.Equal(128, loaded.Tempo);
            Assert.Equal(source.SoundFontPath, loaded.SoundFontPath);
            Assert.Equal(4, loaded.Tracks.Count);
            Assert.Equal(100, loaded.Tracks.Sum(track => track.Notes.Count));
            Assert.Equal(117, loaded.Tracks[0].Notes[0].Velocity);
            Assert.Equal(TrackKind.Drums, loaded.Tracks[3].Kind);
            Assert.Equal(9, loaded.Tracks[3].Channel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MidiExportImport_RoundTripsMultitrackNotesTempoAndDrums()
    {
        var source = MidiProject.CreateDemo();
        source.LoopEnabled = false;
        source.BeatsPerBar = 6;
        source.BeatUnit = 8;
        source.Tracks[0].Bank = 129;
        source.TempoChanges.Add(new TempoChange { Beat = 0, BeatsPerMinute = 128 });
        source.TempoChanges.Add(new TempoChange { Beat = 8, BeatsPerMinute = 96 });
        var path = Path.Combine(Path.GetTempPath(), $"pulsegrid-{Guid.NewGuid():N}.mid");

        try
        {
            MidiFileService.Export(path, source);
            var loaded = MidiFileService.Import(path);

            Assert.Equal(128, loaded.Tempo, 3);
            Assert.Equal(6, loaded.BeatsPerBar);
            Assert.Equal(8, loaded.BeatUnit);
            Assert.Equal(2, loaded.TempoChanges.Count);
            Assert.Equal(96, loaded.TempoChanges[1].BeatsPerMinute, 3);
            Assert.Equal(4, loaded.Tracks.Count);
            Assert.Equal(100, loaded.Tracks.Sum(track => track.Notes.Count));
            Assert.Contains(loaded.Tracks, track => track.Kind == TrackKind.Drums && track.Channel == 9);

            var first = loaded.Tracks.Single(track => track.Name == "Neon Keys").Notes.OrderBy(note => note.StartBeat).First();
            Assert.Equal(129, loaded.Tracks.Single(track => track.Name == "Neon Keys").Bank);
            Assert.Equal(0, first.StartBeat, 6);
            Assert.Equal(3.75, first.LengthBeats, 6);
            Assert.InRange(first.Velocity, 82, 100);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void History_UndoAndRedoPreserveTrackIdentityAndEdits()
    {
        var project = MidiProject.CreateDemo();
        var history = new HistoryService();
        var trackId = project.Tracks[3].Id;

        history.Begin(project);
        project.Tracks[3].Notes.Add(new MidiNote { Pitch = 39, StartBeat = 1.25, Velocity = 101 });
        history.Commit(project);

        var undone = history.Undo(project);
        Assert.NotNull(undone);
        Assert.Equal(trackId, undone.Tracks[3].Id);
        Assert.Equal(56, undone.Tracks[3].Notes.Count);

        var redone = history.Redo(undone);
        Assert.NotNull(redone);
        Assert.Equal(trackId, redone.Tracks[3].Id);
        Assert.Equal(57, redone.Tracks[3].Notes.Count);
    }

    [Fact]
    public void History_DoesNotCreateUndoEntryForSelectionOnlyGesture()
    {
        var project = MidiProject.CreateDemo();
        var history = new HistoryService();

        history.Begin(project);

        Assert.False(history.Commit(project));
        Assert.False(history.CanUndo);
    }

    [Fact]
    public void MidiImport_UsesStandard120BpmWhenInitialTempoIsMissing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pulsegrid-no-tempo-{Guid.NewGuid():N}.mid");
        var track = new TrackChunk();
        using (var notes = track.ManageNotes())
            notes.Objects.Add(new Note((SevenBitNumber)60, 480, 0));
        var file = new MidiFile(track)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        try
        {
            file.Write(path, overwriteFile: true);
            var loaded = MidiFileService.Import(path);
            Assert.Equal(120, loaded.Tempo);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MidiImport_ReadsProgramAndBankFromSeparateSetupTrack()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pulsegrid-setup-{Guid.NewGuid():N}.mid");
        var channel = (FourBitNumber)0;
        var setup = new TrackChunk();
        using (var events = setup.ManageTimedEvents())
        {
            events.Objects.Add(new TimedEvent(new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)1) { Channel = channel }, 100));
            events.Objects.Add(new TimedEvent(new ControlChangeEvent((SevenBitNumber)32, (SevenBitNumber)2) { Channel = channel }, 100));
            events.Objects.Add(new TimedEvent(new ProgramChangeEvent((SevenBitNumber)40) { Channel = channel }, 100));
        }
        var notesTrack = new TrackChunk(
            new SequenceTrackNameEvent("Strings"),
            new ControlChangeEvent((SevenBitNumber)0, (SevenBitNumber)0) { Channel = channel },
            new ProgramChangeEvent((SevenBitNumber)1) { Channel = channel });
        using (var notes = notesTrack.ManageNotes())
            notes.Objects.Add(new Note((SevenBitNumber)60, 480, 200) { Channel = channel, Velocity = (SevenBitNumber)90 });
        var file = new MidiFile(setup, notesTrack)
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        try
        {
            file.Write(path, overwriteFile: true, format: MidiFileFormat.MultiTrack);
            var loaded = MidiFileService.Import(path);
            var track = Assert.Single(loaded.Tracks);
            Assert.Equal(40, track.Program);
            Assert.Equal(130, track.Bank);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MidiImport_AllowsMoreThanFifteenTracksByPreservingSharedChannels()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pulsegrid-too-many-{Guid.NewGuid():N}.mid");
        var chunks = new List<TrackChunk>();
        for (var index = 0; index < 16; index++)
        {
            var chunk = new TrackChunk(
                new SequenceTrackNameEvent($"Instrument {index + 1}"),
                new ProgramChangeEvent((SevenBitNumber)index) { Channel = (FourBitNumber)0 });
            using (var notes = chunk.ManageNotes())
                notes.Objects.Add(new Note((SevenBitNumber)(48 + index), 120, index * 120L) { Channel = (FourBitNumber)0 });
            chunks.Add(chunk);
        }
        var file = new MidiFile(chunks.ToArray())
        {
            TimeDivision = new TicksPerQuarterNoteTimeDivision(480)
        };

        try
        {
            file.Write(path, overwriteFile: true, format: MidiFileFormat.MultiTrack);
            var loaded = MidiFileService.Import(path);
            Assert.Equal(16, loaded.Tracks.Count);
            Assert.All(loaded.Tracks, track => Assert.Equal(0, track.Channel));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MidiImport_IgnoresTrailingIncompleteChunkData()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pulsegrid-trailing-{Guid.NewGuid():N}.mid");
        var track = new TrackChunk();
        using (var notes = track.ManageNotes())
            notes.Objects.Add(new Note((SevenBitNumber)64, 240, 0));
        var file = new MidiFile(track) { TimeDivision = new TicksPerQuarterNoteTimeDivision(480) };

        try
        {
            file.Write(path, overwriteFile: true);
            using (var stream = File.Open(path, FileMode.Append, FileAccess.Write))
                stream.Write(new byte[] { 0x43, 0x7B, 0x00 });

            var loaded = MidiFileService.Import(path);
            Assert.Single(loaded.Tracks);
            Assert.Single(loaded.Tracks[0].Notes);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PlaybackPlan_SchedulesNotesAtExactSamplesAndHonorsSolo()
    {
        var project = new MidiProject
        {
            Tempo = 120,
            Bars = 2,
            LoopEnabled = true,
            LoopStartBeat = 1,
            LoopEndBeat = 4
        };
        var mutedBySolo = new MidiTrack { Name = "Background", Channel = 0 };
        mutedBySolo.Notes.Add(new MidiNote { StartBeat = 0, LengthBeats = 1, Pitch = 48, Velocity = 70 });
        var solo = new MidiTrack { Name = "Solo", Channel = 2, Program = 81, IsSolo = true };
        solo.Notes.Add(new MidiNote { StartBeat = 1, LengthBeats = 0.5, Pitch = 72, Velocity = 111 });
        project.Tracks.Add(mutedBySolo);
        project.Tracks.Add(solo);

        var plan = PlaybackPlan.Create(project, 44100);

        Assert.Equal(22050, plan.Events.Single(item => item.Command == 0x90).Sample);
        Assert.Equal(33075, plan.Events.Single(item => item.Command == 0x80).Sample);
        Assert.DoesNotContain(plan.Events, item => item.Data1 == 48);
        Assert.Contains(plan.InitialMessages, item => item.Channel == 2 && item.Command == 0xC0 && item.Data1 == 81);
        Assert.Equal(22050, plan.LoopStartSample);
        Assert.Equal(88200, plan.LoopEndSample);
        Assert.Contains(plan.Notes, note => note.OnSample == 22050 && note.OffSample == 33075 && note.Pitch == 72);
    }

    [Fact]
    public void PlaybackPlan_UsesTempoMapAndTruncatesSamePitchRetriggers()
    {
        var project = new MidiProject { Tempo = 120, Bars = 2, LoopEnabled = false };
        project.TempoChanges.Add(new TempoChange { Beat = 0, BeatsPerMinute = 120 });
        project.TempoChanges.Add(new TempoChange { Beat = 2, BeatsPerMinute = 60 });
        var track = new MidiTrack { Channel = 0 };
        track.Notes.Add(new MidiNote { StartBeat = 0, LengthBeats = 2, Pitch = 60, Velocity = 90 });
        track.Notes.Add(new MidiNote { StartBeat = 1, LengthBeats = 2, Pitch = 60, Velocity = 100 });
        project.Tracks.Add(track);

        var plan = PlaybackPlan.Create(project, 44100);
        var noteOffSamples = plan.Events.Where(item => item.Command == 0x80).Select(item => item.Sample).ToArray();

        Assert.Equal(new long[] { 22050, 88200 }, noteOffSamples);
        Assert.Equal(88200, plan.BeatToSample(3));
        Assert.Equal(3, plan.SampleToBeat(88200), 6);
    }

    [Fact]
    public void AppSettings_SavingVocalToolsPreservesRememberedSoundFont()
    {
        var settingsPath = Path.Combine(Path.GetTempPath(), $"pulsegrid-settings-{Guid.NewGuid():N}.json");
        try
        {
            AppSettingsService.SaveLastSoundFontPath(@"C:\Sounds\Default.sf2", settingsPath);
            AppSettingsService.SaveVocalSettings(new VocalToolSettings
            {
                VoicebankRootPath = @"C:\Voicebanks",
                OpenUtauPath = @"C:\OpenUtau\OpenUtau.exe",
                ResamplerPath = @"C:\Tools\resampler.exe",
                WavtoolPath = @"C:\Tools\wavtool.exe"
            }, settingsPath);

            Assert.Equal(@"C:\Sounds\Default.sf2", AppSettingsService.LoadLastSoundFontPath(settingsPath));
            var vocal = AppSettingsService.LoadVocalSettings(settingsPath);
            Assert.Equal(@"C:\Voicebanks", vocal.VoicebankRootPath);
            Assert.Equal(@"C:\OpenUtau\OpenUtau.exe", vocal.OpenUtauPath);
            Assert.Equal(@"C:\Tools\resampler.exe", vocal.ResamplerPath);
            Assert.Equal(@"C:\Tools\wavtool.exe", vocal.WavtoolPath);
        }
        finally
        {
            File.Delete(settingsPath);
        }
    }

    [Fact]
    public void ProjectFile_RoundTripsVocalVoicebankAndLyrics()
    {
        var project = new MidiProject { Name = "Vocal Test" };
        var vocal = new MidiTrack
        {
            Name = "Lead Vocal",
            Kind = TrackKind.Vocal,
            Channel = 2,
            Program = 53,
            VoicebankPath = @"C:\Voicebanks\Momo"
        };
        vocal.Notes.Add(new MidiNote { StartBeat = 0.5, LengthBeats = 1.25, Pitch = 64, Velocity = 108, Lyric = "ka" });
        project.Tracks.Add(vocal);
        var path = Path.Combine(Path.GetTempPath(), $"pulsegrid-vocal-{Guid.NewGuid():N}.pulsegrid");
        try
        {
            ProjectFileService.Save(path, project);
            var loaded = ProjectFileService.Load(path);
            var loadedTrack = Assert.Single(loaded.Tracks);
            var loadedNote = Assert.Single(loadedTrack.Notes);
            Assert.Equal(TrackKind.Vocal, loadedTrack.Kind);
            Assert.Equal(vocal.VoicebankPath, loadedTrack.VoicebankPath);
            Assert.Equal("ka", loadedNote.Lyric);
            Assert.Equal(0.5, loadedNote.StartBeat);
            Assert.Equal(1.25, loadedNote.LengthBeats);
            Assert.Equal(64, loadedNote.Pitch);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void VocalIntegration_ExportsOpenUtauCompatibleUstWithConfiguredTools()
    {
        var project = new MidiProject { Name = "Song", Tempo = 135 };
        var vocal = new MidiTrack
        {
            Name = "Vocal",
            Kind = TrackKind.Vocal,
            VoicebankPath = BundledAssetsService.DefaultVoicebankPath
        };
        vocal.Notes.Add(new MidiNote { StartBeat = 0.5, LengthBeats = 1, Pitch = 67, Velocity = 111, Lyric = "a" });
        project.Tracks.Add(vocal);
        var path = Path.Combine(Path.GetTempPath(), $"pulsegrid-{Guid.NewGuid():N}.ust");
        var settings = new VocalToolSettings { ResamplerPath = @"C:\Tools\resampler.exe", WavtoolPath = @"C:\Tools\wavtool.exe" };
        try
        {
            VocalIntegrationService.ExportUst(path, project, vocal, settings);
            var text = File.ReadAllText(path);
            Assert.Contains("UST Version1.2", text);
            Assert.Contains("Tempo=135", text);
            Assert.Contains($"VoiceDir={BundledAssetsService.DefaultVoicebankPath}", text);
            Assert.Contains(@"Tool1=C:\Tools\resampler.exe", text);
            Assert.Contains(@"Tool2=C:\Tools\wavtool.exe", text);
            Assert.Contains("Lyric=R", text);
            Assert.Contains("Lyric=a", text);
            Assert.Contains("NoteNum=67", text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task BundledAssets_LoadSoundFontAndRenderDefaultVocalPreview()
    {
        Assert.True(File.Exists(BundledAssetsService.DefaultSoundFontPath));
        Assert.True(Directory.Exists(BundledAssetsService.DefaultVoicebankPath));

        var soundFont = new MeltySynth.SoundFont(BundledAssetsService.DefaultSoundFontPath);
        Assert.NotNull(soundFont);

        var project = new MidiProject { Tempo = 120 };
        var vocal = new MidiTrack { Kind = TrackKind.Vocal, VoicebankPath = BundledAssetsService.DefaultVoicebankPath };
        vocal.Notes.Add(new MidiNote { StartBeat = 0, LengthBeats = 0.25, Pitch = 60, Velocity = 100, Lyric = "a" });
        project.Tracks.Add(vocal);
        var preview = await VocalIntegrationService.RenderQuickPreviewAsync(project, vocal, new VocalToolSettings());
        Assert.True(File.Exists(preview));
        Assert.True(new FileInfo(preview).Length > 44);
    }
}
