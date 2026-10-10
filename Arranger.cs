using System;
using System.Collections.Generic;
using System.Linq;

namespace piano
{
    public sealed class ChordType
    {
        public string Suffix { get; }
        public int[] Intervals { get; }
        public int Mask { get; }

        public ChordType(string suffix, params int[] intervals)
        {
            Suffix = suffix;
            Intervals = intervals;
            // a ninth is written as 14, but any key with that name counts when the chord is played
            foreach (int interval in intervals) Mask |= 1 << (interval % 12);
        }

        // The seventh, or the sixth of a sixth chord; 0 when the chord has neither
        public int Seventh => Intervals.FirstOrDefault(i => i >= 9 && i <= 11);
        public bool HasNinth => Intervals.Contains(14);

        // Intervals the bass line walks through
        public int Third => Intervals.Length > 2 ? Intervals[1] : Intervals[^1];
        public int Fifth => Intervals.FirstOrDefault(i => i >= 6 && i <= 8, 7);
        public int Color => Seventh > 0 ? Seventh : Intervals.Contains(4) ? 9 : Intervals.Contains(3) ? 10 : 12;
    }

    public sealed record Chord(int Root, ChordType Type)
    {
        private static readonly string[] NoteNames = { "C", "C#", "D", "Eb", "E", "F", "F#", "G", "Ab", "A", "Bb", "B" };

        public string Name => NoteNames[Root] + Type.Suffix;

        public static string NoteName(int note) => NoteNames[note % 12] + (note / 12 - 1);
        public static string PitchName(int pitch) => NoteNames[pitch % 12];
    }

    public static class ChordDetector
    {
        public static readonly ChordType Major = new("", 0, 4, 7);

        // In priority order: when the same notes spell two chords (C6 and Am7, for instance) and the
        // lowest note does not settle it, the earlier entry wins.
        private static readonly ChordType[] Types =
        {
            Major, new("m", 0, 3, 7), new("7", 0, 4, 7, 10), new("m7", 0, 3, 7, 10), new("maj7", 0, 4, 7, 11),
            new("dim", 0, 3, 6), new("m7b5", 0, 3, 6, 10), new("dim7", 0, 3, 6, 9), new("aug", 0, 4, 8),
            new("sus4", 0, 5, 7), new("sus2", 0, 2, 7), new("7sus4", 0, 5, 7, 10), new("6", 0, 4, 7, 9), new("m6", 0, 3, 7, 9),
            new("mMaj7", 0, 3, 7, 11),
            // ninths, over a seventh chord or added to a plain one
            new("9", 0, 4, 7, 10, 14), new("m9", 0, 3, 7, 10, 14), new("maj9", 0, 4, 7, 11, 14),
            new("add9", 0, 4, 7, 14), new("madd9", 0, 3, 7, 14),
            // sevenths and ninths played without the fifth
            new("7", 0, 4, 10), new("m7", 0, 3, 10), new("maj7", 0, 4, 11),
            new("9", 0, 4, 10, 14), new("m9", 0, 3, 10, 14), new("maj9", 0, 4, 11, 14),
            // two fingers
            new("", 0, 4), new("m", 0, 3), new("5", 0, 7), new("7", 0, 10)
        };

        // Returns null when the notes do not suggest any chord
        public static Chord? Detect(IReadOnlyCollection<int> notes)
        {
            if (notes.Count == 0) return null;

            int mask = 0;
            foreach (int note in notes) mask |= 1 << (note % 12);
            int bass = notes.Min() % 12;

            // one finger plays a major chord
            if ((mask & (mask - 1)) == 0) return new Chord(bass, Major);

            Chord? inversion = null;
            foreach (var type in Types)
            {
                for (int root = 0; root < 12; root++)
                {
                    if (Rotate(type.Mask, root) != mask) continue;
                    if (root == bass) return new Chord(root, type);
                    inversion ??= new Chord(root, type);
                }
            }
            if (inversion != null) return inversion;

            // Nothing matches exactly: take the largest chord contained in the notes and ignore the
            // extra ones (elevenths and other tensions).
            Chord? best = null;
            int bestScore = 0;
            for (int t = 0; t < Types.Length; t++)
            {
                var type = Types[t];
                if (type.Intervals.Length < 3) continue;

                for (int root = 0; root < 12; root++)
                {
                    int rotated = Rotate(type.Mask, root);
                    if ((rotated & mask) != rotated) continue;

                    int score = type.Intervals.Length * 100 + (root == bass ? 50 : 0) - t;
                    if (score > bestScore) { bestScore = score; best = new Chord(root, type); }
                }
            }
            return best;
        }

