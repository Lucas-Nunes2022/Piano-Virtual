using System;
using System.IO;
using System.Speech.Synthesis;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Speech
{
    public static class Sp
    {
        private static readonly SpeechEngine _engine = new SpeechEngine();

        public static void Speak(string text)
        {
            try { _engine.Speak(text); } catch { }
        }
    }

    internal static class NvdaNative
    {
        [DllImport("nvdaControllerClient.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvdaController_speakText")]
        public static extern void NvdaControllerSpeakText([MarshalAs(UnmanagedType.LPWStr)] string text);

        [DllImport("nvdaControllerClient.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvdaController_cancelSpeech")]
        public static extern void NvdaControllerCancelSpeech();
    }

    public class SpeechEngine : IDisposable
    {
        private SpeechSynthesizer sapi = null!;
        private readonly object speechLock = new();
        private readonly System.Threading.Timer monitorTimer;
        private bool nvdaDetected;

        public bool ForceSAPI { get; set; } = false;

        public SpeechEngine()
        {
            var initThread = new Thread(() =>
            {
                try {
                    sapi = new SpeechSynthesizer();
                } catch {}
            });

            initThread.SetApartmentState(ApartmentState.STA);
            initThread.Start();
            initThread.Join();

            UpdateNvdaStatus();

            monitorTimer = new System.Threading.Timer(
                _ => UpdateNvdaStatus(),
                null,
                TimeSpan.Zero,
                TimeSpan.FromSeconds(15)
            );
        }

        public int Volume
        {
            get => sapi?.Volume ?? 100;
            set { if(sapi != null) lock (speechLock) sapi.Volume = Math.Clamp(value, 0, 100); }
        }

        public int Rate
        {
            get => sapi?.Rate ?? 0;
            set { if(sapi != null) lock (speechLock) sapi.Rate = Math.Clamp(value, -10, 10); }
        }

        private bool ShouldUseNVDA => nvdaDetected && !ForceSAPI;

        public void Speak(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            
            Stop();
            
            if (ShouldUseNVDA && TryNvdaSpeak(text)) return;
            
            lock (speechLock)
            {
                try { sapi?.SpeakAsync(text); }
                catch { }
            }
        }

        public async Task SpeakTask(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            Stop();
            if (ShouldUseNVDA && TryNvdaSpeak(text)) return;
            
            var tcs = new TaskCompletionSource<bool>();
            SpeechSynthesizer sapiLocal;
            
            lock (speechLock)
            {
                if (sapi == null) { tcs.SetResult(true); return; }
                sapiLocal = sapi;
                
                EventHandler<SpeakCompletedEventArgs> handler = null!;
                handler = (sender, e) => 
                {
                    lock (speechLock) { sapiLocal.SpeakCompleted -= handler; }
                    tcs.TrySetResult(true);
                };
                sapiLocal.SpeakCompleted += handler;
            }

            lock (speechLock)
            {
                try { sapi.SpeakAsync(text); }
                catch 
                { 
                    tcs.TrySetResult(true); 
                }
            }
            await tcs.Task;
        }

        private bool TryNvdaSpeak(string text)
        {
            try { Task.Run(() => NvdaNative.NvdaControllerSpeakText(text)); return true; }
            catch { nvdaDetected = false; return false; }
        }

        private bool IsNvdaRunning()
        {
            try { return Process.GetProcessesByName("nvda").Any(); }
            catch { return false; }
        }

        private void UpdateNvdaStatus()
        {
            if (!IsNvdaRunning()) { nvdaDetected = false; return; }
            if (nvdaDetected) return;
            try { NvdaNative.NvdaControllerSpeakText(" "); nvdaDetected = true; }
            catch { nvdaDetected = false; }
        }

        public void Stop()
        {
            try
            {
                if (!ForceSAPI && nvdaDetected)
                    NvdaNative.NvdaControllerCancelSpeech();
            }
            catch { }
            
            lock (speechLock)
            {
                try { sapi?.SpeakAsyncCancelAll(); }
                catch { }
            }
        }

        public void Dispose()
        {
            monitorTimer?.Dispose();
            Stop();
            sapi?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}