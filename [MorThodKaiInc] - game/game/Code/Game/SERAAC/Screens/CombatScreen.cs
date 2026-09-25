using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SERAAC.Combat;
using SERAAC.Core;
using SERAAC.Data;
using SERAAC.Trial;
using SERAAC.UI;

namespace SERAAC.Screens
{
    // Turn-based party combat.
    //   Choose Action: the player picks an action (+ target) for every living party member;
    //                  enemies have already picked theirs (shown as intents).
    //   Resolve Action: everyone acts in Speed order; the Battle rules produce events that
    //                  this screen plays back one by one.
    public sealed class CombatScreen : Screen
    {
        private enum Phase { Intro, Choose, Resolve, Finished }

        private sealed class Visual
        {
            public int Slot;          // slot currently shown (updated when a Move event plays)
            public float X;           // animated x
            public int Hp;            // HP currently shown (updated as events play)
            public float Flash, Shake, Lunge, Alpha = 1f;
            public bool Dying;
            public string Popup;      // action name shown above the unit while it acts
            public float PopupTime;
            public string Pose;       // sprite-sheet pose while acting (null = idle)
            public float PoseTime;
            public string[] AnimSteps = Array.Empty<string>();
        }

        private readonly TrialRun _run;
        private readonly List<Combatant> _enemies;
        private readonly Action<Battle> _onFinished;
        private Battle _battle;
        private readonly List<Combatant> _all = new();
        private readonly Dictionary<Combatant, Visual> _vis = new();

        private Phase _phase;
        private float _timer;

        // Choose phase state
        private List<Combatant> _choosers = new();
        private int _chooserIndex;
        private int _actionIndex;
        private bool _pickingTarget;
        private List<Combatant> _targets = new();
        private int _targetIndex;
        private string _warning;
        private float _warningTime;
        private bool _showIntents = true;

        // Resolve phase state
        private readonly Queue<Combatant> _resolveQueue = new();
        private readonly Queue<BattleEvent> _events = new();
        private List<Combatant> _turnOrder = new();
        private Combatant _acting;
        private bool _endTurnDone;
        private bool _reported;          // onFinished called

        private readonly List<(string text, Vector2 pos, float age, Color color)> _floaters = new();
        private readonly List<(string text, Color color)> _log = new();

        private const float GroundY = 430;

        public CombatScreen(TrialRun run, List<Combatant> enemies, Action<Battle> onFinished)
        {
            _run = run;
            _enemies = enemies;
            _onFinished = onFinished;
        }

        private Combatant Chooser => _chooserIndex < _choosers.Count ? _choosers[_chooserIndex] : null;

        public override void OnEnter()
        {
            _battle = new Battle(_run.BuildPartyCombatants(), _enemies, _run.Rng);
            _all.AddRange(_battle.Party.Where(c => c != null));
            _all.AddRange(_battle.Enemies.Where(c => c != null));
            foreach (var c in _all)
                _vis[c] = new Visual { Slot = c.Slot, X = SlotX(c.IsParty, c.Slot) + (c.IsParty ? -400 : 400), Hp = c.Hp };
            _phase = Phase.Intro;
            _timer = 0.9f;
            Log("Combat begins!", Palette.Accent);
        }

        private static float SlotX(bool party, int slot) => party ? 530 - slot * 122 : 750 + slot * 122;

        private void Log(string s, Color c)
        {
            _log.Add((s, c));
            if (_log.Count > 60) _log.RemoveAt(0);
        }

        // ------------------------------------------------------------------ flow

        private void BeginChoose()
        {
            CombatAI.PlanAll(_battle, _battle.LivingEnemies, _run.Rng);
            _choosers = _battle.LivingParty.OrderBy(c => c.Slot).ToList();
            _chooserIndex = 0;
            _pickingTarget = false;
            _phase = Phase.Choose;
            SelectFirstUsable();
        }

        private void SelectFirstUsable()
        {
            var c = Chooser;
            if (c == null) return;
            // Keep the previous choice if it's still usable, otherwise jump to the first usable one.
            if (c.Plan != null) _actionIndex = Math.Max(0, c.Actions.IndexOf(c.Plan.Action));
            if (_actionIndex >= c.Actions.Count || !_battle.CanUse(c, c.Actions[_actionIndex]))
                _actionIndex = Math.Max(0, c.Actions.FindIndex(a => _battle.CanUse(c, a)));
        }

