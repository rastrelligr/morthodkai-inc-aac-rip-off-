using System;
using System.Collections.Generic;
using System.Linq;
using SERAAC.UI;
using R = SERAAC.Story.Squad.Reaction;

namespace SERAAC.Story
{
    // Squad conversations, as data. Each talk is a greeting and then a topic menu, like talking to the crew
    // between missions: topics can be asked once each, and leaving ends the talk for this stop. Romance is not a
    // menu item: a flirt is one answer among others inside a topic, easy to miss. Choices carry a hidden tone
    // (UI/VnDialogue.cs, VnTone) and the squadmate reacts in their reply; the Dialogue Helper option also shows
    // the tones and reaction notices (Story/Squad.cs). Some lines have a timed interrupt.
    //
    // Squadmates:
    //   Mara   wealthy, self-assured, hot-headed. Reads as arrogant; is not. Respects nerve and honesty, hates pity.
    //   Elian  ordinary, from a good family, always working to be better. Kind; not always realistic.
    //   Naomi  senior, background unclear. Rules, righteousness, perfection; motherly. Easily swayed once she is
    //          convinced something is right, and does not always see herself clearly.
    //   Relle  the player: the choices are her voice.
    public static class SquadTalk
    {
        public const string Airlift = "airlift";

        private static VnLine S(string who, string text) => VnLine.Say(who, text);
        private static VnLine N(string text) => VnLine.Narrate(text);
        private static VnLine On(Action a, VnLine line) { line.OnShow = a; return line; }   // a line with a side effect as it appears

        private static VnChoice C(VnTone tone, string label, Action react, params VnLine[] then) =>
            new(tone, label, () => then, react);

        private static VnLine Ask(string who, string text, params VnChoice[] choices) =>
            new() { Speaker = who, Text = text, Choices = choices.ToList() };

        // ------------------------------------------------------------------ the playtest's scenes

        public const string OpeningFlag = "vn_opening";

        public static List<VnLine> Opening() => new()
        {
            S("Naomi", "Relle. You're up. The Fallen Academy, one Wyvern, four of us."),
            S("Mara", "Finally. I've been ready since breakfast."),
            S("Elian", "I made a list of everything that could go wrong. It's... two pages."),
            Ask("Naomi", "First real operation. How are you feeling?",
                C(VnTone.Bold, "\"Let's go kill it.\"", Squad.Reacts(Squad.Mara, 1),
                    S("Relle", "Let's go kill it."),
                    S("Mara", "Ha. I like her.")),
                C(VnTone.Kind, "\"Honestly? Scared.\"", Squad.Reacts(Squad.Elian, 1),
                    S("Relle", "Honestly? Scared."),
                    S("Elian", "Me too. That's on page two.")),
                C(VnTone.Ask, "\"Why send a rookie?\"", Squad.Reacts(Squad.Naomi, 0, R.Remember),
                    S("Relle", "Why send a rookie after a Wyvern?"),
                    S("Naomi", "Because it won't wait until you're ready. Neither will I."))),
            S("Naomi", "Take a minute in the Sanctum. We leave when you say."),
        };

        public static List<VnLine> TrialArrive() => new()
        {
            N("The Osprey lifts away. The doors of the old Academy grind shut behind the squad."),
            S("Relle", "...I used to walk past this place."),
            S("Mara", "Now you get to break in."),
            S("Naomi", "The Wyvern's somewhere inside. Which way we go is your call, Relle."),
        };

        public static List<VnLine> FirstWin() => new()
        {
            S("Mara", "See? They bleed like anything else."),
            S("Elian", "Relle, your arm. Let me look at that."),
            Ask("Elian", "It'll only take a second.",
                C(VnTone.Kind, "\"Thanks, Elian.\"", Squad.Reacts(Squad.Elian, 1),
                    S("Relle", "Thanks, Elian."),
                    S("Elian", "Hold still. There. That's one thing off the list.")),
                C(VnTone.Bold, "\"Save it for the Wyvern.\"", () => { Squad.React(Squad.Mara, 1); Squad.React(Squad.Elian, -1); },
                    S("Relle", "I'm fine. Save it for the Wyvern."),
                    S("Mara", "That's the spirit."),
                    S("Elian", "...Right. Of course."))),
        };

        // One chance: who Relle sits with while the shrine's light lasts.
        public static List<VnLine> ShrineTalk() => new()
        {
            N("The shrine's glow settles over the squad. For a moment, nothing is hunting them."),
            S("Naomi", "Two minutes. Then we move."),
            new()
            {
                Text = "Who does Relle sit with?",
                Choices = new()
                {
                    new VnChoice(VnTone.Ask, "Mara, checking her shield", () => MaraShrine().Append(ShrineEnd)),
                    new VnChoice(VnTone.Ask, "Elian, writing in his notebook", () => ElianShrine().Append(ShrineEnd)),
                    new VnChoice(VnTone.Ask, "Naomi, keeping watch", () => NaomiShrine().Append(ShrineEnd)),
                    new VnChoice(VnTone.Leave, "Nobody. Sit alone.", () => new List<VnLine> { N("Relle watches the far door. It watches back."), ShrineEnd }),
                },
            },
        };

