using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Drawing;
using Wforms;

namespace piano
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Config.Load();
            
            string appPath = AppDomain.CurrentDomain.BaseDirectory;
            string[] sf2Files = Directory.GetFiles(appPath, "*.sf2");
            
            string selectedSoundFont = "";

            if (sf2Files.Length == 0)
            {
                MessageBox.Show("Nenhum arquivo de som (.sf2) foi encontrado na pasta do aplicativo.\n\nPor favor, adicione um arquivo SoundFont (.sf2) para tocar.", 
                    "Erro: Falta Arquivo de Som", MessageBoxButtons.OK, MessageBoxIcon.Error);
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

            Wf.InitApp($"Piano Virtual v{Constantes.versao}");

            Wf.Run(
                "",
                UI.BuildMain,
                (600, 350),
                UI.ConfigureWindow
            );
        }

        private static string ShowSelectionDialog(string[] filePaths)
        {
            string selectedPath = "";

            using (Form form = new Form())
            {
                form.Text = "Escolha o Som";
                form.Size = new Size(350, 180);
                form.StartPosition = FormStartPosition.CenterScreen;
                form.FormBorderStyle = FormBorderStyle.FixedDialog;
                form.MaximizeBox = false;
                form.MinimizeBox = false;

                Label lbl = new Label() { Left = 20, Top = 20, Text = "Múltiplos arquivos encontrados.\nQual você deseja usar?", AutoSize = true };
                
                ComboBox cb = new ComboBox() { Left = 20, Top = 50, Width = 290, DropDownStyle = ComboBoxStyle.DropDownList };
                
                var filesDict = filePaths.ToDictionary(p => Path.GetFileName(p), p => p);
                cb.Items.AddRange(filesDict.Keys.ToArray());
                if (cb.Items.Count > 0) cb.SelectedIndex = 0;

                Button btnOk = new Button() { Text = "Carregar", Left = 210, Width = 100, Top = 90, DialogResult = DialogResult.OK };
                
                form.Controls.Add(lbl);
                form.Controls.Add(cb);
                form.Controls.Add(btnOk);
                form.AcceptButton = btnOk;

                if (form.ShowDialog() == DialogResult.OK)
                {
                    string nomeEscolhido = cb.SelectedItem?.ToString() ?? "";
                    if (filesDict.ContainsKey(nomeEscolhido))
                    {
                        selectedPath = filesDict[nomeEscolhido];
                    }
                }
            }

            return selectedPath;
        }
    }
}