        private void CommitPlan(PlannedAction plan)
        {
            Chooser.Plan = plan;
            _pickingTarget = false;
            _chooserIndex++;
            if (_chooserIndex >= _choosers.Count) BeginResolve();
            else SelectFirstUsable();
        }

        private void BeginResolve()
        {
            _phase = Phase.Resolve;
            _turnOrder = _battle.ResolveOrder();
            _resolveQueue.Clear();
            foreach (var c in _turnOrder) _resolveQueue.Enqueue(c);
            _endTurnDone = false;
            _timer = 0.2f;
            Log($"-- Turn {_battle.Turn} --", Palette.TextDim);
        }

        // ------------------------------------------------------------------ update

        public override void Update(GameTime gameTime)
        {
            float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
            AnimateVisuals(dt);
            _warningTime -= dt;

            if (Input.Pressed(Keys.T)) _showIntents = !_showIntents;

            switch (_phase)
            {
                case Phase.Intro:
                    _timer -= dt;
                    if (_timer <= 0) BeginChoose();
                    break;
                case Phase.Choose:
                    UpdateChoose();
                    break;
                case Phase.Resolve:
                    UpdateResolve(dt * (Input.FastForward ? 3f : 1f));
                    break;
                case Phase.Finished:
                    _timer -= dt;
                    if (!_reported && (_timer <= 0 || (_timer < 1.0f && Input.Confirm)))
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
                float target = SlotX(c.IsParty, v.Slot);
                v.X = MathHelper.Lerp(v.X, target, 1 - (float)Math.Pow(0.0001, dt));
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
        }

        private void UpdateChoose()
        {
            var c = Chooser;
            if (c == null) { BeginResolve(); return; }

            if (_pickingTarget)
            {
                var action = c.Actions[_actionIndex];
                if (!action.AreaEffect && _targets.Count > 0)
                {
                    if (Input.Left || Input.Up) _targetIndex = (_targetIndex + _targets.Count - 1) % _targets.Count;
                    if (Input.Right || Input.DownKey) _targetIndex = (_targetIndex + 1) % _targets.Count;
                }
                if (Input.Cancel) _pickingTarget = false;
                else if (Input.Confirm)
                    CommitPlan(new PlannedAction { Action = action, Target = action.AreaEffect ? null : _targets[_targetIndex] });
                return;
            }

            int n = c.Actions.Count;
            if (Input.Up) _actionIndex = (_actionIndex + n - 1) % n;
            if (Input.DownKey) _actionIndex = (_actionIndex + 1) % n;

            if (Input.Cancel && _chooserIndex > 0)
            {
                _chooserIndex--;
                Chooser.Plan = null;
                SelectFirstUsable();
                return;
            }

            if (Input.Confirm)
            {
                var a = c.Actions[_actionIndex];
                string why = _battle.Unusable(c, a);
                if (why != null)
                {
                    _warning = why;
                    _warningTime = 1.5f;
                    return;
                }
                if (a.Side == TargetSide.Self)
                {
                    CommitPlan(new PlannedAction { Action = a });
                    return;
                }
                _targets = _battle.ValidTargets(c, a).OrderBy(t => t.IsParty ? -t.Slot : t.Slot).ToList();
                _targetIndex = a.Kind == ActionKind.Heal
                    ? _targets.IndexOf(_targets.OrderBy(t => (float)t.Hp / t.MaxHp).First())
                    : 0;
                _pickingTarget = true;
            }
        }

        private void UpdateResolve(float dt)
        {
            _timer -= dt;
            if (_timer > 0) return;

            if (_events.Count > 0)
            {
                _timer = PlayEvent(_events.Dequeue());
                return;
            }

            _acting = null;
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
            _timer = _battle.Outcome == BattleOutcome.Victory ? 1.8f : 2.6f;
            Log(_battle.Outcome == BattleOutcome.Victory ? "Victory!" : "The party has fallen...", Palette.Accent);
        }

        // Applies one event's visuals and returns how long to wait before the next one.
        private float PlayEvent(BattleEvent e)
        {
            Vector2 Head(Combatant c) => new(_vis[c].X, GroundY - UnitHeight(c) - 20);

            switch (e.Type)
            {
                case BattleEventType.ActionStart:
                {
                    var v = _vis[e.Actor];
                    v.Popup = e.Text;
                    v.PopupTime = 1.1f;
                    v.Lunge = 1f;
                    v.AnimSteps = e.Action?.AnimSteps ?? Array.Empty<string>();
                    SetPose(v, 0);
                    Log($"{e.Actor.Name}: {e.Text}", e.Actor.IsParty ? Palette.Text : new Color(230, 170, 160));
                    return 0.45f;
                }
                case BattleEventType.NextHit:
                {
                    var v = _vis[e.Actor];
                    v.Lunge = 1f;
                    SetPose(v, e.Amount);
                    return 0.25f;
                }
                case BattleEventType.Damage:
                {
                    var v = _vis[e.Target];
                    v.Hp = Math.Max(0, v.Hp - e.Amount);
                    v.Flash = 1f;
                    v.Shake = e.Crit ? 1.6f : 1f;
                    _floaters.Add((e.Crit ? $"CRIT -{e.Amount}" : $"-{e.Amount}", Head(e.Target), 0, e.Crit ? Palette.Unsecured : Palette.Danger));
                    Log($"   {e.Target.Name} takes {e.Amount}" + (e.Crit ? " (critical!)" : ""), Palette.TextDim);
                    return 0.4f;
                }
                case BattleEventType.Passive:
                    _floaters.Add((e.Text, Head(e.Actor) + new Vector2(0, -26), 0, new Color(255, 120, 110)));
                    Log($"   {e.Actor.Name}: {e.Text}", Palette.TextDim);
                    return 0.3f;
                case BattleEventType.Heal:
                {
                    var v = _vis[e.Target];
                    v.Hp = Math.Min(e.Target.MaxHp, v.Hp + e.Amount);
                    _floaters.Add(($"+{e.Amount}", Head(e.Target), 0, Palette.Heal));
                    Log($"   {e.Target.Name} recovers {e.Amount}", Palette.TextDim);
                    return 0.35f;
                }
                case BattleEventType.Status:
                    _floaters.Add((e.Text.ToUpper(), Head(e.Target) + new Vector2(0, -24), 0, Figures.StatusColor(e.Status)));
                    Log($"   {e.Target.Name}: {e.Text}", Palette.TextDim);
                    return 0.3f;
                case BattleEventType.Cleanse:
                    _floaters.Add(("CLEANSED", Head(e.Target) + new Vector2(0, -24), 0, Palette.Heal));
                    return 0.3f;
                case BattleEventType.Move:
                    _vis[e.Actor].Slot = e.Actor.Slot;
                    if (e.Target != null) _vis[e.Target].Slot = e.Target.Slot;
                    Log($"   {e.Actor.Name} moves to {HubScreen.SlotName(e.Actor.Slot).ToLower()}" + (e.Target != null ? $", swapping with {e.Target.Name}" : ""), Palette.TextDim);
                    return 0.35f;
                case BattleEventType.Death:
                    _vis[e.Target].Dying = true;
                    Log($"   {e.Text}", Palette.Danger);
                    return 0.55f;
                case BattleEventType.Fizzle:
                    _floaters.Add((e.Text, Head(e.Actor), 0, Palette.TextDim));
                    Log($"   {e.Text}", Palette.TextDim);
                    return 0.6f;
                case BattleEventType.Info:
                    if (e.Target != null) _floaters.Add((e.Text, Head(e.Target) + new Vector2(0, -24), 0, Palette.Accent));
                    Log($"   {e.Text}", Palette.TextDim);
                    return 0.3f;
            }
            return 0.2f;
        }

        // ------------------------------------------------------------------ draw

        private static int UnitHeight(Combatant c) => (int)Math.Min(210, 130 * c.Scale);

        private Rectangle UnitRect(Combatant c)
        {
            var v = _vis[c];
            int h = UnitHeight(c), w = (int)(h * 0.62f);
            float lunge = (float)Math.Sin(v.Lunge * Math.PI) * 30 * (c.IsParty ? 1 : -1);
            float shake = v.Shake > 0 ? (float)Math.Sin(v.Shake * 40) * 6 * v.Shake : 0;
            return new Rectangle((int)(v.X - w / 2f + lunge + shake), (int)(GroundY - h), w, h);
        }

        public override void Draw(SpriteBatch sb, GameTime gameTime)
        {
            double t = gameTime.TotalGameTime.TotalSeconds;
            DrawBackground(sb, t);
            DrawOrderBar(sb);
            DrawSlotsMarkers(sb);

            var highlighted = new HashSet<Combatant>();
            if (_phase == Phase.Choose && _pickingTarget)
            {
                var a = Chooser.Actions[_actionIndex];
                if (a.AreaEffect) foreach (var x in _targets) highlighted.Add(x);
                else if (_targets.Count > 0) highlighted.Add(_targets[_targetIndex]);
            }

            foreach (var c in _all.OrderBy(c => c.IsParty ? -c.Slot : c.Slot))
                DrawUnit(sb, c, t, highlighted.Contains(c));

            foreach (var f in _floaters)
            {
                float a = MathHelper.Clamp(1.3f - f.age, 0, 1);
                Core.Draw.TextCentered(sb, Assets.Header, f.text, f.pos + new Vector2(0, -f.age * 40), f.color * a);
            }

            DrawBottomPanel(sb, t);

            if (_phase == Phase.Intro)
                Banner(sb, _run.Current.Type == RoomType.Objective ? "THE SOURCE AWAKENS" : "ENCOUNTER", Palette.Danger);
            if (_phase == Phase.Finished)
                Banner(sb, _battle.Outcome == BattleOutcome.Victory ? "VICTORY" : "THE PARTY HAS FALLEN",
                    _battle.Outcome == BattleOutcome.Victory ? Palette.Accent : Palette.Danger);
        }

        private void Banner(SpriteBatch sb, string text, Color c)
        {
            Core.Draw.Rect(sb, new Rectangle(0, 200, Game1.Width, 90), Color.Black * 0.7f);
            Core.Draw.TextCentered(sb, Assets.Title, text, new Vector2(Game1.Width / 2, 245), c);
        }

        private void DrawBackground(SpriteBatch sb, double t)
        {
            var area = _run.Area;
            if (Core.Draw.Art(sb, "bg_combat_" + area.Id, new Rectangle(0, 0, Game1.Width, 500), t)) return;
            for (int i = 0; i < 25; i++)
                Core.Draw.Rect(sb, 0, i * 20, Game1.Width, 20, Color.Lerp(Palette.Background, area.Wall, i / 25f));
            Core.Draw.Rect(sb, 0, GroundY - 6, Game1.Width, 500 - GroundY + 6, area.Floor);
            Core.Draw.Rect(sb, 0, GroundY - 6, Game1.Width, 3, area.Accent * 0.4f);
            Core.Draw.Rect(sb, 639, 120, 2, 300, Color.White * 0.04f);
        }

        private void DrawSlotsMarkers(SpriteBatch sb)
        {
            for (int s = 0; s < 4; s++)
            {
                foreach (bool party in new[] { true, false })
                {
                    float x = SlotX(party, s);
                    Core.Draw.Rect(sb, x - 44, GroundY + 4, 88, 4, Color.White * 0.08f);
                    string label = party ? HubScreen.SlotName(s) : $"E{s + 1}";
                    Core.Draw.TextCentered(sb, Assets.Small, label, new Vector2(x, GroundY + 58), Palette.TextDim * 0.7f);
                }
            }
        }

        private void DrawUnit(SpriteBatch sb, Combatant c, double t, bool targeted)
        {
            var v = _vis[c];
            if (v.Alpha <= 0) return;
            var r = UnitRect(c);
            bool isChooser = _phase == Phase.Choose && c == Chooser;
            bool isActing = _phase == Phase.Resolve && c == _acting;

            if (targeted)
            {
                var col = c.IsParty ? Palette.Heal : Palette.Danger;
                Core.Draw.Rect(sb, new Rectangle(r.X - 10, r.Y - 10, r.Width + 20, r.Height + 20), col * 0.18f);
                Core.Draw.Border(sb, new Rectangle(r.X - 10, r.Y - 10, r.Width + 20, r.Height + 20), col, 3);
            }
            if (isChooser || isActing)
                Core.Draw.Diamond(sb, new Vector2(v.X, r.Y - 44 + (float)Math.Sin(t * 6) * 4), 14, Palette.Accent);

            string glyph = c.IsParty ? c.Member.Def.Role.Substring(0, 1) : null;
            Figures.Unit(sb, c.ArtId, glyph, r, c.Color, !c.IsParty, t, v.Alpha, v.Flash,
                pose: v.Pose, fallbackSprite: c.IsParty ? null : EnemyDb.MockupSprite);

            // Name, HP bar, statuses under the unit.
            float a = v.Alpha;
            Core.Draw.TextCentered(sb, Assets.Small, c.Name, new Vector2(v.X, GroundY + 16), (c.IsParty ? Palette.Text : new Color(230, 170, 160)) * a);
            Figures.HpBar(sb, new Rectangle((int)v.X - 44, (int)GroundY + 26, 88, 14), v.Hp, c.MaxHp, !c.IsParty);
            if (c.Alive) Figures.Statuses(sb, c.Statuses, new Vector2(v.X - 44, GroundY + 70));
            if (c.Alive) DrawResources(sb, c, new Vector2(v.X - 44, GroundY + 44));

            if (v.PopupTime > 0 && v.Popup != null)
            {
                var size = Assets.Body.MeasureString(v.Popup);
                var pos = new Vector2(v.X, r.Y - 22);
                Core.Draw.Rect(sb, pos.X - size.X / 2 - 8, pos.Y - 14, size.X + 16, 28, Color.Black * 0.75f);
                Core.Draw.TextCentered(sb, Assets.Body, v.Popup, pos, Palette.Accent);
            }

            // Planned actions: party plans under their names, enemy intents above their heads.
            // Planned actions above heads: party plans (green) and enemy intents (red).
            if (_phase == Phase.Choose && c.Alive && c.Plan != null && (c.IsParty || _showIntents))
            {
                string p = c.Plan.Action.Name + (c.Plan.Target != null ? " > " + c.Plan.Target.Name : c.Plan.Action.AreaEffect ? " > ALL" : "");
                var size = Assets.Small.MeasureString(p);
                var pos = new Vector2(v.X, r.Y - 22);
                var back = c.IsParty ? new Color(20, 50, 30) : new Color(60, 16, 20);
                Core.Draw.Rect(sb, pos.X - size.X / 2 - 6, pos.Y - 10, size.X + 12, 20, back * 0.85f);
                Core.Draw.TextCentered(sb, Assets.Small, p, pos, c.IsParty ? new Color(190, 255, 200) : new Color(255, 190, 180));
            }
        }

        private static void SetPose(Visual v, int step)
        {
            if (v.AnimSteps.Length == 0) return;
            v.Pose = v.AnimSteps[Math.Min(step, v.AnimSteps.Length - 1)].Trim();
            v.PoseTime = 0.8f;
        }

        // SP as small blue blocks, Feast stacks as red diamonds, under the HP bar.
        private static void DrawResources(SpriteBatch sb, Combatant c, Vector2 pos)
        {
            for (int i = 0; i < c.MaxSp; i++)
                Core.Draw.Rect(sb, pos.X + i * 9, pos.Y, 7, 6, i < c.Sp ? Palette.Guard : new Color(50, 56, 70));
            if (!c.Has(PassiveKind.Feast)) return;
            for (int i = 0; i < Battle.FeastThreshold; i++)
                Core.Draw.Diamond(sb, new Vector2(pos.X + 88 - i * 10, pos.Y + 3), 7, i < c.Feast ? new Color(230, 60, 60) : new Color(70, 40, 44));
        }

        private void DrawOrderBar(SpriteBatch sb)
        {
            var list = _phase == Phase.Resolve ? _turnOrder : _battle.PredictedOrder();
            Core.Draw.Text(sb, Assets.Small, _phase == Phase.Resolve ? $"TURN {_battle.Turn}  ORDER" : $"TURN {_battle.Turn}  SPEED ORDER",
                new Vector2(20, 16), Palette.TextDim);
            float x = 20;
            foreach (var c in list)
            {
                bool done = _phase == Phase.Resolve && !_resolveQueue.Contains(c) && c != _acting;
                bool dead = !c.Alive;
                var r = new Rectangle((int)x, 36, 118, 40);
                var fill = c.IsParty ? new Color(34, 44, 40) : new Color(50, 28, 30);
                Core.Draw.Rect(sb, r, fill * (done || dead ? 0.4f : 1f));
                if (c == _acting) Core.Draw.Border(sb, r, Palette.Accent);
                var textCol = dead ? Palette.TextDisabled : done ? Palette.TextDim : Palette.Text;
                Core.Draw.Text(sb, Assets.Small, c.Name.Length > 14 ? c.Name.Substring(0, 13) + "." : c.Name, new Vector2(r.X + 6, r.Y + 3), textCol, shadow: false);
                Core.Draw.Text(sb, Assets.Small, $"SPD {c.Speed}", new Vector2(r.X + 6, r.Y + 20), Palette.TextDim, shadow: false);
                x += 124;
                if (x > Game1.Width - 130) break;
            }
        }

        private void DrawBottomPanel(SpriteBatch sb, double t)
        {
            var panel = new Rectangle(0, 510, Game1.Width, 210);
            Core.Draw.Rect(sb, panel, Palette.Panel);
            Core.Draw.Rect(sb, new Rectangle(0, 510, Game1.Width, 2), Palette.Border);

            if (_phase != Phase.Choose || Chooser == null)
            {
                DrawLog(sb, new Rectangle(20, 522, Game1.Width - 40, 190));
                return;
            }

            var c = Chooser;
            // Who is choosing.
            var who = new Rectangle(16, 522, 300, 186);
            Core.Draw.Rect(sb, who, Palette.PanelLight * 0.5f);
            Core.Draw.Text(sb, Assets.Header, c.Name, new Vector2(who.X + 12, who.Y + 8), Palette.Accent);
            Core.Draw.Text(sb, Assets.Small, $"{c.Member.Def.Role}  -  {HubScreen.SlotName(c.Slot)}", new Vector2(who.X + 12, who.Y + 38), Palette.TextDim);
            Core.Draw.Text(sb, Assets.Body, $"HP {c.Hp}/{c.MaxHp}", new Vector2(who.X + 12, who.Y + 58), Palette.Text);
            Core.Draw.Text(sb, Assets.Body, $"DMG {c.Damage}   SPD {c.Speed}", new Vector2(who.X + 12, who.Y + 80), Palette.Text);
            if (c.MaxSp > 0)
                Core.Draw.Text(sb, Assets.Small, $"SP {c.Sp}/{c.MaxSp}  (+{Battle.SpPerTurn}/turn)" + (c.Has(PassiveKind.Feast) ? $"   Feast {c.Feast}/{Battle.FeastThreshold}" : ""),
                    new Vector2(who.X + 12, who.Y + 104), Palette.Guard);
            if (c.Passives.Count > 0)
                Core.Draw.Text(sb, Assets.Small, "Passive: " + string.Join(", ", c.Passives.Select(p => p.Name)), new Vector2(who.X + 12, who.Y + 122), Palette.Accent);
            Core.Draw.Text(sb, Assets.Small, $"Choosing {_chooserIndex + 1} of {_choosers.Count}", new Vector2(who.X + 12, who.Y + 140), Palette.TextDim);
            if (_chooserIndex > 0) Core.Draw.Text(sb, Assets.Small, "Backspace: undo previous", new Vector2(who.X + 12, who.Y + 156), Palette.TextDim);
            Core.Draw.Text(sb, Assets.Small, $"T: enemy intents {(_showIntents ? "on" : "off")}", new Vector2(who.X + 12, who.Y + 170), Palette.TextDim);

            // Action list.
            var list = new Rectangle(326, 522, 520, 186);
            Core.Draw.Rect(sb, list, Palette.PanelLight * 0.3f);
            for (int i = 0; i < c.Actions.Count; i++)
            {
                var a = c.Actions[i];
                bool usable = _battle.CanUse(c, a);
                bool sel = i == _actionIndex;
                int rowH = Math.Min(29, (list.Height - 8) / c.Actions.Count);
                var row = new Rectangle(list.X + 6, list.Y + 4 + i * rowH, list.Width - 12, rowH - 2);
                if (sel) Core.Draw.Rect(sb, row, _pickingTarget ? Palette.PanelLight * 0.6f : Palette.PanelLight);
                Core.Draw.Text(sb, Assets.Body, a.Name, new Vector2(row.X + 8, row.Y + 4),
                    !usable ? Palette.TextDisabled : sel ? Palette.Accent : Palette.Text);
                if (a.SpCost > 0)
                    Core.Draw.Text(sb, Assets.Small, $"{a.SpCost} SP", new Vector2(row.X + 180, row.Y + 5), c.Sp >= a.SpCost ? Palette.Guard : Palette.Danger);
                if (a.Kind != ActionKind.Move && a.Kind != ActionKind.Defend)
                {
                    Figures.RankPips(sb, a.UsableFrom, new Vector2(row.X + 230, row.Y + 7), false, usable ? Palette.Accent : Palette.TextDisabled);
                    if (a.Side == TargetSide.Enemy)
                    {
                        Core.Draw.Text(sb, Assets.Small, ">", new Vector2(row.X + 288, row.Y + 5), Palette.TextDim);
                        Figures.RankPips(sb, a.TargetRanks, new Vector2(row.X + 302, row.Y + 7), true, usable ? Palette.Danger : Palette.TextDisabled);
                    }
                    else if (a.Side == TargetSide.Ally)
                        Core.Draw.Text(sb, Assets.Small, "> ally", new Vector2(row.X + 288, row.Y + 5), Palette.Heal);
                }
                if (a.Kind == ActionKind.Attack || a.Kind == ActionKind.Heal)
                {
                    int est = Math.Max(1, (int)Math.Round(c.Damage * a.Power * Battle.PositionBonus(c, a.Kind == ActionKind.Heal)));
                    Core.Draw.TextRight(sb, Assets.Small, (a.Kind == ActionKind.Heal ? "+" : "~") + est + (a.Hits > 1 ? $" x{a.Hits}" : "") + (a.AreaEffect ? " all" : ""),
                        new Vector2(row.Right - 8, row.Y + 5), a.Kind == ActionKind.Heal ? Palette.Heal : Palette.TextDim);
                }
            }

            // Description / targeting help.
            var desc = new Rectangle(856, 522, 408, 186);
            Core.Draw.Rect(sb, desc, Palette.PanelLight * 0.3f);
            var act = c.Actions[_actionIndex];
            Core.Draw.Text(sb, Assets.Header, act.Name, new Vector2(desc.X + 12, desc.Y + 8), Palette.Accent);
            float y = desc.Y + 40 + Core.Draw.Wrapped(sb, Assets.Body, act.Description ?? "", new Vector2(desc.X + 12, desc.Y + 40), desc.Width - 24, Palette.Text);
            string why = _battle.Unusable(c, act);
            if (why != null) Core.Draw.Text(sb, Assets.Body, why, new Vector2(desc.X + 12, y + 6), Palette.Danger);

            string help = _pickingTarget
                ? (act.AreaEffect ? "Hits all highlighted. Enter confirm, Backspace back" : "Left/Right choose target. Enter confirm, Backspace back")
                : "Up/Down choose action, Enter select";
            Core.Draw.Text(sb, Assets.Small, help, new Vector2(desc.X + 12, desc.Bottom - 24), Palette.TextDim);

            if (_warningTime > 0 && _warning != null)
                Core.Draw.TextCentered(sb, Assets.Header, _warning, new Vector2(Game1.Width / 2, 490), Palette.Danger * MathHelper.Clamp(_warningTime * 2, 0, 1));
        }

        private void DrawLog(SpriteBatch sb, Rectangle r)
        {
            Core.Draw.Text(sb, Assets.Small, "COMBAT LOG" + (_phase == Phase.Resolve ? "      (hold Space to speed up)" : ""), new Vector2(r.X, r.Y), Palette.TextDim);
            int lines = 8;
            int start = Math.Max(0, _log.Count - lines);
            for (int i = start; i < _log.Count; i++)
                Core.Draw.Text(sb, Assets.Body, _log[i].text, new Vector2(r.X, r.Y + 20 + (i - start) * 20), _log[i].color);
        }
    }
}
