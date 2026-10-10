using System;
using System.Collections.Generic;
using System.Linq;
using Synthesizer = MeltySynth.Synthesizer;
using NAudio.Midi;

namespace piano
{
    public readonly record struct SongEvent(int Tick, byte Status, byte Data1, byte Data2);

    // A small multitrack recorder: six tracks for what the player performs (main instrument plus
    // layer) and one for the accompaniment the arranger generated. The song plays on its own
    // synthesizers, so every track keeps its instrument while the player moves on to another one.
    // There are two of them: the twelve channels of the player tracks leave no room for the
    // seven the accompaniment can use (drums and six instruments).
    //
    // Recording and playback run on the audio thread. Track contents are immutable arrays that
    // are swapped whole, which lets the UI thread read them (to save a file) without locking.
    public sealed class Sequencer
    {
        public const int PlayerTracks = 6;
        public const int AccompTrack = 6;
        public const int TrackCount = 7;

        public const int Stopped = 0, Playing = 1, Recording = 2;

        // Channels of the song synthesizer used by each player track. The accompaniment keeps the
        // channels of the arranger on a synthesizer to itself; in a MIDI file from elsewhere,
        // whatever is on channels 9 to 12 is taken for it.
        private static readonly (int Main, int Layer)[] TrackChannels = { (0, 1), (2, 3), (4, 5), (6, 7), (13, 14), (8, 15) };
        private static readonly int[] ChannelTrack = { 0, 0, 1, 1, 2, 2, 3, 3, 5, 6, 6, 6, 6, 4, 4, 5 };

        private readonly AudioEngine engine;
        private readonly Synthesizer synth;
        private readonly Synthesizer accompSynth;
        private readonly int[] cursor = new int[TrackCount];
        private readonly bool[] muted = new bool[TrackCount];
        private readonly HashSet<(int Channel, int Note)> openNotes = new();

        private volatile SongEvent[][] tracks = EmptySong();
        private List<SongEvent>? take, accompTake;
        private int length;

        public volatile int State;
        public volatile int RecordingTrack = -1;

        public Sequencer(AudioEngine engine, Synthesizer synth, Synthesizer accompSynth)
        {
            this.engine = engine;
            this.synth = synth;
            this.accompSynth = accompSynth;
        }

        public bool Active => State != Stopped;
        public SongEvent[][] Tracks => tracks;
        public bool IsEmpty => tracks.All(t => t.Length == 0);

        public static SongEvent[][] EmptySong() =>
            Enumerable.Range(0, TrackCount).Select(_ => Array.Empty<SongEvent>()).ToArray();

        public void Play() => engine.Post(DoPlay);
        public void Record(int track) => engine.Post(() => DoRecord(track));
        public void Stop() => engine.Post(StopNow);
        public void SetMute(int track, bool on) => engine.Post(() => DoMute(track, on));
        public void Clear(int track) => SetTrack(track, Array.Empty<SongEvent>());
        public void SetTrack(int track, SongEvent[] events) => engine.Post(() => { StopNow(); Publish(track, events); });
        public void Load(SongEvent[][] song) => engine.Post(() => { StopNow(); tracks = song; });

        private void DoPlay()
        {
            StopNow();
            if (IsEmpty) return;
            Begin(Playing, -1, 0);
        }

        private void DoRecord(int track)
        {
            StopNow();
            take = new List<SongEvent>();
            openNotes.Clear();
            // the accompaniment is captured only once; clear its track to record it again
            accompTake = tracks[AccompTrack].Length == 0 ? new List<SongEvent>() : null;

            // one bar of count-in
            Begin(Recording, track, -engine.Arranger.BeatsPerBar * AudioEngine.TicksPerBeat);
            engine.SendSetup();
        }

        private void Begin(int state, int recordingTrack, int startTick)
        {
            Array.Clear(cursor);
            int barTicks = engine.Arranger.BeatsPerBar * AudioEngine.TicksPerBeat;
            int last = tracks.Max(t => t.Length == 0 ? 0 : t[^1].Tick);
            length = (last / barTicks + 1) * barTicks;

            engine.ResetTransport(startTick);
            RecordingTrack = recordingTrack;
            State = state;

            // a recorded accompaniment replaces the live one, otherwise both would play at once
            if (tracks[AccompTrack].Length > 0) engine.Arranger.StopNow();
            else if (engine.Arranger.Playing) engine.Arranger.Restart();
        }

