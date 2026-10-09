using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using NAudio.Midi;
using NAudio.Wave;
using Speech;

namespace piano
{
    // Turns keyboard and MIDI input into commands for the audio engine and keeps the state shown
    // on screen. Runs on the UI thread; the arranger and song commands are in MidiManager.Arranger.cs.
    public static partial class MidiManager
    {
        private static AudioEngine? engine;
        private static WaveOutEvent? waveOut;
        private static MidiIn? midiIn;

        private static string _currentRecPath = "";

        public static int BaseOctave { get; private set; } = 48;
        public static int Transpose { get; private set; } = 0;
        public static int CurrentInstrument { get; private set; } = 0;
        public static bool IsLayerActive { get; private set; } = false;
        public static int LayerInstrument { get; private set; } = 48;

        public static bool IsSustainHold { get; private set; } = false;
        public static int SustainLockMode { get; private set; } = 0;
        public static bool IsSustainLocked => SustainLockMode > 0;

        private static int ReverbLevel = 0;
        private static int ChorusLevel = 0;
        private static int ModulationLevel = 0;

        public static bool IsMetronomeOn { get; private set; } = false;

        private static Dictionary<Keys, int> activeNotes = new();

        public static int NoteVelocity { get; private set; } = 100;

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
                    MessageBox.Show(L.T($"Sound file not found: {soundFontPath}", $"Arquivo de som não encontrado: {soundFontPath}"));
                    Instruments.LoadDefault();
                    return;
                }

                engine = new AudioEngine(soundFontPath);
                Instruments.LoadFromSoundFont(engine.SoundFont);

                waveOut = new WaveOutEvent();
                waveOut.DesiredLatency = 50;
                waveOut.Init(engine);
                waveOut.Play();

                engine.SetController(91, ReverbLevel);
                engine.SetController(93, ChorusLevel);
                engine.SetController(1, ModulationLevel);
                engine.SetProgram(AudioEngine.LayerChannel, LayerInstrument);

                int startInstrument = 0;
                if (!Instruments.IsValid(0))
                    startInstrument = Instruments.GetFirstAvailableId();

