using System;
using System.IO;
using System.Windows.Forms;
using Speech;

namespace piano
{
    // Style recorder: create an accompaniment style by playing its parts, one at a time, over a
    // C chord. While it is open the song shortcuts (Ctrl+R, Ctrl+P, Ctrl+T, Ctrl+S) work on the
    // style instead.
    public static partial class MidiManager
    {
        private const int FirstDrumNote = 36;

        private static StyleDraft? draft;
        private static string draftPath = "";
        private static int draftSlot;
        private static int recordingSlot = -1;

        public static bool IsStyleRecorderOpen => draft != null;

        // The PC keys play the drum kit while a drum part is selected
        private static bool DrumKeys => draft != null && StyleDraft.IsDrumSlot(draftSlot);

        // fromCurrent: start from a copy of the selected style and replace only some parts
        public static void OpenStyleRecorder(bool fromCurrent)
        {
            if (engine == null || draft != null) return;

            // a user style is re-recorded in place; anything else becomes a new style
            bool inPlace = fromCurrent && !CurrentStyle.BuiltIn && CurrentStyle.FilePath.Length > 0;
            string name = CurrentStyle.Name;

            if (!inPlace)
            {
                string suggestion = L.T("My style", "Meu estilo");
                for (int i = 2; File.Exists(StylePath(suggestion)) || FindStyle(suggestion) != null; i++)
                    suggestion = L.T($"My style {i}", $"Meu estilo {i}");

                string? typed = PromptText(L.T("New Style", "Novo Estilo"), L.T("Name of the style:", "Nome do estilo:"), suggestion);
                if (typed == null) return;

                name = typed.Trim();
                if (name.Length == 0 || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || FindStyle(name) != null || File.Exists(StylePath(name)))
                {
                    MessageBox.Show(L.T("Choose a name that is not in use and has no special characters.", "Escolha um nome que não esteja em uso e não tenha caracteres especiais."));
                    return;
                }
            }

            engine.Sequencer.Stop();
            engine.Arranger.Stop();
            ReleaseAllKeys();

            draft = fromCurrent
                ? StyleDraft.From(CurrentStyle, name)
                : new StyleDraft(name, CurrentStyle.BeatsPerBar, CurrentStyle.StepsPerBeat);
            draftPath = inPlace ? CurrentStyle.FilePath : StylePath(name);
            draftSlot = 0;
            recordingSlot = -1;
            engine.SetDrumKeys(true);

            UI.RebuildMain();
            Sp.Speak(L.T(
                $"Style recorder, {name}. {draft.DescribeSlot(0)}. The keys now play the drum kit. Control R records, Control T goes to the next part, Control P listens, Control S saves, Escape cancels.",
                $"Gravador de estilos, {name}. {draft.DescribeSlot(0)}. As teclas agora tocam a bateria. Control R grava, Control T vai para a próxima parte, Control P ouve, Control S salva, Escape cancela."));
        }

        private static string? PromptText(string title, string label, string initial)
        {
            using (Form prompt = new Form())
            {
                prompt.Width = 340; prompt.Height = 150;
                prompt.FormBorderStyle = FormBorderStyle.FixedDialog;
                prompt.Text = title;
                prompt.StartPosition = FormStartPosition.CenterScreen;
                prompt.MaximizeBox = false; prompt.MinimizeBox = false;

                Label textLabel = new Label() { Left = 20, Top = 20, Text = label, AutoSize = true };
                TextBox inputBox = new TextBox() { Left = 20, Top = 50, Width = 280, Text = initial };
                Button confirmation = new Button() { Text = "OK", Left = 200, Width = 100, Top = 80, DialogResult = DialogResult.OK };
                Button cancel = new Button() { Text = L.T("Cancel", "Cancelar"), Left = 90, Width = 100, Top = 80, DialogResult = DialogResult.Cancel };

                prompt.Controls.Add(textLabel); prompt.Controls.Add(inputBox); prompt.Controls.Add(confirmation); prompt.Controls.Add(cancel);
                prompt.AcceptButton = confirmation;
                prompt.CancelButton = cancel;
                prompt.Shown += (s, e) => { inputBox.Focus(); inputBox.SelectAll(); };

                return prompt.ShowDialog() == DialogResult.OK ? inputBox.Text : null;
            }
        }

