using System;
using System.IO;

namespace piano
{
    public static class Config
    {
        private static string ConfigPath => Path.Combine(Directory.GetCurrentDirectory(), "config.ini");

        public static int MidiInputId { get; set; } = 0;
        public static string RecordingPath { get; set; } = "";
        public static int[] Favorites { get; set; } = new int[10];

        public static void Load()
        {
            for (int i = 0; i < 10; i++) Favorites[i] = 0;

            if (!File.Exists(ConfigPath)) return;

            try
            {
                var lines = File.ReadAllLines(ConfigPath);
                foreach (var line in lines)
                {
                    var parts = line.Split('=');
                    if (parts.Length != 2) continue;

                    var key = parts[0].Trim();
                    var value = parts[1].Trim();

                    if (key == "MidiInput")
                    {
                        if (int.TryParse(value, out int id)) MidiInputId = id;
                    }
                    else if (key == "RecPath")
                    {
                        RecordingPath = value;
                    }
                    else if (key.StartsWith("Fav"))
                    {
                        string indexStr = key.Replace("Fav", "");
                        if (int.TryParse(indexStr, out int index) && index >= 0 && index < 10)
                        {
                            if (int.TryParse(value, out int instrId)) Favorites[index] = instrId;
                        }
                    }
                }
            }
            catch { }
        }

        public static void Save()
        {
            try
            {
                using var writer = new StreamWriter(ConfigPath);
                writer.WriteLine($"MidiInput={MidiInputId}");
                writer.WriteLine($"RecPath={RecordingPath}");
                
                for (int i = 0; i < 10; i++)
                {
                    writer.WriteLine($"Fav{i}={Favorites[i]}");
                }
            }
            catch { }
        }

        public static string GetRecordingPath()
        {
            if (string.IsNullOrWhiteSpace(RecordingPath) || !Directory.Exists(RecordingPath))
            {
                return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }
            return RecordingPath;
        }
    }
}