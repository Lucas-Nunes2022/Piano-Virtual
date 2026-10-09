using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace piano
{
    public static class Updater
    {
        private const string REPO_OWNER = "Lucas-Nunes2022";
        private const string REPO_NAME = "Piano-Virtual";
        private const string API_URL = $"https://api.github.com/repos/{REPO_OWNER}/{REPO_NAME}/releases/latest";

        private static readonly HttpClient client = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PianoVirtualApp");
            return http;
        }

        private static Task<(string Version, string Url)?>? check;

        // Starts looking for a new version in the background, so the program opens without waiting
        public static void BeginCheck()
        {
            if (!Debugger.IsAttached) check = Task.Run(FindUpdateAsync);
        }

        // Call from the UI thread once the window is open.
        // Returns true when an update is being installed and the program must close.
        public static async Task<bool> InstallIfAvailableAsync()
        {
            if (check == null) return false;

            (string Version, string Url)? update;
            try
            {
                update = await check;
            }
            catch
            {
                // No internet or GitHub unreachable: not worth bothering the user
                return false;
            }

            if (update == null) return false;

            using (var progressForm = new UpdateForm(update.Value.Version, update.Value.Url))
            {
                progressForm.ShowDialog();
                return progressForm.Installing;
            }
        }

        private static async Task<(string Version, string Url)?> FindUpdateAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            string json = await client.GetStringAsync(API_URL, timeout.Token);

            using JsonDocument release = JsonDocument.Parse(json);
            string tagName = release.RootElement.TryGetProperty("tag_name", out var tag) ? tag.GetString() ?? "" : "";
            string serverVersionStr = tagName.Trim().TrimStart('v');

            if (!Version.TryParse(serverVersionStr, out Version? serverVersion)) return null;
            if (serverVersion <= new Version(Constants.Version)) return null;
            if (!release.RootElement.TryGetProperty("assets", out var assets)) return null;

            string downloadUrl = "";
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.GetProperty("name").GetString() ?? "";
                string url = asset.GetProperty("browser_download_url").GetString() ?? "";
                if (downloadUrl == "" || name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) downloadUrl = url;
                if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) break;
            }

            return downloadUrl == "" ? null : (serverVersionStr, downloadUrl);
        }

        private static async Task DownloadAsync(string url, string tempZipPath, UpdateForm progressForm)
        {
            using (HttpResponseMessage response =
                await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var canReport = totalBytes > 0;

                using (var streamToRead = await response.Content.ReadAsStreamAsync())
                using (var streamToWrite = File.Create(tempZipPath))
                {
                    var buffer = new byte[81920];
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
        }

        private static void LaunchInstaller(string tempZipPath)
        {
            string appPath = AppContext.BaseDirectory;
            string exePath = Environment.ProcessPath ?? Path.Combine(appPath, AppDomain.CurrentDomain.FriendlyName + ".exe");
            string batPath = Path.Combine(Path.GetTempPath(), "update_piano.bat");
            string extractPath = Path.Combine(Path.GetTempPath(), "PianoExtracted");

            // chcp 65001: the file is written as UTF-8, and folders such as C:\Users\João must survive
            string script = $@"
@echo off
chcp 65001 >nul
taskkill /F /PID {Environment.ProcessId} >nul 2>&1
timeout /t 1 /nobreak > nul

rmdir /S /Q ""{extractPath}"" >nul 2>&1
powershell -Command ""Expand-Archive -Path '{tempZipPath}' -DestinationPath '{extractPath}' -Force""

if exist ""{extractPath}\config.ini"" del ""{extractPath}\config.ini""
del /S /Q ""{extractPath}\*.sf2"" >nul 2>&1

powershell -Command ""Copy-Item -Path '{extractPath}\*' -Destination '{appPath}' -Recurse -Force""

start """" ""{exePath}""
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
        }

        private class UpdateForm : Form
        {
            private ProgressBar progressBar;
            private Label lblStatus;

            public bool Installing { get; private set; }

            public UpdateForm(string version, string url)
            {
                this.Text = L.T("Updating Virtual Piano", "Atualizando Piano Virtual");
                this.Size = new Size(400, 150);
                this.FormBorderStyle = FormBorderStyle.FixedDialog;
                this.StartPosition = FormStartPosition.CenterScreen;
                this.ControlBox = false;

                Label lblTitle = new Label
                {
                    Text = L.T($"Downloading version {version}...", $"Baixando versão {version}..."),
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
                    Text = L.T("Connecting...", "Conectando..."),
                    Location = new Point(20, 85),
                    AutoSize = true
                };

                this.Controls.Add(lblTitle);
                this.Controls.Add(progressBar);
                this.Controls.Add(lblStatus);

                // The download runs inside this dialog's message loop, so the window stays responsive
                this.Shown += async (s, e) =>
                {
                    string tempZipPath = Path.Combine(Path.GetTempPath(), "piano_update.zip");
                    try
                    {
                        await DownloadAsync(url, tempZipPath, this);

                        UpdateStatus(L.T("Installing...", "Instalando..."));
                        await Task.Delay(500);

                        LaunchInstaller(tempZipPath);
                        Installing = true;
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(
                            L.T($"Update failed: {ex.Message}\nThe program will open normally.", $"Falha ao atualizar: {ex.Message}\nO programa abrirá normalmente."),
                            L.T("Update Error", "Erro no Update"));
                    }
                    Close();
                };
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
