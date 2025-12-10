using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private static HashSet<Keys> pressedKeys = new();
        private static HashSet<int> sustainedNotes = new();

        private static readonly Dictionary<Keys, int> KeyMap = new()
        {
            { Keys.Z, 0 },
            { Keys.X, 2 },
            { Keys.C, 4 },
            { Keys.V, 5 },
            { Keys.B, 7 },
            { Keys.N, 9 },
            { Keys.M, 11 },
            { Keys.Oemcomma, 12 },
            { Keys.OemPeriod, 14 },
            { Keys.OemQuestion, 16 },

            { Keys.S, 1 },
            { Keys.D, 3 },
            { Keys.G, 6 },
            { Keys.H, 8 },
            { Keys.J, 10 },
            { Keys.L, 13 },
            { Keys.Oem1, 15 },

            { Keys.Q, 17 },
            { Keys.W, 19 },
            { Keys.E, 21 },
            { Keys.R, 23 },
            { Keys.T, 24 },
            { Keys.Y, 26 },
            { Keys.U, 28 },
            { Keys.I, 29 },
            { Keys.O, 31 },
            { Keys.P, 33 },
            { Keys.OemOpenBrackets, 35 },
            { Keys.Oem6, 36 },
            { Keys.Return, 38 },

            { Keys.D2, 18 },
            { Keys.D3, 20 },
            { Keys.D4, 22 },
            { Keys.D6, 25 },
            { Keys.D7, 27 },
            { Keys.D9, 30 },
            { Keys.D0, 32 },
            { Keys.OemMinus, 34 },
            { Keys.Oemplus, 37 },

            { Keys.Back, 49 },
            { Keys.Delete, 52 },
            { Keys.End, 53 },
            { Keys.PageUp, 54 },
            { Keys.PageDown, 55 }
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

                int startInstrument = 0;

                if (!Instruments.IsValid(0))
                    startInstrument = Instruments.GetFirstAvailableId();

                SetInstrument(startInstrument, silent: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erro ao carregar SoundFont: " + ex.Message);
                Instruments.LoadDefault();
            }
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
            lock (recordLock)
            {
                isRecording = false;
                recorder?.Dispose();
                recorder = null;
            }

            try
            {
                if (!string.IsNullOrEmpty(_currentRecPath) && File.Exists(_currentRecPath))
                {
                    File.Delete(_currentRecPath);
                }
            }
            catch { }
        }

        public static bool IsRecording() => isRecording;

        public static void ConfigureInput(int inIndex)
        {
            try
            {
                midiIn?.Stop();
                midiIn?.Dispose();

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
                if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
                {
                    int slot = e.KeyCode - Keys.D0;
                    HandleFavorites(slot, e.Shift, e);
                    return;
                }
                else if (e.KeyCode >= Keys.NumPad0 && e.KeyCode <= Keys.NumPad9)
                {
                    int slot = e.KeyCode - Keys.NumPad0;
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

            if (!KeyMap.ContainsKey(e.KeyCode) || pressedKeys.Contains(e.KeyCode)) return;

            pressedKeys.Add(e.KeyCode);
            PlayNote(GetNoteValue(e.KeyCode));
        }

        private static void HandleFavorites(int slot, bool isShift, KeyEventArgs e)
        {
            if (isShift)
            {
                int savedId = Config.Favorites[slot];

                if (Instruments.IsValid(savedId))
                {
                    SetInstrument(savedId);
                    UI.ShowStatusTemp($"Carregado {slot}");
                    Sp.Speak($"Carregado {slot}");
                }
                else
                {
                    UI.ShowStatusTemp($"Instrumento {savedId} não disponível");
                    Sp.Speak("Indisponível");
                }
            }
            else
            {
                Config.Favorites[slot] = CurrentInstrument;
                Config.Save();
                UI.ShowStatusTemp($"Favorito {slot} salvo");
                Sp.Speak($"Favorito {slot} salvo");
            }

            e.SuppressKeyPress = true;
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

        private static int GetNoteValue(Keys k) => BaseOctave + Transpose + KeyMap[k];

        private static string GetInstrName(int id)
            => Instruments.GM.ContainsKey(id) ? Instruments.GM[id] : "Desconhecido";

        public static void ResetOctave()
        {
            BaseOctave = 48;
            Transpose = 0;
            UI.UpdateDisplay();
            Sp.Speak("Configurações resetadas");
        }

        public static void ChangeInstrument(int delta)
        {
            var availableIds = Instruments.GM.Keys.OrderBy(k => k).ToList();
            if (availableIds.Count == 0) return;

            int currentIndex = availableIds.IndexOf(CurrentInstrument);
            if (currentIndex == -1)
            {
                SetInstrument(availableIds[0]);
                return;
            }

            int nextIndex = currentIndex + delta;
            if (nextIndex >= availableIds.Count) nextIndex = 0;
            if (nextIndex < 0) nextIndex = availableIds.Count - 1;

            SetInstrument(availableIds[nextIndex]);
        }

        public static void SetInstrument(int id, bool silent = false)
        {
            if (!Instruments.IsValid(id))
            {
                if (Instruments.GM.Count > 0)
                    id = Instruments.GetFirstAvailableId();
                else return;
            }

            CurrentInstrument = id;
            synthesizer?.ProcessMidiMessage(0, 0xC0, CurrentInstrument, 0);
            UI.UpdateDisplay();

            if (!silent)
                Sp.Speak($"{CurrentInstrument}, {GetInstrName(CurrentInstrument)}");
        }

        private static void ChangeOctave(int semiTones)
        {
            BaseOctave = Math.Clamp(BaseOctave + semiTones, 0, 108);
            UI.UpdateDisplay();
            int oct = (BaseOctave / 12) - 1;
            Sp.Speak($"Oitava {oct}");
        }

        private static void ChangeTranspose(int semiTones)
        {
            Transpose = Math.Clamp(Transpose + semiTones, -12, 12);
            UI.UpdateDisplay();
            Sp.Speak($"Transposição {Transpose}");
        }

        private static void ToggleSustain(bool active)
        {
            IsSustainActive = active;
            UI.UpdateDisplay();

            if (!active)
            {
                foreach (var note in sustainedNotes)
                    synthesizer?.NoteOff(0, Math.Clamp(note, 0, 127));

                sustainedNotes.Clear();
            }
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
                l = new float[1024];
                r = new float[1024];
            }

            public int Read(float[] b, int o, int c)
            {
                int f = c / 2;

                if (l.Length < f)
                {
                    l = new float[f];
                    r = new float[f];
                }

                synth.Render(l.AsSpan(0, f), r.AsSpan(0, f));

                int idx = o;

                for (int i = 0; i < f; i++)
                {
                    b[idx++] = l[i];
                    b[idx++] = r[i];
                }

                if (MidiManager.isRecording && MidiManager.recorder != null)
                    lock (MidiManager.recordLock)
                        MidiManager.recorder?.WriteSamples(b, o, c);

                return c;
            }
        }
    }
}