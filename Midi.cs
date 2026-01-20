using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Midi;
using NAudio.Wave;
using MeltySynth;
using Speech;

namespace piano
{
    public static class MidiManager
    {
        private static Synthesizer? synthesizer;
        private static WaveOutEvent? waveOut;
        private static MidiIn? midiIn;

        private static WaveFileWriter? recorder;
        private static bool isRecording = false;
        private static string _currentRecPath = "";
        public static object recordLock = new object();

        public static int BaseOctave { get; private set; } = 48;
        public static int Transpose { get; private set; } = 0;
        public static int CurrentInstrument { get; private set; } = 0;
        public static bool IsSustainActive { get; private set; } = false;

        private static int ReverbLevel = 0;

        private static System.Timers.Timer? metronomeTimer;
        private static bool isMetronomeOn = false;
        private static int bpm = 120;
        private static int currentBeat = 0;

        private static float[]? metronomeHighBuffer;
        private static float[]? metronomeLowBuffer;
        private static WaveFormat? metronomeFormat;

        private static HashSet<Keys> pressedKeys = new();
        private static HashSet<int> sustainedNotes = new();

        private static readonly Dictionary<Keys, int> KeyMap = new()
        {
            { Keys.Z, 0 }, { Keys.X, 2 }, { Keys.C, 4 }, { Keys.V, 5 }, { Keys.B, 7 }, { Keys.N, 9 }, { Keys.M, 11 },
            { Keys.Oemcomma, 12 }, { Keys.OemPeriod, 14 }, { Keys.OemQuestion, 16 },
            { Keys.S, 1 }, { Keys.D, 3 }, { Keys.G, 6 }, { Keys.H, 8 }, { Keys.J, 10 }, { Keys.L, 13 }, { Keys.Oem1, 15 },
            { Keys.Q, 17 }, { Keys.W, 19 }, { Keys.E, 21 }, { Keys.R, 23 }, { Keys.T, 24 }, { Keys.Y, 26 }, { Keys.U, 28 },
            { Keys.I, 29 }, { Keys.O, 31 }, { Keys.P, 33 }, { Keys.OemOpenBrackets, 35 }, { Keys.Oem6, 36 }, { Keys.Return, 38 },
            { Keys.D2, 18 }, { Keys.D3, 20 }, { Keys.D4, 22 }, { Keys.D6, 25 }, { Keys.D7, 27 }, { Keys.D9, 30 }, { Keys.D0, 32 },
            { Keys.OemMinus, 34 }, { Keys.Oemplus, 37 },
            { Keys.Back, 49 }, { Keys.Delete, 52 }, { Keys.End, 53 }, { Keys.PageUp, 54 }, { Keys.PageDown, 55 }
        };

