using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended.Tweening;
using SERAAC.Combat;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Fx;
using SERAAC.Meta;
using SERAAC.Stage;
using SERAAC.Trial;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Turn-based party combat.
    //   Choose Action: the player picks an action (+ target) for every living party member;
    //                  enemies have already picked theirs (shown in the turn order and as intents).
    //   Resolve Action: everyone acts in Speed order; the Battle rules produce events that
    //                  this screen plays back one by one.
    // Presentation: a pre-rendered background (Art/Stages, picked by Stage/BackgroundDb), a camera that
    // zooms, pans, shakes and sways with the action (Stage/CombatCamera.cs), melee attackers running
    // up to their target, and particles (Fx/, presets in Art/Fx/particles.json).
    //
    // Layout (from the battle UI design):
    //   top-left   character reactions (barks, Data/BarkDb.cs)
    //   top-centre turn priority: who acts next, by Speed, with their action and target
    //   top-right  current objective / quest
    //   middle     the fight; name, HP, SP and statuses under each unit, brackets on targets
    //   bottom     character status | control hints + available skills + Special Abilities | skill description
    public sealed class CombatScreen : Screen
    {
        private enum Phase { Intro, Choose, Resolve, Finished }

        private sealed class Visual
        {
            public int Slot;          // slot currently shown (updated when a Move event plays)
            public float X, PrevX;    // animated x (layer space)
            public float? DashX;      // melee: where the unit runs to while acting (null = its slot)
            public int Hp;            // HP currently shown (updated as events play)
            public float Flash, Shake, Lunge, Alpha = 1f;
            public bool Dying;
            public string Popup;      // action name shown above the unit while it acts
            public float PopupTime;
            public string Pose;       // sprite-sheet pose while acting (null = idle)
            public float PoseTime;
            public string[] AnimSteps = Array.Empty<string>();
        }

        // Something the current action can be aimed at: a unit, or (Position) an empty slot.
        private readonly struct Choice
        {
            public readonly Combatant Unit;
            public readonly int Slot;
            public Choice(Combatant unit, int slot) { Unit = unit; Slot = slot; }
        }

        private sealed class Bark
        {
            public Combatant Who;
            public string Text;
            public float Age;
        }

        private readonly TrialRun _run;
        private readonly List<Combatant> _enemies;

        // A scripted, teaching fight (Combat/CombatTutorial.cs): coaching scenes, one allowed action per turn.
        public CombatTutorial Tutorial { get; init; }
        private Coach _coach;              // this turn's spotlights, if the tutorial has any
        private Rectangle _windUpBox;
        // A fixed background for this fight (the tutorial picks one); null = the room's / a random one.
        public string StageOverride { get; init; }
        private readonly Action<Battle> _onFinished;
        private Battle _battle;
        private readonly List<Combatant> _all = new();
        private readonly Dictionary<Combatant, Visual> _vis = new();

        private Phase _phase;
        private readonly Tweens _tweens = new();   // eased UI (MonoGame.Extended Tweener)
        private readonly Anim _bannerIn = new();    // ENCOUNTER / VICTORY band sweeping open
        private float _timer;
        private float _time;
        private float _hitStop;

        // Choose phase state
        private List<Combatant> _choosers = new();
        private int _chooserIndex;
        private int _actionIndex;              // index into the chooser's Actions (skills, then Special Abilities)
        private bool _pickingTarget;
        private readonly List<Choice> _choices = new();
        private int _choiceIndex;
        private bool _pickingItem;
        private int _itemIndex;
        private string _warning;
        private float _warningTime;

        // Resolve phase state
        private readonly Queue<Combatant> _resolveQueue = new();
        private readonly Queue<BattleEvent> _events = new();
        private Combatant _acting;
        private ActionDef _actingAction;
        private bool _endTurnDone;
        private bool _reported;          // onFinished called

        // Presentation
        private BackgroundDef _background;
        private Ambience _ambience;   // dust, fog, smoke over the background (Art/Fx/ambience.json)
        private readonly CombatCamera _cam = new();
        private readonly ParticleSystem _fx = new();
        private static Texture2D _vignette;
        private readonly List<Bark> _barks = new();
        private float _lastBark = -10f;
        private readonly Random _barkRng = new();

        private readonly List<(string text, Vector2 pos, float age, Color color)> _floaters = new();
        private readonly List<(string text, Color color, float time)> _log = new();   // who did what; entries fade out
        private const float LogLife = 5f;

        // Action-cam state (tuning in Art/Stages/_camera.json).
        private float _slowMo;            // real seconds of slow motion left
        private float _flash;             // screen flash, 1 -> 0
        private Color _flashColor = Color.White;
        private float _cinematic;         // seconds the letterbox bars stay in
        private float _letterbox;         // eased 0..1
        private Combatant _cutInWho;      // skill cut-in banner
        private string _cutInText;
        private float _cutInAge = 99f;

        // Slow motion + flash + letterbox for a moment that deserves it.
        private void Moment(float slowMo, float flash = 0f, Color? flashColor = null, float cinematic = 0f)
        {
            _slowMo = Math.Max(_slowMo, slowMo);
            if (flash > _flash) { _flash = flash; _flashColor = flashColor ?? Color.White; }
            _cinematic = Math.Max(_cinematic, cinematic);
        }

        private const float GroundY = 430;
        private const float DashGap = 78;        // how close a melee attacker stops to its target
        private const float MeleeTravel = 0.32f; // seconds allowed for the run-up
        private const int PanelTop = 492;        // bottom panel
        private const float BarkLife = 4.5f;

        public CombatScreen(TrialRun run, List<Combatant> enemies, Action<Battle> onFinished)
        {
            _run = run;
            _enemies = enemies;
            _onFinished = onFinished;
        }

        private Combatant Chooser => _chooserIndex < _choosers.Count ? _choosers[_chooserIndex] : null;

        // During the fight's playback Enter / Space only speed it up.
        public override bool MenuKeySounds => _phase is Phase.Choose or Phase.Finished;

        public override void OnEnter()
        {
            _battle = new Battle(_run.BuildPartyCombatants(), _enemies, _run.Rng) { Items = _run.Consumables };
            // Identical enemies get a letter (Gnawer A, Gnawer B) so the turn order and the log say which one acts.
            foreach (var group in _battle.Enemies.Where(e => e != null).GroupBy(e => e.Name).Where(g => g.Count() > 1))
            {
                int n = 0;
                foreach (var e in group.OrderBy(e => e.Slot)) e.Name = $"{e.Name} {(char)('A' + n++)}";
            }
            _all.AddRange(_battle.Party.Where(c => c != null));
            _all.AddRange(_battle.Enemies.Where(c => c != null));
            foreach (var c in _all)
            {
                float x = SlotX(c.IsParty, c.Slot) + (c.IsParty ? -400 : 400);
                _vis[c] = new Visual { Slot = c.Slot, X = x, PrevX = x, Hp = c.Hp };
            }

            // The room keeps the background it got on its first fight.
            var room = _run.Current;
            _background = BackgroundDb.Get(StageOverride) ?? BackgroundDb.Get(room.StageId)
                          ?? BackgroundDb.Pick(_run.Area.Id, _run.Trial.Id, BackgroundDb.KindFor(room.Type),
                                               _enemies.Select(e => e.Enemy?.Id).Where(id => id != null), new Random());
            if (_background != null) room.StageId = _background.Image;
            _ambience = new Ambience(AmbiencePreset());
            _cam.Snap(CombatCamera.Tuning.Value.IntroZoom, 0);
            _cam.Focus(1f, 0f);

            _phase = Phase.Intro;
            Audio.Music(_enemies.Any(e => e.Enemy?.Type == EnemyType.Apex) ? "boss" : "combat");
            _tweens.FromTo(_bannerIn, 0f, 1f, 0.5f, EasingFunctions.ExponentialOut);
            Audio.Play("encounter");
            _timer = Math.Max(0.9f, CombatCamera.Tuning.Value.IntroTime);
            Log("Combat begins!", Palette.Accent);
        }

        private string AmbiencePreset() => _background?.Ambience ?? Ambience.ForTrial(_run.Trial?.Id);

        // Extended particle emitters hold native buffers; free them when the fight closes.
        public override void OnExit() => _fx.Dispose();

        private static float SlotX(bool party, int slot) => party ? 530 - slot * 122 : 750 + slot * 122;

        // Only who did what goes in the log ("Relle: Pierce"); the details are shown on the battlefield.
        // Indented detail lines and turn markers are dropped.
        private void Log(string s, Color c)
        {
            if (s.StartsWith(" ") || s.StartsWith("--") || s == "Combat begins!") return;
            _log.Add((s, c, _time));
            if (_log.Count > 12) _log.RemoveAt(0);
        }

        // ------------------------------------------------------------------ flow

        private void BeginChoose()
        {
            bool first = _battle.Turn == 1 && _phase == Phase.Intro;
            CombatAI.PlanAll(_battle, _battle.LivingEnemies, _run.Rng);
            var coaching = Tutorial?.OnTurnStart(_battle);
            if (coaching != null) Screens.PushInstant(new StoryScreen(coaching));
            _choosers = _battle.LivingParty.OrderBy(c => c.Slot).ToList();
            _chooserIndex = 0;
            _pickingTarget = _pickingItem = false;
            _coach = BuildCoach(Tutorial?.HintsFor(_battle));
            _phase = Phase.Choose;
            _cam.Focus(1f, 0f);
            SelectFirstUsable();
            if (first) Say(BarkTrigger.FightStart, RandomPartyMember(), force: true);
        }

        private Coach BuildCoach(List<CombatTutorial.Hint> hints)
        {
            if (hints == null) return null;
            var steps = new List<CoachStep>();
            foreach (var h in hints)
            {
                int who = h.Nth;
                if (who >= _choosers.Count) continue;
                switch (h.Spot)
                {
                    case CombatTutorial.Spot.Pick:
                        steps.Add(new CoachStep
                        {
                            Text = h.Text,
                            Target = () => Ui.Bounds("act") is { } list && Ui.Bounds("special") is { } specials ? Rectangle.Union(list, specials) : null,
                            When = () => _chooserIndex == who && !_pickingTarget && !_pickingItem,
                            Done = () => _pickingTarget || _chooserIndex > who,
                        });
                        break;
                    case CombatTutorial.Spot.Enemies:
                        steps.Add(new CoachStep
                        {
                            Text = h.Text,
                            Target = ChoiceBounds,
                            When = () => _chooserIndex == who && _pickingTarget,
                            Done = () => _chooserIndex > who,
                            Back = () => _chooserIndex == who && !_pickingTarget,
                        });
                        break;
                    default:
                        steps.Add(new CoachStep
                        {
                            Text = h.Text,
                            Target = () => HintBounds(h.Spot),
                            When = () => _chooserIndex == who && !_pickingTarget && !_pickingItem,
                        });
                        break;
                }
            }
            return new Coach(steps);
        }

        private Rectangle? ChoiceBounds()
        {
            Rectangle? all = null;
            foreach (var ch in _choices.Where(ch => ch.Unit != null))
                if (Ui.Bounds("unit", _all.IndexOf(ch.Unit)) is { } r)
                    all = all == null ? r : Rectangle.Union(all.Value, r);
            return all;
        }

        private Rectangle? HintBounds(CombatTutorial.Spot spot)
        {
            switch (spot)
            {
                case CombatTutorial.Spot.TurnOrder: return Ui.Bounds("order");
                case CombatTutorial.Spot.Ethergy: return Ui.Bounds("pips", _all.IndexOf(Chooser));
                case CombatTutorial.Spot.Specials: return Ui.Bounds("special");
                case CombatTutorial.Spot.Guard: return Ui.Bounds("special", Chooser.Actions.FindIndex(a => a.Id == "defend"));
                case CombatTutorial.Spot.WindUp: return _windUpBox;
                case CombatTutorial.Spot.Ranks:
                    return Ui.Bounds("act") is { } list ? new Rectangle(list.X + 298, list.Y, 124, list.Height) : null;
                default: return null;
            }
        }

        private void SelectFirstUsable()
        {
            var c = Chooser;
            if (c == null) return;
            // Keep the previous choice if it's still usable, otherwise jump to the first usable one.
            if (c.Plan != null) _actionIndex = Math.Max(0, c.Actions.IndexOf(c.Plan.Action));
            if (_actionIndex >= c.Actions.Count || !_battle.CanUse(c, c.Actions[_actionIndex]))
                _actionIndex = Math.Max(0, c.Actions.FindIndex(a => _battle.CanUse(c, a)));
            // Tutorial: start on the action the coach is asking for.
            int asked = Tutorial?.Current?.Allow is { } id ? c.Actions.FindIndex(a => a.Id == id) : -1;
            if (asked >= 0) _actionIndex = asked;
        }

        private void CommitPlan(PlannedAction plan)
        {
            string blocked = Tutorial?.Blocked(Chooser, plan);
            if (blocked != null)
            {
                Warn(blocked);
                _pickingTarget = _pickingItem = false;
                return;
            }
            Chooser.Plan = plan;
            Audio.Play("ui_select");
            _pickingTarget = _pickingItem = false;
            _chooserIndex++;
            if (_chooserIndex >= _choosers.Count) BeginResolve();
            else SelectFirstUsable();
        }

        private void BeginResolve()
        {
            _phase = Phase.Resolve;
            _resolveQueue.Clear();
            foreach (var c in _battle.ResolveOrder()) _resolveQueue.Enqueue(c);
            _endTurnDone = false;
            _timer = 0.2f;
            Log($"-- Turn {_battle.Turn} --", Palette.TextDim);
        }

        private void Undo()
        {
            if (_chooserIndex == 0) return;
            _pickingTarget = _pickingItem = false;
            _chooserIndex--;
            Chooser.Plan = null;
            SelectFirstUsable();
        }

        // ------------------------------------------------------------------ update

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            _time += dt;
            BackgroundDb.File.Poll(_time);
            CombatCamera.Tuning.Poll(_time);
            ParticleSystem.Presets.Poll(_time);
            if (_background != null) _background = BackgroundDb.Get(_background.Image) ?? _background; // pick up edits
            Ambience.File.Poll(_time);
            _ambience.SetPreset(AmbiencePreset());
            _ambience.Update(dt, new Vector2(Game1.Width, Game1.Height));

            // Action-cam: slow motion stretches everything that animates (not the UI tweens); holding
            // the fast-forward button skips it. Flash and letterbox run on real time.
            float realDt = dt;
            _slowMo = Math.Max(0, _slowMo - realDt);
            if (_slowMo > 0 && !Input.FastForward) dt *= CombatCamera.Tuning.Value.SlowMoScale;
            _flash = Math.Max(0, _flash - realDt * 3.5f);
            _letterbox = MathHelper.Lerp(_letterbox, _cinematic > 0 ? 1f : 0f, 1 - (float)Math.Pow(0.0005, realDt));
            _cinematic = Math.Max(0, _cinematic - dt);
            _cutInAge += realDt;

            _cam.Update(dt);
            _tweens.Update(realDt);
            _fx.Update(dt);
            // [debug] F4: fire every particle preset in a row, for tuning Art/Fx/particles.json.
            // Playtest only: F2 / the "Leave fight" button gets out of this fight.
            if (Playtest.Active && _phase != Phase.Finished
                && (Input.Pressed(Microsoft.Xna.Framework.Input.Keys.F2) || Ui.Clicked("leavefight")))
            {
                LeaveFight();
                return;
            }
            if (Input.Pressed(Microsoft.Xna.Framework.Input.Keys.F4))
            {
                var ids = ParticleSystem.Presets.Value.Keys.ToList();
                for (int i = 0; i < ids.Count; i++)
                    _fx.Emit(ids[i], new Vector2(80 + (Game1.Width - 160) * (i + 0.5f) / ids.Count, 200 + (i % 2) * 140), 1);
            }
            AnimateVisuals(dt);
            _warningTime -= dt;

            switch (_phase)
            {
                case Phase.Intro:
                    _timer -= dt;
                    if (_timer <= 0 || (_timer < 0.6f && (Input.Confirm || Input.Click))) BeginChoose();
                    break;
                case Phase.Choose:
                    if (_coach?.Update() != true) UpdateChoose();
                    if (_phase == Phase.Choose)
                    {
                        // Lean in toward the aimed target while picking one.
                        var tune = CombatCamera.Tuning.Value;
                        bool aiming = _pickingTarget && _choices.Count > 0;
                        float aimX = aiming ? ChoiceX(_choices[Math.Min(_choiceIndex, _choices.Count - 1)]) : Game1.Width / 2f;
                        _cam.Focus(aiming ? tune.TargetingZoom : 1f, aiming ? (aimX - Game1.Width / 2f) * tune.ActionPan * 0.4f : 0f);
                    }
                    break;
                case Phase.Resolve:
                    if (_hitStop > 0) { _hitStop -= dt; break; }
                    UpdateResolve(dt * (Input.FastForward ? 3f : 1f));
                    break;
                case Phase.Finished:
                    _timer -= dt;
                    if (!_reported && (_timer <= 0 || (_timer < 1.0f && (Input.Confirm || Input.Click))))
                    {
                        _reported = true;
                        _onFinished(_battle);
                    }
                    break;
            }
        }

        private void AnimateVisuals(float dt)
        {
            foreach (var c in _all)
            {
                var v = _vis[c];
                v.PrevX = v.X;
                float target = v.DashX ?? SlotX(c.IsParty, v.Slot);
                // Dashing (to or from a target) is quicker than shuffling between slots.
                bool running = v.DashX.HasValue || Math.Abs(v.X - SlotX(c.IsParty, v.Slot)) > 130;
                v.X = MathHelper.Lerp(v.X, target, 1 - (float)Math.Pow(running ? 0.000002 : 0.0001, dt));
                v.Flash = Math.Max(0, v.Flash - dt * 4);
                v.Shake = Math.Max(0, v.Shake - dt * 3);
                v.Lunge = Math.Max(0, v.Lunge - dt * 2.5f);
                v.PopupTime -= dt;
                v.PoseTime -= dt;
                if (v.PoseTime <= 0) v.Pose = null;
                if (v.Dying) v.Alpha = Math.Max(0, v.Alpha - dt * 1.6f);
            }
            for (int i = _floaters.Count - 1; i >= 0; i--)
            {
                var f = _floaters[i];
                f.age += dt;
                if (f.age > 1.3f) _floaters.RemoveAt(i); else _floaters[i] = f;
            }
            foreach (var b in _barks) b.Age += dt;
            _barks.RemoveAll(b => b.Age > BarkLife);
        }

        // ------------------------------------------------------------------ choosing

        // The chooser's skills (top list) and Special Abilities (bottom buttons), as indexes into Actions.
        private static bool IsSpecial(ActionDef a) => Array.IndexOf(CommonActions.PartySpecials, a) >= 0;
        private static List<int> SkillIndexes(Combatant c) => Enumerable.Range(0, c.Actions.Count).Where(i => !IsSpecial(c.Actions[i])).ToList();
        private static List<int> SpecialIndexes(Combatant c) => Enumerable.Range(0, c.Actions.Count).Where(i => IsSpecial(c.Actions[i])).ToList();

        private void UpdateChoose()
        {
            var c = Chooser;
            if (c == null) { BeginResolve(); return; }

            if (Ui.Clicked("undo")) { Undo(); return; }
            if (_pickingItem) { UpdateItemPick(c); return; }

            // Clicking another action (skill row or Special Ability) while aiming switches to it.
            int clickedAction = Math.Max(Ui.ClickedIndex("act"), Ui.ClickedIndex("special"));
            if (_pickingTarget && clickedAction >= 0)
            {
                _pickingTarget = false;
                _actionIndex = clickedAction;
                ChooseAction(c);
                return;
            }

            if (_pickingTarget)
            {
                var action = c.Actions[_actionIndex];
                if (!action.AreaEffect && _choices.Count > 0)
                {
                    if (Input.Left || Input.Up) _choiceIndex = (_choiceIndex + _choices.Count - 1) % _choices.Count;
                    if (Input.Right || Input.DownKey) _choiceIndex = (_choiceIndex + 1) % _choices.Count;
                }

                // Mouse: hover a valid target to aim, click it to confirm (anywhere on an area skill's targets).
                int hovered = HoveredChoice();
                if (hovered >= 0 && !action.AreaEffect && Input.MouseMoved) _choiceIndex = hovered;
                var hoveredUnit = HoveredUnit();
                if (Input.Click && hovered < 0 && hoveredUnit != null) Warn($"{action.Name} can't reach {hoveredUnit.Name}");

                if (Input.Cancel || Ui.Clicked("back")) _pickingTarget = false;
                else if (Input.Confirm || (Input.Click && hovered >= 0))
                {
                    if (hovered >= 0 && !action.AreaEffect) _choiceIndex = hovered;
                    CommitPlan(PlanFor(action, _choices[_choiceIndex]));
                }
                return;
            }

            int n = c.Actions.Count;
            if (Input.Up) _actionIndex = (_actionIndex + n - 1) % n;
            if (Input.DownKey) _actionIndex = (_actionIndex + 1) % n;
            bool clicked = Ui.Pick("act", ref _actionIndex) | Ui.Pick("special", ref _actionIndex);

            if (Input.Cancel && _chooserIndex > 0 && _coach?.Showing != true)
            {
                Undo();
                return;
            }

            // Shortcut: clicking a unit the selected action can hit uses it on that unit straight away.
            var unit = Input.Click && !clicked ? HoveredUnit() : null;
            if (unit != null)
            {
                var a = c.Actions[_actionIndex];
                if (_battle.Unusable(c, a) == null && a.Side != TargetSide.Self)
                {
                    var choices = ChoicesFor(c, a);
                    int i = choices.FindIndex(x => x.Unit == unit);
                    if (i >= 0) { CommitPlan(PlanFor(a, choices[i])); return; }
                    Warn($"{a.Name} can't reach {unit.Name}");
                }
                return;
            }

            if (Input.Confirm || clicked) ChooseAction(c);
        }

        private void ChooseAction(Combatant c)
        {
            var a = c.Actions[_actionIndex];
            string why = _battle.Unusable(c, a);
            if (why != null)
            {
                Warn(why);
                return;
            }
            if (a.Kind == ActionKind.Item)
            {
                if (ItemOptions(c).All(o => o.why != null)) { Warn("No usable items"); return; }
                _pickingItem = true;
                _itemIndex = Math.Max(0, ItemOptions(c).FindIndex(o => o.why == null));
                return;
            }
            if (a.Side == TargetSide.Self)
            {
                CommitPlan(new PlannedAction { Action = a });
                return;
            }
            _choices.Clear();
            _choices.AddRange(ChoicesFor(c, a));
            _choiceIndex = 0;
            if (a.Kind is ActionKind.Heal or ActionKind.Protect or ActionKind.Reweave)   // start on the most hurt ally
                _choiceIndex = _choices.IndexOf(_choices.OrderBy(t => (float)t.Unit.Hp / t.Unit.MaxHp).First());
            _pickingTarget = true;
        }

        // Targets in screen order. Position offers every other slot, empty ones included.
        private List<Choice> ChoicesFor(Combatant c, ActionDef a)
        {
            if (a.Kind == ActionKind.Swap)
                return Enumerable.Range(0, Battle.Slots).Where(s => s != c.Slot)
                    .Select(s => new Choice(_battle.Allies(c)[s] is { Alive: true } u ? u : null, s))
                    .OrderBy(ch => SlotX(true, ch.Slot)).ToList();
            return _battle.ValidTargets(c, a).OrderBy(t => _vis[t].X).Select(t => new Choice(t, t.Slot)).ToList();
        }

        private static PlannedAction PlanFor(ActionDef a, Choice ch) => a.Kind == ActionKind.Swap
            ? new PlannedAction { Action = a, TargetSlot = ch.Slot, Target = ch.Unit }
            : new PlannedAction { Action = a, Target = a.AreaEffect ? null : ch.Unit };

        private float ChoiceX(Choice ch) => ch.Unit != null ? _vis[ch.Unit].X : SlotX(true, ch.Slot);

        // Index into _choices under the mouse (a unit, or an empty slot for Position), or -1.
        private int HoveredChoice()
        {
            var unit = HoveredUnit();
            if (unit != null) return _choices.FindIndex(x => x.Unit == unit);
            int slot = Ui.Hover("slot");
            return slot >= 0 ? _choices.FindIndex(x => x.Unit == null && x.Slot == slot) : -1;
        }

        // ---- items

        // The trial's consumables, minus what earlier party members already plan to use this turn.
        private List<(ConsumableDef item, int count, string why)> ItemOptions(Combatant c)
        {
            var reserved = _choosers.Take(_chooserIndex).Where(x => x.Plan?.ItemId != null).Select(x => x.Plan.ItemId).ToList();
            var list = new List<(ConsumableDef, int, string)>();
            foreach (var id in _battle.Items.Distinct())
            {
                var item = ItemDb.GetConsumable(id);
                if (item == null) continue;
                int count = _battle.Items.Count(x => x == id) - reserved.Count(x => x == id);
                string why = count <= 0 ? "Someone else is using the last one"
                    : item.Effect == ConsumableEffect.HealParty && _battle.LivingParty.All(p => p.Hp >= p.MaxHp) ? "Everyone is at full HP"
                    : item.Effect == ConsumableEffect.ReviveFallen && !_battle.Fallen.Any(f => f.IsParty) ? "Nobody has fallen"
                    : null;
                list.Add((item, Math.Max(0, count), why));
            }
            return list;
        }

        private void UpdateItemPick(Combatant c)
        {
            var options = ItemOptions(c);
            if (options.Count == 0) { _pickingItem = false; return; }
            if (Input.Up) _itemIndex = (_itemIndex + options.Count - 1) % options.Count;
            if (Input.DownKey) _itemIndex = (_itemIndex + 1) % options.Count;
            bool clicked = Ui.Pick("itm", ref _itemIndex);
            _itemIndex = Math.Clamp(_itemIndex, 0, options.Count - 1);
            if (Input.Cancel || Ui.Clicked("back")) { _pickingItem = false; return; }
            if (!Input.Confirm && !clicked) return;
            var pick = options[_itemIndex];
            if (pick.why != null) { Warn(pick.why); return; }
            CommitPlan(new PlannedAction { Action = CommonActions.UseItem, ItemId = pick.item.Id });
        }

        private void Warn(string s)
        {
            _warning = s;
            _warningTime = 1.5f;
        }

        private Combatant HoveredUnit()
        {
            int i = Ui.Hover("unit");
            return i >= 0 && i < _all.Count ? _all[i] : null;
        }

        // ------------------------------------------------------------------ resolving

        private void UpdateResolve(float dt)
        {
            _timer -= dt;
            if (_timer > 0) return;

            if (_pendingAfterCutIn && _acting != null)
            {
                _pendingAfterCutIn = false;
                bool fizzles = _events.Count > 0 && _events.Peek().Type == BattleEventType.Fizzle;
                _timer = StartMove(_acting, _actingAction, FirstTarget(_acting), fizzles);
                return;
            }

            if (_events.Count > 0)
            {
                _timer = PlayEvent(_events.Dequeue());
                return;
            }

            // The actor's events are done: a melee attacker runs back to its slot first.
            if (_acting != null && _vis[_acting].DashX.HasValue)
            {
                _vis[_acting].DashX = null;
                _cam.Focus(1.04f, 0f);
                _timer = 0.28f;
                return;
            }

            _acting = null;
            _actingAction = null;
            if (_battle.Outcome != BattleOutcome.Ongoing)
            {
                Finish();
                return;
            }

            if (_resolveQueue.Count > 0)
            {
                var actor = _resolveQueue.Dequeue();
                _acting = actor;
                foreach (var e in _battle.Execute(actor)) _events.Enqueue(e);
                _timer = 0.05f;
                return;
            }

            if (!_endTurnDone)
            {
                _endTurnDone = true;
                _cam.Focus(1f, 0f);
                foreach (var e in _battle.EndTurn()) _events.Enqueue(e);
                _timer = 0.1f;
                return;
            }

            if (_battle.Outcome != BattleOutcome.Ongoing) Finish();
            else BeginChoose();
        }

        private void Finish()
        {
            _phase = Phase.Finished;
            _tweens.FromTo(_bannerIn, 0f, 1f, 0.5f, EasingFunctions.ExponentialOut);
            bool won = _battle.Outcome == BattleOutcome.Victory;
            Audio.Play(won ? "victory" : "defeat");
            _timer = won ? 1.8f : 2.6f;
            var t = CombatCamera.Tuning.Value;
            var party = _battle.LivingParty.ToList();
            float px = party.Count > 0 ? party.Average(c => _vis[c].X) : Game1.Width / 2f;
            _cam.Focus(won ? t.VictoryZoom : t.DefeatZoom, won ? (px - Game1.Width / 2f) * 0.6f : 0f);
            if (won)
            {
                foreach (var c in party) _fx.Emit("victory", Chest(c), 1);
                Say(BarkTrigger.Victory, RandomPartyMember(), force: true);
            }
            Log(won ? "Victory!" : "The party has fallen...", Palette.Accent);
        }

        // ------------------------------------------------------------------ barks

        private Combatant RandomPartyMember(Combatant except = null)
        {
            var pool = _battle.LivingParty.Where(c => c != except).ToList();
            return pool.Count == 0 ? null : pool[_barkRng.Next(pool.Count)];
        }

        // A party member reacts (top-left box). Not too often, unless it matters.
        private void Say(BarkTrigger trigger, Combatant who, bool force = false, float chance = 1f)
        {
            if (who == null || !who.IsParty) return;
            if (!force && (_time - _lastBark < 1.2f || _barkRng.NextDouble() > chance)) return;
            string line = BarkDb.Pick(who.Member?.Def.Id, trigger, _barkRng);
            if (line == null) return;
            _barks.Add(new Bark { Who = who, Text = line });
            if (_barks.Count > 2) _barks.RemoveAt(0);
            _lastBark = _time;
        }

        // ------------------------------------------------------------------ events

        private Vector2 Head(Combatant c) => new(_vis[c].X, GroundY - UnitHeight(c) - 20);
        private Vector2 Chest(Combatant c) => new(_vis[c].X, GroundY - UnitHeight(c) * 0.55f);
        private Vector2 Feet(Combatant c) => new(_vis[c].X, GroundY - 4);

        // Where a melee attacker stands to hit `target`.
        private float DashSpot(Combatant actor, Combatant target)
        {
            int dir = actor.IsParty ? 1 : -1;
            float gap = DashGap * (0.6f + 0.2f * (target.Scale + actor.Scale));
            return _vis[target].X - dir * gap;
        }

        // The first unit this action will affect, from the events already queued for it.
        private Combatant FirstTarget(Combatant actor) =>
            _events.FirstOrDefault(e => e.Target != null && e.Target.IsParty != actor.IsParty &&
                                        (e.Type == BattleEventType.Damage || e.Type == BattleEventType.Status || e.Type == BattleEventType.Death))?.Target;

        // Set when an action opened with a cut-in: its run-up / cast plays once the banner has had its moment.
        private bool _pendingAfterCutIn;

        // The user moves: melee runs up to the target, everything else lunges and casts. Returns the wait.
        private float StartMove(Combatant actor, ActionDef a, Combatant target, bool fizzles)
        {
            var tune = CombatCamera.Tuning.Value;
            var v = _vis[actor];
            int dir = actor.IsParty ? 1 : -1;
            if (a != null && a.IsMelee && target != null && !fizzles)
            {
                // Run up to the target. Area melee (Blood Feast, Cleave) starts at the first one.
                v.DashX = DashSpot(actor, target);
                _fx.Emit("dash", Feet(actor), dir);
                Audio.Play("dash");
                float mid = (v.DashX.Value + _vis[target].X) / 2f;
                _cam.Focus(tune.MeleeZoom, (mid - Game1.Width / 2f) * tune.ActionPan);
                return MeleeTravel + 0.12f;
            }

            v.Lunge = 1f;
            string cast = a?.CastFx ?? a?.Kind switch
            {
                ActionKind.Heal or ActionKind.Item => "cast_heal",
                ActionKind.Attack => "cast",
                _ => null,
            };
            _fx.Emit(cast, Chest(actor), dir);
            if (cast != null) Audio.Play("cast");
            float focusX = target != null ? (v.X + _vis[target].X) / 2f : v.X;
            _cam.Focus(tune.ActionZoom, (focusX - Game1.Width / 2f) * tune.ActionPan);
            return 0.45f;
        }

        // Applies one event's visuals and returns how long to wait before the next one.
        private float PlayEvent(BattleEvent e)
        {
            var tune = CombatCamera.Tuning.Value;
            switch (e.Type)
            {
                case BattleEventType.ActionStart:
                {
                    var v = _vis[e.Actor];
                    var a = e.Action;
                    _actingAction = a;
                    if (a == CommonActions.UseItem) Audio.Play("item");
                    v.Popup = e.Text;
                    v.PopupTime = 1.1f;
                    v.AnimSteps = a?.AnimSteps ?? Array.Empty<string>();
                    SetPose(v, 0);
                    Log($"{e.Actor.Name}: {e.Text}", e.Actor.IsParty ? Palette.Lilac300 : Palette.Rose);
                    int dir = e.Actor.IsParty ? 1 : -1;

                    var target = FirstTarget(e.Actor);
                    bool fizzles = _events.Count > 0 && _events.Peek().Type == BattleEventType.Fizzle;

                    // Big moves get a cut-in banner and cinema bars, and the camera holds on the user first:
                    // party skills of CutInMinSp+ SP, and an enemy's wind-up and the attack it unleashes.
                    bool bigMove = a != null && !fizzles &&
                        ((e.Actor.IsParty && a.SpCost >= tune.CutInMinSp) || a.Kind == ActionKind.WindUp || e.Actor.Actions.Any(x => x.WindUp == a)
                         || _events.Any(x => x.Type == BattleEventType.Passive && x.Text == "FEAST!"));
                    if (bigMove)
                    {
                        _cutInWho = e.Actor;
                        _cutInText = a.Name;
                        _cutInAge = 0f;
                        Moment(0f, 0.15f, e.Actor.IsParty ? Palette.Paper : Palette.Danger, cinematic: tune.CutInTime + 0.9f);
                        _cam.Focus(tune.MeleeZoom + 0.06f, (v.X - Game1.Width / 2f) * tune.ActionPan);
                        _cam.Kick(0.04f);
                        _timer = 0f;
                        _pendingAfterCutIn = true;
                        return tune.CutInTime * 0.75f;
                    }
                    return StartMove(e.Actor, a, target, fizzles);
                }
                case BattleEventType.NextHit:
                {
                    var v = _vis[e.Actor];
                    if (!v.DashX.HasValue) v.Lunge = 1f;
                    SetPose(v, e.Amount);
                    return 0.25f;
                }
                case BattleEventType.Damage:
                {
                    var v = _vis[e.Target];
                    Audio.Play(e.Crit ? "crit" : e.Actor == null || e.Actor == e.Target ? "tick" : _actingAction?.IsMelee == false ? "hit_magic" : "hit", 1f, e.Crit ? 0f : (float)(_barkRng.NextDouble() * 0.2 - 0.1));
                    int before = v.Hp;
                    v.Hp = Math.Max(0, v.Hp - e.Amount);
                    v.Flash = 1f;
                    v.Shake = e.Crit ? 1.6f : 1f;
                    _floaters.Add((e.Crit ? $"CRIT -{e.Amount}" : $"-{e.Amount}", Head(e.Target), 0, e.Crit ? Palette.Rose : Palette.Danger));
                    Log($"   {e.Target.Name} takes {e.Amount}" + (e.Crit ? " (critical!)" : ""), Palette.TextDim);

                    float travel = 0f;
                    if (e.Actor != null && e.Actor != e.Target)
                    {
                        // Area melee: hop from victim to victim ("dash through every enemy").
                        var av = _vis[e.Actor];
                        if (av.DashX.HasValue)
                        {
                            float spot = DashSpot(e.Actor, e.Target);
                            travel = Math.Abs(spot - av.DashX.Value) > 30 ? 0.16f : 0f;
                            av.DashX = spot;
                            av.Lunge = 1f;
                        }
                        int dir = e.Actor.IsParty ? 1 : -1;
                        bool melee = _actingAction?.IsMelee == true;
                        string hit = _actingAction?.HitFx ?? (melee ? "slash" : "impact");
                        _fx.Emit(hit, Chest(e.Target), dir);
                        if (e.Crit) _fx.Emit("crit", Chest(e.Target), dir);
                        if (e.Crit) Say(BarkTrigger.Crit, e.Actor, chance: 0.7f);
                    }
                    else _fx.Emit("blight_tick", Chest(e.Target), e.Target.IsParty ? -1 : 1);

                    // Party reactions: someone dropping low, or a big hit.
                    if (e.Target.IsParty && v.Hp > 0)
                    {
                        float low = e.Target.MaxHp * 0.35f;
                        if (before > low && v.Hp <= low) Say(BarkTrigger.AllyLow, RandomPartyMember(e.Target));
                        else if (e.Amount >= e.Target.MaxHp * 0.25f) Say(BarkTrigger.Hurt, e.Target, chance: 0.6f);
                    }

                    _cam.Shake(e.Crit ? tune.ShakeCrit : tune.ShakeHit);
                    _cam.Kick(e.Crit ? tune.CritZoomKick : tune.HitZoomKick);
                    // Action-cam: crits flash and slow down; a killing blow slows down harder; a boss's party-wide hit flashes red.
                    if (e.Crit) Moment(tune.CritSlowMo, tune.CritFlash);
                    if (v.Hp <= 0 && e.Target != e.Actor) Moment(tune.KillSlowMo, 0.12f);
                    if (e.Target.IsParty && _actingAction is { AreaEffect: true } && e.Actor?.Enemy?.Type == EnemyType.Apex) Moment(0.15f, 0.3f, Palette.Danger);
                    _hitStop = e.Crit ? tune.CritHitStop : tune.HitStop;
                    return 0.4f + travel;
                }
                case BattleEventType.Passive:
                {
                    // Big moments (a counter, an interrupt, a boss enraging, a Feast crit) get the display font,
                    // a burst, a camera kick and their own sound, so nobody misses them.
                    string upper = e.Text.ToUpperInvariant();
                    bool big = upper is "RIPOSTE!" or "INTERRUPTED!" or "ENRAGED!" or "FEAST!" or "WOVEN!" or "AEGIS!";
                    var who = e.Target ?? e.Actor;
                    var color = upper switch
                    {
                        "INTERRUPTED!" => Palette.KwPriority,
                        "ENRAGED!" => Palette.Danger,
                        "RIPOSTE!" or "AEGIS!" => Palette.KwGuard,
                        _ => Palette.Coral,
                    };
                    _floaters.Add((big ? upper : e.Text, Head(who) + new Vector2(0, big ? -40 : -26), 0, color));
                    Log($"   {e.Actor.Name}: {e.Text}", Palette.TextDim);
                    if (e.Text == "FEAST!") _fx.Emit("feast", Chest(e.Actor), e.Actor.IsParty ? 1 : -1);
                    if (big)
                    {
                        _fx.Emit("crit", Chest(who), who.IsParty ? 1 : -1);
                        _cam.Kick(tune.CritZoomKick * 0.6f);
                        Moment(tune.BigMoveSlowMo, 0.3f, color, cinematic: 1.0f);
                        Audio.Play(upper.TrimEnd('!').ToLowerInvariant());
                        return 0.55f;
                    }
                    Audio.Play("stack");
                    return 0.3f;
                }
                case BattleEventType.Heal:
                {
                    var v = _vis[e.Target];
                    Audio.Play("heal");
                    v.Hp = Math.Min(e.Target.MaxHp, v.Hp + e.Amount);
                    _floaters.Add(($"+{e.Amount}", Head(e.Target), 0, Palette.Heal));
                    Log($"   {e.Target.Name} recovers {e.Amount}", Palette.TextDim);
                    _fx.Emit("heal", Feet(e.Target), 1);
                    return 0.35f;
                }
                case BattleEventType.Revive:
                {
                    var v = _vis[e.Target];
                    v.Dying = false;
                    v.Alpha = 1f;
                    v.Slot = e.Target.Slot;
                    v.X = SlotX(e.Target.IsParty, v.Slot);
                    v.Hp = e.Target.Hp;
                    _floaters.Add(("REVIVED", Head(e.Target), 0, Palette.Heal));
                    Log($"   {e.Target.Name} gets back up", Palette.Heal);
                    _fx.Emit("heal", Feet(e.Target), 1);
                    _fx.Emit("cleanse", Chest(e.Target), 1);
                    return 0.5f;
                }
                case BattleEventType.Status:
                    _floaters.Add((e.Text.ToUpper(), Head(e.Target) + new Vector2(0, -24), 0, Figures.StatusColor(e.Status)));
                    Audio.Play(e.Status switch { StatusKind.Charging => "windup", StatusKind.Guard or StatusKind.Wall or StatusKind.Protected => "guard", _ => "status" });
                    if (e.Status == StatusKind.Charging) { Moment(tune.BigMoveSlowMo, 0.35f, Palette.KwPriority, cinematic: 1.2f); _cam.Shake(tune.ShakeCrit); }
                    Log($"   {e.Target.Name}: {e.Text}", Palette.TextDim);
                    _fx.Emit("status_" + e.Status.ToString().ToLowerInvariant(), Chest(e.Target), e.Target.IsParty ? -1 : 1);
                    return 0.3f;
                case BattleEventType.Cleanse:
                    _floaters.Add(("CLEANSED", Head(e.Target) + new Vector2(0, -24), 0, Palette.Heal));
                    _fx.Emit("cleanse", Chest(e.Target), 1);
                    return 0.3f;
                case BattleEventType.Move:
                    _vis[e.Actor].Slot = e.Actor.Slot;
                    if (e.Target != null) _vis[e.Target].Slot = e.Target.Slot;
                    _fx.Emit("step", Feet(e.Actor), e.Actor.IsParty ? 1 : -1);
                    Audio.Play("move");
                    Log($"   {e.Actor.Name} moves to {HubScreen.SlotName(e.Actor.Slot).ToLower()}" + (e.Target != null ? $", swapping with {e.Target.Name}" : ""), Palette.TextDim);
                    return 0.35f;
                case BattleEventType.Death:
                    _vis[e.Target].Dying = true;
                    Audio.Play("death");
                    _fx.Emit("death", Chest(e.Target), e.Target.IsParty ? -1 : 1, e.Target.Scale);
                    _cam.Shake(tune.ShakeDeath);
                    _cam.Kick(tune.CritZoomKick);
                    Log($"   {e.Text}", Palette.Danger);
                    if (e.Target.IsParty) Say(BarkTrigger.AllyDown, RandomPartyMember(e.Target), force: true);
                    else if (_acting is { IsParty: true }) Say(BarkTrigger.Kill, _acting, chance: 0.7f);
                    return 0.55f;
                case BattleEventType.Fizzle:
                    _floaters.Add((e.Text, Head(e.Actor), 0, Palette.TextDim));
                    Log($"   {e.Text}", Palette.TextDim);
                    return 0.6f;
                case BattleEventType.Info:
                    if (e.Target != null) _floaters.Add((e.Text, Head(e.Target) + new Vector2(0, -24), 0, Palette.Accent));
                    if (e.Actor != null && e.Actor == e.Target && _actingAction?.Kind == ActionKind.Charge) _fx.Emit("cast", Chest(e.Actor), 1);
                    Log($"   {e.Text}", Palette.TextDim);
                    return 0.3f;
            }
            return 0.2f;
        }

        // ------------------------------------------------------------------ draw: battlefield

        private static int UnitHeight(Combatant c) => (int)Math.Min(210, 130 * c.Scale);

        // Sprite box in layer space.
        private Rectangle UnitRect(Combatant c) => UnitRectAt(c, _vis[c].X);

        private Rectangle UnitRectAt(Combatant c, float x)
        {
            var v = _vis[c];
            int h = UnitHeight(c), w = (int)(h * 0.62f);
            float lunge = (float)Math.Sin(v.Lunge * Math.PI) * 30 * (c.IsParty ? 1 : -1);
            float shake = v.Shake > 0 ? (float)Math.Sin(v.Shake * 40) * 6 * v.Shake : 0;
            return new Rectangle((int)(x - w / 2f + lunge + shake), (int)(GroundY - h), w, h);
        }

        // Layer space -> layout (screen) space, for UI drawn on top of the characters.
        private Vector2 S(Vector2 p) => _cam.LayerToScreen(p);

        private Rectangle ScreenRect(Rectangle r)
        {
            var a = S(new Vector2(r.Left, r.Top));
            var b = S(new Vector2(r.Right, r.Bottom));
            return new Rectangle((int)Math.Min(a.X, b.X), (int)Math.Min(a.Y, b.Y), (int)Math.Abs(b.X - a.X), (int)Math.Abs(b.Y - a.Y));
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double t = gameTime.TotalGameTime.TotalSeconds;
            var layers = BackgroundDb.LayersFor(_background, CombatCamera.Tuning.Value);
            DrawBackground(sb, layers);
            _ambience.DrawBack(sb, new Rectangle(0, 0, Game1.Width, Game1.Height), Core.Draw.FrameTransform);
            DrawVignette(sb);

            // Who gets brackets: the chooser (white), the aimed target(s) (red on enemies, lilac on allies).
            var aimed = new HashSet<Combatant>();
            var aimedSlots = new HashSet<int>();
            if (_phase == Phase.Choose && _pickingTarget && _choices.Count > 0)
            {
                var a = Chooser.Actions[_actionIndex];
                foreach (var (ch, i) in _choices.Select((ch, i) => (ch, i)))
                {
                    if (!a.AreaEffect && i != _choiceIndex) continue;
                    if (ch.Unit != null) aimed.Add(ch.Unit); else aimedSlots.Add(ch.Slot);
                }
            }
            var hoveredUnit = _phase == Phase.Choose ? HoveredUnit() : null;

            // ---- Character layer (moves with the camera)
            var layer = _cam.Layer * Core.Draw.FrameTransform;
            sb.End();
            // Point sampling: the sprites are already resized to their screen size, and smoothing them again as the
            // camera zooms and sways is what made the pixel-art sheets look blurry.
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: layer);
            DrawSlotMarkers(sb, aimedSlots);
            // Whoever is acting draws in front (a melee attacker overlaps its target).
            foreach (var c in _all.OrderBy(c => c == _acting ? 1 : 0).ThenBy(c => c.IsParty ? -c.Slot : c.Slot))
                DrawUnit(sb, c, t, aimed.Contains(c), c == hoveredUnit);
            _fx.Draw(sb, layer);
            // Foreground layers (depth over 1) pass in front of the characters.
            if (layers.Any(l => l.Depth > 1f)) DrawLayers(sb, layers.Where(l => l.Depth > 1f));
            sb.End();
            sb.Begin(samplerState: SamplerState.PointClamp, transformMatrix: Core.Draw.FrameTransform);
            _ambience.DrawFront(sb, new Rectangle(0, 0, Game1.Width, PanelTop + 40), Core.Draw.FrameTransform);

            // ---- Action-cam overlays on the battlefield: flash, then letterbox bars.
            if (_flash > 0) Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, PanelTop), _flashColor * (_flash * _flash * 0.8f));
            float bars = CombatCamera.Tuning.Value.LetterboxHeight * _letterbox;
            if (bars > 0.5f)
            {
                Core.Draw.Rect(sb, new Rectangle(0, 0, Game1.Width, (int)bars), Palette.Ink);
                Core.Draw.Rect(sb, new Rectangle(0, PanelTop - (int)bars, Game1.Width, (int)bars), Palette.Ink);
            }

            // ---- HUD (fixed on screen; unit labels follow their unit)
            DrawDangerEdge(sb);
            _statusHits.Clear();
            foreach (var c in _all) DrawUnitInfo(sb, c);
            foreach (var f in _floaters)
            {
                // Pops up with a little overshoot, then drifts and fades. Numbers and call-outs (CRIT, RIPOSTE!) in the header face.
                float a = MathHelper.Clamp(1.3f - f.age, 0, 1);
                float rise = Ease.Progress(f.age, 0.45f, EasingFunctions.BackOut) * 34 + f.age * 12;
                var font = Assets.Header;
                bool backed = f.text.Any(char.IsLetter);
                var p = S(f.pos) + new Vector2(0, -rise);
                if (backed)
                {
                    var size = font.MeasureString(f.text);
                    Core.Draw.Slant(sb, new Rectangle((int)(p.X - size.X / 2 - 12), (int)(p.Y - size.Y / 2 - 1), (int)size.X + 24, (int)size.Y + 2), 8, Palette.Ink * 0.65f * a);
                }
                Core.Draw.TextCentered(sb, font, f.text, p, f.color * a);
            }

            DrawObjective(sb);
            DrawWindUpAlert(sb);
            DrawCutIn(sb, t);
            DrawBottomPanel(sb, t);
            DrawBarks(sb, t);   // over the panel edge, pointing at the portrait
            if (hoveredUnit is { IsParty: false, Enemy.TypeDef: not null }) DrawTypeTip(sb, hoveredUnit);
            DrawUnitTips(sb);
            DrawTurnOrder(sb, t);   // last: its hover tooltip sits on top of everything

            if (_warningTime > 0 && _warning != null)
            {
                var size = Assets.Header.MeasureString(_warning);
                float wa = MathHelper.Clamp(_warningTime * 2, 0, 1);
                var wr = new Rectangle((int)(Game1.Width / 2 - size.X / 2 - 24), PanelTop - 44, (int)size.X + 48, 36);
                Core.Draw.HighlightBar(sb, wr, Palette.Danger, 10, wa);
                Core.Draw.TextCentered(sb, Assets.Header, _warning, wr.Center.ToVector2(), Palette.Paper * wa, shadow: false);
            }

            DrawCoach(sb);
            if (_phase == Phase.Choose) _coach?.Draw(sb);
            if (_phase == Phase.Intro && Tutorial?.Banner != null) Banner(sb, Tutorial.Banner, Palette.Lilac300);
            else if (_phase == Phase.Intro)
                Banner(sb, _run.Current.Type switch
                {
                    RoomType.Objective when _run.Objective?.RoomLeader is { } leader => leader.Name.ToUpper(),
                    RoomType.Elite => "ELITE ENCOUNTER",
                    _ => "ENCOUNTER",
                }, Palette.Danger);
            if (_phase == Phase.Finished)
            {
                Banner(sb, _battle.Outcome == BattleOutcome.Victory ? "VICTORY" : "THE PARTY HAS FALLEN",
                    _battle.Outcome == BattleOutcome.Victory ? Palette.Gold : Palette.Danger);
                if (_timer < 1.0f)
                    Core.Draw.TextCentered(sb, Assets.Small, "click or Enter to continue", new Vector2(Game1.Width / 2, 300), Palette.TextDim);
            }
        }

        // Tutorial: the coach's instruction for this turn, just above the bottom panel.
        private void DrawCoach(SpriteBatch sb)
        {
            string text = Tutorial?.Instruction;
            if (text == null || _phase != Phase.Choose || _coach?.Showing == true) return;
            string line = $"{Tutorial.Coach}:  {text}";
            var size = Assets.Body.MeasureString(line);
            // Under the turn order, or under the wind-up alert when one is showing.
            int y = _battle.LivingEnemies.Any(e => e.WindUpNext != null) ? 192 : 140;
            var r = new Rectangle((int)(Game1.Width / 2 - size.X / 2 - 30), y, (int)size.X + 60, 36);
            float pulse = 0.75f + 0.25f * MathF.Sin(_time * 4f);
            Core.Draw.Slant(sb, r, 12, Palette.Ink * 0.85f);
            Core.Draw.Slant(sb, new Rectangle(r.X + 8, r.Bottom - 3, r.Width - 16, 3), 3, Palette.Lilac300 * pulse);
            RichText.Line(sb, Assets.Body, line, new Vector2(r.X + 30, r.Y + 7), Palette.Paper);
        }

        // The announcement band opens from its middle line (eased, see _bannerIn).
        private void Banner(SpriteBatch sb, string text, Color c)
            => Ui.Banner(sb, text, new Vector2(Game1.Width / 2f, 245), Game1.Width, c, _bannerIn);

        // Skill cut-in: a slanted band shoots across with the user's face, name and the move's name, holds,
        // then leaves the way it came. Party bands come from the left, enemy bands from the right.
        private void DrawCutIn(SpriteBatch sb, double t)
        {
            float life = CombatCamera.Tuning.Value.CutInTime + 0.35f;
            if (_cutInWho == null || _cutInAge >= life) return;
            bool party = _cutInWho.IsParty;
            float inT = Ease.Progress(_cutInAge, 0.18f, EasingFunctions.CubicOut);
            float outT = MathHelper.Clamp((_cutInAge - (life - 0.22f)) / 0.22f, 0, 1);
            float slide = (1 - inT) * 1400 - outT * outT * 1400;
            float dirSign = party ? -1 : 1;
            int y = 176;
            var accent = party ? Palette.Rose : Palette.Coral;

            // Speed streaks behind the band.
            for (int i = 0; i < 6; i++)
            {
                float sx = ((float)(t * 2600 + i * 431) % 1600) - 160;
                Core.Draw.Rect(sb, new Rectangle((int)(party ? sx : Game1.Width - sx), y - 20 + i * 26, 120 + i * 17, 2), Palette.Paper * 0.25f * inT * (1 - outT));
            }
            var band = new Rectangle((int)(dirSign * slide) - 40, y, Game1.Width + 80, 96);
            Core.Draw.Slant(sb, new Rectangle(band.X, band.Y + 8, band.Width, band.Height), 60, Palette.Ink * 0.75f);
            Core.Draw.Slant(sb, band, 60, accent * 0.92f);
            Core.Draw.Slant(sb, new Rectangle(band.X, band.Y + band.Height - 10, band.Width, 6), 4, Palette.Ink * 0.5f);

            // Face, name, move name.
            int faceX = party ? 150 : Game1.Width - 290;
            var face = new Rectangle(faceX + (int)(dirSign * slide * 1.2f), y - 26, 140, 140);
            Core.Draw.Slant(sb, new Rectangle(face.X - 6, face.Y + 20, face.Width + 12, face.Height - 26), 20, Palette.Ink);
            DrawFace(sb, _cutInWho, face, t);
            float textX = party ? face.Right + 30 : 140;
            float tx = textX + dirSign * slide * 0.8f;
            Core.Draw.Text(sb, Assets.Header, _cutInWho.Name, new Vector2(tx, y + 8), Palette.Paper);
            Core.Draw.Text(sb, Assets.Title, _cutInText.ToUpperInvariant(), new Vector2(tx, y + 34), Palette.Paper);
        }

        // Red screen edge, pulsing while a party member is in danger (HP at or under LowHpShare).
        private void DrawDangerEdge(SpriteBatch sb)
        {
            var low = _battle.LivingParty.Where(c => (float)_vis[c].Hp / c.MaxHp <= LowHpShare).ToList();
            if (low.Count == 0 || _phase == Phase.Finished) return;
            float pulse = (0.55f + 0.45f * MathF.Sin(_time * 5f)) * Math.Min(1f, 0.6f + 0.2f * low.Count);
            for (int i = 0; i < 18; i++)
            {
                float a = (1 - i / 18f) * (1 - i / 18f) * 0.55f * pulse;
                Core.Draw.Rect(sb, new Rectangle(0, i * 2, Game1.Width, 2), Palette.Danger * a);
                Core.Draw.Rect(sb, new Rectangle(i * 2, 0, 2, PanelTop), Palette.Danger * a);
                Core.Draw.Rect(sb, new Rectangle(Game1.Width - 2 - i * 2, 0, 2, PanelTop), Palette.Danger * a);
            }
            // Name who's in trouble, just above the bottom panel.
            string text = string.Join("  ", low.Select(c => c.Name.ToUpperInvariant())) + "  IN DANGER";
            var size = Assets.Small.MeasureString(text);
            var r = new Rectangle(Game1.Width - (int)size.X - 42, PanelTop - 28, (int)size.X + 28, 22);   // right side: the bark bubble sits left
            Core.Draw.Slant(sb, r, 7, Palette.Danger * (0.65f + 0.35f * pulse));
            Core.Draw.Text(sb, Assets.Small, text, new Vector2(r.X + 14, r.Y + 3), Palette.Paper, shadow: false);
        }

        // While choosing: a foe is winding up. Spell out what's coming and the answers to it.
        private void DrawWindUpAlert(SpriteBatch sb)
        {
            if (_phase != Phase.Choose) return;
            var foe = _battle.LivingEnemies.FirstOrDefault(e => e.WindUpNext != null);
            if (foe == null) return;
            const string help = "Brace with Guard (acts first), raise the Aegis Wall, or drag it to the front and Interrupt it.";
            string title = $"{foe.Name} is winding up {foe.WindUpNext.Name}!";
            float w = Math.Max(Assets.Header.MeasureString(title).X, Assets.Small.MeasureString(help).X) + 56;
            var r = new Rectangle((int)(Game1.Width / 2 - w / 2), 128, (int)w, 54);
            _windUpBox = r;
            Core.Draw.Slant(sb, new Rectangle(r.X + 5, r.Y + 4, r.Width, r.Height), 14, Palette.Ink * 0.6f);
            Core.Draw.Slant(sb, r, 14, Palette.Ink * 0.88f);
            Core.Draw.Slant(sb, new Rectangle(r.X + 10, r.Y, r.Width - 20, 4), 4, Palette.KwPriority);
            Icons.Draw(sb, "st_charging", new Rectangle(r.X + 18, r.Y + 16, 22, 22));
            Core.Draw.Text(sb, Assets.Header, title.ToUpperInvariant(), new Vector2(r.X + 46, r.Y + 6), Palette.KwPriority, shadow: false);
            RichText.Line(sb, Assets.Small, help, new Vector2(r.X + 46, r.Y + 32), Palette.Text);
        }

        // The pre-rendered background, following the camera. Without one (missing file or an empty
        // backgrounds.json) a plain gradient in the area's colours is drawn instead.
        private void DrawBackground(SpriteBatch sb, List<StageLayer> layers)
        {
            if (!layers.Any(l => l.Depth <= 1f && Assets.Art(l.ArtId) != null))
            {
                var area = _run.Area;
                for (int i = 0; i < 25; i++)
                    Core.Draw.Rect(sb, 0, i * 20, Game1.Width, 20, Color.Lerp(Palette.Background, area.Wall, i / 25f));
                Core.Draw.Rect(sb, 0, GroundY - 6, Game1.Width, Game1.Height - GroundY + 6, area.Floor);
                return;
            }
            DrawLayers(sb, layers.Where(l => l.Depth <= 1f));
        }

        // Parallax layers, back to front, each following the camera by its depth.
        // Smooth filtering: the images move and scale by fractions of a pixel every frame.
        private void DrawLayers(SpriteBatch sb, IEnumerable<StageLayer> layers)
        {
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: Core.Draw.FrameTransform);
            foreach (var l in layers)
            {
                var art = Assets.Art(l.ArtId);
                if (art != null) _cam.DrawBackdrop(sb, art.Texture, _background?.Overscan ?? 1.12f, l.OffsetY, l.Depth);
            }
        }

        // Darkened edges: pulls the eye to the fight.
        private static void DrawVignette(SpriteBatch sb)
        {
            if (_vignette == null)
            {
                const int w = 64, h = 36;
                var data = new Color[w * h];
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        float dx = (x + 0.5f) / w * 2 - 1, dy = (y + 0.5f) / h * 2 - 1;
                        float d = MathF.Sqrt(dx * dx * 0.8f + dy * dy);
                        float a = MathHelper.Clamp((d - 0.55f) / 0.7f, 0, 1);
                        data[y * w + x] = Palette.Ink * (a * a * 0.85f);
                    }
                _vignette = new Texture2D(sb.GraphicsDevice, w, h);
                _vignette.SetData(data);
            }
            sb.End();
            sb.Begin(samplerState: SamplerState.LinearClamp, transformMatrix: Core.Draw.FrameTransform);
            sb.Draw(_vignette, new Rectangle(0, 0, Game1.Width, Game1.Height), Color.White);
        }

        // Ground marks under each position; empty slots offered by Position show as ghost brackets.
        private void DrawSlotMarkers(SpriteBatch sb, HashSet<int> aimedSlots)
        {
            for (int s = 0; s < 4; s++)
                foreach (bool party in new[] { true, false })
                {
                    float x = SlotX(party, s);
                    Core.Draw.Rect(sb, x - 44, GroundY + 2, 88, 3, (party ? Palette.Lilac300 : Palette.Coral) * 0.25f);
                }

            if (!(_phase == Phase.Choose && _pickingTarget)) return;
            foreach (var ch in _choices.Where(ch => ch.Unit == null))
            {
                var r = new Rectangle((int)SlotX(true, ch.Slot) - 34, (int)GroundY - 110, 68, 110);
                bool on = aimedSlots.Contains(ch.Slot);
                Core.Draw.Rect(sb, r, Palette.Lilac300 * (on ? 0.18f : 0.06f));
                Brackets(sb, r, Palette.Lilac300 * (on ? 1f : 0.5f), on ? 3 : 2);
                Ui.Hit("slot", ch.Slot, ScreenRect(r));
            }
        }

        private void DrawUnit(SpriteBatch sb, Combatant c, double t, bool aimed, bool hovered)
        {
            var v = _vis[c];
            if (v.Alpha <= 0) return;
            var r = UnitRect(c);
            var box = new Rectangle(r.X - 12, r.Y - 10, r.Width + 24, r.Height + 16);

            // Corner brackets: the character choosing (white), aimed targets, and hover.
            bool isChooser = _phase == Phase.Choose && c == Chooser && !_pickingTarget;
            bool isActing = _phase == Phase.Resolve && c == _acting;
            if (aimed) Brackets(sb, box, c.IsParty ? Palette.Lilac300 : Palette.Coral, 3);
            else if (isChooser || isActing) Brackets(sb, box, Palette.Paper, 3);
            else if (hovered) Brackets(sb, box, Palette.Grey500, 2);

            // Afterimages while running.
            float speed = v.X - v.PrevX;
            if (Math.Abs(speed) > 4f)
                for (int k = 1; k <= 2; k++)
                    DrawSprite(sb, c, UnitRectAt(c, v.X - speed * 3 * k), t, v.Alpha * (0.3f / k), 0f, v.Pose);

            DrawSprite(sb, c, r, t, v.Alpha, v.Flash, v.Pose);

            // Clickable in layout space.
            if (c.Alive && !v.Dying) Ui.Hit("unit", _all.IndexOf(c), ScreenRect(r));
        }

        // Four corner brackets, like a camera focus frame.
        private static void Brackets(SpriteBatch sb, Rectangle r, Color c, int t)
        {
            int len = Math.Max(10, Math.Min(r.Width, r.Height) / 4);
            Core.Draw.Rect(sb, r.Left, r.Top, len, t, c); Core.Draw.Rect(sb, r.Left, r.Top, t, len, c);
            Core.Draw.Rect(sb, r.Right - len, r.Top, len, t, c); Core.Draw.Rect(sb, r.Right - t, r.Top, t, len, c);
            Core.Draw.Rect(sb, r.Left, r.Bottom - t, len, t, c); Core.Draw.Rect(sb, r.Left, r.Bottom - len, t, len, c);
            Core.Draw.Rect(sb, r.Right - len, r.Bottom - t, len, t, c); Core.Draw.Rect(sb, r.Right - t, r.Bottom - len, t, len, c);
        }

        private static void DrawSprite(SpriteBatch sb, Combatant c, Rectangle r, double t, float alpha, float flash, string pose)
        {
            string glyph = c.IsParty ? c.Member.Def.Role.Substring(0, 1) : null;
            Figures.Unit(sb, c.ArtId, glyph, r, c.Color, !c.IsParty, t, alpha, flash,
                pose: pose, fallbackSprite: c.IsParty ? null : EnemyDb.MockupSprite);
        }

        // Under each unit: NAME, HP bar with numbers, SP segments, statuses; enemies attacking show "<<<".
        private void DrawUnitInfo(SpriteBatch sb, Combatant c)
        {
            var v = _vis[c];
            if (v.Alpha <= 0) return;
            float a = v.Alpha;
            // Labels stay under the unit's slot (not where it dashed to) and above the bottom panel.
            var foot = S(new Vector2(SlotX(c.IsParty, v.Slot), GroundY));
            float y = Math.Min(foot.Y + 2, PanelTop - 78);
            const int barW = 100;
            float x = foot.X - barW / 2f;   // centred on the slot
            var top = S(new Vector2(v.X, GroundY - UnitHeight(c)));

            // Name on a slanted plate, then a thick HP bar with the numbers inside it.
            var plate = new Rectangle((int)x - 6, (int)y, barW + 12, 19);
            Core.Draw.Slant(sb, plate, 6, Palette.Ink * 0.72f * a);
            Core.Draw.Text(sb, Assets.Small, Fit(c.Name.ToUpperInvariant(), Assets.Small, barW - 4), new Vector2(x + 2, y + 1),
                (c.IsParty ? Palette.Paper : Palette.Blush) * a, shadow: false);
            var bar = new Rectangle((int)x, (int)y + 20, barW, 15);
            float hpShare = c.MaxHp > 0 ? (float)v.Hp / c.MaxHp : 0;
            bool critical = c.IsParty && c.Alive && hpShare <= LowHpShare;
            var hpCol = c.IsParty ? (critical ? Core.Draw.Lerp(Palette.PartyHp, Palette.Paper, 0.5f + 0.5f * MathF.Sin(_time * 8f)) : Palette.PartyHp) : Palette.EnemyHp;
            Core.Draw.Bar(sb, bar, hpShare, hpCol * a, Palette.Ink * 0.85f * a);
            Core.Draw.TextCentered(sb, Assets.Small, $"{v.Hp}/{c.MaxHp}", new Vector2(bar.Center.X, bar.Center.Y - 1), Palette.Paper * a);

            if (c.Alive)
            {
                float rowY = y + 40;
                // Party: SP pips ("SP" so it reads as skill points), then the character's own stacks.
                if (c.MaxSp > 0)
                {
                    var spRow = new Rectangle((int)x, (int)rowY - 2, barW, 14);
                    Core.Draw.Text(sb, Assets.Small, "SP", new Vector2(x, rowY - 4), Palette.KwSp * a);
                    for (int i = 0; i < c.MaxSp; i++)
                        Core.Draw.Slant(sb, new Rectangle((int)x + 22 + i * 9, (int)rowY, 8, 10), 2, (i < c.Sp ? Palette.KwSp : Palette.Grey900) * a);
                    float sx = x + 22 + c.MaxSp * 9 + 4;
                    int stacks = c.Has(PassiveKind.Feast) ? Battle.FeastThreshold : c.Has(PassiveKind.Aegis) ? Battle.AegisThreshold : c.Has(PassiveKind.Weave) ? Battle.ThreadMax : 0;
                    int have = c.Has(PassiveKind.Feast) ? c.Feast : c.Has(PassiveKind.Aegis) ? c.Aegis : c.Thread;
                    var stackCol = c.Has(PassiveKind.Aegis) ? Palette.KwGuard : Palette.KwStack;
                    for (int i = 0; i < stacks; i++)
                        Core.Draw.Diamond(sb, new Vector2(sx + 4 + i * 10, rowY + 5), 8, (i < have ? stackCol : Palette.Grey900) * a);
                    Ui.Hit("pips", _all.IndexOf(c), spRow);
                    rowY += 16;
                }
                // Enemies have no SP: their Type (Brute / Stalker) sits on that row instead. Hover for what it means.
                else if (c.Enemy?.TypeDef != null)
                {
                    Figures.TypeChip(sb, c.Enemy.TypeDef, new Vector2(x, rowY - 1), alpha: a);
                    rowY += 18;
                }
                foreach (var (r, s) in Figures.Statuses(sb, c.Statuses, new Vector2(x, rowY), a))
                {
                    int key = _statusHits.Count;
                    _statusHits.Add((r, c, s));
                    Ui.Hit("status", key, r);
                }

                // Intent: this enemy means to attack someone this turn.
                if (!c.IsParty && _phase == Phase.Choose && c.Plan?.Action.Kind == ActionKind.Attack)
                {
                    float pulse = 0.6f + 0.4f * MathF.Sin(_time * 6f);
                    for (int i = 0; i < 3; i++)   // in the gap left of the bar, pointing at the party
                        Chevron(sb, new Vector2(bar.Left - 8 - i * 6, bar.Center.Y), 4, Palette.Rose * pulse, left: true);
                }

                // Winding up: a pulsing "!" badge over its head (the alert at the top says what's coming).
                if (c.WindUpNext != null)
                {
                    float pulse = 0.5f + 0.5f * MathF.Sin(_time * 7f);
                    var head = new Vector2(top.X, top.Y - 18);
                    Core.Draw.Diamond(sb, head, 34 + pulse * 6, Palette.Ink * 0.8f);
                    Core.Draw.Diamond(sb, head, 28 + pulse * 6, Palette.KwPriority);
                    Icons.Draw(sb, "st_charging", new Rectangle((int)head.X - 10, (int)head.Y - 10, 20, 20));
                }
            }

            if (v.PopupTime > 0 && v.Popup != null)
            {
                // The action being used, on a slanted tag above the unit.
                var size = Assets.Header.MeasureString(v.Popup.ToUpperInvariant());
                var pos = new Vector2(top.X, top.Y - 24);
                var tag = new Rectangle((int)(pos.X - size.X / 2 - 16), (int)(pos.Y - 17), (int)size.X + 32, 32);
                Core.Draw.HighlightBar(sb, tag, c.IsParty ? Palette.Paper : Palette.Rose, 10);
                Core.Draw.TextCentered(sb, Assets.Header, v.Popup.ToUpperInvariant(), pos, c.IsParty ? Palette.Ink : Palette.Paper, shadow: false);
            }
        }

        // HP share at which a party member counts as in danger (pulsing bar, red screen edge).
        private const float LowHpShare = 0.3f;

        // Status icons drawn this frame, for hover tooltips (index = Ui "status" key).
        private readonly List<(Rectangle rect, Combatant unit, StatusInstance status)> _statusHits = new();

        // Hover help for the little things under units: status icons and the SP / stack pips.
        private void DrawUnitTips(SpriteBatch sb)
        {
            int si = Ui.Hover("status");
            if (si >= 0 && si < _statusHits.Count)
            {
                var (r, unit, s) = _statusHits[si];
                Tooltip.Draw(sb, r, Figures.StatusName(s.Kind), Figures.StatusText(s), Figures.StatusColor(s.Kind), unit.Name);
                return;
            }
            int pi = Ui.Hover("pips");
            if (pi >= 0 && pi < _all.Count)
            {
                var c = _all[pi];
                string stacks = c.Has(PassiveKind.Feast) ? $"\nDiamonds: Feast {c.Feast}/{Battle.FeastThreshold}. At {Battle.FeastThreshold}, her next skill is a guaranteed crit."
                    : c.Has(PassiveKind.Aegis) ? $"\nDiamonds: Guard {c.Aegis}/{Battle.AegisThreshold}. At {Battle.AegisThreshold}, she intercepts the next attack on an ally next to her (or spends them on Iron Rebuke)."
                    : c.Has(PassiveKind.Weave) ? $"\nDiamonds: Thread {c.Thread}/{Battle.ThreadMax}. At {Battle.ThreadMax}, his next Mend, Bind or Snare Line is Woven and hits everyone."
                    : "";
                var anchor = new Rectangle((int)S(new Vector2(SlotX(true, _vis[c].Slot), GroundY)).X - 48, PanelTop - 60, 96, 12);
                Tooltip.Draw(sb, anchor, $"{c.Name}: SP {c.Sp}/{c.MaxSp}",
                    $"Squares: SP (Ethergy), spent by skills. +{Battle.SpPerTurn} SP every turn, +{Battle.ChargeSp} more with Focus." + stacks,
                    Palette.KwSp);
            }
        }

        // Hovering an enemy: its Type and what that means for who it goes after.
        private void DrawTypeTip(SpriteBatch sb, Combatant c)
        {
            var type = c.Enemy.TypeDef;
            var foot = S(new Vector2(SlotX(false, _vis[c].Slot), GroundY));
            float plate = Math.Min(foot.Y + 2, PanelTop - 70);
            var anchor = new Rectangle((int)foot.X - 48, (int)plate - 4, 96, 4);
            string intent = c.Plan == null ? "" : $"\nThis turn: {PlanText(c.Plan)}.";
            Tooltip.Draw(sb, anchor, c.Name, type.Summary + intent, Figures.TypeColor(type.Type) == Palette.Grey700 ? Palette.Paper : Figures.TypeColor(type.Type),
                type.Name + " type");
        }

        // A "<" (or ">") arrow head.
        private static void Chevron(SpriteBatch sb, Vector2 tip, float size, Color c, bool left)
        {
            float d = left ? 1 : -1;
            Core.Draw.Line(sb, tip, tip + new Vector2(d * size, -size), c, 2);
            Core.Draw.Line(sb, tip, tip + new Vector2(d * size, size), c, 2);
        }

        private static void SetPose(Visual v, int step)
        {
            if (v.AnimSteps.Length == 0) return;
            v.Pose = v.AnimSteps[Math.Min(step, v.AnimSteps.Length - 1)].Trim();
            v.PoseTime = 0.8f;
        }

        // ------------------------------------------------------------------ draw: top HUD

        // Top-left: the latest party reactions, newest at the bottom.
        private void DrawBarks(SpriteBatch sb, double t)
        {
            // The latest line only, as a light speech bubble just above the bottom panel on the left (over the party's
            // name plates, above the portrait), with the speaker's face and a name tag. It pops up, then fades.
            var b = _barks.LastOrDefault();
            if (b == null) return;
            float a = MathHelper.Clamp(Math.Min(b.Age * 6f, (BarkLife - b.Age) * 2f), 0, 1);
            float pop = Ease.Progress(b.Age, 0.3f, EasingFunctions.BackOut);
            float textW = Assets.Body.MeasureString(b.Text).X;
            var bubble = new Rectangle(14, (int)(PanelTop - 70 + (1 - pop) * 18), (int)Math.Clamp(textW + 90, 260, 430), 54);
            Core.Draw.Slant(sb, new Rectangle(bubble.X + 4, bubble.Y + 4, bubble.Width, bubble.Height), 10, Palette.Ink * 0.6f * a);
            Core.Draw.Slant(sb, bubble, 10, Palette.Paper * a);
            // Tail pointing down at the portrait.
            for (int i = 0; i < 10; i++)
                Core.Draw.Rect(sb, new Rectangle(bubble.X + 40 + i, bubble.Bottom, 12 - i, 1 + i), Palette.Paper * a);
            var face = new Rectangle(bubble.X + 14, bubble.Y + 5, 40, 44);
            Core.Draw.Rect(sb, face, Palette.Grey900 * a);
            if (b.Who.Member != null) Figures.Portrait(sb, b.Who.Member.Def, face, t, a);
            Core.Draw.Text(sb, Assets.Body, Fit(b.Text, Assets.Body, bubble.Width - 80), new Vector2(face.Right + 12, bubble.Y + 17), Palette.Ink * a, shadow: false);
            // Name tag on the bubble's top edge.
            var nameSize = Assets.Header.MeasureString(b.Who.Name);
            var tag = new Rectangle(face.Right + 6, bubble.Y - 18, (int)nameSize.X + 22, 26);
            Core.Draw.Slant(sb, tag, 8, Palette.Rose * a);
            Core.Draw.Text(sb, Assets.Header, b.Who.Name, new Vector2(tag.X + 11, tag.Y - 2), Palette.Paper * a, shadow: false);
        }

        // Top-right: the trial's current objective.
        private bool IsPlaytestFight => Playtest.Active && !_run.Map.Rooms.ContainsValue(_run.Current);

        // Playtest: leave without finishing. A Playtest-menu fight goes back to the menu; a fight inside a playtest
        // trial is skipped (room cleared, no loot, HP lost so far is kept).
        private void LeaveFight()
        {
            _run.ApplyBattleResult(_battle);
            if (IsPlaytestFight) Playtest.LastResult = $"Left the fight on turn {_battle.Turn}.";
            else
            {
                _run.Current.Cleared = true;
                _run.Current.Pack = null;
            }
            Screens.Pop();
        }

        private void DrawObjective(SpriteBatch sb)
        {
            var lines = new List<string>();
            // A Playtest-menu fight happens outside any map: the trial's objective doesn't apply.
            bool playtestFight = IsPlaytestFight;
            if (Tutorial?.Objective != null) lines.Add(Tutorial.Objective);
            else if (playtestFight) lines.Add("Playtest fight: win, lose or leave (F2)");
            else if (!_run.ObjectiveCompleted)
                lines.Add(_run.Objective.Count > 1 ? $"{_run.Objective.Task}  {_run.ObjectiveProgress}/{_run.Objective.Count}" : _run.Objective.Task);
            var profile = Game1.Instance.Profile;
            foreach (var q in QuestDb.All.Where(q => Tutorial == null && Quests.IsActive(profile, q)))
                lines.Add(Quests.IsDone(profile, q) ? $"{q.Name}: report to {q.Giver}" : $"{q.Name}  {Quests.Progress(profile, q)}/{q.Target}");
            if (lines.Count == 0) lines.Add("Find an Extraction Point");

            var r = new Rectangle(Game1.Width - 300, 10, 290, 38 + Math.Min(2, lines.Count) * 18);
            Core.Draw.Slant(sb, r, -14, Palette.Ink * 0.78f);
            Core.Draw.Slant(sb, new Rectangle(r.Right - 18, r.Y, 18, r.Height), -8, Palette.Coral);
            Icons.Draw(sb, "ui_objective", new Rectangle(r.X + 20, r.Y + 7, 20, 20));
            Core.Draw.Text(sb, Assets.Header, "OBJECTIVE", new Vector2(r.X + 46, r.Y + 3), Palette.Paper, shadow: false);
            for (int i = 0; i < lines.Count && i < 2; i++)
                Core.Draw.Text(sb, Assets.Small, Fit(lines[i], Assets.Small, r.Width - 70), new Vector2(r.X + 22, r.Y + 32 + i * 18), Palette.Blush, shadow: false);

            // Playtest: a way out of the fight (F2 does the same).
            if (Playtest.Active && _phase != Phase.Finished)
                Ui.Button(sb, "leavefight", 0, new Rectangle(r.X + 20, r.Bottom + 6, r.Width - 20, 28),
                    IsPlaytestFight ? "Leave fight (F2)" : "Skip this fight (F2)", font: Assets.Small);
        }

        // Top-centre: turn priority by Speed. The next to act is the big diamond on the left.
        private void DrawTurnOrder(SpriteBatch sb, double t)
        {
            var order = (_phase == Phase.Resolve
                    ? (_acting != null ? new[] { _acting } : Array.Empty<Combatant>()).Concat(_resolveQueue)
                    : _battle.PredictedOrder())
                .Where(c => c.Alive).Take(7).ToList();
            if (order.Count == 0 || _phase == Phase.Intro) return;

            const float startX = 340, cy = 42, gap = 92;
            float Cx(int i) => i == 0 ? startX : startX + 26 + i * gap;
            // The rail and its arrows (pointing toward whoever acts first).
            Core.Draw.Line(sb, new Vector2(Cx(0), cy), new Vector2(Cx(order.Count - 1) + 20, cy), Palette.Paper * 0.45f, 3);
            for (int i = 1; i < order.Count; i++)
                Chevron(sb, new Vector2((Cx(i - 1) + Cx(i)) / 2f + 6, cy), 6, Palette.Paper * 0.8f, left: true);

            int hoverIndex = Ui.Hover("order");
            for (int i = 0; i < order.Count; i++)
            {
                var c = order[i];
                float size = i == 0 ? 50 : 34;          // diamond side; its diagonal is ~1.41x
                var center = new Vector2(Cx(i), cy);
                bool current = (_phase == Phase.Choose && c == Chooser) || (_phase == Phase.Resolve && c == _acting);

                Core.Draw.Diamond(sb, center, size + 8, current ? Palette.Paper : Palette.Ink);
                Core.Draw.Diamond(sb, center, size + 2, c.IsParty ? Palette.Blush : Palette.Coral);
                Core.Draw.Diamond(sb, center, size - 4, c.IsParty ? Palette.PartyTag : Palette.EnemyTag);
                int face = (int)(size * 0.95f);
                var faceBox = new Rectangle((int)center.X - face / 2, (int)center.Y - face / 2, face, face);
                DrawFace(sb, c, faceBox, t);

                // Badge: who this unit's action is aimed at.
                var plan = c.Plan;
                if (plan != null && (plan.Target != null || plan.Action.AreaEffect || plan.TargetSlot >= 0))
                {
                    var bc = center + new Vector2(size * 0.5f, size * 0.42f);
                    bool atParty = plan.Target?.IsParty ?? (plan.Action.Kind == ActionKind.Swap || (plan.Action.Side == TargetSide.Ally) == c.IsParty);
                    Core.Draw.Diamond(sb, bc, 15, Palette.Ink);
                    Core.Draw.Diamond(sb, bc, 12, atParty ? Palette.Lilac300 : Palette.Coral);
                    string mark = plan.Action.AreaEffect ? "*" : plan.Target != null ? plan.Target.Name.Substring(0, 1) : (plan.TargetSlot + 1).ToString();
                    Core.Draw.TextCentered(sb, Assets.Small, mark, bc + new Vector2(0, -1), Palette.Ink, shadow: false);
                }

                // Action under the diamond: priority actions are tagged, a wound-up attack glows.
                bool first = plan?.Action.Priority == true;
                bool big = plan != null && plan.Action == c.WindUpNext;
                string act = plan == null ? (c.IsParty ? "CHOOSING" : "...") : (first ? "FIRST: " : "") + ActionShort(plan).ToUpperInvariant();
                var actPos = new Vector2(center.X, cy + size * 0.72f + 10);
                if (big || first)
                {
                    var asz = Assets.Small.MeasureString(Fit(act, Assets.Small, gap - 2));
                    Core.Draw.Slant(sb, new Rectangle((int)(actPos.X - asz.X / 2 - 8), (int)(actPos.Y - 9), (int)asz.X + 16, 18), 5,
                        (big ? Palette.Danger : Palette.Ink) * 0.9f);
                }
                Core.Draw.TextCentered(sb, Assets.Small, Fit(act, Assets.Small, gap - 2), actPos,
                    big ? Palette.Paper : first ? Palette.KwPriority : c.IsParty ? Palette.Lilac100 : Palette.Blush);

                var hit = new Rectangle((int)(center.X - size * 0.72f), (int)(center.Y - size * 0.72f), (int)(size * 1.44f), (int)(size * 1.44f));
                Ui.Hit("order", i, hit);
            }

            // What the rail means (one tester read the order as random).
            Core.Draw.Text(sb, Assets.Small, "TURN ORDER: left acts first. Fastest first; FIRST actions jump the queue.",
                new Vector2(startX - 30, 102), Palette.TextDim * 0.9f, shadow: true);

            // Hover: who it is, how fast, what it's doing.
            if (hoverIndex >= 0 && hoverIndex < order.Count)
            {
                var c = order[hoverIndex];
                string speed = c.EffectiveSpeed < c.Speed ? $"Speed {c.EffectiveSpeed} (Slowed from {c.Speed})" : $"Speed {c.Speed}";
                string doing = c.Plan == null ? (c.IsParty ? "Still choosing." : "Waiting.") : PlanText(c.Plan) + (c.Plan.Action.Priority ? " (Acts first)" : "") + ".";
                // Anchored on the rail's full height so the box opens below it.
                Tooltip.Draw(sb, new Rectangle((int)Cx(hoverIndex) - 20, 0, 40, 118), c.Name,
                    $"{speed}. Place {hoverIndex + 1} in the turn order.\n{doing}", c.IsParty ? Palette.Lilac300 : Palette.Rose, c.IsParty ? "party" : "enemy");
            }
        }

        // A unit's face for small frames: party portraits, enemies from their sprite.
        private static void DrawFace(SpriteBatch sb, Combatant c, Rectangle box, double t)
        {
            if (c.IsParty) { Figures.Portrait(sb, c.Member.Def, box, t); return; }
            var set = Figures.ResolveSprite(c.ArtId, EnemyDb.MockupSprite, c.Color);
            if (set != null) Sprites.DrawFit(sb, set, null, box, Color.White, flip: true);
            else Core.Draw.Rect(sb, box, c.Color);
        }

        private static string ActionShort(PlannedAction p) => p.Action.Kind == ActionKind.Item
            ? ItemDb.GetConsumable(p.ItemId)?.Name ?? "Item"
            : p.Action.Name;

        private static string PlanText(PlannedAction p) => p.Action.Kind switch
        {
            ActionKind.Swap => $"Position > {HubScreen.SlotName(p.TargetSlot)}",
            ActionKind.Item => ActionShort(p),
            _ => p.Action.Name + (p.Target != null && p.Action.IsSingleTarget ? " > " + p.Target.Name : p.Action.AreaEffect ? " > ALL" : ""),
        };

        // Shortens text to fit a width.
        private static string Fit(string s, UiFont f, float width)
        {
            if (f.MeasureString(s).X <= width) return s;
            while (s.Length > 1 && f.MeasureString(s + "..").X > width) s = s.Substring(0, s.Length - 1);
            return s + "..";
        }

        // ------------------------------------------------------------------ draw: bottom panel

        // Bottom panel columns: character card | controls + skills + Special Abilities | action card.
        private static readonly Rectangle StatusBox = new(10, PanelTop + 10, 276, 210);
        private static readonly Rectangle MiddleBox = new(296, PanelTop + 10, 578, 210);
        private static readonly Rectangle CardBox = new(884, PanelTop + 10, 388, 210);

        private void DrawBottomPanel(SpriteBatch sb, double t)
        {
            var panel = new Rectangle(0, PanelTop, Game1.Width, Game1.Height - PanelTop);
            Core.Draw.Rect(sb, panel, Palette.Ink * 0.94f);
            // Top edge: a thin line with a slanted accent block on the left (where the active character sits).
            Core.Draw.Rect(sb, new Rectangle(0, PanelTop, Game1.Width, 2), Palette.Grey900);
            Core.Draw.Slant(sb, new Rectangle(-10, PanelTop - 1, 330, 5), 4, Palette.Rose);
            Ui.Block(panel);

            // Whose status to show: the one choosing, else the acting party member, else the front-most.
            var who = Chooser ?? (_acting is { IsParty: true } ? _acting : null) ?? _battle.LivingParty.OrderBy(c => c.Slot).FirstOrDefault();
            if (who != null) DrawStatus(sb, StatusBox, who, t);

            var middle = MiddleBox;
            if (_phase == Phase.Choose && Chooser != null)
            {
                DrawHints(sb, new Rectangle(middle.X, middle.Y, middle.Width, 22));
                if (_pickingItem) DrawItemList(sb, new Rectangle(middle.X, middle.Y + 30, middle.Width, 176));
                else
                {
                    DrawSkillList(sb, new Rectangle(middle.X, middle.Y + 30, middle.Width, 132));
                    DrawSpecials(sb, new Rectangle(middle.X, middle.Bottom - 40, middle.Width, 40));
                }
                DrawDescription(sb, CardBox);
            }
            else
            {
                DrawLog(sb, middle);
                if (_actingAction != null) DrawActionCard(sb, CardBox, _actingAction, _acting);
            }
        }

        // Bottom-left: portrait, Name [Class], passives (hover for details), HP, SP (Ethergy), ATK and SPD.
        private void DrawStatus(SpriteBatch sb, Rectangle r, Combatant c, double t)
        {
            var portrait = new Rectangle(r.X, r.Y, 96, r.Height);
            Core.Draw.Slant(sb, portrait, 12, Palette.Grey900);
            Core.Draw.Slant(sb, new Rectangle(portrait.X, portrait.Bottom - 6, portrait.Width, 6), 1, Palette.Rose);
            if (c.Member != null) Figures.Portrait(sb, c.Member.Def, new Rectangle(portrait.X + 6, portrait.Y + 8, portrait.Width - 12, portrait.Height - 20), t);

            float x = portrait.Right + 12, y = r.Y - 4, w = r.Right - x;
            Core.Draw.Text(sb, Assets.Header, c.Name, new Vector2(x, y), Palette.Paper, shadow: true);
            string role = c.Member?.Def.Role;
            if (role != null)
            {
                var size = Assets.Small.MeasureString(role.ToUpperInvariant());
                Core.Draw.Slant(sb, new Rectangle((int)x, (int)y + 34, (int)size.X + 18, 18), 5, Palette.Violet);
                Core.Draw.Text(sb, Assets.Small, role.ToUpperInvariant(), new Vector2(x + 9, y + 35), Palette.Paper, shadow: false);
            }

            // Passives: hover a row for the full text.
            y += 58;
            int hoveredPassive = Ui.Hover("passive");
            int passiveRows = Math.Min(3, c.Passives.Count);
            for (int i = 0; i < passiveRows; i++)
            {
                var p = c.Passives[i];
                var row = new Rectangle((int)x - 4, (int)y - 1, (int)w + 4, 18);
                if (hoveredPassive == i) Core.Draw.Slant(sb, row, 5, Palette.Hover);
                Core.Draw.Diamond(sb, new Vector2(x + 4, y + 8), 7, Palette.Coral);
                string stack = p.Kind switch
                {
                    PassiveKind.Feast => $"{c.Feast}/{Battle.FeastThreshold}",
                    PassiveKind.Aegis => $"{c.Aegis}/{Battle.AegisThreshold}",
                    PassiveKind.Weave => c.Thread >= Battle.ThreadMax ? "READY" : $"{c.Thread}/{Battle.ThreadMax}",
                    _ => null,
                };
                Core.Draw.Text(sb, Assets.Small, Fit(p.Name, Assets.Small, w - 50), new Vector2(x + 13, y), hoveredPassive == i ? Palette.Paper : Palette.TextDim, shadow: false);
                if (stack != null) Core.Draw.TextRight(sb, Assets.Small, stack, new Vector2(r.Right - 2, y), p.Kind == PassiveKind.Aegis ? Palette.KwGuard : Palette.KwStack, shadow: false);
                Ui.Hit("passive", i, row);
                y += 18;
            }
            if (passiveRows > 0 && hoveredPassive < 0)
                Core.Draw.Text(sb, Assets.Small, "hover for details", new Vector2(x + 13, y), Palette.Grey500, shadow: false);

            // HP (pulses when in danger), SP pips, ATK / SPD.
            y = r.Y + 128;
            bool low = (float)c.Hp / c.MaxHp <= LowHpShare;
            Core.Draw.Label(sb, "HP", new Vector2(x, y + 2), Palette.TextDim);
            Core.Draw.TextRight(sb, Assets.Body, $"{c.Hp} / {c.MaxHp}", new Vector2(r.Right - 2, y - 1), low ? Palette.Danger : Palette.Paper);
            Core.Draw.Bar(sb, new Rectangle((int)x, (int)y + 20, (int)w, 8), (float)c.Hp / c.MaxHp,
                low ? Core.Draw.Lerp(Palette.PartyHp, Palette.Paper, 0.5f + 0.5f * MathF.Sin(_time * 8f)) : Palette.PartyHp, Palette.Grey900);

            y += 34;
            Core.Draw.Label(sb, "SP", new Vector2(x, y), Palette.KwSp);
            float seg = c.MaxSp > 0 ? (w - 30 - (c.MaxSp - 1) * 4) / c.MaxSp : 0;
            for (int i = 0; i < c.MaxSp; i++)
                Core.Draw.Slant(sb, new Rectangle((int)(x + 26 + i * (seg + 4)), (int)y + 2, (int)seg, 12), 4, i < c.Sp ? Palette.KwSp : Palette.Grey900);

            y += 22;
            Icons.Draw(sb, "ui_damage", new Rectangle((int)x, (int)y, 16, 16));
            Core.Draw.Label(sb, "ATK", new Vector2(x + 20, y), Palette.TextDim);
            Core.Draw.Text(sb, Assets.Body, c.Damage.ToString(), new Vector2(x + 54, y - 3), Palette.Paper);
            Icons.Draw(sb, "ui_speed", new Rectangle((int)x + 84, (int)y, 16, 16));
            Core.Draw.Label(sb, "SPD", new Vector2(x + 104, y), Palette.TextDim);
            Core.Draw.Text(sb, Assets.Body, c.EffectiveSpeed.ToString(), new Vector2(x + 142, y - 3), c.EffectiveSpeed < c.Speed ? Palette.KwSlow : Palette.Paper);

            // Who's choosing: one pip per party member, this one lit.
            if (_phase == Phase.Choose)
                for (int i = 0; i < _choosers.Count; i++)
                    Core.Draw.Slant(sb, new Rectangle(r.Right - (_choosers.Count - i) * 18, r.Bottom - 8, 14, 6), 3,
                        i == _chooserIndex ? Palette.Paper : i < _chooserIndex ? Palette.Grey500 : Palette.Grey900);

            // Passive details pop up above the panel.
            if (hoveredPassive >= 0 && hoveredPassive < c.Passives.Count)
            {
                var p = c.Passives[hoveredPassive];
                Tooltip.Draw(sb, new Rectangle(r.X + 40, PanelTop, 300, 1), p.Name, p.Description, Palette.Coral, "passive", 380);
            }
        }

        // Middle, top: what the keys (and mouse) do right now, as key caps, plus Undo / Back.
        private void DrawHints(SpriteBatch sb, Rectangle r)
        {
            // What to do now, in a few words; the keys live behind the "?" button.
            var a = Chooser.Actions[_actionIndex];
            string task = _pickingItem ? "Pick an item"
                : _pickingTarget ? (a.AreaEffect ? $"{a.Name} hits everyone marked: confirm" : $"Pick a target for {a.Name}")
                : $"Choose {Chooser.Name}'s action";
            Core.Draw.Label(sb, task, new Vector2(r.X + 34, r.Y + 3), Palette.TextDim);
            Ui.HelpButton(sb, new Vector2(r.X + 13, r.Y + 11),
                "Choose an action: Up/Down, or click it.\nSelect it: Enter, or click it again.\nPick a target: Left/Right then Enter, or click the target.\n" +
                "Shortcut: with an action selected, click an enemy to use it on them.\nBack / undo the last member: Esc, Backspace or right click.\n" +
                "While the turn plays: hold Space or the mouse button to speed up.");

            float bx = r.Right;
            if (_pickingTarget || _pickingItem)
            {
                bx -= 70;
                Ui.Button(sb, "back", 0, new Rectangle((int)bx, r.Y, 70, r.Height), "Back", font: Assets.Small);
            }
            if (_chooserIndex > 0)
            {
                bx -= 78;
                Ui.Button(sb, "undo", 0, new Rectangle((int)bx, r.Y, 72, r.Height), "< Undo", font: Assets.Small);
            }
        }

        // Middle: the chooser's skills. Icon, name (+ FIRST / HP cost tags), SP cost, positions it works from,
        // tiles it hits, and what it will do in real numbers.
        private void DrawSkillList(SpriteBatch sb, Rectangle r)
        {
            var c = Chooser;
            var skills = SkillIndexes(c);
            int rowH = Math.Min(33, r.Height / Math.Max(1, skills.Count));
            for (int k = 0; k < skills.Count; k++)
            {
                int i = skills[k];
                var a = c.Actions[i];
                bool usable = _battle.CanUse(c, a);
                bool sel = i == _actionIndex;
                bool hover = Ui.IsHover("act", i);
                var row = new Rectangle(r.X, r.Y + k * rowH, r.Width, rowH - 4);
                if (sel) Core.Draw.HighlightBar(sb, row, _pickingTarget ? Palette.Grey700 : Palette.Rose, 10);
                else if (hover) Core.Draw.Slant(sb, row, 10, Palette.Hover);
                else Core.Draw.Slant(sb, row, 10, Palette.Grey900 * 0.55f);
                Ui.Hit("act", i, row);
                float dim = usable ? 1f : 0.4f;
                var ink = sel ? Palette.Paper : usable ? Palette.Paper : Palette.TextDisabled;

                Icons.Draw(sb, SkillIcon(a), new Rectangle(row.X + 14, row.Y + (row.Height - 18) / 2, 18, 18), dim);
                Core.Draw.Text(sb, Assets.Body, a.Name, new Vector2(row.X + 40, row.Y + (row.Height - 21) / 2), ink, shadow: sel);
                float tx = row.X + 46 + Assets.Body.MeasureString(a.Name).X;
                if (a.Priority) tx = Tag(sb, "FIRST", tx, row, Palette.KwPriority, dim);
                if (a.HpCost > 0) tx = Tag(sb, $"-{a.HpCost} HP", tx, row, Palette.Danger, dim);

                // SP cost: a lilac star and the number ("FREE" for 0 SP skills).
                float cx = row.X + 262;
                if (a.SpCost > 0)
                {
                    Icons.Draw(sb, "ui_sp", new Rectangle((int)cx, row.Y + (row.Height - 14) / 2, 14, 14), dim);
                    Core.Draw.Text(sb, Assets.Body, a.SpCost.ToString(), new Vector2(cx + 17, row.Y + (row.Height - 21) / 2),
                        (c.Sp >= a.SpCost ? (sel ? Palette.Paper : Palette.KwSp) : Palette.Danger) * Math.Max(dim, 0.7f), shadow: false);
                }
                else Core.Draw.Text(sb, Assets.Small, "FREE", new Vector2(cx, row.Y + (row.Height - 17) / 2), (sel ? Palette.Paper : Palette.TextDim) * dim, shadow: false);

                // Positions it can be used from (party side, front on the right) and tiles it hits (enemy side).
                PositionPips(sb, a.UsableFrom, new Vector2(row.X + 304, row.Y + (row.Height - 10) / 2), dim);
                if (a.Side == TargetSide.Enemy)
                    TargetPips(sb, a.TargetRanks, new Vector2(row.X + 364, row.Y + (row.Height - 10) / 2), a.AreaEffect, dim);
                else
                    Core.Draw.Text(sb, Assets.Small, a.Side == TargetSide.Ally ? (a.AreaEffect ? "ALL ALLIES" : "ALLY") : "SELF",
                        new Vector2(row.X + 364, row.Y + (row.Height - 17) / 2), Palette.KwSp * dim, shadow: false);

                var (outcome, oc) = Outcome(c, a);
                Core.Draw.TextRight(sb, Assets.Body, outcome, new Vector2(row.Right - 18, row.Y + (row.Height - 21) / 2),
                    (sel && !_pickingTarget ? Palette.Paper : oc) * dim, shadow: sel);
            }
        }

        // A small slanted tag after a skill name. Returns the x after it.
        private static float Tag(SpriteBatch sb, string text, float x, Rectangle row, Color c, float dim)
        {
            var size = Assets.Small.MeasureString(text);
            var r = new Rectangle((int)x, row.Y + (row.Height - 16) / 2, (int)size.X + 12, 16);
            Core.Draw.Slant(sb, r, 4, c * 0.9f * dim);
            Core.Draw.Text(sb, Assets.Small, text, new Vector2(r.X + 6, r.Y - 1), Palette.Ink, shadow: false);
            return r.Right + 5;
        }

        private static string SkillIcon(ActionDef a) => a.Kind switch
        {
            ActionKind.Heal => "ui_heal",
            ActionKind.Defend or ActionKind.Taunt or ActionKind.Protect or ActionKind.Wall => "ui_guard",
            ActionKind.Item => "ui_item",
            ActionKind.Charge => "ui_charge",
            ActionKind.Swap or ActionKind.Reweave => "ui_position",
            ActionKind.WindUp => "st_charging",
            _ => "ui_attack",
        };

        // What the skill does in real numbers for this character right now (before the target's modifiers):
        // "5-7 DMG", "2 DMG x2", "+6 HP", or the effect.
        private static (string text, Color color) Outcome(Combatant c, ActionDef a)
        {
            switch (a.Kind)
            {
                case ActionKind.Attack:
                {
                    var (min, max) = Battle.EstimateDamage(c, null, a);
                    return ((min == max ? $"{min}" : $"{min}-{max}") + " DMG" + (a.Hits > 1 ? $" x{a.Hits}" : ""), Palette.KwDamage);
                }
                case ActionKind.Heal: return ($"+{Battle.EstimateHeal(c, a)} HP", Palette.KwHeal);
                case ActionKind.Taunt: return ("TAUNT", Palette.Taunt);
                case ActionKind.Defend: return ($"-{Math.Round((1 - Battle.GuardMultiplier) * 100)}% DMG", Palette.KwGuard);
                case ActionKind.Protect: return ("INTERCEPT", Palette.KwGuard);
                case ActionKind.Wall: return ($"-{Math.Round((1 - Battle.WallMultiplier) * 100)}% DMG", Palette.KwGuard);
                case ActionKind.Reweave: return ("PULL 1", Palette.KwMove);
                case ActionKind.Charge: return ($"+{Battle.ChargeSp} SP", Palette.KwSp);
                default: return ("", Palette.Paper);
            }
        }

        // Four squares, Back 2 .. Front 1 left to right (how the party stands on screen).
        private static void PositionPips(SpriteBatch sb, Ranks ranks, Vector2 pos, float dim)
        {
            for (int i = 0; i < 4; i++)
            {
                int slot = 3 - i;
                Core.Draw.Slant(sb, new Rectangle((int)pos.X + i * 13, (int)pos.Y, 11, 10), 3, (ranks.Has(slot) ? Palette.Paper : Palette.Grey700) * dim);
            }
        }

        // Four diamonds, enemy Front 1 .. Back 2 left to right; linked when the skill hits them all.
        private static void TargetPips(SpriteBatch sb, Ranks ranks, Vector2 pos, bool area, float dim)
        {
            if (area) Core.Draw.Rect(sb, pos.X + 5, pos.Y + 4, 13 * 3, 2, Palette.Coral * 0.7f * dim);
            for (int i = 0; i < 4; i++)
                Core.Draw.Diamond(sb, new Vector2(pos.X + 5 + i * 13, pos.Y + 5), 8, (ranks.Has(i) ? Palette.Coral : Palette.Grey700) * dim);
        }

        // Middle, bottom: Guard / Item / Focus / Position, as slanted buttons.
        private void DrawSpecials(SpriteBatch sb, Rectangle r)
        {
            var c = Chooser;
            var specials = SpecialIndexes(c);
            int w = (r.Width - (specials.Count - 1) * 8) / Math.Max(1, specials.Count);
            for (int k = 0; k < specials.Count; k++)
            {
                int i = specials[k];
                var a = c.Actions[i];
                bool usable = _battle.CanUse(c, a) && (a.Kind != ActionKind.Item || ItemOptions(c).Any(o => o.why == null));
                bool sel = i == _actionIndex;
                bool hover = Ui.IsHover("special", i);
                var b = new Rectangle(r.X + k * (w + 8), r.Y, w, r.Height);
                if (sel) Core.Draw.HighlightBar(sb, b, Palette.Rose, 12);
                else Core.Draw.Slant(sb, b, 12, hover ? Palette.Hover : Palette.Grey900);
                Icons.Draw(sb, SkillIcon(a), new Rectangle(b.X + 18, b.Y + (b.Height - 20) / 2, 20, 20), usable ? 1f : 0.4f);
                string label = a.Kind == ActionKind.Item ? $"ITEM ({_battle.Items.Count})" : a.Name.ToUpperInvariant();
                Core.Draw.Text(sb, Assets.Body, label, new Vector2(b.X + 44, b.Y + (b.Height - 21) / 2),
                    !usable ? Palette.TextDisabled : Palette.Paper, shadow: sel);
                if (a.Priority && usable) Core.Draw.Diamond(sb, new Vector2(b.Right - 22, b.Y + 9), 7, Palette.KwPriority);
                Ui.Hit("special", i, b);
            }
        }

        // Middle (Item chosen): the trial's consumables.
        private void DrawItemList(SpriteBatch sb, Rectangle r)
        {
            var options = ItemOptions(Chooser);
            if (options.Count == 0)
            {
                Core.Draw.Text(sb, Assets.Body, "No items. Consumables are found in Trials.", new Vector2(r.X + 8, r.Y + 6), Palette.TextDisabled);
                return;
            }
            for (int i = 0; i < options.Count; i++)
            {
                var (item, count, why) = options[i];
                bool sel = i == _itemIndex;
                var row = new Rectangle(r.X, r.Y + i * 36, r.Width, 32);
                if (sel) Core.Draw.HighlightBar(sb, row, Palette.Rose, 10);
                else Core.Draw.Slant(sb, row, 10, Ui.IsHover("itm", i) ? Palette.Hover : Palette.Grey900 * 0.55f);
                Ui.Hit("itm", i, row);
                Icons.Draw(sb, item.Id, new Rectangle(row.X + 14, row.Y + 3, 26, 26), why == null ? 1f : 0.4f);
                Core.Draw.Text(sb, Assets.Body, $"{item.Name}  x{count}", new Vector2(row.X + 48, row.Y + 5), why == null ? Palette.Paper : Palette.TextDisabled, shadow: sel);
                Core.Draw.TextRight(sb, Assets.Small, why ?? "", new Vector2(row.Right - 18, row.Y + 7), sel ? Palette.Paper : Palette.Danger, shadow: false);
            }
        }

        // Bottom-right: the highlighted action (or item), its target type and cost.
        private void DrawDescription(SpriteBatch sb, Rectangle r)
        {
            var c = Chooser;
            if (_pickingItem)
            {
                var options = ItemOptions(c);
                if (options.Count == 0) return;
                var (item, _, why) = options[Math.Clamp(_itemIndex, 0, options.Count - 1)];
                Core.Draw.SlabPanel(sb, r, Palette.Heal);
                Icons.Draw(sb, item.Id, new Rectangle(r.X + 14, r.Y + 10, 28, 28));
                Core.Draw.Text(sb, Assets.Header, item.Name.ToUpperInvariant(), new Vector2(r.X + 50, r.Y + 8), Palette.Paper, shadow: false);
                RichText.Wrapped(sb, Assets.Body, item.Description, new Vector2(r.X + 16, r.Y + 48), r.Width - 40, Palette.Text, lineGap: 2);
                if (why != null) Core.Draw.Text(sb, Assets.Small, why, new Vector2(r.X + 16, r.Bottom - 48), Palette.Danger, shadow: false);
                Core.Draw.Label(sb, "Party  -  takes this character's turn", new Vector2(r.X + 16, r.Bottom - 24), Palette.TextDim);
                return;
            }
            // Hovering an action previews it.
            int hover = Math.Max(Ui.Hover("act"), Ui.Hover("special"));
            var a = c.Actions[hover >= 0 && hover < c.Actions.Count ? hover : _actionIndex];
            DrawActionCard(sb, r, a, c);
        }

        // Bottom-right: name + tags, the description with its game terms coloured, then
        //   TARGET   single / multiple target, and who
        //   OUTCOME  the damage or healing it would do right now, against the aimed or hovered enemy
        //   EFFECT   statuses and side effects as coloured chips
        private void DrawActionCard(SpriteBatch sb, Rectangle r, ActionDef a, Combatant user)
        {
            var accent = a.Kind == ActionKind.Heal ? Palette.KwHeal : a.Side == TargetSide.Enemy ? Palette.Rose : Palette.KwGuard;
            Core.Draw.SlabPanel(sb, r, accent);
            Icons.Draw(sb, SkillIcon(a), new Rectangle(r.X + 14, r.Y + 12, 24, 24));
            Core.Draw.Text(sb, Assets.Header, a.Name.ToUpperInvariant(), new Vector2(r.X + 46, r.Y + 8), Palette.Paper, shadow: false);

            // Tags: cost and timing, on a row under the title.
            var tags = new List<(string, Color)>();
            if (a.SpCost > 0) tags.Add(($"{a.SpCost} SP", Palette.KwSp));
            else if (a.Kind == ActionKind.Attack || a.Kind == ActionKind.Heal) tags.Add(("FREE", Palette.Grey500));
            if (a.HpCost > 0) tags.Add(($"PAY {a.HpCost} HP", Palette.Danger));
            if (a.Priority) tags.Add(("ACTS FIRST", Palette.KwPriority));
            if (a.Hits > 1) tags.Add(($"{a.Hits} HITS", Palette.Paper));
            if (a.Interrupts) tags.Add(("INTERRUPTS", Palette.KwPriority));
            // Why it can't be picked right now goes first, in red.
            string why = user != null && user.IsParty && _phase == Phase.Choose ? _battle.Unusable(user, a) : null;
            if (why != null) tags.Insert(0, (why.ToUpperInvariant(), Palette.Danger));
            float tagX = r.X + 46, tagY = r.Y + 38;
            foreach (var (text, col) in tags)
            {
                var size = Assets.Small.MeasureString(text);
                if (tagX + size.X + 12 > r.Right - 18) break;   // no room: the rest is in the description anyway
                Core.Draw.Slant(sb, new Rectangle((int)tagX, (int)tagY, (int)size.X + 12, 17), 4, col * 0.9f);
                Core.Draw.Text(sb, Assets.Small, text, new Vector2(tagX + 6, tagY), Palette.Ink, shadow: false);
                tagX += size.X + 16;
            }

            string text2 = a.Kind == ActionKind.Charge
                ? $"Hold still and gather energy: +{Battle.ChargeSp} SP (on top of the usual +{Battle.SpPerTurn} SP per turn)."
                : a.Description ?? "";
            // Body font when it fits in three lines, the smaller one for long descriptions.
            float textW = r.Width - 40, textY = tags.Count > 0 ? r.Y + 60 : r.Y + 44;
            var font = RichText.Measure(Assets.Body, text2, textW) <= Assets.Body.LineSpacing * 3 + 3 ? Assets.Body : Assets.Small;
            RichText.Wrapped(sb, font, text2, new Vector2(r.X + 16, textY), textW, Palette.Text, lineGap: 1);

            float fy = r.Bottom - 72;
            Core.Draw.Rect(sb, r.X + 14, fy - 6, r.Width - 40, 1, Palette.Grey700);
            const float valueX = 110;
            void Label(string s, float yy) => Core.Draw.Label(sb, s, new Vector2(r.X + 16, yy), Palette.TextDim);

            Label("Target", fy);
            Core.Draw.Text(sb, Assets.Small, TargetType(a), new Vector2(r.X + valueX, fy), Palette.Paper, shadow: false);

            fy += 22;
            Label("Outcome", fy);
            var (outcome, color, note) = OutcomeLine(a, user);
            Core.Draw.Text(sb, Assets.Body, outcome, new Vector2(r.X + valueX, fy - 3), color, shadow: false);
            // The note ("vs Gnawer A", "aim or hover an enemy") only where it fits beside the numbers.
            if (note != null && valueX + Assets.Body.MeasureString(outcome).X + Assets.Small.MeasureString(note).X + 34 < r.Width)
                Core.Draw.TextRight(sb, Assets.Small, note, new Vector2(r.Right - 22, fy), Palette.TextDim, shadow: false);

            fy += 22;
            Label("Effect", fy);
            var effects = Effects(a);
            float ex = r.X + valueX;
            if (effects.Count == 0) Core.Draw.Text(sb, Assets.Small, "-", new Vector2(ex, fy), Palette.TextDisabled, shadow: false);
            foreach (var (label, chip) in effects)
            {
                var size = Assets.Small.MeasureString(label);
                if (ex + size.X + 12 > r.Right - 20) break;
                Core.Draw.Slant(sb, new Rectangle((int)ex, (int)fy - 1, (int)size.X + 12, 18), 4, chip * 0.9f);
                Core.Draw.Text(sb, Assets.Small, label, new Vector2(ex + 6, fy - 1), Palette.Ink, shadow: false);
                ex += size.X + 16;
            }
        }

        // "6-8 DMG" against a target (or base numbers with none), "+10 HP" for heals.
        private (string text, Color color, string note) OutcomeLine(ActionDef a, Combatant user)
        {
            if (user == null) return ("-", Palette.TextDisabled, null);
            if (a.Kind == ActionKind.Heal)
                return ($"+{Battle.EstimateHeal(user, a)} HP" + (a.AreaEffect ? " each" : ""), Palette.KwHeal,
                        user.Has(PassiveKind.Sympathetic) ? $"x{Battle.SympatheticMultiplier} if under {Math.Round(Battle.SympatheticHpThreshold * 100)}% HP" : null);
            if (a.Kind == ActionKind.Item) return ("see item", Palette.TextDim, null);
            if (a.Kind == ActionKind.Charge) return ($"+{Battle.ChargeSp} SP", Palette.KwSp, null);
            if (a.Kind != ActionKind.Attack) return (Outcome(user, a).text is { Length: > 0 } o ? o : "-", Outcome(user, a).color, null);

            // Against: the aimed target, else an enemy under the mouse, else nobody in particular.
            Combatant vs = null;
            if (_phase == Phase.Choose && _pickingTarget && _choices.Count > 0 && !a.AreaEffect && a == Chooser?.Actions[_actionIndex])
                vs = _choices[Math.Min(_choiceIndex, _choices.Count - 1)].Unit;
            vs ??= HoveredUnit() is { } h && h.IsParty != user.IsParty ? h : null;

            var (min, max) = Battle.EstimateDamage(user, vs, a);
            string text = (min == max ? $"{min}" : $"{min}-{max}") + " DMG" + (a.Hits > 1 ? $" x{a.Hits}" : "") + (a.AreaEffect ? " each" : "");
            if (vs == null) return (text, Palette.KwDamage, "aim or hover an enemy");
            // Stronger than usual against this one (Mark, Exploit...) = brighter; weaker (Guard, Wall) = grey.
            float m = Battle.TargetMultiplier(user, vs);
            var color = m > 1.01f ? Palette.KwMark : m < 0.99f ? Palette.Grey500 : Palette.KwDamage;
            string why = m > 1.01f ? "weak to this" : m < 0.99f ? "resists this" : null;
            return (text, color, "vs " + vs.Name + (why != null ? $" ({why})" : ""));
        }

        // Buffs / debuffs and other side effects, as chips in their keyword colours.
        private static List<(string label, Color chip)> Effects(ActionDef a)
        {
            var list = new List<(string, Color)>();
            if (a.Applies.HasValue)
            {
                var s = a.Applies.Value;
                string label = s switch
                {
                    StatusKind.Blight => $"BLIGHT {a.StatusPower} DMG x{a.StatusTurns}T",
                    StatusKind.Mark => $"MARK +{Math.Round((Battle.MarkMultiplier - 1) * 100)}% DMG {a.StatusTurns}T",
                    StatusKind.Slow => $"SLOW -{a.StatusPower} SPD {a.StatusTurns}T",
                    _ => s.ToString().ToUpperInvariant(),
                };
                list.Add((label, Figures.StatusColor(s)));
            }
            if (a.Interrupts) list.Add(("INTERRUPTS", Palette.KwPriority));
            if (a.Kind == ActionKind.Defend) list.Add(("GUARD -50% DMG", Palette.KwGuard));
            if (a.Kind == ActionKind.Taunt) list.Add(("TAUNT + GUARD", Palette.Taunt));
            if (a.Kind == ActionKind.Protect) list.Add(("ATTACKS ON THEM HIT MARA", Palette.KwGuard));
            if (a.Kind == ActionKind.Wall) list.Add(($"-{Math.Round((1 - Battle.WallMultiplier) * 100)}% DMG, SELF + ADJACENT", Palette.KwGuard));
            if (a.Kind == ActionKind.Reweave) list.Add(("PULL 1 TOWARD ELIAN", Palette.KwMove));
            if (a.GainAegis > 0) list.Add(($"+{a.GainAegis} GUARD", Palette.KwGuard));
            if (a.PowerPerAegis > 0) list.Add(($"SPENDS GUARD: +{Math.Round(a.PowerPerAegis * 100)}% EACH", Palette.KwGuard));
            if (a.PullForward > 0) list.Add(($"PULL {a.PullForward}", Palette.KwMove));
            if (a.GainThread > 0) list.Add(($"+{a.GainThread} THREAD", Palette.KwStack));
            if (a.Cleanse) list.Add(("CLEANSE", Palette.KwHeal));
            if (a.Lifesteal > 0) list.Add(($"LIFESTEAL {Math.Round(a.Lifesteal * 100)}%", Palette.KwHeal));
            if (a.KnockBack > 0) list.Add(($"KNOCKBACK {a.KnockBack}", Palette.KwMove));
            if (a.ShuffleTargets) list.Add(("SCRAMBLE", Palette.KwMove));
            if (a.SelfMoveAfter != 0) list.Add(($"RECOIL {Math.Abs(a.SelfMoveAfter)}", Palette.Grey500));
            return list;
        }

        // Single / multiple target, and who.
        private static string TargetType(ActionDef a) => a.Kind switch
        {
            ActionKind.Swap => "One position",
            ActionKind.Wall => "Self + adjacent allies",
            ActionKind.Item => "The party",
            ActionKind.WindUp => "Self (then everyone)",
            _ => a.Side switch
            {
                TargetSide.Self => "Self",
                TargetSide.Ally => a.AreaEffect ? "All allies" : "One ally",
                _ => a.AreaEffect ? "All enemies in reach" : "One enemy",
            },
        };

        // Middle while the turn plays out: the combat log, game terms coloured.
        private void DrawLog(SpriteBatch sb, Rectangle r)
        {
            var head = new Rectangle(r.X, r.Y, r.Width, 22);
            Core.Draw.Slant(sb, head, 6, Palette.Grey900);
            Core.Draw.Label(sb, _phase == Phase.Resolve ? $"Turn {_battle.Turn}" : "Combat log",
                new Vector2(r.X + 12, r.Y + 2), Palette.TextDim);
            // Recent actions only: each slides in, stays a few seconds, then fades away (newest at the bottom).
            var recent = _log.Where(l => _time - l.time < LogLife).TakeLast(5).ToList();
            for (int i = 0; i < recent.Count; i++)
            {
                var (text, color, at) = recent[i];
                float age = _time - at;
                float a = MathHelper.Clamp(Math.Min(age * 6f, (LogLife - age) * 1.5f), 0, 1);
                float slide = (1 - MathHelper.Clamp(age * 6f, 0, 1)) * -30;
                var row = new Rectangle((int)(r.X + slide), r.Y + 30 + i * 34, (int)Assets.Body.MeasureString(text).X + 40, 30);
                Core.Draw.Slant(sb, row, 8, Palette.Grey900 * 0.9f * a);
                Core.Draw.Slant(sb, new Rectangle(row.X, row.Y, 10, row.Height), 4, color * a);
                RichText.Line(sb, Assets.Body, text, new Vector2(row.X + 20, row.Y + 6), Palette.Text, a);
            }
        }
    }
}
