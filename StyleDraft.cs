using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace piano
{
    // A style being created by playing. It is made of nine parts ("slots"): drums, bass, chord and
    // pad for variation A, the same four for variation B, and the fill-in. Each recording is
    // snapped to the step grid and turned into the same pattern text a .style file uses.
    public sealed class StyleDraft
    {
        public const int SlotCount = 9;
        public const int FillSlot = 8;
        public const int MaxBars = 4;

        private sealed class PartDraft
        {
            public PartRole Role;
            public int Program, Low, Velocity = 80;
            public string A = "";
            public string? B;   // null: same as A
        }

        // drum note -> pattern, for variation A, variation B and the fill
        private readonly Dictionary<int, string>[] drums = { new(), new(), new() };

        private readonly PartDraft[] parts =
        {
            new PartDraft { Role = PartRole.Bass, Program = 33, Low = 28 },
            new PartDraft { Role = PartRole.Chord, Program = 0, Low = 55 },
            new PartDraft { Role = PartRole.Chord, Program = 48, Low = 55 }
        };

        // What a slot held before each recording or erasure, most recent on top
        private readonly Stack<(int Slot, Dictionary<int, string>? Drums, PartDraft? Part)> history = new();

        // Just what the last recording added, to check a take on its own. Null when there is none.
        public Style? LastTake { get; private set; }

        public string Name { get; }
        public int BeatsPerBar { get; }
        public int StepsPerBeat { get; }

        private int StepsPerBar => BeatsPerBar * StepsPerBeat;
        private int TicksPerStep => AudioEngine.TicksPerBeat / StepsPerBeat;

        public StyleDraft(string name, int beatsPerBar, int stepsPerBeat)
        {
            Name = name;
            BeatsPerBar = beatsPerBar;
            StepsPerBeat = stepsPerBeat;
        }

        public static StyleDraft From(Style style, string name)
        {
            var draft = new StyleDraft(name, style.BeatsPerBar, style.StepsPerBeat);

            foreach (var line in style.DrumsA) draft.drums[0][line.Note] = line.Pattern;
            if (style.DrumsB != style.DrumsA) foreach (var line in style.DrumsB) draft.drums[1][line.Note] = line.Pattern;
            if (style.Fill != style.DrumsA) foreach (var line in style.Fill) draft.drums[2][line.Note] = line.Pattern;

            // bass, then the first chord part, then the second one as the pad
            int chords = 0;
            foreach (var part in style.Parts)
            {
                int index = part.Role == PartRole.Bass ? 0 : ++chords;
                if (index >= draft.parts.Length || draft.parts[index].A.Length > 0) continue;

                draft.parts[index] = new PartDraft
                {
                    Role = part.Role, Program = part.Program, Low = part.LowNote, Velocity = part.Velocity,
                    A = part.A, B = part.B == part.A ? null : part.B
                };
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

            string part = (slot % 4) switch
            {
                0 => L.T("Drums", "Bateria"),
                1 => L.T("Bass", "Baixo"),
                2 => L.T("Chords", "Acordes"),
                _ => "Pad"
            };
            return part + (slot < 4 ? " A" : " B");
        }

        public string DescribeSlot(int slot)
        {
            bool empty = IsDrumSlot(slot)
                ? drums[slot == FillSlot ? 2 : VariationOf(slot)].Count == 0
                : VariationOf(slot) == 0 ? parts[PartOf(slot)].A.Length == 0 : parts[PartOf(slot)].B == null;

            string state = !empty ? L.T("recorded", "gravado")
                         : slot < 4 ? L.T("empty", "vazio")
                         : L.T("same as A", "igual à A");
            return SlotName(slot) + ", " + state;
        }

        private void Remember(int slot)
        {
            if (IsDrumSlot(slot))
            {
                history.Push((slot, new Dictionary<int, string>(drums[slot == FillSlot ? 2 : VariationOf(slot)]), null));
            }
            else
            {
                var part = parts[PartOf(slot)];
                history.Push((slot, null, new PartDraft { Role = part.Role, Program = part.Program, Low = part.Low, Velocity = part.Velocity, A = part.A, B = part.B }));
            }
        }

        // Takes back the last recording or erasure. Returns the slot it belonged to, or -1 if there is none.
        public int Undo()
        {
            if (history.Count == 0) return -1;
            LastTake = null;

            var (slot, savedDrums, savedPart) = history.Pop();
            if (savedDrums != null)
            {
                var target = drums[slot == FillSlot ? 2 : VariationOf(slot)];
                target.Clear();
                foreach (var line in savedDrums) target[line.Key] = line.Value;
            }
            else
            {
                parts[PartOf(slot)] = savedPart!;
            }
            return slot;
        }

        public void Clear(int slot)
        {
            Remember(slot);

            if (IsDrumSlot(slot)) drums[slot == FillSlot ? 2 : VariationOf(slot)].Clear();
            else if (VariationOf(slot) == 0) parts[PartOf(slot)].A = "";
            else parts[PartOf(slot)].B = null;
        }

        // The style heard while a slot is recorded or auditioned: the variation of that slot (the
        // fill-in drums for the fill slot), without the part about to be replaced.
        public Style Backing(int slot, bool withoutSlot)
        {
            int variation = VariationOf(slot);
            var source = slot == FillSlot && drums[2].Count > 0 ? drums[2]
                       : variation == 1 && drums[1].Count > 0 ? drums[1]
                       : drums[0];
            var lines = source.Select(d => new DrumLine(d.Key, d.Value)).ToArray();

            var backing = new List<Part>();
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                string pattern = variation == 1 ? part.B ?? part.A : part.A;
                if (withoutSlot && !IsDrumSlot(slot) && PartOf(slot) == i) pattern = "";
                backing.Add(new Part(part.Role, part.Program, part.Low, part.Velocity, pattern, pattern));
            }

            return new Style
            {
                Name = Name, NamePt = Name, BeatsPerBar = BeatsPerBar, StepsPerBeat = StepsPerBeat,
                DrumsA = lines, DrumsB = lines, Fill = lines, Parts = backing.ToArray()
            };
        }

        // Only the part of a slot, with everything else silent
        public Style Solo(int slot)
        {
            var full = Backing(slot, withoutSlot: false);
            bool drumSlot = IsDrumSlot(slot);
            var lines = drumSlot ? full.DrumsA : Array.Empty<DrumLine>();

            return new Style
            {
                Name = Name, NamePt = Name, BeatsPerBar = BeatsPerBar, StepsPerBeat = StepsPerBeat,
                DrumsA = lines, DrumsB = lines, Fill = lines,
                Parts = full.Parts.Select((part, i) => !drumSlot && i == PartOf(slot) ? part : part with { A = "", B = "" }).ToArray()
            };
        }

        // Turns a recording into the pattern of a slot. Returns its length in bars, 0 if nothing was played.
        // program: the instrument the part was played with.
        public int Apply(int slot, CapturedNote[] notes, int endTick, int program)
        {
            int limit = MaxBars * StepsPerBar;
            var hits = notes
                .Select(n => (Step: Snap(n.Start), End: Snap(n.End), n.Note, n.Velocity))
                .Where(h => h.Step < limit)
                .OrderBy(h => h.Step)
                .ToList();
            if (hits.Count == 0) return 0;

            Remember(slot);

            // whole bars: as many as were played in, or as were left running before stopping
            int bars = hits[^1].Step / StepsPerBar + 1;
            bars = Math.Max(bars, Math.Min(MaxBars, (int)Math.Round(endTick / (double)(StepsPerBar * TicksPerStep))));
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

                var onlyTake = takeLines.ToArray();
                LastTake = new Style
                {
                    Name = Name, NamePt = Name, BeatsPerBar = BeatsPerBar, StepsPerBeat = StepsPerBeat,
                    DrumsA = onlyTake, DrumsB = onlyTake, Fill = onlyTake
                };
                return bars;
            }

            var part = parts[PartOf(slot)];
            var steps = new char[length];
            Array.Fill(steps, '.');
            int firstRoot = hits.Where(h => h.Note % 12 == 0).Select(h => h.Note).DefaultIfEmpty(0).Min();

            foreach (var group in hits.GroupBy(h => h.Step))
            {
                char c;
                if (part.Role == PartRole.Bass)
                {
                    // played over C: C is the root, E the third, G the fifth, A or B flat the seventh/sixth
                    int note = group.First().Note;
                    c = note % 12 == 0 && note >= firstRoot + 12 ? '8' : "113335557777"[note % 12];
                }
                else if (group.Count() > 1)
                {
                    c = group.Max(h => h.Velocity) >= 110 ? 'X' : 'x';
                }
                else
                {
                    // one note at a time is an arpeggio: C, E and G are the first three chord notes
                    c = (group.First().Note % 12) switch { 0 => '1', 3 or 4 => '2', 7 => '3', _ => '4' };
                }

                int end = Math.Min(length, Math.Max(group.Key + 1, group.Max(h => h.End)));
                steps[group.Key] = c;
                for (int i = group.Key + 1; i < length; i++) steps[i] = i < end ? '-' : '.';
            }

            string result = Shortest(new string(steps));
            if (VariationOf(slot) == 0) part.A = result; else part.B = result;
            part.Program = program;
            part.Velocity = Math.Clamp((int)hits.Average(h => h.Velocity) * 8 / 10, 1, 127);
            // an instrument recording replaces its part, so the take is that part alone
            LastTake = Solo(slot);
            return bars;
        }

        private int Snap(int tick) => (int)Math.Round(tick / (double)TicksPerStep);

        // The same bar played four times is a one-bar pattern
        private string Shortest(string pattern)
        {
            while (pattern.Length % (2 * StepsPerBar) == 0 && pattern[..(pattern.Length / 2)] == pattern[(pattern.Length / 2)..])
                pattern = pattern[..(pattern.Length / 2)];
            return pattern;
        }

        // The draft as a .style file
        public string ToText(int tempo)
        {
            var text = new StringBuilder();
            text.Append($"name = {Name}\ntempo = {tempo}\nbeats = {BeatsPerBar}\nsteps = {StepsPerBeat}\n");

            string[] drumSections = { "[drums]", "[drums b]", "[fill]" };
            for (int i = 0; i < drums.Length; i++)
            {
                if (drums[i].Count == 0) continue;
                text.Append($"\n{drumSections[i]}\n");
                foreach (var line in drums[i].OrderBy(d => d.Key)) text.Append($"{Styles.DrumName(line.Key)} = {Spaced(line.Value)}\n");
            }

            string[] partSections = { "[bass]", "[chord]", "[pad]" };
            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];
                if (part.A.Length == 0 && string.IsNullOrEmpty(part.B)) continue;

                text.Append($"\n{partSections[i]}\nprogram = {part.Program}\nlow = {part.Low}\nvelocity = {part.Velocity}\na = {Spaced(part.A)}\n");
                if (part.B != null) text.Append($"b = {Spaced(part.B)}\n");
            }

            return text.ToString();
        }

        // x...x...x...x... -> x... x... x... x... with | between bars
        private string Spaced(string pattern)
        {
            var text = new StringBuilder();
            for (int i = 0; i < pattern.Length; i++)
            {
                if (i > 0 && i % StepsPerBar == 0) text.Append(" | ");
                else if (i > 0 && i % StepsPerBeat == 0) text.Append(' ');
                text.Append(pattern[i]);
            }
            return text.ToString();
        }
    }
}