        internal void StopNow()
        {
            if (State == Stopped) return;

            if (State == Recording && take != null)
            {
                engine.Arranger.StopNow();

                int tick = Math.Max(engine.Tick, 0);
                foreach (var (channel, note) in openNotes)
                    take.Add(new SongEvent(tick, (byte)(0x80 | channel), (byte)note, 0));

                if (HasNotes(take)) Publish(RecordingTrack, Sorted(take));
                if (accompTake != null && HasNotes(accompTake)) Publish(AccompTrack, Sorted(accompTake));
                take = accompTake = null;
            }

            synth.NoteOffAll(false);
            accompSynth.NoteOffAll(false);
            State = Stopped;
            RecordingTrack = -1;
        }

        private static bool HasNotes(List<SongEvent> events) => events.Any(e => (e.Status & 0xF0) == 0x90);

        // OrderBy is stable, so events on the same tick keep the order they were played in
        private static SongEvent[] Sorted(IEnumerable<SongEvent> events) => events.OrderBy(e => e.Tick).ToArray();

        private void Publish(int track, SongEvent[] events)
        {
            var copy = (SongEvent[][])tracks.Clone();
            copy[track] = events;
            tracks = copy;
        }

        private void DoMute(int track, bool on)
        {
            muted[track] = on;
            if (!on) return;

            if (track == AccompTrack)
            {
                accompSynth.NoteOffAll(false);
                return;
            }

            for (int channel = 0; channel < ChannelTrack.Length; channel++)
                if (ChannelTrack[channel] == track) synth.NoteOffAll(channel, false);
        }

        // What the player performs: channel is AudioEngine.MainChannel or LayerChannel
        internal void Capture(int channel, int command, int data1, int data2)
        {
            if (take == null) return;

            var pair = TrackChannels[RecordingTrack];
            int target = channel == AudioEngine.MainChannel ? pair.Main : pair.Layer;
            int tick = engine.EventTick;

            if (command == 0x90)
            {
                // a note slightly ahead of the first beat still belongs to it; earlier ones are warm-up
                if (tick < -AudioEngine.TicksPerBeat / 4) return;
                openNotes.Add((target, data1));
            }
            else if (command == 0x80 && !openNotes.Remove((target, data1))) return;

            take.Add(new SongEvent(Math.Max(tick, 0), (byte)(command | target), (byte)data1, (byte)data2));
        }

        internal void CaptureAccomp(int channel, int command, int data1, int data2)
        {
            accompTake?.Add(new SongEvent(Math.Max(engine.Tick, 0), (byte)(command | channel), (byte)data1, (byte)data2));
        }

        internal void Advance(int tick)
        {
            if (State == Stopped || tick < 0) return;

            var song = tracks;
            for (int t = 0; t < TrackCount; t++)
            {
                if (t == RecordingTrack) continue;

                var events = song[t];
                var target = t == AccompTrack ? accompSynth : synth;
                while (cursor[t] < events.Length && events[cursor[t]].Tick <= tick)
                {
                    var e = events[cursor[t]++];
                    int command = e.Status & 0xF0;
                    // a muted track still follows its program changes, so it can be unmuted mid-song
                    if (muted[t] && command == 0x90) continue;
                    target.ProcessMidiMessage(e.Status & 0x0F, command, e.Data1, e.Data2);
                }
            }

            if (State == Playing && tick >= length) StopNow();
        }

        // Moves every note towards the nearest grid line: all the way at strength 1, half way at 0.5.
        // A note keeps its length: its end moves along with its start.
        public static SongEvent[] Quantize(SongEvent[] events, int gridTicks, double strength = 1)
        {
            var shift = new Dictionary<(int Channel, int Note), int>();
            var result = new List<SongEvent>(events.Length);

            foreach (var e in events)
            {
                int command = e.Status & 0xF0;
                var key = (e.Status & 0x0F, (int)e.Data1);
                int tick = e.Tick;

                if (command == 0x90)
                {
                    int nearest = (int)Math.Round(e.Tick / (double)gridTicks) * gridTicks;
                    tick = e.Tick + (int)Math.Round((nearest - e.Tick) * strength);
                    shift[key] = tick - e.Tick;
                }
                else if (command == 0x80 && shift.Remove(key, out int delta))
                {
                    tick = Math.Max(e.Tick + delta, 0);
                }

                result.Add(e with { Tick = tick });
            }

            // on the same tick: instrument and controller changes first, then note-offs, then the new notes
            static int Order(SongEvent e) => (e.Status & 0xF0) switch { 0x90 => 2, 0x80 => 1, _ => 0 };
            return result.OrderBy(e => e.Tick).ThenBy(Order).ToArray();
        }