        private static VnLine ShrineEnd => S("Naomi", "Time. On your feet.");

        private static List<VnLine> MaraShrine() => new()
        {
            S("Mara", "Don't look at me like that. I'm not tired."),
            S("Relle", "Your shield arm's shaking."),
            S("Mara", "It's heavy. Expensive things are."),
            Ask("Mara", "Scared of what's past that door?",
                C(VnTone.Kind, "\"A little. Be careful, okay?\"", Squad.Reacts(Squad.Mara, 1),
                    S("Relle", "A little. Be careful, okay?"),
                    S("Mara", "...Fine. I'll be careful. Don't tell anyone I said that.")),
                C(VnTone.Bold, "\"Not at all.\"", null,
                    S("Relle", "Not at all."),
                    S("Mara", "Liar. But a good one.")),
                C(VnTone.Flirt, "\"Not with you standing in front of me.\"", () => Squad.Flirt(Squad.Mara),
                    S("Relle", "Not with you standing in front of me. That armour's really something."),
                    S("Mara", "Obviously. It's tailored."),
                    S("Mara", "..."),
                    S("Mara", "Wait. Are you- Ask me again when we're back."))),
        };

        private static List<VnLine> ElianShrine() => new()
        {
            S("Relle", "You really keep a list?"),
            S("Elian", "Things to get better at. Fourteen is \"stop apologising.\" Sorry."),
            Ask("Elian", "It's silly, isn't it?",
                C(VnTone.Kind, "\"It isn't.\"", Squad.Reacts(Squad.Elian, 1),
                    S("Relle", "It isn't. I wish I had one."),
                    S("Elian", "...Thank you. Most people laugh.")),
                C(VnTone.Bold, "\"A little.\"", Squad.Reacts(Squad.Elian, -1),
                    S("Relle", "A little, yeah."),
                    S("Elian", "Yeah. Sorry. Number fourteen. Sorry.")),
                C(VnTone.Flirt, "\"Only if there's no room on it for dinner with me.\"", () => Squad.Flirt(Squad.Elian),
                    S("Relle", "It's only silly if there's no room on it for \"have dinner with Relle.\""),
                    S("Elian", "I- that's- um."),
                    N("He writes something very carefully and closes the notebook."),
                    S("Elian", "Number sixteen. It's written down now, so it has to happen."))),
        };

        private static List<VnLine> NaomiShrine() => new()
        {
            N("Naomi starts checking the straps on Relle's gear without asking."),
            S("Relle", "I can do my own buckles."),
            S("Naomi", "You missed two. Hold still."),
            Ask("Naomi", "When it goes bad in there, you follow my call. Understood?",
                C(VnTone.Kind, "\"Understood.\"", Squad.Reacts(Squad.Naomi, 1),
                    S("Relle", "Understood."),
                    S("Naomi", "Good. That's how everyone gets home.")),
                C(VnTone.Bold, "\"If it's the right call.\"", Squad.Reacts(Squad.Naomi, 0, R.Remember),
                    S("Relle", "If it's the right call."),
                    S("Naomi", "...That's fair. I'll hold myself to that.")),
                C(VnTone.Flirt, "\"I'd follow you anywhere.\"", () => Squad.Flirt(Squad.Naomi),
                    S("Relle", "I'd follow you anywhere."),
                    S("Naomi", "That is not- I was not-"),
                    S("Naomi", "Did you eat? Here. A ration bar. Don't argue."))),
        };

        public static List<VnLine> BossRoom() => new()
        {
            N("The roof of the lecture hall is gone. Something long and ringed uncoils in the dark."),
            S("Elian", "That's... bigger than my notes said."),
            S("Mara", "Then I'll hit it harder."),
            S("Relle", "...Here we go."),
        };

        // ------------------------------------------------------------------ the conversation frame

        private sealed class Talk
        {
            public string Id, Speaker;
            public string FirstPrompt;                         // when the menu first opens
            public string MenuPrompt;                          // "Anything else?"
            public readonly List<(string label, Func<List<VnLine>> lines)> Topics = new();
            public string LeaveLabel;
            public Func<List<VnLine>> Leave;
            public readonly HashSet<int> Asked = new();
        }

        private static VnLine Menu(Talk t)
        {
            var choices = new List<VnChoice>();
            for (int i = 0; i < t.Topics.Count; i++)
            {
                if (t.Asked.Contains(i)) continue;
                int idx = i;
                choices.Add(new VnChoice(VnTone.Ask, t.Topics[i].label, () =>
                {
                    t.Asked.Add(idx);
                    return t.Topics[idx].lines().Append(Menu(t));
                }));
            }
            choices.Add(new VnChoice(VnTone.Leave, t.LeaveLabel, () => t.Leave()));
            return new VnLine { Speaker = t.Speaker, Text = t.Asked.Count == 0 ? t.FirstPrompt ?? t.MenuPrompt : t.MenuPrompt, Choices = choices };
        }

