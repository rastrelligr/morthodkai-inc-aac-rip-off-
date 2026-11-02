using System;
using System.Collections.Generic;

namespace SERAAC.Data
{
    // Moments in a fight that can make a party member say something (battle UI, top-left box).
    public enum BarkTrigger
    {
        FightStart,   // a random party member, when the fight opens
        AllyLow,      // someone else, when an ally drops under 35% HP
        AllyDown,     // someone else, when an ally falls
        Crit,         // the attacker, after landing a critical hit
        Kill,         // the attacker, after finishing an enemy
        Hurt,         // the one hit, after taking a big hit (25%+ of max HP)
        Victory,      // a random survivor
    }

    // Short reaction lines (placeholder writing until the final script). A character with no line for a
    // trigger uses the "default" lines. Add a character by its id from CharacterDb.
    public static class BarkDb
    {
        public const string Default = "default";

        public static readonly Dictionary<string, Dictionary<BarkTrigger, string[]>> Lines = new()
        {
            [Default] = new()
            {
                [BarkTrigger.FightStart] = new[] { "Here they come!", "Stay sharp.", "Formation!" },
                [BarkTrigger.AllyLow] = new[] { "Be careful!", "Fall back if you need to!", "Hang in there!" },
                [BarkTrigger.AllyDown] = new[] { "No!", "We lost one!" },
                [BarkTrigger.Crit] = new[] { "Got it!", "Right there!" },
                [BarkTrigger.Kill] = new[] { "One down.", "Next!" },
                [BarkTrigger.Hurt] = new[] { "Ngh-!", "That hurt..." },
                [BarkTrigger.Victory] = new[] { "Clear.", "That's all of them." },
            },
            ["naomi"] = new()
            {
                [BarkTrigger.FightStart] = new[] { "Watch their feet, not their teeth.", "Keep it tidy." },
                [BarkTrigger.AllyLow] = new[] { "Pull back, I've got the front.", "Breathe. Then move." },
                [BarkTrigger.Crit] = new[] { "Opening.", "There." },
                [BarkTrigger.Kill] = new[] { "Done.", "Next one." },
                [BarkTrigger.Hurt] = new[] { "...Sloppy of me." },
                [BarkTrigger.Victory] = new[] { "Clean enough.", "Good work. Don't get cocky." },
            },
            ["relle"] = new()
            {
                [BarkTrigger.FightStart] = new[] { "Pulverizer's hungry.", "Let's make this quick." },
                [BarkTrigger.AllyLow] = new[] { "Be careful!", "Stay behind me!" },
                [BarkTrigger.Crit] = new[] { "It's feeding!", "Bleed." },
                [BarkTrigger.Kill] = new[] { "Swallowed whole.", "Next." },
                [BarkTrigger.Victory] = new[] { "...it's still hungry." },
            },
            ["warden"] = new()   // Mara
            {
                [BarkTrigger.FightStart] = new[] { "Behind the Aegis. All of you.", "I'll take the first hit." },
                [BarkTrigger.AllyLow] = new[] { "Get behind me!", "Stay close to the shield!" },
                [BarkTrigger.Hurt] = new[] { "The Aegis holds.", "Still standing." },
            },
            ["priest"] = new()   // Elian
            {
                [BarkTrigger.FightStart] = new[] { "Threads ready.", "Nobody gets left behind." },
                [BarkTrigger.AllyLow] = new[] { "Hold still, I'll stitch that!", "Don't you dare fall!" },
                [BarkTrigger.AllyDown] = new[] { "No... the thread snapped." },
                [BarkTrigger.Victory] = new[] { "Everyone in one piece? Good." },
            },
            ["knight"] = new()   // Pillow
            {
                [BarkTrigger.FightStart] = new[] { "*yawn* ...is this a meeting?", "Wake me when it's- oh. It's now.", "Is MorThodKai open yet?" },
                [BarkTrigger.Crit] = new[] { "I didn't calculate that. I don't calculate.", "Twelve out of ten." },
                [BarkTrigger.Kill] = new[] { "Good boy. Wait, wrong one.", "Back to the bag." },
                [BarkTrigger.Hurt] = new[] { "Ow. My nap.", "That's coming out of my sleep schedule." },
                [BarkTrigger.Victory] = new[] { "Fried chicken. Now. Please.", "Can I go back in the Osprey?" },
            },
            // Mireille is out of the game for now (see CharacterDb).
            // ["occultist"] = new()
            // {
            //     [BarkTrigger.FightStart] = new[] { "Fascinating specimens.", "Let's see what's inside." },
            //     [BarkTrigger.Kill] = new[] { "Noted.", "Hm. Brittle." },
            // },
        };

        // A line for this character, falling back to the default lines. Null if there is none.
        public static string Pick(string charId, BarkTrigger trigger, Random rng)
        {
            string[] options = null;
            if (charId != null && Lines.TryGetValue(charId, out var own)) own.TryGetValue(trigger, out options);
            if (options == null || options.Length == 0) Lines[Default].TryGetValue(trigger, out options);
            return options == null || options.Length == 0 ? null : options[rng.Next(options.Length)];
        }
    }
}