                SetInstrument(startInstrument, silent: true);
                InitArranger();
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Error starting audio: ", "Erro ao iniciar áudio: ") + ex.Message);
                Instruments.LoadDefault();
            }
        }

        public static void Shutdown()
        {
            SaveArrangerSettings();

            midiIn?.Dispose();
            midiIn = null;
            waveOut?.Dispose();
            waveOut = null;
            // closes a recording in progress, otherwise its WAV header would be left unfinished
            engine?.Dispose();
        }

        private static void Announce(string message)
        {
            UI.ShowStatusTemp(message);
            Sp.Speak(message);
        }

        public static void ChangeVelocity(int amount)
        {
            NoteVelocity = Math.Clamp(NoteVelocity + amount, 10, 127);
            UI.UpdateDisplay();
            Sp.Speak(L.T($"Velocity {NoteVelocity}", $"Força {NoteVelocity}"));
        }

        public static void ToggleMetronome()
        {
            if (engine == null) return;

            if (IsMetronomeOn)
            {
                IsMetronomeOn = false;
                engine.SetMetronome(false);
                UI.ShowStatusTemp(L.T("Metronome Off", "Metrônomo Desligado"));
                Sp.Speak(L.T("Off", "Desligado"));
            }
            else
            {
                int newBpm = ShowBpmDialog();
                if (newBpm > 0)
                {
                    SetTempo(newBpm, silent: true);
                    IsMetronomeOn = true;
                    engine.SetMetronome(true);
                    UI.ShowStatusTemp(L.T($"Metronome On ({Tempo} BPM)", $"Metrônomo Ligado ({Tempo} BPM)"));
                    Sp.Speak(L.T($"Metronome {Tempo}", $"Metrônomo {Tempo}"));
                }
            }
        }

        private static int ShowBpmDialog()
        {
            using (Form prompt = new Form())
            {
                prompt.Width = 300; prompt.Height = 150;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = L.T("Set Up Metronome", "Configurar Metrônomo");
                prompt.StartPosition = FormStartPosition.CenterScreen;
                prompt.MaximizeBox = false; prompt.MinimizeBox = false;

                Label textLabel = new Label() { Left = 20, Top = 20, Text = L.T("Enter the BPM (e.g. 120):", "Digite o BPM (ex: 120):"), AutoSize = true };
                TextBox inputBox = new TextBox() { Left = 20, Top = 50, Width = 240, Text = Tempo.ToString() };
                Button confirmation = new Button() { Text = L.T("Start", "Iniciar"), Left = 160, Width = 100, Top = 80, DialogResult = DialogResult.OK };
                Button cancel = new Button() { Text = L.T("Cancel", "Cancelar"), Left = 50, Width = 100, Top = 80, DialogResult = DialogResult.Cancel };

                prompt.Controls.Add(textLabel); prompt.Controls.Add(inputBox); prompt.Controls.Add(confirmation); prompt.Controls.Add(cancel);
                prompt.AcceptButton = confirmation;
                prompt.CancelButton = cancel;
                prompt.Shown += (s, e) => { inputBox.Focus(); inputBox.SelectAll(); };

                if (prompt.ShowDialog() == DialogResult.OK)
                {
                    if (int.TryParse(inputBox.Text, out int val) && val >= MinTempo && val <= MaxTempo) return val;
                }
                return -1;
            }
        }

        public static void AdjustReverb(int amount) => AdjustEffect(ref ReverbLevel, 91, "Reverb", amount);
        public static void AdjustChorus(int amount) => AdjustEffect(ref ChorusLevel, 93, "Chorus", amount);
        public static void AdjustModulation(int amount) => AdjustEffect(ref ModulationLevel, 1, L.T("Modulation", "Modulação"), amount);

        private static void AdjustEffect(ref int level, int controller, string name, int amount)
        {
            if (engine == null) return;
            level = Math.Clamp(level + amount, 0, 127);
            engine.SetController(controller, level);
            int percent = (int)Math.Round((level / 127.0) * 100);
            UI.ShowStatusTemp($"{name}: {percent}%");
            Sp.Speak($"{name} {percent}");
        }

        public static void StartRecording(string filename)
        {
            _currentRecPath = filename;
            engine?.StartRecording(filename);
        }

        public static void StopRecording() => engine?.StopRecording();

        public static void AbortRecording()
        {
            StopRecording();
            try { if (File.Exists(_currentRecPath)) File.Delete(_currentRecPath); } catch { }
        }

        public static bool IsRecording() => engine?.IsRecording ?? false;

        public static void ConfigureInput(int inIndex)
        {
            try
            {
                midiIn?.Stop(); midiIn?.Dispose();
                midiIn = null;
                if (inIndex >= 0 && inIndex < MidiIn.NumberOfDevices)
                {
                    midiIn = new MidiIn(inIndex);
                    midiIn.MessageReceived += (s, e) => ProcessRawMidi(e.RawMessage);
                    midiIn.Start();
                }
            }
            catch { }
        }

        // Runs on the MIDI driver thread. Notes go straight to the engine, so a MIDI keyboard gets
        // the layer, the pedal modes, the arranger split and track recording like the PC keys do.
        private static void ProcessRawMidi(int rawMsg)
        {
            if (engine == null) return;

            int command = rawMsg & 0xF0;
            int data1 = (rawMsg >> 8) & 0x7F;
            int data2 = (rawMsg >> 16) & 0x7F;

            switch (command)
            {
                case 0x90 when data2 > 0: engine.KeyDown(data1, data2); break;
                case 0x80: case 0x90: engine.KeyUp(data1); break;
                case 0xB0 when data1 == 64: UI.RunOnUiThread(() => SetSustainHold(data2 >= 64)); break;
                case 0xC0: UI.RunOnUiThread(() => { if (Instruments.IsValid(data1)) SetInstrument(data1); }); break;
                case 0xB0: case 0xD0: case 0xE0: engine.SendChannelMessage(command, data1, data2); break;
            }
        }

        public static void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.Control)
            {
                // Ctrl is reserved for commands, so a shortcut never sounds a note
                if (!e.Alt && HandleCommandKey(e)) e.SuppressKeyPress = true;
                return;
            }

            // Alt + letter opens the menus
            if (e.Alt) return;

            if (e.KeyCode == Keys.Escape && IsStyleRecorderOpen) { CancelStyleRecorder(); return; }

            if (e.KeyCode == Keys.Space)
            {
                if (e.Shift)
                {
                    SustainLockMode = (SustainLockMode + 1) % 3;
                    engine?.SetSustainLock(SustainLockMode);
                    UI.UpdateDisplay();

                    string msg = SustainLockMode == 0 ? L.T("Pedal lock off", "Pedal fixo desligado") :
                                (SustainLockMode == 1 ? L.T("Pedal lock: Continuous", "Pedal fixo: Contínuo") : L.T("Pedal lock: Smart", "Pedal fixo: Inteligente"));
                    Announce(msg);
                }
                else if (IsSustainLocked)
                {
                    engine?.ReleaseSustained();
                }
                else
                {
                    SetSustainHold(true);
                }
                return;
            }

            if (e.KeyCode == Keys.F10)
            {
                ToggleLayer();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Shift && e.KeyCode == Keys.Up) { ChangeVelocity(10); return; }
            if (e.Shift && e.KeyCode == Keys.Down) { ChangeVelocity(-10); return; }

            if (e.Shift && e.KeyCode == Keys.Right) { ChangeLayerInstrument(1); return; }
            if (e.Shift && e.KeyCode == Keys.Left) { ChangeLayerInstrument(-1); return; }

            if (!e.Shift && e.KeyCode == Keys.Right) { ChangeInstrument(1); return; }
            if (!e.Shift && e.KeyCode == Keys.Left) { ChangeInstrument(-1); return; }

            if (!e.Shift && e.KeyCode == Keys.Up) { ChangeOctave(12); return; }
            if (!e.Shift && e.KeyCode == Keys.Down) { ChangeOctave(-12); return; }
            if (e.KeyCode == Keys.F1) { ChangeTranspose(-1); return; }
            if (e.KeyCode == Keys.F2) { ChangeTranspose(1); return; }
            if (e.KeyCode == Keys.F3) { AdjustReverb(-10); return; }
            if (e.KeyCode == Keys.F4) { AdjustReverb(10); return; }
            if (e.KeyCode == Keys.F5) { ToggleMetronome(); return; }
            if (e.KeyCode == Keys.F6) { AdjustChorus(-10); return; }
            if (e.KeyCode == Keys.F7) { AdjustChorus(10); return; }
            if (e.KeyCode == Keys.F8) { AdjustModulation(-10); return; }
            if (e.KeyCode == Keys.F9) { AdjustModulation(10); return; }
            if (e.KeyCode == Keys.F11) { ToggleArranger(e.Shift); return; }
            if (e.KeyCode == Keys.F12) { ArrangerFill(e.Shift); return; }

            if (!KeyMap.TryGetValue(e.KeyCode, out int noteOffset) || activeNotes.ContainsKey(e.KeyCode)) return;

            // for a drum part the keys are the GM drum kit, whatever the octave: Z kick, X snare, G hi-hat...
            int actualNote = DrumKeys ? FirstDrumNote + noteOffset : BaseOctave + Transpose + noteOffset;
            activeNotes[e.KeyCode] = actualNote;
            // Shift + a chord key is the minor chord
            engine?.KeyDown(actualNote, NoteVelocity, minor: e.Shift);
        }

        public static void OnKeyUp(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Space)
            {
                SetSustainHold(false);
                return;
            }

            if (activeNotes.TryGetValue(e.KeyCode, out int actualNote))
            {
                activeNotes.Remove(e.KeyCode);
                engine?.KeyUp(actualNote);
            }
        }

        // The window lost the keyboard (another app, a dialog, a menu): the key-up events will
        // never arrive, so let go of everything instead of leaving notes stuck.
        public static void ReleaseAllKeys()
        {
            activeNotes.Clear();
            engine?.ReleaseAllKeys();
            SetSustainHold(false);
        }

        private static void SetSustainHold(bool on)
        {
            if (IsSustainHold == on) return;
            IsSustainHold = on;
            engine?.SetSustainHold(on);
            UI.UpdateDisplay();
        }

        public static void ToggleLayer()
        {
            IsLayerActive = !IsLayerActive;
            engine?.SetLayer(IsLayerActive);
            UI.UpdateDisplay();
            Sp.Speak(IsLayerActive ? L.T("Layer on", "Camada ligada") : L.T("Layer off", "Camada desligada"));
        }

        public static void ChangeLayerInstrument(int delta)
        {
            if (!IsLayerActive) return;

            var ids = Instruments.Ids;
            if (ids.Length == 0) return;

            int idx = Array.IndexOf(ids, LayerInstrument);
            int next = (idx == -1) ? 0 : (idx + delta + ids.Length) % ids.Length;

            LayerInstrument = ids[next];
            engine?.SetProgram(AudioEngine.LayerChannel, LayerInstrument);

            UI.UpdateDisplay();
            Sp.Speak(L.T($"Layer {LayerInstrument}, {Instruments.NameOf(LayerInstrument)}", $"Camada {LayerInstrument}, {Instruments.NameOf(LayerInstrument)}"));
        }

        private static void HandleFavorites(int slot, bool isShift)
        {
            if (isShift)
            {
                int savedId = Config.Favorites[slot];
                if (Instruments.IsValid(savedId)) { SetInstrument(savedId); UI.ShowStatusTemp(L.T($"Loaded {slot}", $"Carregado {slot}")); Sp.Speak(L.T($"Loaded {slot}", $"Carregado {slot}")); }
                else { UI.ShowStatusTemp(L.T("Unavailable", "Indisponível")); Sp.Speak(L.T("Empty", "Vazio")); }
            }
            else
            {
                Config.Favorites[slot] = CurrentInstrument; Config.Save();
                Announce(L.T($"Saved {slot}", $"Salvo {slot}"));
            }
        }

        public static void ResetOctave() { BaseOctave = 48; Transpose = 0; UI.UpdateDisplay(); Sp.Speak(L.T("Reset", "Resetado")); }

        public static void ChangeInstrument(int delta)
        {
            var ids = Instruments.Ids;
            if (ids.Length == 0) return;
            int idx = Array.IndexOf(ids, CurrentInstrument);
            int next = (idx == -1) ? 0 : (idx + delta + ids.Length) % ids.Length;
            SetInstrument(ids[next]);
        }

        public static void SetInstrument(int id, bool silent = false)
        {
            if (!Instruments.IsValid(id) && Instruments.GM.Count > 0) id = Instruments.GetFirstAvailableId();
            CurrentInstrument = id;
            engine?.SetProgram(AudioEngine.MainChannel, CurrentInstrument);

            UI.UpdateDisplay();
            if (!silent) Sp.Speak($"{CurrentInstrument}, {Instruments.NameOf(CurrentInstrument)}");
        }

        private static void ChangeOctave(int val) { BaseOctave = Math.Clamp(BaseOctave + val, 0, 108); UI.UpdateDisplay(); Sp.Speak(L.T($"Octave {(BaseOctave / 12) - 1}", $"Oitava {(BaseOctave / 12) - 1}")); }
        private static void ChangeTranspose(int val) { Transpose = Math.Clamp(Transpose + val, -12, 12); UI.UpdateDisplay(); Sp.Speak($"Transp {Transpose}"); }
    }
}