        // A squadmate's talk on the Airlift (one chance); afterwards a short line.
        public static List<VnLine> AirliftTalk(string id, Action ready = null)
        {
            if (Squad.Talked(id, Airlift)) return Repeat(id, ready);
            Squad.MarkTalked(id, Airlift);
            return id switch
            {
                Squad.Mara => MaraAirlift(),
                Squad.Elian => ElianAirlift(),
                Squad.Naomi => NaomiAirlift(ready),
                _ => new() { N("...") },
            };
        }

        private static List<VnLine> Repeat(string id, Action ready) => id switch
        {
            Squad.Mara => new() { S("Mara", Squad.Rapport(Squad.Mara) > 0 ? "We'll talk after we've put that thing down. Go on." : "We're done talking, Striker.") },
            Squad.Elian => new() { S("Elian", Squad.Rapport(Squad.Elian) > 0 ? "Let's talk again after the mission. I'll have a new list by then." : "I should... go over my notes. Sorry.") },
            Squad.Naomi => NaomiReady(ready),
            _ => new() { N("...") },
        };

        // ------------------------------------------------------------------ Mara

        private static List<VnLine> MaraAirlift()
        {
            var t = new Talk
            {
                Id = Squad.Mara, Speaker = "Mara",
                FirstPrompt = "Well? You came over here for a reason.",
                MenuPrompt = "What else? Make it quick, I'm warming up.",
                LeaveLabel = "\"See you down there.\"",
            };

            t.Topics.Add(("\"What's your story?\"", () => new List<VnLine>
            {
                S("Relle", "So what's your story? How does someone end up on a Wyvern hunt on day one?"),
                S("Mara", "Let me guess. Someone already told you about my family."),
                S("Mara", "The money. The house on the hill. The tutors. The \"oh, she must have bought her way in.\""),
                Ask("Mara", "Go on. Say what everyone says.",
                    C(VnTone.Kind, "\"I don't care where you're from.\"", Squad.Reacts(Squad.Mara, 1),
                        S("Relle", "Honestly? I don't care where you're from. I care if you've got my back down there."),
                        S("Mara", "...Huh."),
                        S("Mara", "Good answer. Keep it that way and we'll get along fine.")),
                    C(VnTone.Bold, "\"Must be nice, never worrying about money.\"", Squad.Reacts(Squad.Mara, -1),
                        S("Relle", "Must be nice. Never having to worry about rent."),
                        S("Mara", "Must be nice, being exactly as predictable as everyone else."),
                        S("Mara", "Money doesn't hold a shield up, Striker. I do.")),
                    C(VnTone.Ask, "\"Did you earn the Aegis too?\"", null,
                        S("Relle", "And the Aegis? Did that come with the house?"),
                        S("Mara", "Nobody buys an infected. It picks you, or it eats you."),
                        S("Mara", "It picked me. Everything since then, I earned.")))
            }));

            t.Topics.Add(("\"Why do you fight up front?\"", () => new List<VnLine>
            {
                S("Relle", "You fight right at the front. Why put yourself there?"),
                S("Mara", "Because somebody has to stand in front, and everyone else keeps flinching."),
                N("Mara hops up onto the railing and walks along it, arms out, clouds yawning below her."),
                new()
                {
                    Speaker = "Mara", Text = "See? Nothing to it. You just don't look-",
                    Interrupt = new VnInterrupt
                    {
                        Label = "Pull her down", Tone = VnTone.Kind,
                        Replaces = 2,
                        Pick = Squad.Reacts(Squad.Mara, 1, R.Remember),
                        Then = () => new List<VnLine>
                        {
                            N("The wind gusts. Relle grabs Mara's wrist and hauls her back onto the deck."),
                            S("Mara", "Hey! I had it, I-"),
                            S("Mara", "..."),
                            S("Mara", "...You actually grabbed me. Without thinking."),
                            S("Mara", "Fine. Noted. Don't make a thing of it."),
                        },
                    },
                },
                N("A gust slams into the platform. Mara wobbles, then drops back onto the deck, grinning too hard."),
                S("Mara", "...Like I said. Nothing to it."),
            }));

            t.Topics.Add(("\"Ready for the Wyvern?\"", () => new List<VnLine>
            {
                S("Relle", "Are you ready for the Wyvern?"),
                S("Mara", "Ready? I've been waiting all term for something worth hitting."),
                Ask("Mara", "What about you? Scared?",
                    C(VnTone.Bold, "\"Save some of it for me.\"", Squad.Reacts(Squad.Mara, 1),
                        S("Relle", "Scared? Just don't hog it. Save some for me."),
                        S("Mara", "Ha! Now that's a Striker. Keep up and maybe I will.")),
                    C(VnTone.Kind, "\"A little. Be careful, okay?\"", null,
                        S("Relle", "A little. Be careful down there, okay?"),
                        S("Mara", "Careful is for people who plan on losing."),
                        S("Mara", "...But fine. I'll be careful. Don't tell anyone I said that.")),
                    C(VnTone.Flirt, "\"Not with you standing in front of me.\"", () => Squad.Flirt(Squad.Mara),
                        S("Relle", "Scared? Not with you standing in front of me. Best view on the whole deck, too."),
                        S("Mara", "Obviously. The armour's tailored."),
                        S("Mara", "..."),
                        S("Mara", "Wait. Are you-"),
                        S("Mara", "Focus on the mission, Striker. Ask me again when we're back.")))
            }));

            t.Leave = () => new()
            {
                S("Relle", "See you down there."),
                S("Mara", Squad.Rapport(Squad.Mara) > 0 ? "Try to keep up. I mean it. I'd rather not carry you." : "Try not to slow us down."),
            };

            return new()
            {
                S("Mara", "So you're the one who slept through her own spar. Naomi told the whole deck."),
                Ask("Mara", "Mara. Warden. I'm the one standing between you and whatever's down there.",
                    C(VnTone.Bold, "\"And you're the one who listens to gossip.\"", Squad.Reacts(Squad.Mara, 1),
                        S("Relle", "And you're the one who listens to gossip. Nice to meet you."),
                        S("Mara", "Ha! Fair. At least you bite back.")),
                    C(VnTone.Kind, "\"Guilty. Nice to meet you properly.\"", null,
                        S("Relle", "Guilty. I'm Relle. Nice to meet you properly."),
                        S("Mara", "Hm. At least you own it.")),
                    C(VnTone.Ask, "\"Between me and what, exactly?\"", null,
                        S("Relle", "Between me and what, exactly?"),
                        S("Mara", "Everything. That's the job."))),
                Menu(t),
            };
        }

