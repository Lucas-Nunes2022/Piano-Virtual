using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Speech;

namespace piano
{
    // Arranger (automatic accompaniment) and song (multitrack recorder) commands
    public static partial class MidiManager
    {
        public const int MinTempo = 20, MaxTempo = 300;
        private const int MinSplit = 36, MaxSplit = 96;

        private static readonly bool[] trackMuted = new bool[Sequencer.TrackCount];
        private static int lastSongState;
        private static bool stopRequested;

        public static int Tempo { get; private set; } = 120;
        public static Style CurrentStyle { get; private set; } = Styles.All[0];
        public static int SplitPoint { get; private set; } = 60;
        public static int AccompVolume { get; private set; } = 80;
        public static int SelectedTrack { get; private set; } = 0;

        public static bool IsArrangerOn => engine?.Arranger.Playing ?? false;

        private static void InitArranger()
        {
            if (engine == null) return;

            var errors = Styles.Load();
            if (errors.Count > 0) ShowStyleErrors(errors);

            CurrentStyle = FindStyle(Config.Style) ?? Styles.All[0];
            engine.Arranger.SetStyle(CurrentStyle);
            engine.Arranger.SetVolume(AccompVolume * 127 / 100);

            SplitPoint = Math.Clamp(Config.SplitPoint, MinSplit, MaxSplit);
            engine.SetSplitPoint(SplitPoint);

            SetTempo(Config.Tempo > 0 ? Config.Tempo : CurrentStyle.Tempo, silent: true);
        }

        private static void SaveArrangerSettings()
        {
            Config.Style = CurrentStyle.Name;
            Config.Tempo = Tempo;
            Config.SplitPoint = SplitPoint;
            Config.Save();
        }

        private static Style? FindStyle(string name) => Styles.All.FirstOrDefault(s => s.Name == name);

