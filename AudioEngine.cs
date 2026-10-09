using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using MeltySynth;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace piano
{
    // Owns the synthesizers and everything that has to stay in time with the audio: the notes the
    // player presses, the metronome, the arranger and the song sequencer.
    //
    // All of that state belongs to the audio thread. Other threads never touch it directly: the
    // public methods only queue work with Post(), which runs at the start of the next audio
    // buffer. That keeps the synthesizer from being changed while it renders and lets the clock
    // count samples instead of relying on timers.
    public sealed class CapturedNote
    {
        public int Start, End, Note, Velocity;
    }

    public sealed class AudioEngine : ISampleProvider, IDisposable
    {
        public const int SampleRate = 44100;
        public const int TicksPerBeat = 480;
        public const int MainChannel = 0;
        public const int LayerChannel = 1;

        private const int BlockFrames = 64;

        private readonly Synthesizer live;
        private readonly Synthesizer songSynth;
        private readonly Synthesizer clickSynth;
        private readonly ConcurrentQueue<(long Time, Action Run)> inbox = new();
        private float[][] buffers = NewBuffers(2048);

        // player
        private readonly HashSet<int> heldNotes = new();
        private readonly HashSet<int> sustainedNotes = new();
        private readonly HashSet<int> layerNotes = new();
        private readonly HashSet<int> chordZone = new();
        private readonly HashSet<int> minorKeys = new();
        private readonly HashSet<int> chordNotes = new();
        private readonly int[] programs = { 0, 48 };
        private readonly SortedDictionary<int, int> controllers = new();
        private bool sustainHold, layerActive;
        private int sustainLock;
        private int splitPoint = 60;

        // style recorder: what the player performs is kept as plain notes for StyleDraft to convert
        private readonly List<CapturedNote> captured = new();
        private readonly Dictionary<int, CapturedNote> openCaptured = new();
        private bool drumKeys, capturing;
        private int captureLimit;
        private CapturedNote[]? captureResult;
        private volatile int captureEnd;

        // clock
        private double tickPosition;
        private double tempo = 120;
        private bool running, metronomeOn;
        private int lastBeat = int.MinValue;
        private int eventAge;
        private volatile int position;

        // metronome click
        private readonly float[]? highClick, lowClick;
        private float[]? clickSample;
        private int clickOffset, clickFrames;

        // WAV recording
        private readonly object recordLock = new();
        private WaveFileWriter? recorder;

        public AudioEngine(string soundFontPath)
        {
            var font = new SoundFont(soundFontPath);
            live = new Synthesizer(font, SampleRate);
            songSynth = new Synthesizer(font, SampleRate);
            clickSynth = new Synthesizer(font, new SynthesizerSettings(SampleRate) { EnableReverbAndChorus = false });

            live.ProcessMidiMessage(9, 0xB0, 10, 64); // Center pan for drums

            Arranger = new Arranger(this);
            Sequencer = new Sequencer(this, songSynth);

            highClick = LoadClick("1.wav");
            lowClick = LoadClick("2.wav");
        }

        public Arranger Arranger { get; }
        public Sequencer Sequencer { get; }
        public SoundFont SoundFont => live.SoundFont;
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);

        // Song position in ticks, negative during the count-in
        public int Position => position;
        public bool IsRecording => recorder != null;

        internal int Tick => (int)Math.Floor(tickPosition);

        // Tick at which the event being handled really happened. Events wait in the inbox for up
        // to one audio buffer, and that wait is subtracted so recorded notes keep their timing.
        internal int EventTick => Tick - eventAge;

        internal void Post(Action action) => inbox.Enqueue((Stopwatch.GetTimestamp(), action));

        // minor: in the chord zone, this key alone asks for the minor chord (Shift on the PC keyboard)
        public void KeyDown(int note, int velocity, bool minor = false) => Post(() => PressKey(note, velocity, minor));
        public void KeyUp(int note) => Post(() => ReleaseKey(note));
        public void ReleaseAllKeys() => Post(() => { foreach (int note in new List<int>(heldNotes)) ReleaseKey(note); ClearChordZone(); });
        public void SetSustainHold(bool on) => Post(() => { sustainHold = on; if (!on && sustainLock == 0) ReleaseSustainedNotes(); });
        public void SetSustainLock(int mode) => Post(() => { sustainLock = mode; if (mode == 0 && !sustainHold) ReleaseSustainedNotes(); });
        public void ReleaseSustained() => Post(ReleaseSustainedNotes);
        public void SetLayer(bool on) => Post(() => layerActive = on);
        public void SetProgram(int channel, int program) => Post(() => { programs[channel] = program; LiveOut(channel, 0xC0, program, 0); });
        public void SetController(int controller, int value) => Post(() => { controllers[controller] = value; ChannelMessage(0xB0, controller, value); });
        public void SendChannelMessage(int command, int data1, int data2) => Post(() => ChannelMessage(command, data1, data2));
        public void SetSplitPoint(int note) => Post(() => splitPoint = note);
        public void SetTempo(double bpm) => Post(() => tempo = bpm);
        public void SetMetronome(bool on) => Post(() => metronomeOn = on);

        // While on, the keys play the drum kit (GM drum notes) instead of the instrument
        public void SetDrumKeys(bool on) => Post(() => { foreach (int note in new List<int>(heldNotes)) ReleaseKey(note); drumKeys = on; });

        public bool Capturing => capturing;
        public int CaptureEnd => captureEnd;

        // Records what is played over the backing style, after one bar of count-in, for up to maxBars
        public void BeginCapture(Style backing, int maxBars) => Post(() =>
        {
            Interlocked.Exchange(ref captureResult, null);
            Sequencer.StopNow();
            captured.Clear();
            openCaptured.Clear();
            int barTicks = backing.BeatsPerBar * TicksPerBeat;
            captureLimit = maxBars * barTicks;
            ResetTransport(-barTicks);
            Arranger.StartPreview(backing);
            capturing = true;
        });

        public void EndCapture() => Post(FinishCapture);

        // The notes of the last capture, once; null while there is nothing new
        public CapturedNote[]? TakeCapture() => Interlocked.Exchange(ref captureResult, null);

        private void FinishCapture()
        {
            if (!capturing) return;

            int tick = Math.Max(Tick, 0);
            foreach (var note in openCaptured.Values) note.End = tick;
            openCaptured.Clear();

            capturing = false;
            Arranger.StopNow();
            captureEnd = tick;
            Interlocked.Exchange(ref captureResult, captured.ToArray());
        }

        private void Capture(int note, int velocity)
        {
            int tick = EventTick;
            // a note slightly ahead of the first beat still belongs to it; earlier ones are warm-up
            if (tick < -TicksPerBeat / 4) return;

            var entry = new CapturedNote { Start = Math.Max(tick, 0), End = Math.Max(tick, 0), Note = note, Velocity = velocity };
            captured.Add(entry);
            openCaptured[note] = entry;
        }

        private void PressKey(int note, int velocity, bool minor = false)
        {
            note = Math.Clamp(note, 0, 127);

            if (capturing) Capture(note, velocity);

            if (drumKeys)
            {
                heldNotes.Add(note);
                live.ProcessMidiMessage(Arranger.DrumChannel, 0x90, note, velocity);
                return;
            }

            if (Arranger.Playing && !Arranger.Preview && note < splitPoint)
            {
                // below the split point the keys choose the chord instead of sounding
                bool changed = chordZone.Add(note);
                if (minor) changed |= minorKeys.Add(note);
                if (changed)
                {
                    // a key pressed as minor counts as that key plus its minor third
                    chordNotes.Clear();
                    foreach (int key in chordZone)
                    {
                        chordNotes.Add(key);
                        if (minorKeys.Contains(key)) chordNotes.Add(key + 3);
                    }
                    Arranger.SetChordNotes(chordNotes);
                }
                return;
            }

            if (sustainLock == 2 && !sustainHold && heldNotes.Count == 0) ReleaseSustainedNotes();

            // Prevent note buildup by stopping the currently sustained instance
            if (sustainedNotes.Remove(note)) NoteOff(note);

            heldNotes.Add(note);
            LiveOut(MainChannel, 0x90, note, velocity);
            if (layerActive)
            {
                LiveOut(LayerChannel, 0x90, note, (int)(velocity * 0.8));
                layerNotes.Add(note);
            }
        }

        private void ReleaseKey(int note)
        {
            note = Math.Clamp(note, 0, 127);

            if (openCaptured.Remove(note, out var entry)) entry.End = Math.Max(EventTick, entry.Start);

            if (chordZone.Remove(note)) { minorKeys.Remove(note); return; }
            if (!heldNotes.Remove(note)) return;

            if (drumKeys)
            {
                live.ProcessMidiMessage(Arranger.DrumChannel, 0x80, note, 0);
                return;
            }

            if (sustainHold || sustainLock > 0) sustainedNotes.Add(note);
            else NoteOff(note);
        }

        private void NoteOff(int note)
        {
            LiveOut(MainChannel, 0x80, note, 0);
            if (layerNotes.Remove(note)) LiveOut(LayerChannel, 0x80, note, 0);
        }

        private void ReleaseSustainedNotes()
        {
            foreach (int note in sustainedNotes) NoteOff(note);
            sustainedNotes.Clear();
        }

        internal void ClearChordZone() { chordZone.Clear(); minorKeys.Clear(); }

        private void ChannelMessage(int command, int data1, int data2)
        {
            LiveOut(MainChannel, command, data1, data2);
            LiveOut(LayerChannel, command, data1, data2);
        }

        // Instruments and effects in use, sent again so that a new recording starts with them
        internal void SendSetup()
        {
            LiveOut(MainChannel, 0xC0, programs[MainChannel], 0);
            LiveOut(LayerChannel, 0xC0, programs[LayerChannel], 0);
            foreach (var controller in controllers) ChannelMessage(0xB0, controller.Key, controller.Value);
        }

        internal void LiveOut(int channel, int command, int data1, int data2)
        {
            live.ProcessMidiMessage(channel, command, data1, data2);
            Sequencer.Capture(channel, command, data1, data2);
        }

        internal void AccompOut(int channel, int command, int data1, int data2)
        {
            live.ProcessMidiMessage(channel, command, data1, data2);
            Sequencer.CaptureAccomp(channel, command, data1, data2);
        }

        internal void ResetTransport(int startTick)
        {
            tickPosition = startTick;
            lastBeat = int.MinValue;
        }

        private void UpdateRunning()
        {
            bool needed = metronomeOn || Arranger.Playing || Sequencer.Active || capturing;
            if (needed == running) return;

            running = needed;
            if (!running) ResetTransport(0);
        }

        private void RunInbox()
        {
            long now = Stopwatch.GetTimestamp();
            double ticksPerSecond = tempo * TicksPerBeat / 60.0;

            while (inbox.TryDequeue(out var item))
            {
                double age = Math.Clamp((now - item.Time) / (double)Stopwatch.Frequency, 0, 0.05);
                eventAge = running ? (int)(age * ticksPerSecond) : 0;
                item.Run();
            }

            eventAge = 0;
            UpdateRunning();
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            if (buffers[0].Length < frames) buffers = NewBuffers(frames);
            float[] left = buffers[0], right = buffers[1], songLeft = buffers[2], songRight = buffers[3], clickLeft = buffers[4], clickRight = buffers[5];

            RunInbox();

            for (int done = 0; done < frames;)
            {
                int n = Math.Min(BlockFrames, frames - done);

                if (running)
                {
                    int tick = Tick;
                    PlayMetronome(tick);
                    Sequencer.Advance(tick);
                    Arranger.Advance(tick);
                    if (capturing && tick >= captureLimit) FinishCapture();
                    UpdateRunning();
                }

                live.Render(left.AsSpan(done, n), right.AsSpan(done, n));

                if (Sequencer.Active || songSynth.ActiveVoiceCount > 0) songSynth.Render(songLeft.AsSpan(done, n), songRight.AsSpan(done, n));
                else { songLeft.AsSpan(done, n).Clear(); songRight.AsSpan(done, n).Clear(); }

                RenderClick(clickLeft.AsSpan(done, n), clickRight.AsSpan(done, n));

                if (running) tickPosition += n * tempo * TicksPerBeat / (60.0 * SampleRate);
                done += n;
            }

            for (int i = 0, o = offset; i < frames; i++)
            {
                buffer[o++] = left[i] + songLeft[i];
                buffer[o++] = right[i] + songRight[i];
            }

            if (recorder != null)
            {
                lock (recordLock) recorder?.WriteSamples(buffer, offset, frames * 2);
            }

            // the metronome is mixed in after the recording tap: it is heard but never recorded
            for (int i = 0, o = offset; i < frames; i++)
            {
                buffer[o++] += clickLeft[i];
                buffer[o++] += clickRight[i];
            }

            position = Tick;
            return count;
        }

        private static float[][] NewBuffers(int frames)
        {
            var result = new float[6][];
            for (int i = 0; i < result.Length; i++) result[i] = new float[frames];
            return result;
        }

        private void PlayMetronome(int tick)
        {
            int beat = (int)Math.Floor((double)tick / TicksPerBeat);
            if (beat == lastBeat) return;
            lastBeat = beat;

            // the count-in before a recording always clicks, and so does a style being recorded
            if (!metronomeOn && tick >= 0 && !capturing) return;

            int firstBeat = Arranger.Playing ? Arranger.StartBeat : 0;
            int beatsPerBar = Arranger.BeatsPerBar;
            bool accent = ((beat - firstBeat) % beatsPerBar + beatsPerBar) % beatsPerBar == 0;

            float[]? sample = accent ? highClick : lowClick;
            if (sample != null)
            {
                clickSample = sample;
                clickOffset = 0;
                return;
            }

            clickSynth.NoteOffAll(true);
            clickSynth.NoteOn(9, accent ? 76 : 77, 100);
            clickFrames = SampleRate / 2;
        }

        private void RenderClick(Span<float> left, Span<float> right)
        {
            if (clickFrames > 0)
            {
                clickSynth.Render(left, right);
                clickFrames -= left.Length;
            }
            else
            {
                left.Clear();
                right.Clear();
            }

            if (clickSample == null) return;

            for (int i = 0; i < left.Length && clickOffset + 1 < clickSample.Length; i++)
            {
                left[i] += clickSample[clickOffset++];
                right[i] += clickSample[clickOffset++];
            }
            if (clickOffset + 1 >= clickSample.Length) clickSample = null;
        }

        // Optional custom metronome sounds next to the executable, converted to the engine format
        private static float[]? LoadClick(string fileName)
        {
            try
            {
                string path = Path.Combine(AppContext.BaseDirectory, fileName);
                if (!File.Exists(path)) return null;

                using var reader = new AudioFileReader(path);
                ISampleProvider source = reader;
                if (source.WaveFormat.Channels == 1) source = new MonoToStereoSampleProvider(source);
                if (source.WaveFormat.Channels != 2) return null;
                if (source.WaveFormat.SampleRate != SampleRate) source = new WdlResamplingSampleProvider(source, SampleRate);

                var samples = new List<float>();
                var chunk = new float[8192];
                int read;
                while ((read = source.Read(chunk, 0, chunk.Length)) > 0)
                    samples.AddRange(new ArraySegment<float>(chunk, 0, read));
                return samples.ToArray();
            }
            catch
            {
                return null;
            }
        }

        public void StartRecording(string fileName)
        {
            lock (recordLock) recorder = new WaveFileWriter(fileName, WaveFormat);
        }

        public void StopRecording()
        {
            lock (recordLock)
            {
                recorder?.Dispose();
                recorder = null;
            }
        }

        public void Dispose() => StopRecording();
    }
}