        // ------------------------------------------------------------------ Elian

        private static List<VnLine> ElianAirlift()
        {
            var t = new Talk
            {
                Id = Squad.Elian, Speaker = "Elian",
                FirstPrompt = "Did you want to ask me something? I like questions.",
                MenuPrompt = "Was there something else? I've got time. Well, eleven minutes.",
                LeaveLabel = "\"Good luck out there, Elian.\"",
            };

            t.Topics.Add(("\"What's in the notebook?\"", () => new List<VnLine>
            {
                S("Relle", "What's in the notebook?"),
                S("Elian", "Oh! This? It's, um. A list. Things I want to be better at."),
                S("Elian", "Number one: heal faster. Number six: stop overthinking. Number fourteen: stop apologising to doors."),
                S("Relle", "...Doors?"),
                new()
                {
                    Speaker = "Elian", Text = "I walked into one last week and said sorry. Twice. It's a work in progress-",
                    Interrupt = new VnInterrupt
                    {
                        Label = "Catch the notebook", Tone = VnTone.Kind,
                        Replaces = 2,
                        Pick = Squad.Reacts(Squad.Elian, 1),
                        Then = () => new List<VnLine>
                        {
                            N("The wind rips the notebook out of his hands. Relle snatches it out of the air a step from the edge."),
                            S("Elian", "You caught it! You actually- thank you. Thank you. That's three months of lists."),
                            S("Elian", "I'm adding you to it. \"Be more like Relle.\" Number thirty-one."),
                        },
                    },
                },
                N("A gust snatches a few loose pages from the notebook and scatters them over the clouds."),
                S("Elian", "...There go numbers twenty through thirty. That's fine. I'll write them again. Better."),
                Ask("Elian", "Is it silly? Keeping a list like that?",
                    C(VnTone.Kind, "\"It's admirable, actually.\"", Squad.Reacts(Squad.Elian, 1),
                        S("Relle", "It's not silly. Most people never even try. That's admirable."),
                        S("Elian", "...Thank you. Really. People usually just laugh.")),
                    C(VnTone.Bold, "\"You can't fix everything with a list.\"", Squad.Reacts(Squad.Elian, 0, R.Remember),
                        S("Relle", "You know you can't fix everything with a list, right?"),
                        S("Elian", "...Maybe not. But I can fix number fourteen."),
                        S("Elian", "And maybe fifteen. Fifteen is \"listen when people are honest with me.\"")),
                    C(VnTone.Flirt, "\"Only if there's room on it for dinner with me.\"", () => Squad.Flirt(Squad.Elian),
                        S("Relle", "It's only silly if there's no room on it for \"have dinner with Relle.\""),
                        S("Elian", "I- that's- um."),
                        N("He opens the notebook, writes something very carefully, and closes it again."),
                        S("Elian", "Number sixteen. It's written down now. So it has to happen. That's how lists work."),
                        S("Elian", "Is that weird? That's weird. Sorry. Number fourteen. Sorry.")))
            }));

            t.Topics.Add(("\"Why did you join?\"", () => new List<VnLine>
            {
                S("Relle", "Why did you join the Academy?"),
                S("Elian", "To help people. That sounds like a pamphlet, I know. But it's true."),
                S("Elian", "If we clear enough of the old campus, families could move back in. By spring, maybe."),
                Ask("Elian", "Don't you think?",
                    C(VnTone.Kind, "\"I hope you're right.\"", Squad.Reacts(Squad.Elian, 1),
                        S("Relle", "I hope you're right. I'd like to see that."),
                        S("Elian", "You will. We will. I really think so.")),
                    C(VnTone.Bold, "\"By spring? That's not how this works.\"", Squad.Reacts(Squad.Elian, -1),
                        S("Relle", "By spring? Elian, the Blight's had that campus for years."),
                        S("Elian", "...You sound like Naomi."),
                        S("Elian", "Someone has to believe it can get better. Otherwise why are we going?")),
                    C(VnTone.Ask, "\"Is that what the instructors say?\"", null,
                        S("Relle", "Is that what the instructors say?"),
                        S("Elian", "They said \"unlikely.\""),
                        S("Elian", "Unlikely isn't impossible.")))
            }));

            t.Topics.Add(("\"Tell me about your infected.\"", () => new List<VnLine>
            {
                S("Relle", "Tell me about your infected. Threadweaver, right?"),
                N("Elian lifts a hand. Pale threads uncurl from his sleeve and drift in the wind like spider silk."),
                S("Elian", "My mother sewed. Every evening, the whole time I was growing up."),
                S("Elian", "When it found me, it was thread. I think it knew what home felt like."),
                S("Elian", "It mends people. And sometimes it moves them, when they're too stubborn to get out of the way. Mostly Mara."),
            }));

            t.Leave = () => new()
            {
                S("Relle", "Good luck out there, Elian."),
                S("Elian", "You too. If you get hurt, come find me first, okay? Not last. First."),
            };

            return new()
            {
                N("A young man in a long coat is scribbling in a battered notebook, lips moving as he writes."),
                S("Elian", "-and number nine, introduce yourself properly. Okay. Okay."),
                S("Elian", "Oh! Hi! You must be Relle. I'm Elian. Adept. I keep everyone stitched together."),
                Ask("Elian", "I've been practising that. Was it okay?",
                    C(VnTone.Kind, "\"It was great. Hi, Elian.\"", Squad.Reacts(Squad.Elian, 1),
                        S("Relle", "It was great. Hi, Elian."),
                        S("Elian", "Yes. Good. Number nine, done.")),
                    C(VnTone.Bold, "\"You practised saying hi?\"", null,
                        S("Relle", "You practised saying hi?"),
                        S("Elian", "Everyone practises something. I just write mine down."))),
                Menu(t),
            };
        }