        private static bool HandleStyleRecorderKey(KeyEventArgs e)
        {
            switch (e.KeyCode)
            {
                case Keys.R: ToggleStyleRecording(); return true;
                case Keys.P: ToggleStylePreview(solo: e.Shift); return true;
                case Keys.T: SelectStyleSlot(e.Shift ? -1 : 1); return true;
                case Keys.Delete: ClearStyleSlot(); return true;
                case Keys.Z: UndoStyleRecording(); return true;
                case Keys.L: ToggleLastTakePreview(); return true;
                case Keys.S: SaveStyleDraft(); return true;
                case Keys.Up: ChangeTempo(e.Shift ? 1 : 5); return true;
                case Keys.Down: ChangeTempo(e.Shift ? -1 : -5); return true;
                // everything else Ctrl does (songs, favorites, changing style) waits until the recorder is closed
                default: return true;
            }
        }

        private static bool StyleBusy => engine != null && (engine.Capturing || recordingSlot >= 0);

        public static void SelectStyleSlot(int delta)
        {
            if (engine == null || draft == null || StyleBusy) return;

            engine.Arranger.Stop();
            ReleaseAllKeys();
            draftSlot = (draftSlot + delta + StyleDraft.SlotCount) % StyleDraft.SlotCount;
            engine.SetDrumKeys(DrumKeys);
            Announce(draft.DescribeSlot(draftSlot));
        }

        public static void ToggleStyleRecording()
        {
            if (engine == null || draft == null) return;

            if (StyleBusy)
            {
                engine.EndCapture();
                return;
            }

            ReleaseAllKeys();
            engine.TakeCapture();
            recordingSlot = draftSlot;
            // drums are layered over what is there; an instrument part is replaced, so it is left out
            engine.BeginCapture(draft.Backing(draftSlot, withoutSlot: true), StyleDraft.MaxBars);
            Announce(L.T($"Recording {StyleDraft.SlotName(draftSlot)}", $"Gravando {StyleDraft.SlotName(draftSlot)}"));
        }

        // solo: only the selected part, without the rest of the style
        public static void ToggleStylePreview(bool solo = false)
        {
            if (engine == null || draft == null || StyleBusy) return;

            if (engine.Arranger.Playing) engine.Arranger.Stop();
            else engine.Arranger.PlayPreview(solo ? draft.Solo(draftSlot) : draft.Backing(draftSlot, withoutSlot: false));
        }

        // Plays only what the last recording added: the snare just played, without the hi-hat from before
        public static void ToggleLastTakePreview()
        {
            if (engine == null || draft == null || StyleBusy) return;

            if (engine.Arranger.Playing) engine.Arranger.Stop();
            else if (draft.LastTake == null) Announce(L.T("No recording to listen to", "Nenhuma gravação para ouvir"));
            else engine.Arranger.PlayPreview(draft.LastTake);
        }

        public static void ClearStyleSlot()
        {
            if (engine == null || draft == null || StyleBusy) return;

            engine.Arranger.Stop();
            draft.Clear(draftSlot);
            Announce(L.T("Erased. ", "Apagado. ") + draft.DescribeSlot(draftSlot));
        }

        // Takes back the last recording only: a wrong snare take goes away and the hi-hat recorded before it stays
        public static void UndoStyleRecording()
        {
            if (engine == null || draft == null || StyleBusy) return;

            engine.Arranger.Stop();
            int slot = draft.Undo();
            if (slot < 0)
            {
                Announce(L.T("Nothing to undo", "Nada para desfazer"));
                return;
            }

            ReleaseAllKeys();
            draftSlot = slot;
            engine.SetDrumKeys(DrumKeys);
            Announce(L.T("Undone. ", "Desfeito. ") + draft.DescribeSlot(slot));
        }

