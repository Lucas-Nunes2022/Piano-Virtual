using System;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Midi;
using Wforms;
using Speech; 

namespace piano
{
    public static class UI
    {
        private static string _tempRecPath = "";
        private static bool _firstLoad = true;
        private static bool _isPianoScreen = true;

        public static void BuildMain()
        {
            _isPianoScreen = true;

            var form = Wf.Get<Form>("_Form");
            
            if (form != null)
            {
                form.Text = $"Piano Virtual v{Constantes.versao}";
                
                form.Controls.Clear();
                if (form.MainMenuStrip != null)
                {
                    form.MainMenuStrip.Dispose();
                    form.MainMenuStrip = null;
                }
            }

            Wf.menu(
                ("&Menu", new (string, Action)[] {
                    ("&Configurar...", OpenSettingsWindow),
                    ("&Oitava padrão", () => { MidiManager.ResetOctave(); Wf.msg("Oitava definida para a padrão!"); }),
                    ("-", () => {}), 
                    ("&Sair", () => Application.Exit())
                }),
                ("&Gravação", new (string, Action)[] {
                    ("&Gravar Performance...", ToggleRecording)
                }),
("&Ajuda", new (string, Action)[] {
    ("Ver &Atalhos", ShowShortcuts),
    ("-", () => {}),
    ("&Visite meu site", () => {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Constantes.site) { UseShellExecute = true }); } catch { }
    }),
    ("&código fonte", () => {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Constantes.github) { UseShellExecute = true }); } catch { }
    }),
    ("&Sobre", () => Wf.msg($"Piano Virtual v{Constantes.versao}.\nDesenvolvido por Lucas Nunes Costa.", "Sobre"))
})
            );

            Wf.vStack(() =>
            {
                Wf.panel(() => { }, p => { p.Height = 20; p.BorderStyle = BorderStyle.None; });

                Wf.label("Piano Virtual", l => {
                    l.Font = new Font("Segoe UI", 24, FontStyle.Bold);
                    l.ForeColor = Color.DarkSlateBlue;
                    l.TextAlign = ContentAlignment.MiddleCenter;
                    l.Dock = DockStyle.Top;
                });

                Wf.label("", "lblStatus", l => {
                    l.ForeColor = Color.Red;
                    l.Font = new Font("Segoe UI", 10, FontStyle.Bold);
                    l.TextAlign = ContentAlignment.MiddleCenter;
                    l.Height = 25;
                });

                Wf.panel(() => {
                    Wf.hStack(() => {
                        Wf.label("Instrumento: 000", "lbl_instr", l => StyleInfoLabel(l));
                        Wf.label("Oitava: 4", "lbl_octave", l => StyleInfoLabel(l));
                        Wf.label("Transp: 0", "lbl_transpose", l => StyleInfoLabel(l));
                    });
                }, p => { p.Padding = new Padding(20, 0, 20, 10); p.Height = 45; });

                Wf.label("PEDAL LIVRE", "lbl_pedal", l => {
                    l.Font = new Font("Segoe UI", 12, FontStyle.Bold);
                    l.ForeColor = Color.Gray;
                    l.TextAlign = ContentAlignment.MiddleCenter;
                    l.Padding = new Padding(0, 5, 0, 10);
                });

                Wf.label("Espaço: Pedal  |  F1/F2: Transpose  |  Ctrl + (Shift) + Nº: Favoritos", l => {
                    l.ForeColor = Color.DimGray;
                    l.TextAlign = ContentAlignment.MiddleCenter;
                    l.Dock = DockStyle.Bottom;
                    l.Padding = new Padding(0, 0, 0, 20);
                });
            });

            UpdateDisplay();
            
            if (_firstLoad) 
            {
                Sp.Speak($"Piano Virtual v{Constantes.versao} janela. Bem-vindo ao piano virtual. Utilize o menu ajuda para conhecer os atalhos de teclado.");
                _firstLoad = false;
            }

            if (form != null)
            {
                form.ActiveControl = null;
                form.Activate();
                form.Focus();
            }
        }

        private static void OpenSettingsWindow()
        {
            _isPianoScreen = false;
            _tempRecPath = Config.GetRecordingPath();
            var form = Wf.Get<Form>("_Form");
            
            Wf.wt("Configurações");

            form.Controls.Clear();
            if (form.MainMenuStrip != null) form.MainMenuStrip = null;

            Wf.vStack(() =>
            {
                Wf.label("Configurações", l => {
                    l.Font = new Font("Segoe UI", 18, FontStyle.Bold);
                    l.ForeColor = Color.DarkSlateBlue;
                    l.Margin = new Padding(0, 20, 0, 20);
                });

                Wf.group("Dispositivos", () =>
                {
                    Wf.hStack(() => {
                        Wf.label("Selecione o dispositivo de entrada:");
                        
                        Wf.combo(GetMidiInDevices(), "cb_in");

                        Wf.button("Atualizar lista de dispositivos", () => {
                            var cb = Wf.Get<ComboBox>("cb_in");
                            if(cb != null) {
                                cb.Items.Clear();
                                cb.Items.AddRange(GetMidiInDevices());
                                if(cb.Items.Count > 0) cb.SelectedIndex = 0;
                                Sp.Speak("Lista atualizada");
                            }
                        }, b => {
                            b.Width = 30; 
                            b.Height = 23;
                            b.Padding = new Padding(0);
                            b.TextAlign = ContentAlignment.MiddleCenter;
                            b.Font = new Font("Segoe UI", 10, FontStyle.Bold);
                        });
                    });
                });

                Wf.group("Gravações", () =>
                {
                    Wf.label("Pasta Padrão:");
                    Wf.label(_tempRecPath, "lbl_path", l => {
                        l.AutoEllipsis = true;
                        l.ForeColor = Color.Gray;
                        l.BorderStyle = BorderStyle.FixedSingle;
                        l.Padding = new Padding(5);
                        l.Width = 300;
                    });

                    Wf.button("Selecionar Pasta...", () => {
                        using var fbd = new FolderBrowserDialog();
                        fbd.SelectedPath = _tempRecPath;
                        if (fbd.ShowDialog() == DialogResult.OK)
                        {
                            _tempRecPath = fbd.SelectedPath;
                            Wf.Set("lbl_path", _tempRecPath);
                        }
                    });
                });

                Wf.panel(() => { }, p => p.Height = 20);

                Wf.hStack(() =>
                {
                    Wf.button("Aplicar", () => {
                        ApplySettings();
                        Wf.msg("Configurações salvas com sucesso!");
                        BuildMain();
                    }, b => {
                        b.BackColor = Color.LightGreen;
                        b.Width = 120;
                    });

                    Wf.button("Cancelar", () => {
                        BuildMain();
                    }, b => {
                        b.Width = 120;
                    });
                });

            }, p => {
                p.Padding = new Padding(40);
                p.Dock = DockStyle.Fill;
            });

            var cb = Wf.Get<Control>("cb_in");
            if (cb != null) cb.Select();
        }

        private static async void ToggleRecording()
        {
            if (MidiManager.IsRecording())
            {
                StopRecordingUI();
                UpdateMenuText("Parar Gravação", "&Gravar Performance...");
            }
            else
            {
                if (await StartRecordingUI())
                {
                    UpdateMenuText("&Gravar Performance...", "Parar Gravação");
                }
            }
        }

        private static async Task<bool> StartRecordingUI()
        {
            string savedPath = Config.RecordingPath;

            if (!string.IsNullOrWhiteSpace(savedPath) && System.IO.Directory.Exists(savedPath))
            {
                string fileName = $"Piano_Rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
                string fullPath = System.IO.Path.Combine(savedPath, fileName);

                MidiManager.StartRecording(fullPath);
                UpdateStatus("GRAVANDO...");
                
                await Task.Delay(500);
                Sp.Speak("Gravando");
                return true;
            }

            using var sfd = new SaveFileDialog();
            sfd.Filter = "Arquivo de Áudio WAV|*.wav";
            sfd.FileName = $"Piano_Rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            sfd.InitialDirectory = Config.GetRecordingPath();

            if (sfd.ShowDialog() == DialogResult.OK)
            {
                string? pasta = System.IO.Path.GetDirectoryName(sfd.FileName);
                
                if (!string.IsNullOrEmpty(pasta))
                {
                    Config.RecordingPath = pasta;
                    Config.Save();
                }

                MidiManager.StartRecording(sfd.FileName);
                UpdateStatus("GRAVANDO...");
                
                await Task.Delay(500);
                Sp.Speak("Gravando");
                return true;
            }
            return false;
        }

        private static void StopRecordingUI()
        {
            if (!MidiManager.IsRecording()) return;

            MidiManager.StopRecording();
            UpdateStatus("");
            Wf.msg("Gravação salva com sucesso!");
        }

        private static void UpdateMenuText(string currentText, string newText)
        {
            var form = Wf.Get<Form>("_Form");
            if (form != null && form.MainMenuStrip != null)
            {
                foreach (ToolStripMenuItem topItem in form.MainMenuStrip.Items)
                {
                    if (TryFindAndReplace(topItem, currentText, newText)) break;
                }
            }
        }

        private static bool TryFindAndReplace(ToolStripDropDownItem item, string target, string replacement)
        {
            string cleanItemText = (item.Text ?? "").Replace("&", "");
            string cleanTarget = target.Replace("&", "");

            if (cleanItemText == cleanTarget)
            {
                item.Text = replacement;
                return true;
            }

            if (item.DropDownItems != null)
            {
                foreach (ToolStripItem subItem in item.DropDownItems)
                {
                    if (subItem is ToolStripDropDownItem dropDownItem)
                    {
                        if (TryFindAndReplace(dropDownItem, target, replacement)) return true;
                    }
                    else if ((subItem.Text ?? "").Replace("&", "") == cleanTarget)
                    {
                        subItem.Text = replacement;
                        return true;
                    }
                }
            }
            return false;
        }

        public static async void ShowStatusTemp(string msg)
        {
            if (MidiManager.IsRecording()) return;
            UpdateStatus(msg);
            await Task.Delay(2000);
            if (!MidiManager.IsRecording()) UpdateStatus("");
        }

        private static void ShowShortcuts()
        {
            _isPianoScreen = false;
            var form = Wf.Get<Form>("_Form");
            
            Wf.wt("Atalhos do Teclado");

            form.Controls.Clear();
            if (form.MainMenuStrip != null) form.MainMenuStrip = null;

            ListBox? lbFocus = null;

            Wf.vStack(() => 
            {
                Wf.label("Lista de Comandos:", l => l.Font = new Font("Segoe UI", 12, FontStyle.Bold));

                Wf.panel(() => {}, p => {
                    p.AutoSize = false;
                    p.Size = new Size(380, 420); 
                    
                    var lb = new ListBox();
                    lb.Dock = DockStyle.Fill;
                    lb.Font = new Font("Segoe UI", 11);
                    lb.BorderStyle = BorderStyle.FixedSingle;
                    
                    lb.Items.AddRange(new object[] {
                        "--- NOTAS ---",
                        "Teclas de Z a M  : Oitava Central",
                        "Teclas de Q a P: Oitava Aguda",
                        "--- CONTROLES ---",
                        "Espaço          : Pedal Sustain",
                        "Setas Esquerda/Direita   : Instrumento Anterior/Próximo",
                        "Setas Cima/Baixo: Alterar Oitava",
                        "F1 / F2         : Transposição",
                        "--- FAVORITOS ---",
                        "Ctrl + números de 0 a 9      : Salvar instrumento como favorito",
                        "Ctrl+Shift+ números de 0 a 9 : Carregar instrumento favorito"
                    });

                    lbFocus = lb;
                    p.Controls.Add(lb);
                });

                Wf.button("Voltar", () => BuildMain(), b => {
                    b.Width = 120;
                    b.Height = 35;
                });

            }, p => {
                p.Padding = new Padding(40);
                p.Dock = DockStyle.Fill;
            });

            if (lbFocus != null) lbFocus.Select();
        }

        private static void StyleInfoLabel(Label l)
        {
            l.Font = new Font("Consolas", 10);
            l.AutoSize = true;
            l.Padding = new Padding(5);
            l.BorderStyle = BorderStyle.FixedSingle;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Margin = new Padding(5);
        }

        public static void UpdateDisplay()
        {
            try
            {
                var form = Wf.Get<Form>("_Form");
                if (form == null || form.IsDisposed) return;
                
                if (!form.Controls.ContainsKey("lbl_instr")) return;

                if (form.InvokeRequired) { form.Invoke((MethodInvoker)UpdateDisplay); return; }

                string instrName = Instruments.GM.ContainsKey(MidiManager.CurrentInstrument) 
                    ? Instruments.GM[MidiManager.CurrentInstrument] 
                    : "Unknown";
                
                if (instrName.Length > 20) instrName = instrName.Substring(0, 18) + "..";

                Wf.Set("lbl_instr", $"{MidiManager.CurrentInstrument:000}: {instrName}");
                Wf.Set("lbl_octave", $"Oitava: {(MidiManager.BaseOctave / 12) - 1}");
                Wf.Set("lbl_transpose", $"Transp: {MidiManager.Transpose:+#;-#;0}");

                var lblPedal = Wf.Get<Label>("lbl_pedal");
                if (lblPedal != null)
                {
                    if (MidiManager.IsSustainActive) {
                        lblPedal.Text = "PEDAL SUSTAIN";
                        lblPedal.ForeColor = Color.DarkRed;
                    } else {
                        lblPedal.Text = "PEDAL LIVRE";
                        lblPedal.ForeColor = Color.LightGray;
                    }
                }
            }
            catch { }
        }

        private static void UpdateStatus(string text)
        {
            var form = Wf.Get<Form>("_Form");
            if (form != null && form.Controls.ContainsKey("lblStatus"))
                form.Invoke((MethodInvoker)(() => Wf.Set("lblStatus", text)));
        }

        public static void ConfigureWindow(Form f)
        {
            f.KeyPreview = true;
            
            f.KeyDown += (s, e) => {
                if (_isPianoScreen) MidiManager.OnKeyDown(s, e);
            };
            
            f.KeyUp += (s, e) => {
                if (_isPianoScreen) MidiManager.OnKeyUp(s, e);
            };
        }

        private static string[] GetMidiInDevices() => 
            Enumerable.Range(0, MidiIn.NumberOfDevices)
            .Select(i => $"{i}: {MidiIn.DeviceInfo(i).ProductName}").ToArray();

        private static void ApplySettings()
        {
            int inIdx = ParseId(Wf.Get<string>("cb_in"));
            Config.MidiInputId = inIdx;
            Config.RecordingPath = _tempRecPath;
            Config.Save();
            MidiManager.ConfigureInput(inIdx);
        }

        private static int ParseId(string? txt)
        {
            if (string.IsNullOrEmpty(txt)) return 0;
            var part = txt.Split(new[] { ':', '-' })[0].Trim();
            return int.TryParse(part, out int id) ? id : 0;
        }
    }
}