        // ------------------------------------------------------------------ Naomi

        private static List<VnLine> NaomiAirlift(Action ready)
        {
            var t = new Talk
            {
                Id = Squad.Naomi, Speaker = "Naomi",
                FirstPrompt = "Go ahead.",
                MenuPrompt = "Anything else on your mind?",
                LeaveLabel = "\"That's all. Let's go.\"",
            };

            t.Topics.Add(("\"About this morning...\"", () => new List<VnLine>
            {
                S("Relle", "About this morning..."),
                Ask("Naomi", "Yes?",
                    C(VnTone.Kind, "\"I'm sorry. It won't happen again.\"", Squad.Reacts(Squad.Naomi, 1),
                        S("Relle", "I'm sorry. Really. It won't happen again."),
                        S("Naomi", "Apology accepted. Make sure it doesn't."),
                        S("Naomi", "You did well in the simulation, for the record. Better than I did, my first time.")),
                    C(VnTone.Bold, "\"You could've just let me sleep.\"", Squad.Reacts(Squad.Naomi, -1),
                        S("Relle", "You could've just let me sleep, you know."),
                        S("Naomi", "And the next time you sleep through something, it won't be a spar."),
                        S("Naomi", "Rules are rules, Relle. People bled to write them.")),
                    C(VnTone.Flirt, "\"Worth it, if you're the one who comes knocking.\"", () => Squad.Flirt(Squad.Naomi),
                        S("Relle", "Honestly? Worth it, if you're the one who comes knocking."),
                        S("Naomi", "That is not- I was not-"),
                        S("Naomi", "..."),
                        S("Naomi", "I worry about every junior on this deck. You're just louder about needing it than most."),
                        S("Naomi", "Did you eat? You skipped breakfast. Here. A ration bar. Don't argue.")))
            }));

            t.Topics.Add(("\"Where are you from?\"", () => new List<VnLine>
            {
                S("Relle", "Where are you from, Naomi? I don't think I've ever heard you say."),
                S("Naomi", "Here. The Academy is where I'm from now."),
                S("Relle", "And before that?"),
                N("For a moment Naomi looks out over the clouds, somewhere very far away."),
                S("Naomi", "Some things are better left in the files."),
                Ask("Naomi", "...",
                    C(VnTone.Kind, "\"Okay. Whenever you're ready.\"", Squad.Reacts(Squad.Naomi, 1, R.Remember),
                        S("Relle", "Okay. If you ever want to tell me, I'll listen."),
                        S("Naomi", "..."),
                        S("Naomi", "Thank you, Relle.")),
                    C(VnTone.Bold, "\"Everyone's from somewhere.\"", Squad.Reacts(Squad.Naomi, -1),
                        S("Relle", "Everyone's from somewhere, Naomi."),
                        S("Naomi", "Drop it. That's not a request.")))
            }));

            t.Topics.Add(("\"Do you ever break the rules?\"", () => new List<VnLine>
            {
                S("Relle", "Do you ever break the rules? Even a little?"),
                S("Naomi", "No."),
                S("Relle", "Not even once?"),
                S("Naomi", "Rules exist because someone made a mistake that cost lives. Following them is how we honour that."),
                Ask("Naomi", "Why do you ask?",
                    C(VnTone.Bold, "\"What if a rule is wrong?\"", null,
                        S("Relle", "What if a rule is wrong? What if following it hurts people?"),
                        S("Naomi", "Then it gets changed. By the people whose job that is."),
                        S("Relle", "And if they don't?"),
                        N("Naomi opens her mouth, and doesn't answer straight away."),
                        S("Naomi", "...Then I would do what's right. After I was certain it was right."),
                        On(() => Squad.React(Squad.Naomi, 0, R.Remember), S("Naomi", "You ask strange questions for a junior."))),
                    C(VnTone.Kind, "\"I figured. It suits you.\"", Squad.Reacts(Squad.Naomi, 1),
                        S("Relle", "I figured. It suits you. Somebody has to keep the rest of us alive."),
                        S("Naomi", "That's exactly the point. I'm glad somebody listens.")))
            }));

            t.Leave = () => NaomiReady(ready);

            return new()
            {
                S("Naomi", "Seen enough?"),
                Ask("Naomi", "If something's on your mind, now's the time. Once we're down there, there won't be.",
                    new VnChoice(VnTone.Ask, "\"Can I ask you something first?\"", () => new List<VnLine> { S("Relle", "Can I ask you something first?"), Menu(t) }),
                    new VnChoice(VnTone.None, "\"I'm ready.\"", () => NaomiReady(ready))),
            };
        }

