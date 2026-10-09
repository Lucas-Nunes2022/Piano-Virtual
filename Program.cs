using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using Wforms;

namespace piano
{
    public static class Program
    {
        private const string MutexName = "PianoVirtual_App_Mutex_Instance";

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Config.Load();
            L.Init(Config.Language);

            using (Mutex mutex = new Mutex(true, MutexName, out bool createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(L.T("Virtual Piano is already open!", "O Piano Virtual já está aberto!"), L.T("Warning", "Aviso"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                Updater.BeginCheck();

                string appPath = AppContext.BaseDirectory;
                string[] sf2Files = Directory.GetFiles(appPath, "*.sf2");

                string selectedSoundFont = "";

                if (sf2Files.Length == 0)
                {
                    MessageBox.Show(
                        L.T("No sound file (.sf2) was found in the application folder.\n\nPlease add a SoundFont file (.sf2) to play.",
                            "Nenhum arquivo de som (.sf2) foi encontrado na pasta do aplicativo.\n\nPor favor, adicione um arquivo SoundFont (.sf2) para tocar."),
                        L.T("Error: Sound File Missing", "Erro: Falta Arquivo de Som"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                else if (sf2Files.Length == 1)
                {
                    selectedSoundFont = sf2Files[0];
                }
                else
                {
                    selectedSoundFont = ShowSelectionDialog(sf2Files);
                    if (string.IsNullOrEmpty(selectedSoundFont)) return;
                }

                MidiManager.Init(selectedSoundFont);
                MidiManager.ConfigureInput(Config.MidiInputId);

                Wf.InitApp(L.T($"Virtual Piano v{Constants.Version}", $"Piano Virtual v{Constants.Version}"));

                Wf.Run(
                    "",
                    UI.BuildMain,
                    (600, 350),
                    UI.ConfigureWindow
                );
            }
        }

        private static string ShowSelectionDialog(string[] filePaths)
        {
            string selectedPath = "";

            using (Form form = new Form())
            {
                form.Text = L.T("Choose the Sound", "Escolha o Som");
                form.Size = new Size(350, 180);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                Label lbl = new Label() { Left = 20, Top = 20, Text = L.T("Multiple files found.\nWhich one do you want to use?", "Múltiplos arquivos encontrados.\nQual você deseja usar?"), AutoSize = true };

                ComboBox cb = new ComboBox() { Left = 20, Top = 50, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };

                var filesDict = filePaths.ToDictionary(p => Path.GetFileName(p), p => p);
                cb.Items.AddRange(filesDict.Keys.ToArray());
                if (cb.Items.Count > 0) cb.SelectedIndex = 0;

                Button btnOk = new Button() { Text = L.T("Load", "Carregar"), Left = 210, Width = 100, Top = 90, DialogResult = DialogResult.OK };

                form.Controls.Add(lbl);
                form.Controls.Add(cb);
                form.Controls.Add(btnOk);
                form.AcceptButton = btnOk;

                if (form.ShowDialog() == DialogResult.OK)
                {
                    string chosenName = cb.SelectedItem?.ToString() ?? "";
                    if (filesDict.ContainsKey(chosenName))
                    {
                        selectedPath = filesDict[chosenName];
                    }
                }
            }

            return selectedPath;
        }
    }
}