        public static void Init(string soundFontPath)
        {
            try
            {
                if (!File.Exists(soundFontPath))
                {
                    MessageBox.Show($"Arquivo de som não encontrado: {soundFontPath}");
                    Instruments.LoadDefault();
                    return;
                }

                synthesizer = new Synthesizer(soundFontPath, 44100);
                Instruments.LoadFromSoundFont(synthesizer.SoundFont);

                var sampleProvider = new MidiSampleProvider(synthesizer);
                waveOut = new WaveOutEvent();
                waveOut.DesiredLatency = 50;
                waveOut.Init(sampleProvider);
                waveOut.Play();

                LoadMetronomeSounds();
                SetupMetronomeTimer();

                int startInstrument = 0;
                if (!Instruments.IsValid(0))
                    startInstrument = Instruments.GetFirstAvailableId();

                SetInstrument(startInstrument, silent: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao iniciar áudio: " + ex.Message);
                Instruments.LoadDefault();
            }
        }

        private static void LoadMetronomeSounds()
        {
            try
            {
                if (File.Exists("1.wav")) metronomeHighBuffer = LoadWavToMemory("1.wav");
                if (File.Exists("2.wav")) metronomeLowBuffer = LoadWavToMemory("2.wav");
            }
            catch { /* Ignora erros de carregamento, usará fallback MIDI */ }
        }

        private static float[] LoadWavToMemory(string path)
        {
            using (var reader = new AudioFileReader(path))
            {
                metronomeFormat = reader.WaveFormat;
                var samples = new List<float>();
                var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
                int read;
                while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
                {
                    samples.AddRange(buffer.Take(read));
                }
                return samples.ToArray();
            }
        }

        private static void SetupMetronomeTimer()
        {
            if (metronomeTimer != null) { metronomeTimer.Stop(); metronomeTimer.Dispose(); }
            
            double interval = 60000.0 / bpm;
            metronomeTimer = new System.Timers.Timer(interval);
            metronomeTimer.Elapsed += (s, e) => PlayMetronomeTick();
            metronomeTimer.AutoReset = true;
        }

        private static void PlayMetronomeTick()
        {
            float[]? soundToPlay = (currentBeat == 0) ? metronomeHighBuffer : metronomeLowBuffer;

            if (soundToPlay != null)
            {
                PlayBufferFireAndForget(soundToPlay);
            }
            else
            {
                if (synthesizer != null)
                {
                    int note = (currentBeat == 0) ? 76 : 77;
                    synthesizer.NoteOn(9, note, 100);
                    Task.Delay(100).ContinueWith(_ => synthesizer.NoteOff(9, note));
                }
            }

            currentBeat++;
            if (currentBeat >= 4) currentBeat = 0;
        }

        private static void PlayBufferFireAndForget(float[] buffer)
        {
            Task.Run(() =>
            {
                try
                {
                    var provider = new BufferedWaveProvider(metronomeFormat ?? new WaveFormat(44100, 2));
                    provider.BufferDuration = TimeSpan.FromSeconds(2);
                    provider.DiscardOnBufferOverflow = true;

                    byte[] bytes = new byte[buffer.Length * 4];
                    Buffer.BlockCopy(buffer, 0, bytes, 0, bytes.Length);
                    provider.AddSamples(bytes, 0, bytes.Length);

                    using (var tempOut = new WaveOutEvent())
                    {
                        tempOut.Init(provider);
                        tempOut.Play();
                        int ms = (int)((buffer.Length / (float)(metronomeFormat?.Channels ?? 2) / (metronomeFormat?.SampleRate ?? 44100)) * 1000);
                        Thread.Sleep(ms + 100); 
                    }
                }
                catch { }
            });
        }

        public static void ToggleMetronome()
        {
            if (isMetronomeOn)
            {
                metronomeTimer?.Stop();
                isMetronomeOn = false;
                currentBeat = 0;
                UI.ShowStatusTemp("Metrônomo Desligado");
                Sp.Speak("Desligado");
            }
            else
            {
                int newBpm = ShowBpmDialog();
                if (newBpm > 0)
                {
                    bpm = newBpm;
                    SetupMetronomeTimer();
                    metronomeTimer?.Start();
                    isMetronomeOn = true;
                    currentBeat = 0;
                    UI.ShowStatusTemp($"Metrônomo Ligado ({bpm} BPM)");
                    Sp.Speak($"Metrônomo {bpm}");
                    PlayMetronomeTick();
                }
            }
        }

        private static int ShowBpmDialog()
        {
            using (Form prompt = new Form())
            {
                prompt.Width = 300; prompt.Height = 150;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = "Configurar Metrônomo";
                prompt.StartPosition = FormStartPosition.CenterScreen;
                prompt.MaximizeBox = false; prompt.MinimizeBox = false;

                Label textLabel = new Label() { Left = 20, Top = 20, Text = "Digite o BPM (ex: 120):", AutoSize = true };
                TextBox inputBox = new TextBox() { Left = 20, Top = 50, Width = 240, Text = bpm.ToString() };
                Button confirmation = new Button() { Text = "Iniciar", Left = 160, Width = 100, Top = 80, DialogResult = DialogResult.OK };

                prompt.Controls.Add(textLabel); prompt.Controls.Add(inputBox); prompt.Controls.Add(confirmation);
                prompt.AcceptButton = confirmation;
                prompt.Shown += (s, e) => { inputBox.Focus(); inputBox.SelectAll(); };

                if (prompt.ShowDialog() == DialogResult.OK)
                {
                    if (int.TryParse(inputBox.Text, out int val) && val > 0 && val < 500) return val;
                }
                return -1;
            }
        }

        public static void AdjustReverb(int amount)
        {
            if (synthesizer == null) return;
            ReverbLevel = Math.Clamp(ReverbLevel + amount, 0, 127);
            synthesizer.ProcessMidiMessage(0, 0xB0, 91, ReverbLevel);
            int percent = (int)Math.Round((ReverbLevel / 127.0) * 100);
            UI.ShowStatusTemp($"Reverb: {percent}%");
            Sp.Speak($"Reverb {percent}");
        }

        public static void StartRecording(string filename)
        {
            lock (recordLock)
            {
                _currentRecPath = filename;
                recorder = new WaveFileWriter(filename, WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));
                isRecording = true;
            }
        }

