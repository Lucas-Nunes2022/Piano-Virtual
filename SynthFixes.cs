using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MeltySynth;

namespace piano
{
    // Two corrections to how MeltySynth plays. It has no setting for either, so they reach into
    // its private fields; if a future version is different inside, they simply do nothing.
    internal static class SynthFixes
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

        // General MIDI drums that silence each other: hi-hats, whistles, guiros, cuicas and triangles
        private static readonly int[][] ExclusiveDrums =
        {
            new[] { 42, 44, 46 }, new[] { 71, 72 }, new[] { 73, 74 }, new[] { 78, 79 }, new[] { 80, 81 }
        };

        // Position of the exclusive class among the generators of a SoundFont region, and the
        // first class handed out here, far from the low numbers sound fonts use for their own
        private const int ExclusiveClassGenerator = 57;
        private const int FirstExclusiveClass = 120;

        // Closing the hi-hat must stop the open one, which a sound font arranges by giving both
        // the same exclusive class. Many leave it out of their drum kits, and then the open hi-hat
        // rings over the closed one. A kit that has no class for one of the groups gets it here.
        public static void AddDrumExclusiveClasses(SoundFont font)
        {
            try
            {
                var generators = typeof(InstrumentRegion).GetField("gs", Hidden);
                if (generators == null) return;

                foreach (var preset in font.Presets.Where(p => p.BankNumber == 128))
                {
                    for (int group = 0; group < ExclusiveDrums.Length; group++)
                    {
                        var regions = RegionsOf(preset, ExclusiveDrums[group]);
                        if (regions.Any(region => region.ExclusiveClass != 0)) continue;

                        foreach (var region in regions)
                        {
                            if (generators.GetValue(region) is not short[] values || values.Length <= ExclusiveClassGenerator) return;

                            values[ExclusiveClassGenerator] = (short)(FirstExclusiveClass + group);
                            if (region.ExclusiveClass == FirstExclusiveClass + group) continue;

                            // not where the class is kept after all: leave the sound font alone
                            values[ExclusiveClassGenerator] = 0;
                            return;
                        }
                    }
                }
            }
            catch { }
        }

        // The regions of a drum kit that play these keys and no others
        private static List<InstrumentRegion> RegionsOf(Preset preset, int[] keys)
        {
            var regions = new List<InstrumentRegion>();
            foreach (var presetRegion in preset.Regions)
            {
                foreach (var region in presetRegion.Instrument.Regions)
                {
                    bool onlyThese = Enumerable.Range(region.KeyRangeStart, region.KeyRangeEnd - region.KeyRangeStart + 1).All(keys.Contains);
                    bool reachable = keys.Any(key => presetRegion.KeyRangeStart <= key && key <= presetRegion.KeyRangeEnd
                                                  && region.KeyRangeStart <= key && key <= region.KeyRangeEnd);
                    if (onlyThese && reachable && !regions.Contains(region)) regions.Add(region);
                }
            }
            return regions;
        }

        // The synthesizer leaves a voice out of the reverb when what it sends there is too quiet
        // to matter, but it measures that after the reverb's own input gain of 0.015. So a soft
        // note got no reverb at all, and a loud one lost it while fading. Moving that gain from
        // the input of the reverb to its output sounds the same and takes it out of the comparison.
        public static void KeepReverbOnSoftNotes(Synthesizer synth)
        {
            try
            {
                object? reverb = typeof(Synthesizer).GetField("reverb", Hidden)?.GetValue(synth);
                if (reverb == null) return;

                var gain = reverb.GetType().GetField("gain", Hidden);
                var straight = reverb.GetType().GetField("wet1", Hidden);
                var crossed = reverb.GetType().GetField("wet2", Hidden);
                if (gain?.GetValue(reverb) is not float input || input <= 0 || input >= 1) return;
                if (straight?.GetValue(reverb) is not float wet1 || crossed?.GetValue(reverb) is not float wet2) return;

                gain.SetValue(reverb, 1f);
                straight.SetValue(reverb, wet1 * input);
                crossed.SetValue(reverb, wet2 * input);
            }
            catch { }
        }
    }
}