        // Naomi's last check before the briefing: whether Relle has met the squad, then ready or not.
        private static List<VnLine> NaomiReady(Action ready)
        {
            var lines = new List<VnLine>();
            var unmet = new[] { Squad.Mara, Squad.Elian }.Where(id => !Squad.Talked(id, Airlift)).Select(Squad.Name).ToList();
            if (unmet.Count > 0)
                lines.Add(S("Naomi", $"You haven't spoken to {string.Join(" or ", unmet)} yet. You'll be trusting them with your life. Might be worth a word."));
            lines.Add(new VnLine
            {
                Speaker = "Naomi", Text = "Ready for your first real operation?",
                Choices = new() { new VnChoice("\"I'm ready.\"", ready), new VnChoice("\"Give me a minute.\"", null) },
            });
            return lines;
        }

        // ------------------------------------------------------------------ the briefing (everyone reacts)

        // Mara and Elian disagree and Naomi puts Relle on the spot: whatever she says, someone has an opinion.
        public static List<VnLine> Briefing() => new()
        {
            N("Mara and Elian drift over as Naomi pulls up the operation file."),
            S("Naomi", "The Fallen Academy. Our old campus, before the Blight took it."),
            S("Naomi", "Something big has been nesting in the lecture halls. Scouts call it the Wyvern."),
            S("Relle", "My first operation. Today."),
            S("Mara", "Finally. We go in, we find it, we put it down. Simple."),
            S("Elian", "There might still be people down there. Sheltering in the dorms. We should look-"),
            S("Mara", "There's nobody left, Elian. It's been years."),
            S("Elian", "You don't know that."),
            S("Naomi", "Enough. Relle, you're on point for this one."),
            Ask("Naomi", "What's the call?",
                C(VnTone.Bold, "\"We hunt the Wyvern. Everything else waits.\"", () => { Squad.React(Squad.Mara, 1); Squad.React(Squad.Elian, -1); },
                    S("Relle", "We hunt the Wyvern. Everything else waits until it's dead."),
                    S("Mara", "Now we're talking."),
                    S("Elian", "...Right. Of course.")),
                C(VnTone.Kind, "\"We look for survivors on the way.\"", () => { Squad.React(Squad.Elian, 1); Squad.React(Squad.Mara, -1); },
                    S("Relle", "We look for survivors on the way. If anyone's down there, we bring them home."),
                    S("Elian", "Thank you. Thank you, Relle."),
                    S("Mara", "Soft. We'll see how long that lasts.")),
                C(VnTone.None, "\"By the book. Objective first, then we sweep.\"", Squad.Reacts(Squad.Naomi, 1),
                    S("Relle", "By the book. The Wyvern is the objective. Once it's down, we sweep for survivors before we extract."),
                    S("Naomi", "Good. That's the right answer."),
                    S("Mara", "Fine."),
                    S("Elian", "...Okay. Okay, I can live with that."))),
            S("Naomi", "To the Sanctum, then. I'll show you how to prepare a squad."),
        };