        // The name of the accompaniment track in a MIDI file. It tells that track apart from the
        // player tracks: with more than three instruments the arranger is on channels they use too.
        private const string AccompName = "Accompaniment";

        public static void SaveMidi(string path, SongEvent[][] song, double tempo, int beatsPerBar)
        {
            var file = new MidiEventCollection(1, AudioEngine.TicksPerBeat);
            file.AddEvent(new TempoEvent((int)Math.Round(60000000.0 / tempo), 0), 0);
            file.AddEvent(new TimeSignatureEvent(0, beatsPerBar, 2, 24, 8), 0);
            if (song[AccompTrack].Length > 0)
                file.AddEvent(new TextEvent(AccompName, MetaEventType.SequenceTrackName, 0), AccompTrack + 1);

            for (int t = 0; t < song.Length; t++)
            {
                foreach (var e in song[t])
                {
                    int channel = (e.Status & 0x0F) + 1;
                    MidiEvent? midi = (e.Status & 0xF0) switch
                    {
                        0x90 => new NoteEvent(e.Tick, channel, MidiCommandCode.NoteOn, e.Data1, e.Data2),
                        0x80 => new NoteEvent(e.Tick, channel, MidiCommandCode.NoteOff, e.Data1, 0),
                        0xB0 => new ControlChangeEvent(e.Tick, channel, (MidiController)e.Data1, e.Data2),
                        0xC0 => new PatchChangeEvent(e.Tick, channel, e.Data1),
                        0xE0 => new PitchWheelChangeEvent(e.Tick, channel, e.Data1 | (e.Data2 << 7)),
                        _ => null
                    };
                    if (midi != null) file.AddEvent(midi, t + 1);
                }
            }

            file.PrepareForExport();
            MidiFile.Export(path, file);
        }

        // Reads any Standard MIDI File. Channels are kept, so files saved here come back exactly
        // as they were and files from elsewhere still play with the right instruments.
        public static (SongEvent[][] Song, double Tempo) LoadMidi(string path)
        {
            var file = new MidiFile(path, false);
            var song = Enumerable.Range(0, TrackCount).Select(_ => new List<SongEvent>()).ToArray();
            double tempo = 0;

            for (int t = 0; t < file.Tracks; t++)
            {
                bool accomp = file.Events[t].OfType<TextEvent>()
                    .Any(name => name.MetaEventType == MetaEventType.SequenceTrackName && name.Text == AccompName);

                foreach (var midi in file.Events[t])
                {
                    if (midi is TempoEvent tempoEvent)
                    {
                        if (tempo == 0) tempo = tempoEvent.Tempo;
                        continue;
                    }

                    (int command, int data1, int data2) = midi switch
                    {
                        NoteEvent n when n.CommandCode == MidiCommandCode.NoteOn && n.Velocity > 0 => (0x90, n.NoteNumber, n.Velocity),
                        NoteEvent n when n.CommandCode is MidiCommandCode.NoteOn or MidiCommandCode.NoteOff => (0x80, n.NoteNumber, 0),
                        ControlChangeEvent c => (0xB0, (int)c.Controller, c.ControllerValue),
                        PatchChangeEvent p => (0xC0, p.Patch, 0),
                        PitchWheelChangeEvent w => (0xE0, w.Pitch & 0x7F, w.Pitch >> 7),
                        _ => (0, 0, 0)
                    };
                    if (command == 0) continue;

                    int channel = midi.Channel - 1;
                    int tick = (int)(midi.AbsoluteTime * AudioEngine.TicksPerBeat / file.DeltaTicksPerQuarterNote);
                    song[accomp ? AccompTrack : ChannelTrack[channel]].Add(new SongEvent(tick, (byte)(command | channel), (byte)data1, (byte)data2));
                }
            }

            return (song.Select(Sorted).ToArray(), tempo > 0 ? tempo : 120);
        }
    }
}