        private static int Rotate(int mask, int semitones) => ((mask << semitones) | (mask >> (12 - semitones))) & 0xFFF;
    }

    // Plays the selected style following the chords held below the split point. Except for the
    // posting methods and the volatile status fields, everything here runs on the audio thread.
    public sealed class Arranger
    {
        public const int DrumChannel = 9;
        public const int FirstPartChannel = 10;

        private enum Section { Intro, Main, Fill, Ending, Final }

        private readonly AudioEngine engine;
        private readonly List<(int Note, int OffStep)> drumsOn = new();
        private readonly List<int>[] sounding = new List<int>[Styles.MaxParts];
        // Note lines that are playing: where each one sounds in the current chord (-1 while there
        // is no chord yet) and how hard it was struck. Added marks the ninth a line brings along
        // besides its own note (-1 while the chord has none).
        private readonly List<(int Line, int Note, int Velocity, bool Added)>[] lineNotes = new List<(int, int, int, bool)>[Styles.MaxParts];
        private readonly char[] current = new char[Styles.MaxParts];
        private readonly int[] voicing = new int[8];

        private volatile Style style = Styles.All[0];
        private Style? pendingStyle;
        private Chord? chord;
        private Section section;
        private int startTick, lastStep, bar, finalStep;
        private int pendingVariation = -1;
        private bool fillQueued, endRequested;
        private int volume = 100;

        // Preview: plays a style on a fixed C chord, with the whole keyboard free. Used by the
        // style recorder, where the patterns are heard (and played in) over C.
        private Style? styleBeforePreview;
        private int variationBeforePreview;
        public volatile bool Preview;

        public volatile bool Playing;
        public volatile int Variation;
        public volatile string ChordName = "";

        public Arranger(AudioEngine engine)
        {
            this.engine = engine;
            for (int i = 0; i < sounding.Length; i++)
            {
                sounding[i] = new List<int>();
                lineNotes[i] = new List<(int, int, int, bool)>();
            }
        }

        public int BeatsPerBar => style.BeatsPerBar;
        internal int StartBeat => startTick / AudioEngine.TicksPerBeat;

        private int TicksPerStep => AudioEngine.TicksPerBeat / style.StepsPerBeat;
        private int StepsPerBar => style.BeatsPerBar * style.StepsPerBeat;

        // A drum is let go about a sixteenth note after it is hit. On a finer grid one step would
        // be too soon and could cut its sound short.
        private int DrumSteps => (style.StepsPerBeat + 3) / 4;

        public void Start(bool withIntro) => engine.Post(() => DoStart(withIntro));
        public void Stop() => engine.Post(StopNow);
        public void End() => engine.Post(() => { if (Playing) endRequested = true; });
        public void Fill(bool switchVariation) => engine.Post(() => DoFill(switchVariation));
        public void SetStyle(Style next) => engine.Post(() => { if (Playing) pendingStyle = next; else style = next; });
        public void SetVolume(int value) => engine.Post(() => { volume = value; if (Playing) SendVolume(); });
        public void PlayPreview(Style backing) => engine.Post(() => { engine.ResetTransport(0); StartPreview(backing); });

        internal void StartPreview(Style backing)
        {
            StopNow();
            styleBeforePreview = style;
            variationBeforePreview = Variation;
            style = backing;
            Variation = 0;
            Preview = true;

            startTick = 0;
            Begin(Section.Main);
            chord = new Chord(0, ChordDetector.Major);
            ChordName = chord.Name;
        }

        private void DoStart(bool withIntro)
        {
            if (Playing) return;

            if (engine.Sequencer.Active)
            {
                // join the song at the next bar line
                int barTicks = BeatsPerBar * AudioEngine.TicksPerBeat;
                startTick = (Math.Max(engine.Tick, 0) + barTicks - 1) / barTicks * barTicks;
            }
            else
            {
                engine.ResetTransport(0);
                startTick = 0;
            }

            Begin(withIntro ? Section.Intro : Section.Main);
        }

