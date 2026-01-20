using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;

namespace piano
{
    public static class Updater
    {
        private const string REPO_OWNER = "Lucas-Nunes2022";
        private const string REPO_NAME = "Piano-Virtual";
        private const string API_URL = $"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/latest";

        public static async Task<bool> CheckAndUpdateBlocking()
        {
            if (Debugger.IsAttached) return true;

            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.UserAgent.ParseAdd("PianoVirtualApp");

                    string json = await client.GetStringAsync(API_URL);
                    JObject release = JObject.Parse(json);

                    string tagName = release["tag_name"]?.ToString() ?? "";
                    string downloadUrl = release["assets"]?[0]?["browser_download_url"]?.ToString() ?? "";

                    if (string.IsNullOrEmpty(tagName) || string.IsNullOrEmpty(downloadUrl))
                        return true;

                    string serverVersionStr = tagName.Trim().TrimStart('v');
                    Version serverVersion = new Version(serverVersionStr);
                    Version localVersion = new Version(Constantes.versao);

                    if (serverVersion > localVersion)
                    {
                        await PerformUpdateWithUI(client, downloadUrl, serverVersionStr);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Erro ao verificar atualizações: {ex.Message}\nO programa abrirá normalmente.",
                    "Erro no Update"
                );
            }

            return true;
        }

        private static async Task PerformUpdateWithUI(HttpClient client, string url, string version)
        {
            using (var progressForm = new UpdateForm(version))
            {
                progressForm.Show();
                Application.DoEvents();

                string tempZipPath = Path.Combine(Path.GetTempPath(), "piano_update.zip");
                string appPath = AppDomain.CurrentDomain.BaseDirectory;
                string exeName = AppDomain.CurrentDomain.FriendlyName;

                try
                {
                    using (HttpResponseMessage response =
                        await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                        var canReport = totalBytes != -1;

                        using (var streamToRead = await response.Content.ReadAsStreamAsync())
                        using (var streamToWrite = File.Create(tempZipPath))
                        {
                            var buffer = new byte[8192];
                            var totalRead = 0L;
                            var bytesRead = 0;

                            while ((bytesRead =
                                await streamToRead.ReadAsync(buffer, 0, buffer.Length)) > 0)
                            {
                                await streamToWrite.WriteAsync(buffer, 0, bytesRead);
                                totalRead += bytesRead;

                                if (canReport)
                                {
                                    int progress =
                                        (int)((totalRead * 100) / totalBytes);
                                    progressForm.UpdateProgress(progress);
                                }
                            }
                        }
                    }

                    progressForm.UpdateStatus("Instalando...");
                    await Task.Delay(500);

                    string batPath = Path.Combine(Path.GetTempPath(), "update_piano.bat");
                    string extractPath = Path.Combine(Path.GetTempPath(), "PianoExtracted");

                    string script = $@"
@echo off
taskkill /F /PID {Process.GetCurrentProcess().Id} >nul 2>&1
timeout /t 1 /nobreak > nul

rmdir /S /Q ""{extractPath}"" >nul 2>&1
powershell -Command ""Expand-Archive -Path '{tempZipPath}' -DestinationPath '{extractPath}' -Force""

if exist ""{extractPath}\config.ini"" del ""{extractPath}\config.ini""
del /S /Q ""{extractPath}\*.sf2"" >nul 2>&1

powershell -Command ""Copy-Item -Path '{extractPath}\*' -Destination '{appPath}' -Recurse -Force""

start """" ""{Path.Combine(appPath, exeName)}""
del ""{tempZipPath}""
rmdir /S /Q ""{extractPath}""
del ""%~f0""
";
                    File.WriteAllText(batPath, script);

                    ProcessStartInfo psi = new ProcessStartInfo
                    {
                        FileName = batPath,
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };

                    Process.Start(psi);
                    Environment.Exit(0);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Falha ao atualizar: " + ex.Message);
                }
            }
        }

        private class UpdateForm : Form
        {
            private ProgressBar progressBar;
            private Label lblStatus;

            public UpdateForm(string version)
            {
                this.Text = "Atualizando Piano Virtual";
                this.Size = new Size(400, 150);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.StartPosition = FormStartPosition.CenterScreen;
                this.ControlBox = false;

                Label lblTitle = new Label
                {
                    Text = $"Baixando versão {version}...",
                    Location = new Point(20, 20),
                    AutoSize = true,
                    Font = new Font(
                        FontFamily.GenericSansSerif,
                        10,
                        FontStyle.Bold
                    )
                };

                progressBar = new ProgressBar
                {
                    Location = new Point(20, 50),
                    Size = new Size(340, 25),
                    Style = ProgressBarStyle.Continuous
                };

                lblStatus = new Label
                {
                    Text = "Conectando...",
                    Location = new Point(20, 85),
                    AutoSize = true
                };

                this.Controls.Add(lblTitle);
                this.Controls.Add(progressBar);
                this.Controls.Add(lblStatus);
            }

            public void UpdateProgress(int value)
            {
                if (InvokeRequired)
                    Invoke(new Action(() => UpdateProgress(value)));
                else
                    progressBar.Value = Math.Min(100, Math.Max(0, value));
            }

            public void UpdateStatus(string text)
            {
                if (InvokeRequired)
                    Invoke(new Action(() => UpdateStatus(text)));
                else
                    lblStatus.Text = text;
            }
        }
    }
}
