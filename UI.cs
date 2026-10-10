using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
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
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, IntPtr ProcessId);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool BringWindowToTop(IntPtr hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern void SwitchToThisWindow(IntPtr hWnd, bool fAltTab);

        private static void ForceForeground(Form form)
        {
            try
            {
                IntPtr handle = form.Handle;
                uint foreThread = GetWindowThreadProcessId(GetForegroundWindow(), IntPtr.Zero);
                uint appThread = GetCurrentThreadId();

                if (foreThread != appThread && foreThread != 0)
                {
                    AttachThreadInput(foreThread, appThread, true);
                    BringWindowToTop(handle);
                    SetForegroundWindow(handle);
                    AttachThreadInput(foreThread, appThread, false);
                }
                else
                {
                    BringWindowToTop(handle);
                    SetForegroundWindow(handle);
                }

                form.Activate();
                form.Focus();
            }
            catch { }
        }

        // The screens are rebuilt every time the user moves between them, so the fonts are shared
        // instead of being created (and leaked) on each rebuild.
        private static readonly Font TitleFont = new Font("Segoe UI", 24, FontStyle.Bold);
        private static readonly Font HeadingFont = new Font("Segoe UI", 18, FontStyle.Bold);
        private static readonly Font PedalFont = new Font("Segoe UI", 12, FontStyle.Bold);
        private static readonly Font BoldFont = new Font("Segoe UI", 10, FontStyle.Bold);
        private static readonly Font InfoFont = new Font("Consolas", 10);

        private static string _tempRecPath = "";
        private static bool _firstLoad = true;
        private static bool _isPianoScreen = true;
        private static int _statusVersion;

        private static Form? _form;
        private static Label? _lblStatus, _lblInstr, _lblOctave, _lblTranspose, _lblVelocity, _lblPedal, _lblArranger, _lblSong;
        private static ToolStripItem? _miRecord, _miCancelRecording;

        private static string RecordText => L.T("&Record Performance...", "&Gravar Performance...");
        private static string StopRecordText => L.T("Stop Recording", "Parar Gravação");
        private static string CancelRecordText => L.T("&Cancel Recording", "&Cancelar Gravação");
        private static string QuantizeText => L.T("&Quantize Recordings to", "&Quantizar Gravações em");

        private static void ClearFormControls(Form form)
        {
            while (form.Controls.Count > 0)
            {
                Control c = form.Controls[0];
                form.Controls.RemoveAt(0);
                c.Dispose();
            }

            _lblStatus = _lblInstr = _lblOctave = _lblTranspose = _lblVelocity = _lblPedal = _lblArranger = _lblSong = null;
            _miRecord = _miCancelRecording = null;
        }

        private static void OpenLink(string url)
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }

        public static void BuildMain()
        {
            _isPianoScreen = true;

            var form = Wf.Get<Form>("_Form");
            _form = form;

            if (form != null)
            {
                form.Text = L.T($"Virtual Piano v{Constants.Version}", $"Piano Virtual v{Constants.Version}");

                ClearFormControls(form);

                if (form.MainMenuStrip != null)
                {
                    form.MainMenuStrip.Dispose();
                    form.MainMenuStrip = null;
                }
            }

            // A tab separates the name of a command from its keyboard shortcut
            var arranger = new List<(string, Action)>
            {
                (L.T("&Start / Stop\tF11", "&Ligar / Desligar\tF11"), () => MidiManager.ToggleArranger(false)),
                (L.T("Start with &Intro / Ending\tShift+F11", "Ligar com &Introdução / Finalização\tShift+F11"), () => MidiManager.ToggleArranger(true)),
                (L.T("&Fill-in\tF or F12", "&Virada\tF ou F12"), () => MidiManager.ArrangerFill(false)),
                (L.T("Switch &Variation A/B\tA or Shift+F12", "Trocar V&ariação A/B\tA ou Shift+F12"), () => MidiManager.ArrangerFill(true)),
                ("-", () => { }),
                (L.T("&Next Style\tCtrl+Right", "&Próximo Estilo\tCtrl+Direita"), () => MidiManager.ChangeStyle(1)),
                (L.T("&Previous Style\tCtrl+Left", "Estilo A&nterior\tCtrl+Esquerda"), () => MidiManager.ChangeStyle(-1)),
                (L.T("Tempo &Up\tCtrl+Up", "A&umentar Andamento\tCtrl+Cima"), () => MidiManager.ChangeTempo(5)),
                (L.T("Tempo &Down\tCtrl+Down", "&Diminuir Andamento\tCtrl+Baixo"), () => MidiManager.ChangeTempo(-5)),
                (L.T("Accompaniment &Louder\tCtrl+PageUp", "Acompanhamento Mais Al&to\tCtrl+PageUp"), () => MidiManager.ChangeAccompVolume(10)),
                (L.T("Accompaniment &Quieter\tCtrl+PageDown", "Acompanhamento Mais &Baixo\tCtrl+PageDown"), () => MidiManager.ChangeAccompVolume(-10)),
                (L.T("Split Point Hi&gher\tCtrl+Shift+Right", "Ponto de Divisão Mais A&gudo\tCtrl+Shift+Direita"), () => MidiManager.ChangeSplitPoint(1)),
                (L.T("Split Point Lo&wer\tCtrl+Shift+Left", "Ponto de Divisão Mais Gra&ve\tCtrl+Shift+Esquerda"), () => MidiManager.ChangeSplitPoint(-1)),
                ("-", () => { }),
                (L.T("Record a New Style by Pla&ying...", "Gravar um Novo Estilo &Tocando..."), () => MidiManager.OpenStyleRecorder(false)),
                (L.T("Re-record Parts of the Current St&yle...", "Regravar Partes do Estilo At&ual..."), () => MidiManager.OpenStyleRecorder(true)),
                (L.T("&Edit a Copy of the Current Style as Text...", "Editar uma &Cópia do Estilo Atual como Texto..."), MidiManager.NewStyleFromCurrent),
                (L.T("&Open Styles Folder", "Abrir Pasta de &Estilos"), MidiManager.OpenStylesFolder),
                (L.T("&Reload Styles", "&Recarregar Estilos"), MidiManager.ReloadStyles),
                ("-", () => { })
            };
            foreach (var style in Styles.All)
                arranger.Add((L.T("Style: ", "Estilo: ") + style.DisplayName, () => MidiManager.SetStyle(style)));

            var menus = new List<(string, (string, Action)[])>
            {
                ("&Menu", new (string, Action)[] {
                    (L.T("&Settings...", "&Configurar..."), OpenSettingsWindow),
                    (L.T("&Default octave", "&Oitava padrão"), () => { MidiManager.ResetOctave(); Wf.msg(L.T("Octave set back to the default!", "Oitava definida para a padrão!")); }),
                    ("-", () => { }),
                    (L.T("E&xit", "&Sair"), () => Application.Exit())
                }),
                (L.T("&Arranger", "A&rranjador"), arranger.ToArray()),
                (L.T("&Song", "Mú&sica"), new (string, Action)[] {
                    (L.T("&Record / Stop Recording Track\tCtrl+R", "&Gravar / Parar Gravação da Pista\tCtrl+R"), MidiManager.ToggleSongRecording),
                    (L.T("&Play / Stop\tCtrl+P", "&Tocar / Parar\tCtrl+P"), MidiManager.TogglePlayback),
                    ("-", () => { }),
                    (L.T("&Next Track\tCtrl+T", "&Próxima Pista\tCtrl+T"), () => MidiManager.SelectTrack(1)),
                    (L.T("Pre&vious Track\tCtrl+Shift+T", "Pista &Anterior\tCtrl+Shift+T"), () => MidiManager.SelectTrack(-1)),
                    (L.T("&Mute / Unmute Track\tCtrl+M", "&Silenciar / Ouvir Pista\tCtrl+M"), MidiManager.ToggleTrackMute),
                    (L.T("&Quantize Track...\tCtrl+Q", "&Quantizar Pista...\tCtrl+Q"), MidiManager.QuantizeTrack),
                    (L.T("&Undo Quantize", "&Desfazer Quantização"), MidiManager.UndoQuantize),
                    (L.T("&Clear Track...", "&Limpar Pista..."), MidiManager.ClearTrack),
                    ("-", () => { }),
                    (L.T("N&ew Song\tCtrl+N", "&Nova Música\tCtrl+N"), MidiManager.NewSong),
                    (L.T("&Open MIDI File...\tCtrl+O", "A&brir Arquivo MIDI...\tCtrl+O"), MidiManager.OpenSong),
                    (L.T("&Save as MIDI File...\tCtrl+S", "Sal&var como Arquivo MIDI...\tCtrl+S"), MidiManager.SaveSong)
                }),
                (L.T("Audio &Recording", "&Gravação de Áudio"), new (string, Action)[] {
                    (RecordText, ToggleRecording),
                    (CancelRecordText, CancelRecordingUI)
                }),
                (L.T("&Help", "&Ajuda"), new (string, Action)[] {
                    (L.T("View &Shortcuts", "Ver &Atalhos"), ShowShortcuts),
                    ("-", () => { }),
                    (L.T("&Visit my website", "&Visite meu site"), () => OpenLink(Constants.Site)),
                    (L.T("Source &code", "&Código fonte"), () => OpenLink(Constants.GitHub)),
                    (L.T("&About", "&Sobre"), () => Wf.msg(
                        L.T($"Virtual Piano v{Constants.Version}.\nDeveloped by Lucas Nunes Costa.", $"Piano Virtual v{Constants.Version}.\nDesenvolvido por Lucas Nunes Costa."),
                        L.T("About", "Sobre")))
                })
            };

            // While a style is being recorded its commands get a menu of their own, right after "Menu"
            if (MidiManager.IsStyleRecorderOpen)
            {
                menus.Insert(1, (L.T("Style Re&corder", "Gravador de &Estilos"), new (string, Action)[] {
                    (L.T("&Record / Stop Recording this Part\tCtrl+R", "&Gravar / Parar Gravação desta Parte\tCtrl+R"), MidiManager.ToggleStyleRecording),
                    // a submenu with the figures, filled in below; Ctrl+Q goes to the next one
                    (QuantizeText + "\tCtrl+Q", () => { }),
                    (L.T("&Listen / Stop\tCtrl+P", "&Ouvir / Parar\tCtrl+P"), () => MidiManager.ToggleStylePreview()),
                    (L.T("Listen to this Part &Only / Stop\tCtrl+Shift+P", "Ouvir &Só esta Parte / Parar\tCtrl+Shift+P"), () => MidiManager.ToggleStylePreview(solo: true)),
                    ("-", () => { }),
                    (L.T("&Next Part\tCtrl+T", "&Próxima Parte\tCtrl+T"), () => MidiManager.SelectStyleSlot(1)),
                    (L.T("&Previous Part\tCtrl+Shift+T", "Parte &Anterior\tCtrl+Shift+T"), () => MidiManager.SelectStyleSlot(-1)),
                    (L.T("Go Up for One &More Chord\tCtrl+Right", "Subir até um Acorde a &Mais\tCtrl+Direita"), () => MidiManager.ChangeStyleUpLimit(1)),
                    (L.T("Go Up for One Chord &Fewer\tCtrl+Left", "Subir até um Acorde a Me&nos\tCtrl+Esquerda"), () => MidiManager.ChangeStyleUpLimit(-1)),
                    (L.T("Se&venth and Ninth in this Part: On / Off\tCtrl+N", "Sétima &e Nona nesta Parte: Ligar / Desligar\tCtrl+N"), MidiManager.ToggleStyleTensions),
                    (L.T("Listen to the Last Recording Onl&y / Stop\tCtrl+L", "Ouvir Só a Ú&ltima Gravação / Parar\tCtrl+L"), MidiManager.ToggleLastTakePreview),
                    (L.T("&Undo Last Recording\tCtrl+Z", "&Desfazer Última Gravação\tCtrl+Z"), MidiManager.UndoStyleRecording),
                    (L.T("&Erase this Part\tCtrl+Delete", "A&pagar esta Parte\tCtrl+Delete"), MidiManager.ClearStyleSlot),
                    (L.T("Erase &Everything and Start Over...", "Apagar &Tudo e Recomeçar..."), MidiManager.ClearStyleDraft),
                    ("-", () => { }),
                    (L.T("&Save Style and Close\tCtrl+S", "&Salvar Estilo e Fechar\tCtrl+S"), MidiManager.SaveStyleDraft),
                    (L.T("&Close without Saving\tEsc", "&Fechar sem Salvar\tEsc"), MidiManager.CancelStyleRecorder)
                }));
            }

            Wf.menu(menus.ToArray());

            Wf.vStack(() =>
            {
                Wf.panel(() => { }, p => { p.Height = 20; p.BorderStyle = BorderStyle.None; });

                Wf.label(L.T("Virtual Piano", "Piano Virtual"), l => {
                    l.Font = TitleFont;
                    l.ForeColor = Color.DarkSlateBlue;
                    l.TextAlign = ContentAlignment.MiddleCenter;
                    l.Dock = DockStyle.Top;
                });

                _lblStatus = Wf.label("", "lblStatus", l => {
                    l.ForeColor = Color.Red;
                    l.Font = BoldFont;
                    l.TextAlign = ContentAlignment.MiddleCenter;
                    l.Height = 25;
                });

                Wf.panel(() => {
                    Wf.hStack(() => {
                        _lblInstr = Wf.label("", "lbl_instr", l => StyleInfoLabel(l));
                        _lblOctave = Wf.label("", "lbl_octave", l => StyleInfoLabel(l));
                        _lblTranspose = Wf.label("", "lbl_transpose", l => StyleInfoLabel(l));
                        _lblVelocity = Wf.label("", "lbl_velocity", l => StyleInfoLabel(l));
                    });
                }, p => { p.Padding = new Padding(10, 0, 10, 10); p.Height = 45; });

                _lblPedal = Wf.label("", "lbl_pedal", l => {
                    l.Font = PedalFont;
                    l.ForeColor = Color.Gray;
                    l.TextAlign = ContentAlignment.MiddleCenter;
                    l.Padding = new Padding(0, 5, 0, 10);
                });

                _lblArranger = Wf.label("", "lbl_arranger", l => StyleInfoLabel(l));
                _lblSong = Wf.label("", "lbl_song", l => StyleInfoLabel(l));

                Wf.label(L.T("Space: Pedal  |  F1/F2: Transpose  |  F10: Layer  |  F11: Arranger  |  F: Fill-in  |  A: Variation A/B  |  K: Ending  |  Ctrl+R: Record track",
                             "Espaço: Pedal  |  F1/F2: Transpose  |  F10: Camada  |  F11: Arranjador  |  F: Virada  |  A: Variação A/B  |  K: Finalização  |  Ctrl+R: Gravar pista"), l => {
                    l.ForeColor = Color.DimGray;
                    l.TextAlign = ContentAlignment.MiddleCenter;
                    l.Dock = DockStyle.Bottom;
                    l.Padding = new Padding(0, 0, 0, 20);
                });
            });

            if (form?.MainMenuStrip != null)
            {
                foreach (ToolStripItem top in form.MainMenuStrip.Items) SplitShortcuts(top);
                _miRecord = FindMenuItem(RecordText);
                _miCancelRecording = FindMenuItem(CancelRecordText);
                if (FindMenuItem(QuantizeText) is ToolStripMenuItem quantize) FillQuantizeMenu(quantize);
            }

            UpdateDisplay();
            UpdateLive();
            UpdateRecordingMenu();

            if (form != null)
            {
                form.ActiveControl = null;
                form.Activate();
                form.Focus();
            }
        }

        // "Name\tShortcut" becomes a menu item with the shortcut shown on the right, where screen
        // readers also announce it.
        private static void SplitShortcuts(ToolStripItem item)
        {
            if (item is not ToolStripMenuItem menuItem) return;

            string text = menuItem.Text ?? "";
            int tab = text.IndexOf('\t');
            if (tab >= 0)
            {
                menuItem.Text = text.Substring(0, tab);
                menuItem.ShortcutKeyDisplayString = text.Substring(tab + 1);
            }

            foreach (ToolStripItem child in menuItem.DropDownItems) SplitShortcuts(child);
        }

        // The figures a style recording can be snapped to, with a check mark on the one in use
        private static void FillQuantizeMenu(ToolStripMenuItem menu)
        {
            string[] names = MidiManager.QuantizeNames;
            for (int i = 0; i < names.Length; i++)
            {
                int figure = i;
                menu.DropDownItems.Add(new ToolStripMenuItem(names[i], null, (s, e) => MidiManager.SetStyleQuantize(figure)));
            }

            menu.DropDownOpening += (s, e) =>
            {
                for (int i = 0; i < menu.DropDownItems.Count; i++)
                    ((ToolStripMenuItem)menu.DropDownItems[i]).Checked = i == MidiManager.StyleQuantize;
            };
        }

        private static ToolStripItem? FindMenuItem(string text)
        {
            var menu = _form?.MainMenuStrip;
            if (menu == null) return null;

            foreach (ToolStripItem top in menu.Items)
            {
                var found = FindMenuItem(top, text);
                if (found != null) return found;
            }
            return null;
        }

        private static ToolStripItem? FindMenuItem(ToolStripItem item, string text)
        {
            if (item.Text == text) return item;
            if (item is not ToolStripDropDownItem dropDown) return null;

            foreach (ToolStripItem child in dropDown.DropDownItems)
            {
                var found = FindMenuItem(child, text);
                if (found != null) return found;
            }
            return null;
        }

        private static void UpdateRecordingMenu()
        {
            bool recording = MidiManager.IsRecording();
            if (_miRecord != null) _miRecord.Text = recording ? StopRecordText : RecordText;
            if (_miCancelRecording != null) _miCancelRecording.Visible = recording;
            if (recording) UpdateStatus(L.T("RECORDING...", "GRAVANDO..."));
        }

        private static void OpenSettingsWindow()
        {
            _isPianoScreen = false;
            _tempRecPath = Config.GetRecordingPath();
            var form = Wf.Get<Form>("_Form");

            if (form == null) return;

            Wf.wt(L.T("Settings", "Configurações"));

            ClearFormControls(form);
            if (form.MainMenuStrip != null) form.MainMenuStrip = null;

            Wf.vStack(() =>
            {
                Wf.label(L.T("Settings", "Configurações"), l => {
                    l.Font = HeadingFont;
                    l.ForeColor = Color.DarkSlateBlue;
                    l.Margin = new Padding(0, 20, 0, 20);
                });

                Wf.group(L.T("Devices", "Dispositivos"), () =>
                {
                    Wf.hStack(() => {
                        Wf.label(L.T("Select the input device:", "Selecione o dispositivo de entrada:"));

                        Wf.combo(GetMidiInDevices(), "cb_in", cb => {
                            if (Config.MidiInputId >= 0 && Config.MidiInputId < cb.Items.Count) cb.SelectedIndex = Config.MidiInputId;
                        });

                        Wf.button(L.T("Refresh device list", "Atualizar lista de dispositivos"), () => {
                            var cb = Wf.Get<ComboBox>("cb_in");
                            if (cb != null)
                            {
                                cb.Items.Clear();
                                cb.Items.AddRange(GetMidiInDevices());
                                if (cb.Items.Count > 0) cb.SelectedIndex = 0;
                                Sp.Speak(L.T("List refreshed", "Lista atualizada"));
                            }
                        }, b => {
                            b.Width = 30;
                            b.Height = 23;
                            b.Padding = new Padding(0);
                            b.TextAlign = ContentAlignment.MiddleCenter;
                            b.Font = BoldFont;
                        });
                    });
                });

                Wf.group(L.T("Recordings", "Gravações"), () =>
                {
                    Wf.label(L.T("Default Folder:", "Pasta Padrão:"));
                    Wf.label(_tempRecPath, "lbl_path", l => {
                        l.AutoEllipsis = true;
                        l.ForeColor = Color.Gray;
                        l.BorderStyle = BorderStyle.FixedSingle;
                        l.Padding = new Padding(5);
                        l.Width = 300;
                    });

                    Wf.button(L.T("Select Folder...", "Selecionar Pasta..."), () => {
                        using var fbd = new FolderBrowserDialog();
                        fbd.SelectedPath = _tempRecPath;
                        if (fbd.ShowDialog() == DialogResult.OK)
                        {
                            _tempRecPath = fbd.SelectedPath;
                            Wf.Set("lbl_path", _tempRecPath);
                        }
                    });
                });

                Wf.group(L.T("Language", "Idioma"), () =>
                {
                    Wf.hStack(() => {
                        Wf.label(L.T("Language of the program:", "Idioma do programa:"));
                        Wf.combo(new[] { L.T("Same as Windows", "O mesmo do Windows"), "English", "Português" }, "cb_lang", cb => {
                            int index = Math.Max(0, Array.IndexOf(LanguageCodes, Config.Language));
                            if (index < cb.Items.Count) cb.SelectedIndex = index;
                        });
                    });
                });

                Wf.panel(() => { }, p => p.Height = 20);

                Wf.hStack(() =>
                {
                    Wf.button(L.T("Apply", "Aplicar"), () => {
                        ApplySettings();
                        Wf.msg(L.T("Settings saved successfully!", "Configurações salvas com sucesso!"));
                        BuildMain();
                    }, b => {
                        b.BackColor = Color.LightGreen;
                        b.Width = 120;
                    });

                    Wf.button(L.T("Cancel", "Cancelar"), () => {
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

        private static readonly string[] LanguageCodes = { "auto", "en", "pt" };

        private static async void ToggleRecording()
        {
            if (MidiManager.IsRecording())
            {
                StopRecordingUI();
            }
            else
            {
                await StartRecordingUI();
            }
            UpdateRecordingMenu();
        }

        private static async Task<bool> StartRecordingUI()
        {
            string savedPath = Config.RecordingPath;
            string fileName = $"Piano_Rec_{DateTime.Now:yyyyMMdd_HHmmss}.wav";
            string fullPath;

            if (!string.IsNullOrWhiteSpace(savedPath) && Directory.Exists(savedPath))
            {
                fullPath = Path.Combine(savedPath, fileName);
            }
            else
            {
                using var sfd = new SaveFileDialog();
                sfd.Filter = L.T("WAV Audio File|*.wav", "Arquivo de Áudio WAV|*.wav");
                sfd.FileName = fileName;
                sfd.InitialDirectory = Config.GetRecordingPath();

                if (sfd.ShowDialog() != DialogResult.OK) return false;

                string? folder = Path.GetDirectoryName(sfd.FileName);

                if (!string.IsNullOrEmpty(folder))
                {
                    Config.RecordingPath = folder;
                    Config.Save();
                }

                fullPath = sfd.FileName;
            }

            try
            {
                MidiManager.StartRecording(fullPath);
            }
            catch (Exception ex)
            {
                Wf.msg(L.T("Could not start recording: ", "Não foi possível iniciar a gravação: ") + ex.Message);
                return false;
            }

            UpdateStatus(L.T("RECORDING...", "GRAVANDO..."));

            await Task.Delay(500);
            Sp.Speak(L.T("Recording", "Gravando"));
            return true;
        }

        private static void StopRecordingUI()
        {
            if (!MidiManager.IsRecording()) return;

            MidiManager.StopRecording();
            UpdateStatus("");
            Wf.msg(L.T("Recording saved successfully!", "Gravação salva com sucesso!"));
        }

        private static void CancelRecordingUI()
        {
            if (!MidiManager.IsRecording()) return;

            MidiManager.AbortRecording();
            UpdateStatus("");
            UpdateRecordingMenu();

            Wf.msg(L.T("Recording canceled and file discarded.", "Gravação cancelada e arquivo descartado."));
            Sp.Speak(L.T("Recording canceled", "Gravação cancelada"));
        }

        public static async void ShowStatusTemp(string msg)
        {
            if (MidiManager.IsRecording()) return;

            // a newer message must not be wiped by the timer of an older one
            int version = ++_statusVersion;
            UpdateStatus(msg);
            await Task.Delay(2000);
            if (version == _statusVersion && !MidiManager.IsRecording()) UpdateStatus("");
        }

        private static void ShowShortcuts()
        {
            _isPianoScreen = false;

            var form = Wf.Get<Form>("_Form");
            if (form == null) return;

            Wf.wt(L.T("Help - Commands", "Ajuda - Comandos"));
            ClearFormControls(form);
            if (form.MainMenuStrip != null) form.MainMenuStrip = null;


            Wf.btn(L.T("Back", "Voltar"), () => BuildMain(), b => {
                b.Dock = DockStyle.Bottom;
                b.Height = 40;
                b.Cursor = Cursors.Hand;
                b.Font = BoldFont;
            });

            var browser = new WebBrowser();
            browser.Dock = DockStyle.Fill;
            browser.IsWebBrowserContextMenuEnabled = false;
            browser.WebBrowserShortcutsEnabled = false;

            string path = Path.Combine(AppContext.BaseDirectory, $"help.{L.Code}.html");
            if (File.Exists(path))
            {
                browser.AllowNavigation = false;
                browser.Navigate(path);
            }
            else
            {
                browser.DocumentText = L.T(
                    $"<html><body><h1>Error</h1><p>File help.{L.Code}.html not found.</p></body></html>",
                    $"<html><body><h1>Erro</h1><p>Arquivo help.{L.Code}.html não encontrado.</p></body></html>");
            }

            form.Controls.Add(browser);

            browser.BringToFront();
        }

        private static void StyleInfoLabel(Label l)
        {
            l.Font = InfoFont;
            l.AutoSize = true;
            l.Padding = new Padding(5);
            l.BorderStyle = BorderStyle.FixedSingle;
            l.TextAlign = ContentAlignment.MiddleCenter;
            l.Margin = new Padding(5);
        }

        // For commands that come from the menu: rebuilds the screen once the menu has finished closing
        public static void RebuildMain() => RunOnUiThread(BuildMain);

        public static void RunOnUiThread(Action action)
        {
            var form = _form;
            if (form == null || form.IsDisposed || !form.IsHandleCreated) return;
            try { form.BeginInvoke(action); } catch { }
        }

        public static void UpdateDisplay()
        {
            try
            {
                var form = _form;
                if (form == null || form.IsDisposed || _lblInstr == null) return;

                if (form.InvokeRequired) { form.BeginInvoke((MethodInvoker)UpdateDisplay); return; }

                string instrName = Instruments.NameOf(MidiManager.CurrentInstrument);

                if (instrName.Length > 20) instrName = instrName.Substring(0, 18) + "..";

                if (MidiManager.IsLayerActive)
                {
                    _lblInstr.Text = $"{MidiManager.CurrentInstrument:000} + {MidiManager.LayerInstrument:000}";
                }
                else
                {
                    _lblInstr.Text = $"{MidiManager.CurrentInstrument:000}: {instrName}";
                }

                if (_lblOctave != null) _lblOctave.Text = L.T($"Octave: {(MidiManager.BaseOctave / 12) - 1}", $"Oitava: {(MidiManager.BaseOctave / 12) - 1}");
                if (_lblTranspose != null) _lblTranspose.Text = $"Transp: {MidiManager.Transpose:+#;-#;0}";
                if (_lblVelocity != null) _lblVelocity.Text = L.T($"Velocity: {MidiManager.NoteVelocity}", $"Força: {MidiManager.NoteVelocity}");

                var lblPedal = _lblPedal;
                if (lblPedal != null)
                {
                    bool continuous = MidiManager.SustainLockMode == 1;

                    if (MidiManager.IsSustainHold && MidiManager.IsSustainLocked)
                    {
                        lblPedal.Text = L.T($"SUSTAIN PEDAL AND LOCK ({(continuous ? "CONT." : "SMART")})", $"PEDAL SUSTAIN E FIXO ({(continuous ? "CONT." : "INTEL.")})");
                        lblPedal.ForeColor = Color.DarkRed;
                    }
                    else if (MidiManager.IsSustainHold)
                    {
                        lblPedal.Text = L.T("SUSTAIN PEDAL", "PEDAL SUSTAIN");
                        lblPedal.ForeColor = Color.DarkRed;
                    }
                    else if (MidiManager.IsSustainLocked)
                    {
                        lblPedal.Text = L.T($"PEDAL LOCK ({(continuous ? "CONTINUOUS" : "SMART")})", $"PEDAL FIXO ({(continuous ? "CONTÍNUO" : "INTELIGENTE")})");
                        lblPedal.ForeColor = Color.DarkGoldenrod;
                    }
                    else
                    {
                        lblPedal.Text = L.T("PEDAL FREE", "PEDAL LIVRE");
                        lblPedal.ForeColor = Color.LightGray;
                    }
                }

                UpdateLive();
            }
            catch { }
        }

        // The arranger and song lines follow the music (chord, bar), so a timer refreshes them
        private static void UpdateLive()
        {
            if (_lblArranger == null || _lblSong == null) return;

            _lblArranger.Text = MidiManager.ArrangerStatus();
            _lblArranger.ForeColor = MidiManager.IsArrangerOn ? Color.DarkGreen : Color.DimGray;
            _lblSong.Text = MidiManager.SongStatus();
        }

        private static void UpdateStatus(string text)
        {
            if (_lblStatus != null) _lblStatus.Text = text;
        }

        public static void ConfigureWindow(Form f)
        {
            _form = f;
            f.WindowState = FormWindowState.Maximized;
            f.KeyPreview = true;

            f.Shown += async (s, e) => {
                f.TopMost = true;
                ForceForeground(f);
                SwitchToThisWindow(f.Handle, true);
                await Task.Delay(100);
                f.TopMost = false;
                f.Activate();
                f.Focus();

                try { SendKeys.SendWait("%"); SendKeys.SendWait("{ESC}"); } catch { }

                await Task.Delay(100); // Give Windows time to process the key before speaking

                if (_firstLoad)
                {
                    Sp.Speak(L.T(
                        $"Virtual Piano v{Constants.Version} window. Welcome to the virtual piano. Use the help menu to learn the keyboard shortcuts.",
                        $"Piano Virtual v{Constants.Version} janela. Bem-vindo ao piano virtual. Utilize o menu ajuda para conhecer os atalhos de teclado."));
                    _firstLoad = false;
                }

                if (await Updater.InstallIfAvailableAsync()) Application.Exit();
            };

            f.KeyDown += (s, e) => {
                if (_isPianoScreen) MidiManager.OnKeyDown(s, e);
            };

            f.KeyUp += (s, e) => {
                if (_isPianoScreen) MidiManager.OnKeyUp(s, e);
            };

            f.Deactivate += (s, e) => MidiManager.ReleaseAllKeys();

            var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (s, e) => {
                MidiManager.Poll();
                if (_isPianoScreen) UpdateLive();
            };
            timer.Start();

            f.FormClosed += (s, e) => {
                timer.Dispose();
                MidiManager.Shutdown();
            };
        }

        private static string[] GetMidiInDevices() =>
            Enumerable.Range(0, MidiIn.NumberOfDevices)
                .Select(i => $"{i}: {MidiIn.DeviceInfo(i).ProductName}")
                .ToArray();

        private static void ApplySettings()
        {
            int inIdx = ParseId(Wf.Get<string>("cb_in"));
            Config.MidiInputId = inIdx;
            Config.RecordingPath = _tempRecPath;

            int language = Wf.Get<ComboBox>("cb_lang")?.SelectedIndex ?? 0;
            Config.Language = LanguageCodes[Math.Clamp(language, 0, LanguageCodes.Length - 1)];
            L.Init(Config.Language);

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