        // ------------------------------------------------------------------ the flight home (playtest ending)

        // Whoever Relle is closest to sits beside her on the Osprey home.
        public static List<VnLine> FlightHome()
        {
            var lines = new List<VnLine>
            {
                VnLine.TitleCard("The Osprey, somewhere above the clouds"),
                N("The cabin hums. Somebody's boots are on the seat opposite. The Fallen Academy shrinks behind them into the grey."),
            };
            string who = Squad.Closest();
            bool romance = who != null && Squad.Interested(who);
            switch (who)
            {
                case Squad.Mara:
                    lines.Add(N("Mara drops into the seat beside Relle, close enough that their shoulders touch."));
                    lines.Add(S("Mara", "Not bad, Striker. Not bad at all."));
                    lines.Add(romance ? S("Mara", "You asked about the armour. ...Ask me again sometime. When nobody's listening.")
                                      : S("Mara", "Next time, you take the first hit. I'll take the second."));
                    break;
                case Squad.Elian:
                    lines.Add(N("Elian sits beside Relle, notebook open on his knee, and quietly writes something down."));
                    lines.Add(S("Elian", "Everyone made it back. That's the only number I care about."));
                    lines.Add(romance ? S("Elian", "Also, um. Number sixteen. Dinner. I haven't forgotten. I don't forget lists.")
                                      : S("Elian", "Thank you. For listening to me, back on the pad."));
                    break;
                case Squad.Naomi:
                    lines.Add(N("Naomi sits down next to Relle and, without a word, presses a ration bar into her hand."));
                    lines.Add(S("Naomi", "You did well today. I mean that."));
                    lines.Add(romance ? S("Naomi", "...I worried. More than I should have. Don't make me do that again.")
                                      : S("Naomi", "Get some sleep tonight. Real sleep. That's an order."));
                    break;
                default:
                    lines.Add(N("Mara has her boots up. Elian is writing something down. Naomi counts heads, then counts them again."));
                    lines.Add(S("Naomi", "Everyone's here. Good work, all of you."));
                    break;
            }
            if (Squad.Flag(PillowFlag))
                lines.Add(N("From somewhere in the cargo hold comes a long, contented snore."));
            lines.Add(S("Relle", "...We actually did it."));
            lines.Add(VnLine.TitleCard("End of the playtest"));
            return lines;
        }

        // ------------------------------------------------------------------ Pillow (developer cameo)

        public const string PillowFlag = "pillow_found";
        private static int _pillowBit;

        // Knocking on the Osprey: the third knock wakes something up in the cargo bay.
        public static List<VnLine> OspreyKnock(int knocks) => knocks switch
        {
            1 => new()
            {
                N("A tilt-rotor transport sits on the pad, engines cold, its hull scuffed from a hundred landings."),
                S("Relle", "That's what takes squads out to the Trials. I've only ever seen it from the courtyard."),
            },
            2 => new()
            {
                N("Relle knocks on the hull. It rings hollow."),
                N("From somewhere inside the cargo bay, something snores."),
                S("Relle", "...Hello?"),
            },
            _ => new()
            {
                N("Relle knocks again, harder."),
                N("The cargo ramp groans open. A man rolls out, still wrapped in a sleeping bag, and lies face down on the deck."),
                S("Pillow", "Five more minutes."),
                S("Relle", "...That's my line."),
            },
        };

        // Pillow talks about whatever is on his mind. None of it is about the game. (He is one of the developers:
        // the dogs, the math and the fried chicken are his.)
        public static List<VnLine> PillowTalk()
        {
            var bits = PillowBits();
            var bit = bits[_pillowBit % bits.Count];
            _pillowBit++;
            return bit;
        }