        public static void StopRecording()
        {
            lock (recordLock)
            {
                isRecording = false;
                recorder?.Dispose();
                recorder = null;
            }
        }

        public static void AbortRecording()
        {
            StopRecording();
            try { if (File.Exists(_currentRecPath)) File.Delete(_currentRecPath); } catch { }
        }

        public static bool IsRecording() => isRecording;

        public static void ConfigureInput(int inIndex)
        {
            try
            {
                midiIn?.Stop(); midiIn?.Dispose();
                if (inIndex >= 0 && inIndex < MidiIn.NumberOfDevices)
                {
                    midiIn = new MidiIn(inIndex);
                    midiIn.MessageReceived += (s, e) => ProcessRawMidi(e.RawMessage);
                    midiIn.Start();
                }
            }
            catch { }
        }

        private static void ProcessRawMidi(int rawMsg)
        {
            if (synthesizer == null) return;
            synthesizer.ProcessMidiMessage(rawMsg & 0xF, rawMsg & 0xF0, (rawMsg >> 8) & 0xFF, (rawMsg >> 16) & 0xFF);
        }

        public static void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                if ((e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) || (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9))
                {
                    int slot = (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9) ? e.KeyCode - Keys.D0 : e.KeyCode - Keys.NumPad0;
                    HandleFavorites(slot, e.Shift, e);
                    return;
                }
            }

            if (e.KeyCode == Keys.Space && !IsSustainActive) { ToggleSustain(true); return; }
            if (e.KeyCode == Keys.Right) { ChangeInstrument(1); return; }
            if (e.KeyCode == Keys.Left) { ChangeInstrument(-1); return; }
            if (e.KeyCode == Keys.Up) { ChangeOctave(12); return; }
            if (e.KeyCode == Keys.Down) { ChangeOctave(-12); return; }
            if (e.KeyCode == Keys.F1) { ChangeTranspose(-1); return; }
            if (e.KeyCode == Keys.F2) { ChangeTranspose(1); return; }
            if (e.KeyCode == Keys.F3) { AdjustReverb(-10); return; }
            if (e.KeyCode == Keys.F4) { AdjustReverb(10); return; }
            if (e.KeyCode == Keys.F5) { ToggleMetronome(); return; }

            if (!KeyMap.ContainsKey(e.KeyCode) || pressedKeys.Contains(e.KeyCode)) return;