        // Called by the sequencer so that accompaniment and song start together
        internal void Restart()
        {
            ReleaseAll();
            startTick = 0;
            Begin(Section.Main);
        }

        private void Begin(Section first)
        {
            section = first;
            lastStep = -1;
            bar = -1;
            pendingVariation = -1;
            fillQueued = endRequested = false;
            Array.Fill(current, '.');
            Playing = true;
            SendSetup();
        }

        internal void StopNow()
        {
            if (!Playing) return;
            ReleaseAll();
            Playing = false;
            pendingStyle = null;
            chord = null;
            ChordName = "";
            engine.ClearChordZone();

            if (Preview)
            {
                style = styleBeforePreview!;
                Variation = variationBeforePreview;
                Preview = false;
            }
        }

        private void DoFill(bool switchVariation)
        {
            if (!Playing)
            {
                if (switchVariation) Variation = 1 - Variation;
                return;
            }

            if (switchVariation) pendingVariation = 1 - Variation;

            // pressed on the last beat, the fill takes the whole next bar instead
            int barStep = Math.Max(lastStep, 0) % StepsPerBar;
            if (barStep >= StepsPerBar - style.StepsPerBeat) fillQueued = true;
            else if (section == Section.Main) section = Section.Fill;
        }

        private void SendSetup()
        {
            SendVolume();
            for (int i = 0; i < style.Parts.Length; i++)
                engine.AccompOut(FirstPartChannel + i, 0xC0, style.Parts[i].Program, 0);
        }

        private void SendVolume()
        {
            for (int channel = DrumChannel; channel < FirstPartChannel + Styles.MaxParts; channel++)
                engine.AccompOut(channel, 0xB0, 7, volume);
        }

        internal void SetChordNotes(IReadOnlyCollection<int> notes)
        {
            // releasing the keys keeps the last chord, as on an arranger keyboard
            var detected = ChordDetector.Detect(notes);
            if (detected == null || detected == chord) return;

            chord = detected;
            ChordName = detected.Name;

            for (int i = 0; i < style.Parts.Length; i++)
            {
                MoveLines(i);
                if (current[i] == '.') continue;
                Release(i);
                Trigger(i);
            }
        }

        internal void Advance(int tick)
        {
            while (Playing && tick >= startTick && lastStep < (tick - startTick) / TicksPerStep)
                PlayStep(lastStep + 1);
        }

        private void PlayStep(int step)
        {
            if (pendingStyle != null && step % StepsPerBar == 0)
            {
                // styles change on the bar line; the new one may have a different meter or step size
                ReleaseAll();
                startTick += step * TicksPerStep;
                style = pendingStyle;
                pendingStyle = null;
                Array.Fill(current, '.');
                SendSetup();
                step = 0;
                bar = -1;
            }

            lastStep = step;
            int barStep = step % StepsPerBar;
            ReleaseDrums(step);

            if (barStep == 0)
            {
                if (section == Section.Ending) { PlayFinalHit(step); return; }

                if (section == Section.Fill || (section == Section.Intro && step > 0)) section = Section.Main;

                if (endRequested) { section = Section.Ending; endRequested = false; }
                else if (fillQueued) { section = Section.Fill; fillQueued = false; }
                else if (pendingVariation >= 0 && section == Section.Main)
                {
                    // the other variation has note lines of its own
                    for (int i = 0; i < lineNotes.Length; i++) ReleaseLines(i);
                    Variation = pendingVariation;
                    pendingVariation = -1;
                }

                if (section != Section.Intro) bar++;
            }

            if (section == Section.Final)
            {
                if (step >= finalStep) StopNow();
                return;
            }

            int position = Math.Max(bar, 0) * StepsPerBar + barStep;

            var lines = section != Section.Main ? style.Fill : Variation == 1 ? style.DrumsB : style.DrumsA;
            foreach (var line in lines)
            {
                int velocity = line.Pattern[position % line.Pattern.Length] switch { 'x' => 90, 'X' => 115, 'o' => 55, _ => 0 };
                if (velocity > 0) Drum(line.Note, velocity);
            }

            if (section == Section.Intro) return;

            for (int i = 0; i < style.Parts.Length; i++)
            {
                var notes = Lines(i);
                for (int line = 0; line < notes.Length; line++)
                {
                    char hit = notes[line].Pattern[position % notes[line].Pattern.Length];
                    if (hit == '-') continue;

                    ReleaseLine(i, line);
                    if (hit != '.') SoundLine(i, line, hit);
                }

                string pattern = Variation == 1 ? style.Parts[i].B : style.Parts[i].A;
                char c = pattern.Length == 0 ? '.' : pattern[position % pattern.Length];
                if (c == '-') continue;

                Release(i);
                current[i] = c;
                Trigger(i);
            }
        }