        private static void ShowStyleErrors(List<string> errors)
        {
            MessageBox.Show(
                L.T("These style files have errors and were not loaded:", "Estes arquivos de estilo têm erros e não foram carregados:")
                    + "\n\n" + string.Join("\n", errors),
                L.T("Styles", "Estilos"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private static bool HandleCommandKey(KeyEventArgs e)
        {
            if (IsStyleRecorderOpen) return HandleStyleRecorderKey(e);

            switch (e.KeyCode)
            {
                case >= Keys.D0 and <= Keys.D9: HandleFavorites(e.KeyCode - Keys.D0, e.Shift); return true;
                case >= Keys.NumPad0 and <= Keys.NumPad9: HandleFavorites(e.KeyCode - Keys.NumPad0, e.Shift); return true;

                case Keys.Right: if (e.Shift) ChangeSplitPoint(1); else ChangeStyle(1); return true;
                case Keys.Left: if (e.Shift) ChangeSplitPoint(-1); else ChangeStyle(-1); return true;
                case Keys.Up: ChangeTempo(e.Shift ? 1 : 5); return true;
                case Keys.Down: ChangeTempo(e.Shift ? -1 : -5); return true;
                case Keys.PageUp: ChangeAccompVolume(10); return true;
                case Keys.PageDown: ChangeAccompVolume(-10); return true;

                case Keys.R: ToggleSongRecording(); return true;
                case Keys.P: TogglePlayback(); return true;
                case Keys.T: SelectTrack(e.Shift ? -1 : 1); return true;
                case Keys.M: ToggleTrackMute(); return true;
                case Keys.Q: QuantizeTrack(); return true;
                case Keys.N: NewSong(); return true;
                case Keys.O: OpenSong(); return true;
                case Keys.S: SaveSong(); return true;

                default: return false;
            }
        }

        // ---------- arranger ----------

        // endingOrIntro: start with a drum intro, or finish with an ending instead of stopping at once
        public static void ToggleArranger(bool endingOrIntro)
        {
            if (engine == null) return;

            // inside the style recorder F11 auditions the style being made
            if (IsStyleRecorderOpen) { ToggleStylePreview(); return; }

            if (engine.Arranger.Playing)
            {
                if (endingOrIntro) { engine.Arranger.End(); Announce(L.T("Ending", "Finalização")); }
                else { engine.Arranger.Stop(); Announce(L.T("Arranger off", "Arranjador desligado")); }
            }
            else
            {
                engine.Arranger.Start(endingOrIntro);
                Announce(endingOrIntro ? L.T("Arranger on, intro", "Arranjador ligado, introdução") : L.T("Arranger on", "Arranjador ligado"));
            }
        }

        public static void ArrangerFill(bool switchVariation)
        {
            if (engine == null || IsStyleRecorderOpen) return;

            engine.Arranger.Fill(switchVariation);
            if (switchVariation)
            {
                string next = engine.Arranger.Variation == 0 ? "B" : "A";
                Announce(L.T($"Variation {next}", $"Variação {next}"));
            }
            else if (engine.Arranger.Playing)
            {
                UI.ShowStatusTemp(L.T("Fill", "Virada"));
            }
        }

        // A, F and K are the keys of the middle row that play no note. While the arranger plays
        // they are its buttons, so a fill-in does not take a hand off the keys. Shift changes
        // nothing here: it may be down for a minor chord.
        private static readonly HashSet<Keys> heldArrangerKeys = new();

        private static bool HandleArrangerKey(Keys key)
        {
            if (key != Keys.A && key != Keys.F && key != Keys.K) return false;
            if (engine == null || !engine.Arranger.Playing || IsStyleRecorderOpen) return false;

            // a key held down repeats, and the repeats would switch the variation back and forth
            if (!heldArrangerKeys.Add(key)) return true;

            switch (key)
            {
                case Keys.F: ArrangerFill(false); break;
                case Keys.A: ArrangerFill(true); break;
                default: engine.Arranger.End(); Announce(L.T("Ending", "Finalização")); break;
            }
            return true;
        }

        public static void ChangeStyle(int delta)
        {
            var all = Styles.All;
            int index = 0;
            for (int i = 0; i < all.Count; i++) if (all[i] == CurrentStyle) index = i;
            SetStyle(all[(index + delta + all.Count) % all.Count]);
        }

        public static void SetStyle(Style style, bool silent = false)
        {
            if (engine == null || IsStyleRecorderOpen) return;

            // While it plays the style changes on the next bar and keeps the current tempo
            bool busy = engine.Arranger.Playing || engine.Sequencer.Active;
            CurrentStyle = style;
            engine.Arranger.SetStyle(style);
            if (!busy) SetTempo(style.Tempo, silent: true);

            if (!silent) Announce(L.T($"Style {style.DisplayName}", $"Estilo {style.DisplayName}"));
        }

        public static void ReloadStyles()
        {
            if (IsStyleRecorderOpen) return;

            var errors = Styles.Load();
            SetStyle(FindStyle(CurrentStyle.Name) ?? Styles.All[0], silent: true);
            UI.RebuildMain();

            if (errors.Count > 0) ShowStyleErrors(errors);
            Announce(L.T($"{Styles.All.Count} styles loaded", $"{Styles.All.Count} estilos carregados"));
        }

        // Writes the current style to the user folder as a commented text file and opens it for editing
        public static void NewStyleFromCurrent()
        {
            try
            {
                Directory.CreateDirectory(Styles.UserFolder);

                string baseName = L.T("My style", "Meu estilo");
                string name = baseName;
                for (int i = 2; File.Exists(StylePath(name)) || FindStyle(name) != null; i++) name = $"{baseName} {i}";

                var body = CurrentStyle.Source.Replace("\r", "").Split('\n')
                    .Where(line => !line.TrimStart().StartsWith("name", StringComparison.OrdinalIgnoreCase));
                string text = Styles.FormatGuide + "\nname = " + name + "\n" + string.Join("\n", body);

                File.WriteAllText(StylePath(name), text.Replace("\n", Environment.NewLine));
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{StylePath(name)}\"") { UseShellExecute = true });

                Announce(L.T(
                    $"Created {name}, a copy of {CurrentStyle.DisplayName}. Edit it, save, then choose Reload styles.",
                    $"Criado {name}, uma cópia de {CurrentStyle.DisplayName}. Edite, salve e escolha Recarregar estilos."));
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Could not create the style: ", "Não foi possível criar o estilo: ") + ex.Message);
            }
        }

        private static string StylePath(string name) => Path.Combine(Styles.UserFolder, name + Styles.FileExtension);

        public static void OpenStylesFolder()
        {
            try
            {
                Directory.CreateDirectory(Styles.UserFolder);
                Process.Start(new ProcessStartInfo(Styles.UserFolder) { UseShellExecute = true });
            }
            catch { }
        }

        public static void ChangeTempo(int delta) => SetTempo(Tempo + delta);

        public static void SetTempo(int bpm, bool silent = false)
        {
            Tempo = Math.Clamp(bpm, MinTempo, MaxTempo);
            engine?.SetTempo(Tempo);
            UI.UpdateDisplay();
            if (!silent) Sp.Speak(L.T($"Tempo {Tempo}", $"Andamento {Tempo}"));
        }

        public static void ChangeSplitPoint(int delta)
        {
            SplitPoint = Math.Clamp(SplitPoint + delta, MinSplit, MaxSplit);
            engine?.SetSplitPoint(SplitPoint);
            Announce(L.T($"Split point {Chord.NoteName(SplitPoint)}", $"Ponto de divisão {Chord.NoteName(SplitPoint)}"));
        }

        public static void ChangeAccompVolume(int delta)
        {
            AccompVolume = Math.Clamp(AccompVolume + delta, 0, 100);
            engine?.Arranger.SetVolume(AccompVolume * 127 / 100);
            Announce(L.T($"Accompaniment volume {AccompVolume}%", $"Volume do acompanhamento {AccompVolume}%"));
        }

        public static string ArrangerStatus()
        {
            if (engine == null) return "";

            string variation = engine.Arranger.Variation == 0 ? "A" : "B";
            string chord = engine.Arranger.ChordName;
            if (chord.Length == 0) chord = "-";

            return $"{L.T("Style", "Estilo")}: {CurrentStyle.DisplayName} {variation}  |  {Tempo} BPM  |  "
                 + $"{L.T("Split", "Divisão")}: {Chord.NoteName(SplitPoint)}  |  {L.T("Chord", "Acorde")}: {chord}";
        }

        // ---------- song ----------

        private static string TrackName(int track) =>
            track == Sequencer.AccompTrack
                ? L.T("Accompaniment track", "Pista de acompanhamento")
                : L.T($"Track {track + 1}", $"Pista {track + 1}");

        private static bool IsTrackEmpty(int track) => engine == null || engine.Sequencer.Tracks[track].Length == 0;

        private static string DescribeTrack(int track)
        {
            string text = TrackName(track) + ", " + (IsTrackEmpty(track) ? L.T("empty", "vazia") : L.T("recorded", "gravada"));
            if (trackMuted[track]) text += ", " + L.T("muted", "sem som");
            return text;
        }

        public static void SelectTrack(int delta)
        {
            SelectedTrack = (SelectedTrack + delta + Sequencer.TrackCount) % Sequencer.TrackCount;
            Announce(DescribeTrack(SelectedTrack));
        }

        public static void ToggleSongRecording()
        {
            if (engine == null || IsStyleRecorderOpen) return;

            if (engine.Sequencer.State == Sequencer.Recording)
            {
                stopRequested = true;
                engine.Sequencer.Stop();
                Announce(L.T("Track recording stopped", "Gravação da pista finalizada"));
                return;
            }

            if (SelectedTrack == Sequencer.AccompTrack)
            {
                Announce(L.T("Choose a track from 1 to 6 to record", "Escolha uma pista de 1 a 6 para gravar"));
                return;
            }

            // Whatever is held belongs to the moment before the take
            ReleaseAllKeys();
            engine.Sequencer.Record(SelectedTrack);
            Announce(L.T($"Recording {TrackName(SelectedTrack)}", $"Gravando {TrackName(SelectedTrack)}"));
        }

        public static void TogglePlayback()
        {
            if (engine == null || IsStyleRecorderOpen) return;

            if (engine.Sequencer.Active)
            {
                bool wasRecording = engine.Sequencer.State == Sequencer.Recording;
                stopRequested = true;
                engine.Sequencer.Stop();
                Announce(wasRecording ? L.T("Track recording stopped", "Gravação da pista finalizada") : L.T("Stopped", "Parado"));
            }
            else if (engine.Sequencer.IsEmpty)
            {
                Announce(L.T("Nothing recorded yet", "Nada gravado ainda"));
            }
            else
            {
                engine.Sequencer.Play();
                Announce(L.T("Playing", "Tocando"));
            }
        }

        public static void ToggleTrackMute()
        {
            if (engine == null) return;

            trackMuted[SelectedTrack] = !trackMuted[SelectedTrack];
            engine.Sequencer.SetMute(SelectedTrack, trackMuted[SelectedTrack]);
            Announce(TrackName(SelectedTrack) + ", " + (trackMuted[SelectedTrack] ? L.T("muted", "sem som") : L.T("sound on", "com som")));
        }

        public static void ClearTrack()
        {
            if (engine == null) return;

            if (IsTrackEmpty(SelectedTrack))
            {
                Announce(DescribeTrack(SelectedTrack));
                return;
            }

            string name = TrackName(SelectedTrack);
            if (!Confirm(L.T($"Erase everything recorded on {name}?", $"Apagar tudo que foi gravado em {name}?"))) return;

            engine.Sequencer.Clear(SelectedTrack);
            Announce(L.T($"{name} cleared", $"{name} apagada"));
        }

        private static readonly (string En, string Pt, int Ticks)[] QuantizeGrids =
        {
            ("Quarter notes (1/4)", "Semínimas (1/4)", 480),
            ("Eighth notes (1/8)", "Colcheias (1/8)", 240),
            ("Sixteenth notes (1/16)", "Semicolcheias (1/16)", 120),
            ("Eighth-note triplets (swing)", "Tercinas de colcheia (suingue)", 160),
            ("Sixteenth-note triplets", "Tercinas de semicolcheia", 80),
            ("Thirty-second notes (1/32)", "Fusas (1/32)", 60)
        };

        private static (int Track, SongEvent[] Events)? quantizeUndo;

        // Snaps the notes of the selected track to a time grid, like "quantize" in a DAW
        public static void QuantizeTrack()
        {
            if (engine == null || IsStyleRecorderOpen) return;

            if (IsTrackEmpty(SelectedTrack))
            {
                Announce(DescribeTrack(SelectedTrack));
                return;
            }

            var (grid, strength) = PromptQuantize();
            if (grid <= 0) return;

            var original = engine.Sequencer.Tracks[SelectedTrack];
            quantizeUndo = (SelectedTrack, original);
            engine.Sequencer.SetTrack(SelectedTrack, Sequencer.Quantize(original, grid, strength));
            Announce(L.T($"{TrackName(SelectedTrack)} quantized", $"{TrackName(SelectedTrack)} quantizada"));
        }

        public static void UndoQuantize()
        {
            if (engine == null || IsStyleRecorderOpen) return;

            if (quantizeUndo == null)
            {
                Announce(L.T("Nothing to undo", "Nada para desfazer"));
                return;
            }

            var (track, events) = quantizeUndo.Value;
            quantizeUndo = null;
            engine.Sequencer.SetTrack(track, events);
            Announce(L.T($"{TrackName(track)} back to how it was played", $"{TrackName(track)} de volta a como foi tocada"));
        }

        private static readonly int[] QuantizeStrengths = { 100, 75, 50, 25 };
        private static int lastQuantizeStrength;

        private static (int Grid, double Strength) PromptQuantize()
        {
            using (Form prompt = new Form())
            {
                prompt.Width = 340; prompt.Height = 215;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = L.T($"Quantize {TrackName(SelectedTrack)}", $"Quantizar {TrackName(SelectedTrack)}");
                prompt.StartPosition = FormStartPosition.CenterScreen;
                prompt.MaximizeBox = false; prompt.MinimizeBox = false;

                Label textLabel = new Label() { Left = 20, Top = 20, Text = L.T("Move the notes to the nearest:", "Ajustar as notas para as mais próximas:"), AutoSize = true };
                ComboBox grids = new ComboBox() { Left = 20, Top = 50, Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (var grid in QuantizeGrids) grids.Items.Add(L.T(grid.En, grid.Pt));
                // swing and shuffle styles are played in triplets
                grids.SelectedIndex = CurrentStyle.StepsPerBeat % 3 == 0 ? 3 : 2;

                // below 100% the notes only move part of the way, which keeps some of the human feel
                Label strengthLabel = new Label() { Left = 20, Top = 85, Text = L.T("Strength:", "Força:"), AutoSize = true };
                ComboBox strengths = new ComboBox() { Left = 20, Top = 110, Width = 280, DropDownStyle = ComboBoxStyle.DropDownList };
                foreach (int percent in QuantizeStrengths)
                    strengths.Items.Add(percent == 100 ? L.T("100% (exactly on the grid)", "100% (exatamente na grade)") : $"{percent}%");
                strengths.SelectedIndex = lastQuantizeStrength;

                Button confirmation = new Button() { Text = L.T("Quantize", "Quantizar"), Left = 200, Width = 100, Top = 142, DialogResult = DialogResult.OK };
                Button cancel = new Button() { Text = L.T("Cancel", "Cancelar"), Left = 90, Width = 100, Top = 142, DialogResult = DialogResult.Cancel };

                // added in reading order, which is also the Tab order
                prompt.Controls.Add(textLabel); prompt.Controls.Add(grids); prompt.Controls.Add(strengthLabel); prompt.Controls.Add(strengths);
                prompt.Controls.Add(confirmation); prompt.Controls.Add(cancel);
                prompt.AcceptButton = confirmation;
                prompt.CancelButton = cancel;

                if (prompt.ShowDialog() != DialogResult.OK) return (0, 1);

                lastQuantizeStrength = strengths.SelectedIndex;
                return (QuantizeGrids[grids.SelectedIndex].Ticks, QuantizeStrengths[strengths.SelectedIndex] / 100.0);
            }
        }

        public static void NewSong()
        {
            if (engine == null) return;
            if (!engine.Sequencer.IsEmpty && !Confirm(L.T("Discard the current song and start a new one?", "Descartar a música atual e começar uma nova?"))) return;

            LoadSong(Sequencer.EmptySong());
            Announce(L.T("New song", "Nova música"));
        }

        private static void LoadSong(SongEvent[][] song)
        {
            quantizeUndo = null;
            engine!.Sequencer.Load(song);
            for (int i = 0; i < trackMuted.Length; i++)
            {
                trackMuted[i] = false;
                engine.Sequencer.SetMute(i, false);
            }
            SelectedTrack = 0;
        }

        public static void OpenSong()
        {
            if (engine == null) return;
            if (!engine.Sequencer.IsEmpty && !Confirm(L.T("Discard the current song and open another one?", "Descartar a música atual e abrir outra?"))) return;

            using var dialog = new OpenFileDialog();
            dialog.Filter = L.T("MIDI file|*.mid;*.midi", "Arquivo MIDI|*.mid;*.midi");
            dialog.InitialDirectory = Config.GetRecordingPath();
            if (dialog.ShowDialog() != DialogResult.OK) return;

            try
            {
                var (song, tempo) = Sequencer.LoadMidi(dialog.FileName);
                LoadSong(song);
                SetTempo((int)Math.Round(tempo), silent: true);
                Announce(L.T("Song loaded", "Música carregada"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Could not open the MIDI file: ", "Não foi possível abrir o arquivo MIDI: ") + ex.Message);
            }
        }

        public static void SaveSong()
        {
            if (engine == null) return;

            if (engine.Sequencer.IsEmpty)
            {
                Announce(L.T("Nothing recorded yet", "Nada gravado ainda"));
                return;
            }

            using var dialog = new SaveFileDialog();
            dialog.Filter = L.T("MIDI file|*.mid", "Arquivo MIDI|*.mid");
            dialog.FileName = $"Piano_Song_{DateTime.Now:yyyyMMdd_HHmmss}.mid";
            dialog.InitialDirectory = Config.GetRecordingPath();
            if (dialog.ShowDialog() != DialogResult.OK) return;

            try
            {
                Sequencer.SaveMidi(dialog.FileName, engine.Sequencer.Tracks, Tempo, CurrentStyle.BeatsPerBar);
                Announce(L.T("Song saved", "Música salva"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Could not save the MIDI file: ", "Não foi possível salvar o arquivo MIDI: ") + ex.Message);
            }
        }

        private static bool Confirm(string question) =>
            MessageBox.Show(question, L.T("Confirm", "Confirmar"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

        public static string SongStatus()
        {
            if (engine == null) return "";
            if (IsStyleRecorderOpen) return StyleRecorderStatus();

            int state = engine.Sequencer.State;
            string status = state == Sequencer.Recording ? L.T("RECORDING", "GRAVANDO")
                          : state == Sequencer.Playing ? L.T("Playing", "Tocando")
                          : L.T("Stopped", "Parado");

            if (state != Sequencer.Stopped)
            {
                int position = engine.Position;
                int barTicks = CurrentStyle.BeatsPerBar * AudioEngine.TicksPerBeat;
                status += "  |  " + (position < 0
                    ? L.T("Count-in", "Contagem")
                    : L.T($"Bar {position / barTicks + 1}", $"Compasso {position / barTicks + 1}"));
            }

            return DescribeTrack(SelectedTrack) + "  |  " + status;
        }

        // Called a few times per second: the song can stop on its own when it reaches the end
        public static void Poll()
        {
            if (engine == null) return;

            int state = engine.Sequencer.State;
            if (state == Sequencer.Stopped && lastSongState == Sequencer.Playing && !stopRequested)
                Announce(L.T("End of song", "Fim da música"));

            if (state == Sequencer.Stopped) stopRequested = false;
            lastSongState = state;

            PollStyleRecorder();
        }
    }
}
