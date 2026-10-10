using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace piano
{
    // A style being created by playing. It is made of nine parts ("slots"): drums, bass, chord and
    // pad for variation A, the same four for variation B, and the fill-in. A recording only has
    // its timing changed, by being snapped to the quantize grid: the notes, their octave and how
    // long each one lasts are kept as played, in the same pattern text a .style file uses.
    //
    // Recordings add up. On the drums, each one layers more pieces. On bass, chords and pad, each
    // one adds notes to the instrument it was played with, or brings that instrument into the
    // part when it was not there: the chords can be an electric piano, a guitar and an organ.
    public sealed class StyleDraft
    {
        public const int SlotCount = 9;
        public const int FillSlot = 8;
        public const int MaxBars = 4;

        // How long the recording of a slot can be. The arranger plays the fill-in for one bar.
        public static int MaxBarsOf(int slot) => slot == FillSlot ? 1 : MaxBars;

        // Divisible by every figure a recording can be snapped to: 1, 2, 3, 4, 6, 8 or 12 steps per beat
        private const int FinestGrid = 24;

        // What an instrument plays in one variation: a pattern of chord degrees, as the factory
        // styles have, or the notes of a recording, one line per pitch
        private sealed record Take(string Pattern, NoteLine[] Notes)
        {
            public static readonly Take Empty = new("", Array.Empty<NoteLine>());

            public bool IsEmpty => Pattern.Length == 0 && Notes.Length == 0;

            public Take Each(Func<string, string> change) =>
                new(change(Pattern), Notes.Select(line => line with { Pattern = change(line.Pattern) }).ToArray());
        }

        // One instrument of a part
        private sealed class Layer
        {
            public int Program, Low, Velocity = 80;
            public Take A = Take.Empty;
            public Take? B;   // null: same as A

            public bool IsEmpty => A.IsEmpty && (B == null || B.IsEmpty);
            public Layer Copy() => (Layer)MemberwiseClone();
        }

        // Bass, chords or pad: the instruments recorded into it, and what goes for all of them
        private sealed class PartDraft
        {
            public PartRole Role;
            public int Up = Part.DefaultUp;
            public bool Tensions = true;
            public List<Layer> Layers = new();
        }

        // drum note -> pattern, for variation A, variation B and the fill
        private readonly Dictionary<int, string>[] drums = { new(), new(), new() };

        private readonly PartDraft[] parts =
        {
            new PartDraft { Role = PartRole.Bass },
            new PartDraft { Role = PartRole.Chord },
            new PartDraft { Role = PartRole.Pad }
        };

        // What a slot held before each recording or erasure, most recent on top
        private readonly Stack<(int Slot, Dictionary<int, string>? Drums, List<Layer>? Layers)> history = new();

        // Just what the last recording added, to check a take on its own. Null when there is none.
        public Style? LastTake { get; private set; }

        public string Name { get; }
        public int BeatsPerBar { get; }
        // The grid of the style the draft started from. The saved style keeps it whenever it can.
        public int StepsPerBeat { get; }

        // Steps per beat the next recording is snapped to: 4 for sixteenth notes, 3 for eighth-note
        // triplets... It can change between recordings: a straight kick under a triplet hi-hat.
        public int Quantize { get; set; }

        // The patterns are kept on a grid fine enough for every quantize figure. They only go down
        // to a coarser grid on the way out, to be heard or saved.
        private readonly int resolution;

        private int StepsPerBar => BeatsPerBar * resolution;
        private int TicksPerStep => AudioEngine.TicksPerBeat / resolution;

        public StyleDraft(string name, int beatsPerBar, int stepsPerBeat)
        {
            Name = name;
            BeatsPerBar = beatsPerBar;
            StepsPerBeat = stepsPerBeat;
            Quantize = stepsPerBeat;

            resolution = FinestGrid;
            while (resolution % stepsPerBeat != 0) resolution += FinestGrid;
        }

        public static StyleDraft From(Style style, string name)
        {
            var draft = new StyleDraft(name, style.BeatsPerBar, style.StepsPerBeat);
            int finer = draft.resolution / style.StepsPerBeat;

            void Copy(Dictionary<int, string> target, DrumLine[] lines)
            {
                foreach (var line in lines) target[line.Note] = Stretch(line.Pattern, finer, '.');
            }

            Copy(draft.drums[0], style.DrumsA);
            if (style.DrumsB != style.DrumsA) Copy(draft.drums[1], style.DrumsB);
            if (style.Fill != style.DrumsA) Copy(draft.drums[2], style.Fill);

            // every instrument goes to its part: [chord], [chord 2] and [chord 3] are all the chords
            foreach (var part in style.Parts)
            {
                bool sameB = part.B == part.A && part.NotesB.SequenceEqual(part.NotesA);
                var layer = new Layer
                {
                    Program = part.Program, Low = part.LowNote, Velocity = part.Velocity,
                    A = new Take(part.A, part.NotesA).Each(pattern => Stretch(pattern, finer, '-')),
                    B = sameB ? null : new Take(part.B, part.NotesB).Each(pattern => Stretch(pattern, finer, '-'))
                };
                if (layer.IsEmpty) continue;

                var target = draft.parts.First(p => p.Role == part.Role);
                if (target.Layers.Count == 0) (target.Up, target.Tensions) = (part.Up, part.Tensions);
                target.Layers.Add(layer);
            }

            return draft;
        }

        // Slots 0-3: drums, bass, chord, pad of variation A. 4-7: the same for B. 8: fill.
        public static bool IsDrumSlot(int slot) => slot == FillSlot || slot % 4 == 0;
        private static int VariationOf(int slot) => slot == FillSlot ? 0 : slot / 4;
        private static int PartOf(int slot) => slot % 4 - 1;

        public static string SlotName(int slot)
        {
            if (slot == FillSlot) return L.T("Fill-in", "Virada");

            return PartName(slot) + (slot < 4 ? " A" : " B");
        }

        // The instrument of a slot, whatever the variation
        public static string PartName(int slot) => (slot % 4) switch
        {
            0 => L.T("Drums", "Bateria"),
            1 => L.T("Bass", "Baixo"),
            2 => L.T("Chords", "Acordes"),
            _ => "Pad"
        };

        // Moves the last chord root up to which the instrument of a slot goes up (see Part.Up), for
        // both variations. Returns the new one, or -1 for the drums, which do not follow the chords.
        public int MoveUpLimit(int slot, int delta)
        {
            if (IsDrumSlot(slot)) return -1;

            var part = parts[PartOf(slot)];
            part.Up = Math.Clamp(part.Up + delta, 0, 11);
            return part.Up;
        }

        // Turns on or off the sevenths and ninths the arranger adds to the instrument of a slot
        // (see Part.Tensions), for both variations. Returns the new state, or null for the drums
        // and the bass, which never get them.
        public bool? ToggleTensions(int slot)
        {
            if (IsDrumSlot(slot) || parts[PartOf(slot)].Role == PartRole.Bass) return null;

            var part = parts[PartOf(slot)];
            part.Tensions = !part.Tensions;
            return part.Tensions;
        }

        public string DescribeSlot(int slot)
        {
            string instruments = InstrumentNames(slot);
            bool empty = IsDrumSlot(slot) ? drums[slot == FillSlot ? 2 : VariationOf(slot)].Count == 0 : instruments.Length == 0;

            string state = !empty ? L.T("recorded", "gravado") + (instruments.Length > 0 ? ": " + instruments : "")
                         : slot < 4 ? L.T("empty", "vazio")
                         : L.T("same as A", "igual à A");
            return SlotName(slot) + ", " + state;
        }

        // The instruments recorded in a slot, by name. Empty for the drums and for a slot with nothing in it.
        public string InstrumentNames(int slot)
        {
            if (IsDrumSlot(slot)) return "";

            bool inB = VariationOf(slot) == 1;
            return string.Join(", ", parts[PartOf(slot)].Layers
                .Where(layer => inB ? layer.B != null : !layer.A.IsEmpty)
                .Select(layer => Instruments.NameOf(layer.Program)));
        }

        // A style has room for Styles.MaxParts instruments. One the part already has can always
        // be recorded again, since its notes join the ones there.
        public bool CanRecord(int slot, int program) =>
            IsDrumSlot(slot)
            || parts.Sum(part => part.Layers.Count) < Styles.MaxParts
            || parts[PartOf(slot)].Layers.Any(layer => layer.Program == program);

        private void Remember(int slot)
        {
            if (IsDrumSlot(slot))
            {
                history.Push((slot, new Dictionary<int, string>(drums[slot == FillSlot ? 2 : VariationOf(slot)]), null));
            }
            else
            {
                history.Push((slot, null, parts[PartOf(slot)].Layers.Select(layer => layer.Copy()).ToList()));
            }
        }

        // Takes back the last recording or erasure. Returns the slot it belonged to, or -1 if there is none.
        public int Undo()
        {
            if (history.Count == 0) return -1;
            LastTake = null;

            var (slot, savedDrums, savedLayers) = history.Pop();
            if (savedDrums != null)
            {
                var target = drums[slot == FillSlot ? 2 : VariationOf(slot)];
                target.Clear();
                foreach (var line in savedDrums) target[line.Key] = line.Value;
            }
            else
            {
                // the recording is taken back, not a setting of the part changed since
                parts[PartOf(slot)].Layers = savedLayers!;
            }
            return slot;
        }

        public void Clear(int slot)
        {
            Remember(slot);

            if (IsDrumSlot(slot))
            {
                drums[slot == FillSlot ? 2 : VariationOf(slot)].Clear();
                return;
            }

            var layers = parts[PartOf(slot)].Layers;
            foreach (var layer in layers)
            {
                if (VariationOf(slot) == 0) layer.A = Take.Empty; else layer.B = null;
            }
            // an instrument left with nothing to play makes room for another one
            layers.RemoveAll(layer => layer.IsEmpty);
        }

        // The style heard while a slot is auditioned or recorded: the variation of that slot (the
        // fill-in drums for the fill slot). Recordings add up, so to record everything plays
        // along, but not what only stands in for the part about to be recorded: the fill-in plays
        // on its own, with neither the instruments nor the drums of variation A, and an instrument
        // (program) with nothing of its own in variation B does not play its A.
        public Style Backing(int slot, bool recording, int program = -1)
        {
            int variation = VariationOf(slot);
            bool fillAlone = recording && slot == FillSlot;
            var source = slot == FillSlot && (fillAlone || drums[2].Count > 0) ? drums[2]
                       : variation == 1 && drums[1].Count > 0 ? drums[1]
                       : drums[0];
            var lines = source.Select(d => new DrumLine(d.Key, d.Value)).ToArray();

            var backing = new List<Part>();
            for (int i = 0; i < parts.Length; i++)
            {
                foreach (var layer in parts[i].Layers)
                {
                    var take = variation == 1 ? layer.B ?? layer.A : layer.A;
                    bool standsIn = variation == 1 && layer.B == null && !IsDrumSlot(slot) && PartOf(slot) == i && layer.Program == program;
                    if (fillAlone || (recording && standsIn)) take = Take.Empty;
                    backing.Add(ToPart(parts[i], layer, take));
                }
            }

            return Audible(lines, backing.ToArray());
        }

        private static Part ToPart(PartDraft part, Layer layer, Take take) =>
            new(part.Role, layer.Program, layer.Low, layer.Velocity, take.Pattern, take.Pattern) { NotesA = take.Notes, NotesB = take.Notes, Up = part.Up, Tensions = part.Tensions };

        // A style to listen to, on the grid the saved style will have
        private Style Audible(DrumLine[] lines, Part[] audibleParts)
        {
            int grid = SavedGrid(), coarser = resolution / grid;
            lines = lines.Select(line => line with { Pattern = Shrink(line.Pattern, coarser) }).ToArray();

            return new Style
            {
                Name = Name, NamePt = Name, BeatsPerBar = BeatsPerBar, StepsPerBeat = grid,
                DrumsA = lines, DrumsB = lines, Fill = lines,
                Parts = audibleParts.Select(part =>
                {
                    // to be heard, both variations of a part are the same take
                    var take = new Take(part.A, part.NotesA).Each(pattern => Shrink(pattern, coarser));
                    return part with { A = take.Pattern, B = take.Pattern, NotesA = take.Notes, NotesB = take.Notes };
                }).ToArray()
            };
        }

        // Only the part of a slot, with everything else silent
        public Style Solo(int slot)
        {
            var full = Backing(slot, recording: false);
            bool drumSlot = IsDrumSlot(slot);
            var lines = drumSlot ? full.DrumsA : Array.Empty<DrumLine>();

            // Backing lists the instruments part by part
            int first = drumSlot ? 0 : parts.Take(PartOf(slot)).Sum(part => part.Layers.Count);
            int count = drumSlot ? 0 : parts[PartOf(slot)].Layers.Count;

            return new Style
            {
                Name = Name, NamePt = Name, BeatsPerBar = BeatsPerBar, StepsPerBeat = full.StepsPerBeat,
                DrumsA = lines, DrumsB = lines, Fill = lines,
                Parts = full.Parts.Select((part, i) => i >= first && i < first + count ? part : Silent(part)).ToArray()
            };
        }

        private static Part Silent(Part part) =>
            part with { A = "", B = "", NotesA = Array.Empty<NoteLine>(), NotesB = Array.Empty<NoteLine>() };

        // Adds a recording to a slot. Returns its length in bars, 0 if nothing was played.
        // program: the instrument it was played with.
        public int Apply(int slot, CapturedNote[] notes, int endTick, int program)
        {
            int maxBars = MaxBarsOf(slot), limit = maxBars * StepsPerBar;
            var hits = notes
                .Select(n => (Step: Snap(n.Start), End: Snap(n.End), n.Note, n.Velocity))
                .Where(h => h.Step < limit)
                .OrderBy(h => h.Step)
                .ToList();
            if (hits.Count == 0 || !CanRecord(slot, program)) return 0;

            Remember(slot);

            // whole bars: as many as were played in, or as were left running before stopping
            int bars = hits[^1].Step / StepsPerBar + 1;
            bars = Math.Max(bars, Math.Min(maxBars, (int)Math.Round(endTick / (double)(StepsPerBar * TicksPerStep))));
            if (bars == 3) bars = 4;
            int length = bars * StepsPerBar;

            if (IsDrumSlot(slot))
            {
                // drums add up: every take layers more pieces over what is already there
                var target = drums[slot == FillSlot ? 2 : VariationOf(slot)];
                var takeLines = new List<DrumLine>();
                foreach (var group in hits.GroupBy(h => h.Note))
                {
                    var alone = new char[length];
                    Array.Fill(alone, '.');
                    foreach (var hit in group) alone[hit.Step] = hit.Velocity >= 110 ? 'X' : hit.Velocity <= 60 ? 'o' : 'x';
                    takeLines.Add(new DrumLine(group.Key, new string(alone)));

                    target.TryGetValue(group.Key, out string? existing);
                    int total = Math.Max(length, existing?.Length ?? 0);
                    var pattern = new char[total];
                    for (int i = 0; i < total; i++) pattern[i] = existing != null ? existing[i % existing.Length] : '.';

                    foreach (var hit in group)
                        for (int step = hit.Step; step < total; step += length)
                            pattern[step] = hit.Velocity >= 110 ? 'X' : hit.Velocity <= 60 ? 'o' : 'x';

                    target[group.Key] = Shortest(new string(pattern));
                }

                LastTake = Audible(takeLines.ToArray(), Array.Empty<Part>());
                return bars;
            }

            // an instrument is kept as it was played over C: one line per pitch, each note with its length
            var part = parts[PartOf(slot)];
            int shortest = resolution / Quantize;
            var lines = new SortedDictionary<int, char[]>();

            foreach (var hit in hits)
            {
                if (!lines.TryGetValue(hit.Note, out char[]? line))
                {
                    line = new char[length];
                    Array.Fill(line, '.');
                    lines[hit.Note] = line;
                }

                // a note lasts at least one step of the quantize grid, and ends the one before it
                int end = Math.Min(length, Math.Max(hit.Step + shortest, hit.End));
                line[hit.Step] = hit.Velocity >= 110 ? 'X' : hit.Velocity <= 60 ? 'o' : 'x';
                for (int i = hit.Step + 1; i < length; i++) line[i] = i < end ? '-' : '.';
            }

            var played = lines.Select(line => new NoteLine(line.Key, Shortest(new string(line.Value)))).ToArray();
            int velocity = Math.Clamp((int)hits.Average(h => h.Velocity) * 8 / 10, 1, 127);

            // the notes join the ones this instrument already plays in the part; an instrument
            // the part does not have yet comes in beside the others
            var layer = part.Layers.FirstOrDefault(l => l.Program == program);
            if (layer == null)
            {
                layer = new Layer { Program = program, Low = part.Role == PartRole.Bass ? 28 : 55, Velocity = velocity };
                part.Layers.Add(layer);
            }

            // a variation B with nothing of its own yet starts from what was played, not from A
            var before = VariationOf(slot) == 0 ? layer.A : layer.B ?? Take.Empty;
            var after = new Take(before.Pattern, Merge(before.Notes, played));
            if (VariationOf(slot) == 0) layer.A = after; else layer.B = after;

            var justPlayed = new Layer { Program = program, Low = layer.Low, Velocity = velocity };
            LastTake = Audible(Array.Empty<DrumLine>(), new[] { ToPart(part, justPlayed, new Take("", played)) });
            return bars;
        }

        // Two recordings of an instrument as one. On a pitch both have, the key is down while it
        // is down in either of them, and it is struck whenever one of them strikes it.
        private NoteLine[] Merge(NoteLine[] before, NoteLine[] played)
        {
            var lines = new SortedDictionary<int, string>();
            foreach (var line in before) lines[line.Note] = line.Pattern;

            foreach (var line in played)
            {
                if (!lines.TryGetValue(line.Note, out string? old))
                {
                    lines[line.Note] = line.Pattern;
                    continue;
                }

                var both = new char[Math.Max(old.Length, line.Pattern.Length)];
                for (int i = 0; i < both.Length; i++)
                {
                    char a = line.Pattern[i % line.Pattern.Length], b = old[i % old.Length];
                    both[i] = a != '.' && a != '-' ? a
                            : b != '.' && b != '-' ? b
                            : a == '-' || b == '-' ? '-' : '.';
                }
                lines[line.Note] = Shortest(new string(both));
            }

            return lines.Select(line => new NoteLine(line.Key, line.Value)).ToArray();
        }

        // The nearest line of the quantize grid, in steps
        private int Snap(int tick)
        {
            int stepsPerLine = resolution / Quantize;
            return (int)Math.Round(tick / (double)(stepsPerLine * TicksPerStep)) * stepsPerLine;
        }

        // The same bar played four times is a one-bar pattern
        private string Shortest(string pattern)
        {
            while (pattern.Length % (2 * StepsPerBar) == 0 && pattern[..(pattern.Length / 2)] == pattern[(pattern.Length / 2)..])
                pattern = pattern[..(pattern.Length / 2)];
            return pattern;
        }

        // The grid of the saved style: its own while everything recorded fits it, otherwise the
        // coarsest one that does (12 steps per beat for sixteenth notes mixed with triplets)
        private int SavedGrid()
        {
            if (Fits(StepsPerBeat)) return StepsPerBeat;

            for (int grid = 2; grid < resolution; grid++)
                if (resolution % grid == 0 && Fits(grid)) return grid;
            return resolution;
        }

        private bool Fits(int grid)
        {
            int coarser = resolution / grid;
            bool TakeFits(Take? take) =>
                take == null || (Fits(take.Pattern, coarser, '-') && take.Notes.All(line => Fits(line.Pattern, coarser, '-')));

            return drums.All(kit => kit.Values.All(pattern => Fits(pattern, coarser, '.')))
                && parts.All(part => part.Layers.All(layer => TakeFits(layer.A) && TakeFits(layer.B)));
        }

        // Nothing is lost on a coarser grid when no note starts or ends between two of its steps
        private static bool Fits(string pattern, int coarser, char hold)
        {
            for (int i = 0; i < pattern.Length; i++)
            {
                if (i % coarser == 0) continue;

                char first = pattern[i - i % coarser];
                if (pattern[i] != (first == '.' ? '.' : hold)) return false;
            }
            return true;
        }

        // x.x. -> x...x... on a grid twice as fine. hold is what follows a note there: '-' keeps an
        // instrument sounding for as long as before, '.' is for drums.
        private static string Stretch(string pattern, int finer, char hold)
        {
            if (finer == 1) return pattern;

            var text = new StringBuilder(pattern.Length * finer);
            foreach (char c in pattern) text.Append(c).Append(c == '.' ? '.' : hold, finer - 1);
            return text.ToString();
        }

        private static string Shrink(string pattern, int coarser)
        {
            if (coarser == 1) return pattern;

            var text = new StringBuilder(pattern.Length / coarser);
            for (int i = 0; i < pattern.Length; i += coarser) text.Append(pattern[i]);
            return text.ToString();
        }

        // The draft as a .style file
        public string ToText(int tempo)
        {
            int grid = SavedGrid(), coarser = resolution / grid;
            string Spaced(string pattern) => WithSpaces(Shrink(pattern, coarser), grid);

            var text = new StringBuilder();
            text.Append($"name = {Name}\ntempo = {tempo}\nbeats = {BeatsPerBar}\nsteps = {grid}\n");

            string[] drumSections = { "[drums]", "[drums b]", "[fill]" };
            for (int i = 0; i < drums.Length; i++)
            {
                if (drums[i].Count == 0) continue;
                text.Append($"\n{drumSections[i]}\n");
                foreach (var line in drums[i].OrderBy(d => d.Key)) text.Append($"{Styles.DrumName(line.Key)} = {Spaced(line.Value)}\n");
            }

            string[] partSections = { "bass", "chord", "pad" };
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                int written = 0;
                foreach (var layer in part.Layers)
                {
                    if (layer.IsEmpty) continue;

                    // [chord], then [chord 2], [chord 3]... for the other instruments of the part
                    string section = ++written == 1 ? partSections[i] : $"{partSections[i]} {written}";
                    text.Append($"\n[{section}]\nprogram = {layer.Program}\nlow = {layer.Low}\nvelocity = {layer.Velocity}\n");
                    // only recorded notes go up or down with the chords; degree patterns use low
                    if (layer.A.Notes.Length > 0 || layer.B?.Notes.Length > 0)
                    {
                        text.Append($"up = {Chord.PitchName(part.Up)}\n");
                        if (part.Role != PartRole.Bass) text.Append($"tensions = {(part.Tensions ? "on" : "off")}\n");
                    }
                    Write("a", layer.A);
                    if (layer.B != null) Write("b", layer.B);
                }
            }

            return text.ToString();

            void Write(string variation, Take take)
            {
                // an empty pattern is still written when there are no notes either: for b it
                // means silence, while no b at all means "the same as a"
                if (take.Pattern.Length > 0 || take.Notes.Length == 0) text.Append($"{variation} = {Spaced(take.Pattern)}\n");
                foreach (var line in take.Notes) text.Append($"{variation} {Chord.NoteName(line.Note)} = {Spaced(line.Pattern)}\n");
            }
        }

        // x...x...x...x... -> x... x... x... x... with | between bars
        private string WithSpaces(string pattern, int stepsPerBeat)
        {
            var text = new StringBuilder();
            for (int i = 0; i < pattern.Length; i++)
            {
                if (i > 0 && i % (BeatsPerBar * stepsPerBeat) == 0) text.Append(" | ");
                else if (i > 0 && i % stepsPerBeat == 0) text.Append(' ');
                text.Append(pattern[i]);
            }
            return text.ToString();
        }
    }
}
