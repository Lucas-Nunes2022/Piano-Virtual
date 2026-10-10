using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace piano
{
    // Pad plays just as Chord does. It is a part of its own in the style recorder.
    public enum PartRole { Bass, Chord, Pad }

    public sealed record DrumLine(int Note, string Pattern);

    // One pitch of an instrument, as it sounds over a C chord, and when it plays
    public sealed record NoteLine(int Note, string Pattern);

    public sealed record Part(PartRole Role, int Program, int LowNote, int Velocity, string A, string B)
    {
        public const int DefaultUp = 6;

        // Besides the patterns of chord degrees in A and B, the notes exactly as they were played
        public NoteLine[] NotesA { get; init; } = Array.Empty<NoteLine>();
        public NoteLine[] NotesB { get; init; } = Array.Empty<NoteLine>();

        // The last chord root (0 = C ... 11 = B) those notes go up to from where they were played
        // in C. For the roots after it they go down instead: up to F sharp, down from G on.
        public int Up { get; init; } = DefaultUp;

        // Chords and pad: on a chord with a seventh or a ninth that those notes do not have, the
        // highest C among them plays the seventh instead, and the ninth is added above it
        public bool Tensions { get; init; } = true;
    }

    // An accompaniment style. Patterns are strings with one character per step, see Styles.FormatGuide.
    public sealed class Style
    {
        public string Name { get; init; } = "";
        public string NamePt { get; init; } = "";
        public int Tempo { get; init; } = 120;
        public int BeatsPerBar { get; init; } = 4;
        public int StepsPerBeat { get; init; } = 4;
        public DrumLine[] DrumsA { get; init; } = Array.Empty<DrumLine>();
        public DrumLine[] DrumsB { get; init; } = Array.Empty<DrumLine>();
        public DrumLine[] Fill { get; init; } = Array.Empty<DrumLine>();
        public Part[] Parts { get; init; } = Array.Empty<Part>();
        public string Source { get; init; } = "";
        public bool BuiltIn { get; init; }
        // Where a user style was loaded from
        public string FilePath { get; set; } = "";

        public string DisplayName => L.T(Name, NamePt);
    }

    public static class Styles
    {
        // One channel each, from Arranger.FirstPartChannel to the last of the sixteen
        public const int MaxParts = 6;
        public const string FileExtension = ".style";

        private static readonly Dictionary<string, int> DrumNames = new(StringComparer.OrdinalIgnoreCase)
        {
            { "kick", 36 }, { "rim", 37 }, { "snare", 38 }, { "clap", 39 }, { "hat", 42 }, { "pedalhat", 44 },
            { "openhat", 46 }, { "floortom", 43 }, { "lowtom", 45 }, { "midtom", 47 }, { "hightom", 50 },
            { "crash", 49 }, { "ride", 51 }, { "tambourine", 54 }, { "cowbell", 56 }, { "conga", 63 },
            { "lowconga", 64 }, { "agogo", 67 }, { "lowagogo", 68 }, { "maracas", 70 }, { "claves", 75 },
            { "woodblock", 76 }, { "mutetriangle", 80 }, { "triangle", 81 }, { "shaker", 82 }
        };

        private static IReadOnlyList<Style>? all;

        public static IReadOnlyList<Style> All => all ??= BuiltIn.Select(text => Parse(text, "", true)).ToArray();

        // User styles live outside the app folder so that updates never touch them
        public static string UserFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Piano Virtual", "Styles");

        // Reloads the built-in styles plus every *.style file in the user folder.
        // Returns one message per user file that could not be read.
        public static List<string> Load()
        {
            var styles = BuiltIn.Select(text => Parse(text, "", true)).ToList();
            var errors = new List<string>();

            try
            {
                if (Directory.Exists(UserFolder))
                {
                    foreach (string file in Directory.GetFiles(UserFolder, "*" + FileExtension).OrderBy(f => f))
                    {
                        try
                        {
                            var style = Parse(File.ReadAllText(file), Path.GetFileNameWithoutExtension(file), false);
                            style.FilePath = file;
                            styles.Add(style);
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"{Path.GetFileName(file)}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add(ex.Message);
            }

            all = styles;
            return errors;
        }

        public static string FormatGuide => L.T(
@"# Piano Virtual style file. Edit it in any text editor, save it, then use
# Arranger > Reload styles. Lines starting with # are comments.
#
# name, tempo (BPM), beats (per bar) and steps (per beat: 4 = sixteenth
# notes, 3 = swing/triplet feel, 12 = both mixed) describe the style.
#
# Every pattern has one character per step. Spaces and | are ignored, so
# use them to separate beats and bars. A pattern can be several bars long.
#
# [drums] is variation A, [drums b] is variation B and [fill] is the fill-in,
# also used for the intro and the ending. Each line is one drum:
#   . silence   x hit   X accent   o soft hit
# Drums: kick snare rim clap hat openhat pedalhat ride crash hightom midtom
# lowtom floortom tambourine cowbell shaker maracas claves triangle
# mutetriangle conga lowconga agogo lowagogo woodblock, or a GM note number.
#
# Up to six instrument sections: [bass], [chord] and [pad], and more of each
# with a number, like [chord 2], for another instrument. Each one has
# program (GM instrument 0-127), low (lowest MIDI note it may play),
# velocity (1-127), a (pattern for variation A) and optionally b.
#   . silence   - keep the previous note sounding
#   [bass]         1 root   3 third   5 fifth   7 seventh or sixth   8 octave
#   [chord] [pad]  x whole chord   X accented chord   1 2 3 4 single chord notes
#
# An instrument can also have its notes written one per line, which is how
# the style recorder saves what you play: ""a C2 = x--- .... x-.. ...."" is
# the note C2 in variation A (""b C2 = ..."" for variation B; middle C is C4).
#   . silence   x note   X accent   o soft note   - keep it sounding
# Write them as they sound over a C major chord. The arranger moves them to
# the chord you play: the third, the fifth and the seventh follow its type.
# up = F# says how far: up to an F sharp chord the notes go up from where they
# were written, and from G on they go down instead. Any note from C to B.
# In [chord] and [pad], tensions = on (the default) completes the chords with
# a seventh or a ninth: when no note written is the seventh, the highest C
# plays it instead, and a ninth that is not written is added above that C.
# tensions = off plays only what is written.
",
@"# Arquivo de estilo do Piano Virtual. Edite em qualquer editor de texto,
# salve e use Arranjador > Recarregar estilos. Linhas com # são comentários.
#
# name (nome), tempo (BPM), beats (tempos por compasso) e steps (passos por
# tempo: 4 = semicolcheias, 3 = suingue/tercinas, 12 = os dois misturados)
# descrevem o estilo.
#
# Cada padrão tem um caractere por passo. Espaços e | são ignorados, então
# use-os para separar tempos e compassos. Um padrão pode ter vários compassos.
#
# [drums] é a variação A, [drums b] é a variação B e [fill] é a virada,
# usada também na introdução e na finalização. Cada linha é uma peça:
#   . silêncio   x toque   X acento   o toque fraco
# Peças: kick snare rim clap hat openhat pedalhat ride crash hightom midtom
# lowtom floortom tambourine cowbell shaker maracas claves triangle
# mutetriangle conga lowconga agogo lowagogo woodblock, ou um número de nota GM.
#
# Até seis seções de instrumento: [bass], [chord] e [pad], e mais de cada uma
# com um número, como [chord 2], para outro instrumento. Cada uma tem
# program (instrumento GM 0-127), low (nota MIDI mais grave que pode tocar),
# velocity (1-127), a (padrão da variação A) e, se quiser, b.
#   . silêncio   - mantém a nota anterior soando
#   [bass]         1 tônica   3 terça   5 quinta   7 sétima ou sexta   8 oitava
#   [chord] [pad]  x acorde inteiro   X acorde acentuado   1 2 3 4 notas soltas do acorde
#
# Um instrumento também pode ter suas notas escritas uma por linha, que é
# como o gravador de estilos salva o que você toca: ""a C2 = x--- .... x-.. ....""
# é a nota C2 (Dó 2) na variação A (""b C2 = ..."" na variação B; o Dó central é C4).
#   . silêncio   x nota   X acento   o nota fraca   - mantém a nota soando
# Escreva como elas soam sobre um acorde de Dó maior. O arranjador leva as
# notas para o acorde tocado: a terça, a quinta e a sétima seguem o tipo dele.
# up = F# diz até onde: até o acorde de Fá sustenido as notas sobem de onde
# foram escritas, e de Sol em diante elas descem. Vale qualquer nota de C a B.
# Em [chord] e [pad], tensions = on (o padrão) completa os acordes com sétima
# ou com nona: se nenhuma nota escrita é a sétima, o Dó mais agudo toca a
# sétima no lugar dele, e a nona que não está escrita entra acima desse Dó.
# tensions = off toca só o que está escrito.
");

        public static Style Parse(string text, string fallbackName, bool builtIn)
        {
            string name = fallbackName, namePt = "";
            int tempo = 120, beats = 4, steps = 4;
            var drumsA = new List<(int Note, string Pattern)>();
            var drumsB = new List<(int Note, string Pattern)>();
            var fill = new List<(int Note, string Pattern)>();
            var parts = new List<Dictionary<string, string>>();
            bool hasDrumsB = false;
            string section = "";

            string[] lines = text.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;

                try
                {
                    if (line[0] == '[')
                    {
                        section = string.Join(" ", line.Trim('[', ']').ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries));
                        if (section == "drums b") hasDrumsB = true;
                        else if (section != "drums" && section != "fill")
                        {
                            if (!IsPartSection(section)) throw new FormatException(L.T($"unknown section [{section}]", $"seção desconhecida [{section}]"));
                            if (parts.Count == MaxParts) throw new FormatException(L.T($"at most {MaxParts} instrument sections", $"no máximo {MaxParts} seções de instrumento"));
                            parts.Add(new Dictionary<string, string> { { "role", section.StartsWith("bass") ? "bass" : section.StartsWith("pad") ? "pad" : "chord" } });
                        }
                        continue;
                    }

                    var pair = line.Split('=', 2);
                    if (pair.Length != 2) throw new FormatException(L.T("expected key = value", "esperado chave = valor"));
                    string key = pair[0].Trim().ToLowerInvariant();
                    string value = pair[1].Trim();

                    switch (section)
                    {
                        case "":
                            if (key == "name") name = value;
                            else if (key == "name.pt") namePt = value;
                            else if (key == "tempo") tempo = Number(value, 20, 300);
                            else if (key == "beats") beats = Number(value, 1, 12);
                            else if (key == "steps") steps = Number(value, 2, AudioEngine.TicksPerBeat);
                            else throw new FormatException(L.T($"unknown setting {key}", $"configuração desconhecida {key}"));
                            break;
                        case "drums": drumsA.Add((DrumNote(key), value)); break;
                        case "drums b": drumsB.Add((DrumNote(key), value)); break;
                        case "fill": fill.Add((DrumNote(key), value)); break;
                        default: parts[^1][key] = value; break;
                    }
                }
                catch (FormatException ex)
                {
                    throw new FormatException(L.T($"line {i + 1}: {ex.Message}", $"linha {i + 1}: {ex.Message}"));
                }
            }

            if (string.IsNullOrWhiteSpace(name)) throw new FormatException(L.T("the style has no name", "o estilo não tem nome"));
            if (AudioEngine.TicksPerBeat % steps != 0) throw new FormatException(L.T("steps must be 2, 3, 4, 5, 6, 8, 12 or another divisor of 480", "steps deve ser 2, 3, 4, 5, 6, 8, 12 ou outro divisor de 480"));
            if (drumsA.Count == 0 && parts.Count == 0) throw new FormatException(L.T("the style has no patterns", "o estilo não tem padrões"));

            int stepsPerBar = beats * steps;
            DrumLine[] Drums(List<(int Note, string Pattern)> source, string where) =>
                source.Select(d => new DrumLine(d.Note, Pattern(d.Pattern, ".xXo", stepsPerBar, where, false))).ToArray();

            var a = Drums(drumsA, "[drums]");
            return new Style
            {
                Name = name,
                NamePt = namePt.Length > 0 ? namePt : name,
                Tempo = tempo,
                BeatsPerBar = beats,
                StepsPerBeat = steps,
                DrumsA = a,
                DrumsB = hasDrumsB ? Drums(drumsB, "[drums b]") : a,
                Fill = fill.Count > 0 ? Drums(fill, "[fill]") : a,
                Parts = parts.Select(p => BuildPart(p, stepsPerBar)).ToArray(),
                Source = text,
                BuiltIn = builtIn
            };
        }

        private static bool IsPartSection(string section) =>
            section.StartsWith("bass") || section.StartsWith("chord") || section.StartsWith("pad");

        private static Part BuildPart(Dictionary<string, string> values, int stepsPerBar)
        {
            var role = values["role"] switch { "bass" => PartRole.Bass, "pad" => PartRole.Pad, _ => PartRole.Chord };
            string where = $"[{values["role"]}]";
            string allowed = role == PartRole.Bass ? ".-13578" : ".-xX1234";

            var notesA = new List<NoteLine>();
            var notesB = new List<NoteLine>();
            foreach (var (key, value) in values)
            {
                if (key is "role" or "program" or "low" or "velocity" or "up" or "tensions" or "a" or "b") continue;

                // "a C2 = x--- ...." is one note of variation A
                string[] words = key.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length != 2 || words[0] is not ("a" or "b") || !TryNote(words[1], out int note))
                    throw new FormatException(L.T($"unknown setting {key} in {where}", $"configuração desconhecida {key} em {where}"));

                (words[0] == "a" ? notesA : notesB).Add(new NoteLine(note, Pattern(value, ".-xXo", stepsPerBar, where, false)));
            }

            // with nothing written for b, variation B plays the same as A
            bool hasB = values.ContainsKey("b") || notesB.Count > 0;
            string a = Pattern(values.GetValueOrDefault("a", ""), allowed, stepsPerBar, where, true);
            string b = hasB ? Pattern(values.GetValueOrDefault("b", ""), allowed, stepsPerBar, where, true) : a;
            var linesA = notesA.ToArray();

            int up = Part.DefaultUp;
            if (values.TryGetValue("up", out string? rawUp) && !TryPitch(rawUp, out up))
                throw new FormatException(L.T($"up must be a note from C to B, not {rawUp}", $"up deve ser uma nota de C a B, não {rawUp}"));

            string rawTensions = values.GetValueOrDefault("tensions", "on").ToLowerInvariant();
            if (rawTensions is not ("on" or "off"))
                throw new FormatException(L.T($"tensions must be on or off, not {rawTensions}", $"tensions deve ser on ou off, não {rawTensions}"));

            return new Part(
                role,
                Number(values.GetValueOrDefault("program", "0"), 0, 127),
                Number(values.GetValueOrDefault("low", role == PartRole.Bass ? "28" : "55"), 0, 108),
                Number(values.GetValueOrDefault("velocity", "80"), 1, 127),
                a, b)
            {
                NotesA = linesA,
                NotesB = hasB ? notesB.ToArray() : linesA,
                Up = up,
                Tensions = rawTensions == "on"
            };
        }

        private static int Letter(char letter) =>
            char.ToLowerInvariant(letter) switch { 'c' => 0, 'd' => 2, 'e' => 4, 'f' => 5, 'g' => 7, 'a' => 9, 'b' => 11, _ => -1 };

        // C, F#, Bb, or a number from 0 (C) to 11 (B)
        private static bool TryPitch(string text, out int pitch)
        {
            if (int.TryParse(text, out pitch)) return pitch >= 0 && pitch <= 11;

            pitch = text.Length is 1 or 2 ? Letter(text[0]) : -1;
            if (pitch < 0) return false;

            if (text.Length == 2)
            {
                if (text[1] == '#') pitch++;
                else if (text[1] is 'b' or 'B') pitch--;
                else return false;
            }

            pitch = (pitch + 12) % 12;
            return true;
        }

        // C2, F#3, Bb1 (middle C is C4) or a MIDI note number
        private static bool TryNote(string text, out int note)
        {
            if (int.TryParse(text, out note)) return note >= 0 && note <= 127;

            int pitch = Letter(text[0]);
            if (pitch < 0) return false;

            int octaveAt = 1;
            if (text.Length > 2 && text[1] == '#') { pitch++; octaveAt = 2; }
            else if (text.Length > 2 && text[1] == 'b') { pitch--; octaveAt = 2; }

            if (!int.TryParse(text[octaveAt..], out int octave)) return false;
            note = (octave + 1) * 12 + pitch;
            return note >= 0 && note <= 127;
        }

        private static string Pattern(string raw, string allowed, int stepsPerBar, string where, bool mayBeEmpty)
        {
            string pattern = raw.Replace(" ", "").Replace("|", "").Replace("\t", "");
            if (pattern.Length == 0 && mayBeEmpty) return pattern;

            foreach (char c in pattern)
            {
                if (!allowed.Contains(c))
                    throw new FormatException(L.T($"character '{c}' is not valid in {where}", $"o caractere '{c}' não é válido em {where}"));
            }

            if (pattern.Length == 0 || pattern.Length % stepsPerBar != 0)
                throw new FormatException(L.T(
                    $"a pattern in {where} has {pattern.Length} steps; it must be a multiple of {stepsPerBar}",
                    $"um padrão em {where} tem {pattern.Length} passos; deve ser múltiplo de {stepsPerBar}"));

            return pattern;
        }

        public static string DrumName(int note)
        {
            foreach (var pair in DrumNames) if (pair.Value == note) return pair.Key;
            return note.ToString();
        }

        private static int DrumNote(string key)
        {
            if (DrumNames.TryGetValue(key, out int note)) return note;
            if (int.TryParse(key, out note) && note >= 0 && note <= 127) return note;
            throw new FormatException(L.T($"unknown drum {key}", $"peça de bateria desconhecida {key}"));
        }

        private static int Number(string value, int min, int max)
        {
            if (int.TryParse(value, out int number) && number >= min && number <= max) return number;
            throw new FormatException(L.T($"{value} must be a number from {min} to {max}", $"{value} deve ser um número de {min} a {max}"));
        }

        // The factory styles use the same text format as user styles, so any of them can be
        // exported as a starting point for a new one.
        private static readonly string[] BuiltIn =
        {
@"name = Pop
tempo = 108
beats = 4
steps = 4

[drums]
kick  = x... ..x. ..x. ....
snare = .... x... .... x...
hat   = x.x. x.x. x.x. x.x.

[drums b]
kick       = x... ..x. x.x. ....
snare      = .... x... .... x...
ride       = x.x. x.x. x.x. x.x.
tambourine = .... x... .... x...

[fill]
kick     = x... .... x... ....
snare    = .... x.x. xx.. ....
hightom  = .... .... ..xx ....
lowtom   = .... .... .... xx..
floortom = .... .... .... ..xX

[bass]
program = 33
low = 28
velocity = 100
a = 1--- --1. ..1- 5-3.

[chord]
program = 4
low = 55
velocity = 70
a = x--- ..x- ..x- x---

[pad]
program = 48
low = 55
velocity = 55
a =
b = x--- ---- ---- ----
",
@"name = Rock
tempo = 120
beats = 4
steps = 4

[drums]
kick  = x... .... x.x. ....
snare = .... X... .... X...
hat   = x.x. x.x. x.x. x.x.

[drums b]
kick  = x... ..x. x.x. ....
snare = .... X... .... X...
ride  = x.x. x.x. x.x. x.x.

[fill]
kick     = x... x... x... x...
snare    = x.x. x.x. xxxx ....
hightom  = .... .... .... xx..
floortom = .... .... .... ..xX

[bass]
program = 34
low = 28
velocity = 105
a = 1.1. 1.1. 1.1. 1.5.

[chord]
program = 29
low = 48
velocity = 70
a = x-x- x-x- x-x- x-x-
b = x--- x-x- .x-x x---

[pad]
program = 18
low = 55
velocity = 50
a =
b = x--- ---- ---- ----
",
@"name = Ballad
name.pt = Balada
tempo = 72
beats = 4
steps = 4

[drums]
kick = x... .... ..x. ....
rim  = .... x... .... x...
hat  = o.o. o.o. o.o. o.o.

[drums b]
kick  = x... ...x ..x. ....
snare = .... x... .... x...
hat   = xoxo xoxo xoxo xoxo

[fill]
kick   = x... .... x... ....
snare  = .... x... ..x. x.xx
lowtom = .... ..x. .... ....

[bass]
program = 33
low = 28
velocity = 95
a = 1--- ---- ..1- 5---

[chord]
program = 0
low = 52
velocity = 70
a = 1-2- 3-2- 4-2- 3-2-

[pad]
program = 48
low = 55
velocity = 60
a = x--- ---- ---- ----
",
@"name = Disco
tempo = 120
beats = 4
steps = 4

[drums]
kick    = x... x... x... x...
clap    = .... x... .... x...
hat     = x... x... x... x...
openhat = ..x. ..x. ..x. ..x.

[drums b]
kick    = x... x... x... x...
snare   = .... x... .... x...
hat     = xx.x xx.x xx.x xx.x
openhat = ..x. ..x. ..x. ..x.

[fill]
kick  = x... x... x... x...
snare = .... x... x.x. xxxx

[bass]
program = 33
low = 28
velocity = 100
a = 1.8. 1.8. 1.8. 1.8.

[chord]
program = 27
low = 55
velocity = 65
a = ..x. .x.. ..x. .x..

[pad]
program = 48
low = 60
velocity = 55
a = x--- ---- ---- ----
",
@"name = Reggae
tempo = 80
beats = 4
steps = 4

[drums]
kick = .... .... x... ....
rim  = .... .... x... ....
hat  = x.x. x.x. x.x. x.x.

[drums b]
kick  = x... .... x... ....
snare = .... .... x... ....
hat   = x.xx x.x. x.xx x.x.

[fill]
kick  = x... .... .... ...x
snare = ..x. ..xx .x.x xx..

[bass]
program = 33
low = 28
velocity = 105
a = 1--. 1... 5--. 3.1.

[chord]
program = 27
low = 55
velocity = 75
a = ..x. ..x. ..x. ..x.

[pad]
program = 16
low = 55
velocity = 50
a = ..xx ..xx ..xx ..xx
",
@"name = Country
tempo = 112
beats = 4
steps = 4

[drums]
kick  = x... .... x... ....
snare = .... x... .... x...
hat   = x.x. x.x. x.x. x.x.

[drums b]
kick  = x... ..x. x... ..x.
snare = .... x... .... x...
hat   = x.xx x.xx x.xx x.xx

[fill]
kick  = x... x... x... ...x
snare = x.xx x.xx xxxx xx..

[bass]
program = 32
low = 28
velocity = 100
a = 1--- .... 5--- ....
b = 1--- .... 5--- ..3.

[chord]
program = 25
low = 52
velocity = 70
a = .... x-.. .... x-..
b = ..1. x-3. ..1. x-3.
",
@"name = Bossa Nova
tempo = 130
beats = 4
steps = 4

[drums]
kick = x... ..x. x... ..x.
rim  = x... ..x. .... x... | .... x... ..x. ....
hat  = x.o. x.o. x.o. x.o.

[drums b]
kick   = x... ..x. x... ..x.
rim    = x... ..x. .... x... | .... x... ..x. ....
hat    = x.o. x.o. x.o. x.o.
shaker = xoxo xoxo xoxo xoxo

[fill]
kick  = x... ..x. x... ....
snare = .... .... x.x. xx.x

[bass]
program = 32
low = 28
velocity = 95
a = 1--- --5. 5--- --1.

[chord]
program = 24
low = 55
velocity = 70
a = x-.. ..x- .... x-.. | .... x-.. ..x- ....
",
@"name = Samba
tempo = 100
beats = 4
steps = 4

[drums]
kick   = x..x x..x x..x x..x
shaker = xoox xoox xoox xoox
rim    = x.xx .x.x .xx. x.x.

[drums b]
kick   = x..x x..x x..x x..x
shaker = xoox xoox xoox xoox
rim    = x.xx .x.x .xx. x.x.
agogo  = x.x. .x.x ..x. x.x.

[fill]
kick  = x... x... x... x..x
snare = x.xx .xx. xxxx x...

[bass]
program = 32
low = 28
velocity = 100
a = 1--5 -..1 1--5 -..1

[chord]
program = 24
low = 55
velocity = 72
a = .x.x ..x. .x.x ..x.
",
@"name = Baiao
name.pt = Baião
tempo = 100
beats = 4
steps = 4

[drums]
kick         = x..x ..x. x..x ..x.
rim          = .... x... .... x...
mutetriangle = xx.x xx.x xx.x xx.x
triangle     = ..x. ..x. ..x. ..x.

[drums b]
kick         = x..x ..x. x..x ..x.
rim          = .... x... .... x...
mutetriangle = xx.x xx.x xx.x xx.x
triangle     = ..x. ..x. ..x. ..x.
shaker       = xoxo xoxo xoxo xoxo

[fill]
kick  = x..x ..x. x... ...x
snare = x..x ..x. xxxx xx..

[bass]
program = 33
low = 28
velocity = 100
a = 1--1 --5. 1--1 --5.
b = 1--1 --5. 1--5 --7.

[chord]
program = 21
low = 55
velocity = 68
a = x-.x -.x- x-.x -.x-
",
@"name = Arrocha
tempo = 135
beats = 4
steps = 4

[drums]
kick    = X... .... X... X...
hat     = X.XX X.X. X.X. X.X.
openhat = .... ..X. .... ....
40      = .... .... .... x...
57      = X... .... .... .... | .... .... .... .... | .... .... .... .... | .... .... .... ....

[drums b]
kick    = X... .... X... X...
rim     = .... X... .... ....
hat     = X.XX X.X. X.X. X.X.
openhat = .... ..X. .... ....
57      = X... .... .... .... | .... .... .... .... | .... .... .... .... | .... .... .... ....

[fill]
kick    = X... .... X... X...
40      = .... .... ...X X...
lowtom  = .... .... X... ....
midtom  = .... .X.. .... ....
48      = ..X. .... .... ....

[bass]
program = 34
low = 28
velocity = 93
up = F#
a C2 = X--- --.. .... ....
a E2 = .... ..X- ---- ....
a G2 = .... .... .... X---
b C2 = X--- -.X- .... ....
b G2 = .... .... X--. X--.

[chord]
program = 25
low = 55
velocity = 93
up = F#
tensions = on
a E4 = ..X. ..X. ..X. ....
a G4 = ..X. ..X. ..X. ....
a C5 = ..X- ..X. ..X. .... | ..X. ..X. ..X. ....
b E4 = ..X. ..X. ..X. ..X.
b G4 = ..X. ..X. ..X. ..X.
b C5 = ..X. ..X. ..X. ..X.

[pad]
program = 48
low = 55
velocity = 93
up = F#
tensions = on
a E4 = X--- ---- ---- ---- | ---- ---- ---- ---- | ---- ---- ---- ---- | ---- ---- ---- ----
a G4 = X--- ---- ---- ---- | ---- ---- ---- ---- | ---- ---- ---- ---- | ---- ---- ---- ----
a C5 = X--- ---- ---- ---- | ---- ---- ---- ---- | ---- ---- ---- ---- | ---- ---- ---- ----
",
@"name = Swing
name.pt = Suingue
tempo = 140
beats = 4
steps = 3

[drums]
ride     = x.. x.x x.. x.x
pedalhat = ... x.. ... x..
kick     = o.. ... o.. ...

[drums b]
ride     = x.. x.x x.. x.x
pedalhat = ... x.. ... x..
kick     = o.. ... o.. ...
snare    = ... ... ..o ... | ..o ... ... ...

[fill]
kick  = x.. x.. x.. x..
snare = x.x x.x xxx xxx

[bass]
program = 32
low = 28
velocity = 100
a = 1-- 3-- 5-- 7-- | 8-- 7-- 5-- 3--

[chord]
program = 0
low = 55
velocity = 65
a = x-. ..x -.. ... | ... ..x -.. x-.
",
@"name = Blues Shuffle
tempo = 100
beats = 4
steps = 3

[drums]
kick  = x.. ... x.. ...
snare = ... X.. ... X..
hat   = x.x x.x x.x x.x

[drums b]
kick  = x.. ..x x.. ...
snare = ... X.. ... X..
ride  = x.x x.x x.x x.x

[fill]
kick  = x.. x.. x.. x..
snare = x.x x.x xxx xxx

[bass]
program = 33
low = 28
velocity = 105
a = 1.1 3.3 5.5 7.5

[chord]
program = 26
low = 52
velocity = 70
a = ... x-. ... x-.

[pad]
program = 16
low = 55
velocity = 50
a =
b = x-- --- --- ---
",
@"name = Waltz
name.pt = Valsa
tempo = 90
beats = 3
steps = 4

[drums]
kick = x... .... ....
hat  = .... x... x...

[drums b]
kick = x... .... ....
rim  = .... x... x...
ride = x... x... x...

[fill]
kick  = x... .... ....
snare = x... x.x. xxxx

[bass]
program = 32
low = 28
velocity = 100
a = 1--- ---- ---- | 5--- ---- ----

[chord]
program = 0
low = 55
velocity = 68
a = .... x-.. x-..

[pad]
program = 48
low = 55
velocity = 55
a = x--- ---- ----
"
        };
    }
}