        private void PlayFinalHit(int step)
        {
            section = Section.Final;
            finalStep = step + style.StepsPerBeat * 2;

            Drum(49, 110);
            Drum(36, 110);
            for (int i = 0; i < style.Parts.Length; i++)
            {
                Release(i);
                ReleaseLines(i);
                current[i] = style.Parts[i].Role == PartRole.Bass ? '1' : 'x';
                Trigger(i);
            }
        }

        private void Drum(int note, int velocity)
        {
            // hit again before being let go: the note-off of the first hit must not end the second
            for (int i = 0; i < drumsOn.Count; i++)
            {
                if (drumsOn[i].Note != note) continue;
                engine.AccompOut(DrumChannel, 0x80, note, 0);
                drumsOn.RemoveAt(i);
                break;
            }

            engine.AccompOut(DrumChannel, 0x90, note, velocity);
            drumsOn.Add((note, lastStep + DrumSteps));
        }

        // Lets go of the drums due by this step; of all of them when no step is given
        private void ReleaseDrums(int step = int.MaxValue)
        {
            for (int i = drumsOn.Count - 1; i >= 0; i--)
            {
                if (drumsOn[i].OffStep > step) continue;
                engine.AccompOut(DrumChannel, 0x80, drumsOn[i].Note, 0);
                drumsOn.RemoveAt(i);
            }
        }

        private void Trigger(int index)
        {
            char c = current[index];
            if (c == '.' || chord == null) return;

            var part = style.Parts[index];
            var type = chord.Type;
            int velocity = Math.Min(127, part.Velocity + (c == 'X' ? 20 : 0));
            int channel = FirstPartChannel + index;

            if (part.Role == PartRole.Bass)
            {
                int interval = c switch { '3' => type.Third, '5' => type.Fifth, '7' => type.Color, '8' => 12, _ => 0 };
                Sound(index, channel, InRegister(chord.Root, part.LowNote) + interval, velocity);
                return;
            }

            // Close voicing inside the octave above LowNote: every chord lands in the same register,
            // so changes move each voice as little as possible.
            int count = type.Intervals.Length;
            for (int i = 0; i < count; i++) voicing[i] = InRegister(chord.Root + type.Intervals[i], part.LowNote);
            Array.Sort(voicing, 0, count);

            if (c == 'x' || c == 'X')
            {
                for (int i = 0; i < count; i++) Sound(index, channel, voicing[i], velocity);
            }
            else
            {
                int tone = c - '1';
                Sound(index, channel, voicing[tone % count] + 12 * (tone / count), velocity);
            }
        }

        private static int InRegister(int pitchClass, int lowNote) => lowNote + ((pitchClass - lowNote) % 12 + 12) % 12;

        private void Sound(int index, int channel, int note, int velocity)
        {
            note = Math.Clamp(note, 0, 127);
            engine.AccompOut(channel, 0x90, note, velocity);
            sounding[index].Add(note);
        }

        private void Release(int index)
        {
            foreach (int note in sounding[index]) engine.AccompOut(FirstPartChannel + index, 0x80, note, 0);
            sounding[index].Clear();
        }

        private NoteLine[] Lines(int index) => Variation == 1 ? style.Parts[index].NotesB : style.Parts[index].NotesA;