        private static List<List<VnLine>> PillowBits() => new()
        {
            new()
            {
                S("Pillow", "Oh. Hi. I'm Pillow. I'm not supposed to be here. Don't tell the director."),
                S("Relle", "What director?"),
                S("Pillow", "Exactly. Good. You get it."),
                Ask("Pillow", "Quick question. Is a hot dog a sandwich?",
                    C(VnTone.Kind, "\"...Yes?\"", null, S("Relle", "...Yes?"), S("Pillow", "Wrong, but I respect the confidence.")),
                    C(VnTone.Bold, "\"Absolutely not.\"", null, S("Relle", "Absolutely not."), S("Pillow", "Wrong, but I respect the confidence.")),
                    C(VnTone.Ask, "\"Why are you asking me this?\"", null, S("Relle", "Why are you asking me this?"), S("Pillow", "It's been keeping me up at night. Not literally. I sleep great. Look at me."))),
            },
            new()
            {
                S("Pillow", "I've been trying to beat my instant noodle record. Two minutes fifty-eight."),
                S("Relle", "The packet says three minutes."),
                S("Pillow", "The packet doesn't believe in me like I do."),
                S("Pillow", "Next week I go for two fifty-five. No water. Just vibes."),
            },
            new()
            {
                S("Pillow", "Okay, real talk. I rate dogs. Every dog on campus."),
                S("Pillow", "The system is simple. Every dog is twelve out of ten. No exceptions. I don't make the rules. Okay, I do."),
                S("Pillow", "My favourite is Chao-guay. Black, one ear up, the other one just gave up. I called him Chao-guay for three months."),
                S("Pillow", "He came every time. We had a bond."),
                S("Pillow", "Then one day I read his collar."),
                S("Pillow", "His name is Project."),
                S("Relle", "...Project?"),
                S("Pillow", "Project. Who names a dog Project? Now every time someone says \"how's the project going\" I think about him."),
                Ask("Pillow", "I still call him Chao-guay. He still comes. Is that wrong?",
                    C(VnTone.Kind, "\"Chao-guay suits him better.\"", null, S("Relle", "Chao-guay suits him better."), S("Pillow", "Thank you. Finally. Someone gets it. Twelve out of ten, Relle.")),
                    C(VnTone.Bold, "\"Project is a great name, though.\"", null, S("Relle", "Honestly, Project is a great name."), S("Pillow", "Don't. He's a good boy, not a deadline."))),
            },
            new()
            {
                S("Pillow", "I hate math."),
                S("Relle", "Okay...?"),
                S("Pillow", "Not \"I don't like it.\" Hate. Numbers should stay where you put them. Why does x keep moving to the other side? Who said it could leave?"),
                S("Pillow", "Somebody told me there's math in balancing a fight. Damage, speed, percentages."),
                S("Pillow", "I've been hiding in this Osprey ever since."),
                Ask("Pillow", "You like math?",
                    C(VnTone.Bold, "\"Math isn't that bad.\"", null, S("Relle", "Math isn't that bad."), S("Pillow", "That's exactly what math wants you to think.")),
                    C(VnTone.Kind, "\"Same. I hate it too.\"", null, S("Relle", "Same. I hate it too."), S("Pillow", "A comrade. Finally. We should start a club. No counting allowed."))),
            },
            new()
            {
                S("Pillow", "Have you been to MorKaiThod? The fried chicken place."),
                S("Relle", "You mean MorThodKai?"),
                S("Pillow", "That's what I said."),
                S("Pillow", "Best fried chicken in the world. The crunch. The sauce. I'd name a whole game studio after it."),
                S("Pillow", "Problem is, it's open maybe twice a month. Nobody knows which days. There's no sign. There's no schedule. There's just hope."),
                S("Pillow", "I walk past every day and stare at the shutter. Sometimes the shutter stares back."),
                S("Pillow", "One day I'll catch it open. And on that day I will be the happiest man alive."),
            },
            new()
            {
                S("Pillow", "I slept from four a.m. to four twenty a.m. last night."),
                S("Relle", "That's twenty minutes."),
                S("Pillow", "Power nap. I'm basically a phone on fast charge."),
                S("Pillow", "Twenty minutes of sleep, seventy percent battery. The math checks out. Don't check the math."),
            },
            new()
            {
                S("Pillow", "Have you ever had condensed milk on toast at three in the morning?"),
                S("Relle", "No...?"),
                S("Pillow", "Life-changing. I saw colours. I heard a choir. I missed a deadline."),
                S("Pillow", "Worth it."),
            },
            new()
            {
                S("Pillow", "Rate my haircut. Out of ten. Be honest. Actually, don't be honest."),
                Ask("Pillow", "Well?",
                    C(VnTone.Kind, "\"Ten. Easy.\"", null, S("Relle", "Ten. Easy."), S("Pillow", "I'm framing that. Hanging it in the cargo bay.")),
                    C(VnTone.Bold, "\"...Four.\"", null, S("Relle", "...Four?"), S("Pillow", "Four is a ten in some countries. Probably."))),
            },
            new()
            {
                S("Pillow", "Anyway. I should get back in the bag. The bag misses me."),
                S("Pillow", "If anyone asks, you never saw me. I'm a texture. I'm part of the floor."),
                N("Pillow does not move."),
            },
        };
    }
}