            pressedKeys.Add(e.KeyCode);
            PlayNote(GetNoteValue(e.KeyCode));
        }

        public static void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space) { ToggleSustain(false); return; }
            if (pressedKeys.Contains(e.KeyCode))
            {
                pressedKeys.Remove(e.KeyCode);
                if (KeyMap.ContainsKey(e.KeyCode)) StopNote(GetNoteValue(e.KeyCode));
            }
        }

        private static void HandleFavorites(int slot, bool isShift, KeyEventArgs e)
        {
            if (isShift)
            {
                int savedId = Config.Favorites[slot];
                if (Instruments.IsValid(savedId)) { SetInstrument(savedId); UI.ShowStatusTemp($"Carregado {slot}"); Sp.Speak($"Carregado {slot}"); }
                else { UI.ShowStatusTemp("Indisponível"); Sp.Speak("Vazio"); }
            }
            else
            {
                Config.Favorites[slot] = CurrentInstrument; Config.Save();
                UI.ShowStatusTemp($"Salvo {slot}"); Sp.Speak($"Salvo {slot}");
            }
            e.SuppressKeyPress = true;
        }

        private static int GetNoteValue(Keys k) => BaseOctave + Transpose + KeyMap[k];
        private static string GetInstrName(int id) => Instruments.GM.ContainsKey(id) ? Instruments.GM[id] : "Desconhecido";

        public static void ResetOctave() { BaseOctave = 48; Transpose = 0; UI.UpdateDisplay(); Sp.Speak("Resetado"); }
        
        public static void ChangeInstrument(int delta)
        {
            var ids = Instruments.GM.Keys.OrderBy(k => k).ToList();
            if (ids.Count == 0) return;
            int idx = ids.IndexOf(CurrentInstrument);
            int next = (idx == -1) ? 0 : (idx + delta + ids.Count) % ids.Count;
            SetInstrument(ids[next]);
        }

        public static void SetInstrument(int id, bool silent = false)
        {
            if (!Instruments.IsValid(id) && Instruments.GM.Count > 0) id = Instruments.GetFirstAvailableId();
            CurrentInstrument = id;
            synthesizer?.ProcessMidiMessage(0, 0xC0, CurrentInstrument, 0);
            UI.UpdateDisplay();
            if (!silent) Sp.Speak($"{CurrentInstrument}, {GetInstrName(CurrentInstrument)}");
        }

        private static void ChangeOctave(int val) { BaseOctave = Math.Clamp(BaseOctave + val, 0, 108); UI.UpdateDisplay(); Sp.Speak($"Oitava {(BaseOctave / 12) - 1}"); }
        private static void ChangeTranspose(int val) { Transpose = Math.Clamp(Transpose + val, -12, 12); UI.UpdateDisplay(); Sp.Speak($"Transp {Transpose}"); }

        private static void ToggleSustain(bool active)
        {
            IsSustainActive = active; UI.UpdateDisplay();
            if (!active) { foreach (var n in sustainedNotes) synthesizer?.NoteOff(0, Math.Clamp(n, 0, 127)); sustainedNotes.Clear(); }
        }

        private static void PlayNote(int note)
        {
            if (sustainedNotes.Contains(note)) sustainedNotes.Remove(note);
            synthesizer?.NoteOn(0, Math.Clamp(note, 0, 127), 100);
        }

        private static void StopNote(int note)
        {
            if (IsSustainActive) sustainedNotes.Add(note);
            else synthesizer?.NoteOff(0, Math.Clamp(note, 0, 127));
        }

        private class MidiSampleProvider : ISampleProvider
        {
            private readonly Synthesizer synth;
            private float[] l, r;
            public WaveFormat WaveFormat { get; }

            public MidiSampleProvider(Synthesizer s)
            {
                synth = s;
                WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
                l = new float[1024]; r = new float[1024];
            }

            public int Read(float[] b, int o, int c)
            {
                int f = c / 2;
                if (l.Length < f) { l = new float[f]; r = new float[f]; }

                synth.Render(l.AsSpan(0, f), r.AsSpan(0, f));

                int idx = o;
                for (int i = 0; i < f; i++) { b[idx++] = l[i]; b[idx++] = r[i]; }

                if (MidiManager.isRecording && MidiManager.recorder != null)
                {
                    lock (MidiManager.recordLock) MidiManager.recorder?.WriteSamples(b, o, c);
                }
                return c;
            }
        }
    }
}