        public static void ClearStyleDraft()
        {
            if (engine == null || draft == null || StyleBusy) return;
            if (!Confirm(L.T("Erase every part of this style and start over?", "Apagar todas as partes deste estilo e recomeçar?"))) return;

            engine.Arranger.Stop();
            for (int slot = 0; slot < StyleDraft.SlotCount; slot++) draft.Clear(slot);
            Announce(L.T("Everything erased. ", "Tudo apagado. ") + draft.DescribeSlot(draftSlot));
        }

        public static void SaveStyleDraft()
        {
            if (engine == null || draft == null || StyleBusy) return;

            string text = draft.ToText(Tempo);
            try
            {
                Styles.Parse(text, draft.Name, false);
            }
            catch (FormatException)
            {
                Announce(L.T("Record at least one part before saving", "Grave pelo menos uma parte antes de salvar"));
                return;
            }

            try
            {
                Directory.CreateDirectory(Styles.UserFolder);
                File.WriteAllText(draftPath, text.Replace("\n", Environment.NewLine));
            }
            catch (Exception ex)
            {
                MessageBox.Show(L.T("Could not save the style: ", "Não foi possível salvar o estilo: ") + ex.Message);
                return;
            }

            string name = draft.Name;
            int tempo = Tempo;
            CloseStyleRecorder();

            Styles.Load();
            SetStyle(FindStyle(name) ?? Styles.All[0], silent: true);
            SetTempo(tempo, silent: true);
            Announce(L.T($"Style {name} saved. F11 starts it.", $"Estilo {name} salvo. F11 liga."));
        }

        public static void CancelStyleRecorder()
        {
            if (draft == null) return;
            if (!Confirm(L.T("Leave the style recorder without saving?", "Sair do gravador de estilos sem salvar?"))) return;

            CloseStyleRecorder();
            Announce(L.T("Style recorder closed", "Gravador de estilos fechado"));
        }

        private static void CloseStyleRecorder()
        {
            engine?.EndCapture();
            engine?.Arranger.Stop();
            ReleaseAllKeys();
            engine?.SetDrumKeys(false);
            engine?.TakeCapture();

            draft = null;
            recordingSlot = -1;
            UI.RebuildMain();
        }

        // A recording ends when the player stops it or after StyleDraft.MaxBars bars
        private static void PollStyleRecorder()
        {
            if (engine == null || draft == null || recordingSlot < 0) return;

            var notes = engine.TakeCapture();
            if (notes == null) return;

            int slot = recordingSlot;
            recordingSlot = -1;

            int bars = draft.Apply(slot, notes, engine.CaptureEnd, CurrentInstrument);
            string name = StyleDraft.SlotName(slot);
            Announce(bars == 0 ? L.T($"{name}: nothing recorded", $"{name}: nada gravado")
                   : bars == 1 ? L.T($"{name}: 1 bar recorded", $"{name}: 1 compasso gravado")
                   : L.T($"{name}: {bars} bars recorded", $"{name}: {bars} compassos gravados"));
        }

        public static string StyleRecorderStatus()
        {
            if (draft == null) return "";

            string state = StyleBusy ? L.T("RECORDING", "GRAVANDO") : draft.DescribeSlot(draftSlot);
            return $"{L.T("STYLE RECORDER", "GRAVADOR DE ESTILOS")}: {draft.Name}  |  {(StyleBusy ? StyleDraft.SlotName(draftSlot) + ", " : "")}{state}  |  "
                 + L.T("Ctrl+R record, Ctrl+T next part, Ctrl+P listen, Ctrl+Shift+P this part only, Ctrl+L last recording only, Ctrl+Z undo last recording, Ctrl+Del erase, Ctrl+S save, Esc cancel",
                       "Ctrl+R grava, Ctrl+T próxima parte, Ctrl+P ouve, Ctrl+Shift+P só esta parte, Ctrl+L só a última gravação, Ctrl+Z desfaz a última gravação, Ctrl+Del apaga, Ctrl+S salva, Esc cancela");
        }
    }
}