        // Where a note written over C major sounds in the current chord. Only its timing was
        // touched when it was recorded, so it stays as close to what was played as the chord
        // allows: the third, the fifth and the seventh follow the type of the chord, and the
        // whole part goes up to the root (as far as the one in "up") or down to it (after that).
        //
        // The chords and the pad also get the seventh and the ninth nobody recorded, through
        // their tension line: it plays the seventh, just below, instead of the root, and its
        // added note is the ninth, just above. Returns -1 when there is no ninth to add.
        private int InChord(int index, int line, bool added)
        {
            var part = style.Parts[index];
            var type = chord!.Type;
            int note = Lines(index)[line].Note;
            int interval = note % 12;
            int adapted;

            if (added)
            {
                if (!type.HasNinth || Plays(index, 2)) return -1;
                adapted = 2;
            }
            else if (line == TensionLine(index) && type.Seventh > 0 && !Plays(index, type.Seventh)) adapted = type.Seventh - 12;
            else adapted = Adapted(interval);

            int shift = chord.Root <= part.Up ? chord.Root : chord.Root - 12;
            return Math.Clamp(note - interval + adapted + shift, 0, 127);
        }

        private int Adapted(int interval) => interval switch
        {
            4 => chord!.Type.Third,
            7 => chord!.Type.Fifth,
            10 or 11 when chord!.Type.Seventh > 0 => chord.Type.Seventh,
            _ => interval
        };

        // Whether some line of the part already lands on that interval of the chord
        private bool Plays(int index, int interval)
        {
            foreach (var line in Lines(index))
                if (Adapted(line.Note % 12) == interval) return true;
            return false;
        }

        // The line that takes the tensions: the highest root the part has. A lower one keeps
        // playing the root. -1 for the bass, for a part with tensions off or with no root.
        private int TensionLine(int index)
        {
            var part = style.Parts[index];
            if (part.Role == PartRole.Bass || !part.Tensions) return -1;

            var lines = Lines(index);
            int top = -1;
            for (int i = 0; i < lines.Length; i++)
                if (lines[i].Note % 12 == 0 && (top < 0 || lines[i].Note > lines[top].Note)) top = i;
            return top;
        }

        private void SoundLine(int index, int line, char hit)
        {
            var part = style.Parts[index];
            int velocity = hit switch
            {
                'X' => Math.Min(127, part.Velocity + 20),
                'o' => Math.Max(1, part.Velocity * 6 / 10),
                _ => part.Velocity
            };

            Hold(index, line, velocity, false);
            if (line == TensionLine(index)) Hold(index, line, velocity, true);
        }

        private void Hold(int index, int line, int velocity, bool added)
        {
            int note = chord == null ? -1 : InChord(index, line, added);
            if (note >= 0) engine.AccompOut(FirstPartChannel + index, 0x90, note, velocity);
            lineNotes[index].Add((line, note, velocity, added));
        }

        private void ReleaseLine(int index, int line)
        {
            var held = lineNotes[index];
            for (int i = held.Count - 1; i >= 0; i--)
            {
                if (held[i].Line != line) continue;
                if (held[i].Note >= 0) engine.AccompOut(FirstPartChannel + index, 0x80, held[i].Note, 0);
                held.RemoveAt(i);
            }
        }

        private void ReleaseLines(int index)
        {
            foreach (var (_, note, _, _) in lineNotes[index])
                if (note >= 0) engine.AccompOut(FirstPartChannel + index, 0x80, note, 0);
            lineNotes[index].Clear();
        }

        // The chord changed: the notes that are sounding go to their place in the new one
        private void MoveLines(int index)
        {
            var held = lineNotes[index];
            for (int i = 0; i < held.Count; i++)
            {
                var (line, note, velocity, added) = held[i];
                int moved = InChord(index, line, added);
                if (moved == note) continue;

                if (note >= 0) engine.AccompOut(FirstPartChannel + index, 0x80, note, 0);
                if (moved >= 0) engine.AccompOut(FirstPartChannel + index, 0x90, moved, velocity);
                held[i] = (line, moved, velocity, added);
            }
        }

        private void ReleaseAll()
        {
            for (int i = 0; i < sounding.Length; i++)
            {
                Release(i);
                ReleaseLines(i);
            }
            ReleaseDrums();
        }
    }
}
