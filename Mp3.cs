using System;
using System.IO;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace piano
{
    // MP3 through the encoder that comes with Windows (Media Foundation), so nothing is installed
    public static class Mp3
    {
        private const int BitRate = 320000;

        public static bool IsMp3(string path) => Path.GetExtension(path).Equals(".mp3", StringComparison.OrdinalIgnoreCase);

        // Encodes the WAV into the MP3 and deletes it. If Windows has no MP3 encoder (the N
        // editions without the Media Feature Pack) the WAV is kept beside the MP3 asked for.
        // Returns the file that was saved.
        public static string Convert(string wav, string mp3)
        {
            try
            {
                MediaFoundationApi.Startup();
                using (var reader = new WaveFileReader(wav))
                {
                    // MP3 only goes up to 48 kHz; the sound card may run faster
                    ISampleProvider samples = reader.ToSampleProvider();
                    if (samples.WaveFormat.SampleRate is not (32000 or 44100 or 48000))
                        samples = new WdlResamplingSampleProvider(samples, 48000);

                    MediaFoundationEncoder.EncodeToMp3(samples.ToWaveProvider16(), mp3, BitRate);
                }

                File.Delete(wav);
                return mp3;
            }
            catch
            {
                try { if (File.Exists(mp3)) File.Delete(mp3); } catch { }
                return KeepWav(wav, mp3);
            }
        }

        private static string KeepWav(string wav, string mp3)
        {
            string target = Path.ChangeExtension(mp3, ".wav");
            for (int n = 2; File.Exists(target); n++)
                target = Path.Combine(Path.GetDirectoryName(mp3) ?? "", $"{Path.GetFileNameWithoutExtension(mp3)} ({n}).wav");

            try
            {
                File.Move(wav, target);
                return target;
            }
            catch
            {
                return wav;
            }
        }
    }
}
