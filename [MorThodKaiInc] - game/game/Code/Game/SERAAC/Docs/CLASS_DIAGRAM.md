# SERAAC prototype: class diagram and code guide

Describes the build at commit **`eababdd`** on `dev` ("Increase Winter char_hunter sprite scale", 2026-09-25).
Later work (3D combat stages, combat camera, particles, mouse support, palette) is **not** included here.

MonoGame 3.8 (DesktopGL), .NET 9 (see section 8 for how MonoGame is used). The diagrams use Mermaid and render on GitHub,
in VS Code (with a Mermaid extension), and in most Markdown viewers.

---

## 1. Overview

The game is a stack of **screens**. Each screen reads input, updates its state and draws itself.
Game rules live outside the screens: **Trial** (one dive into a dungeon), **Combat** (the battle rules),
**Meta** (the save file and permanent progress) and **Data** (all content as plain C# tables).

```mermaid
flowchart LR
    Program --> Game1
    Game1 --> ScreenManager
    ScreenManager --> Screens

    subgraph Screens
        MainMenuScreen --> HubScreen
        HubScreen --> ExplorationScreen
        ExplorationScreen -- push / pop --> CombatScreen
        ExplorationScreen --> ResultScreen
        ResultScreen --> HubScreen
        MainMenuScreen --> ArtViewerScreen
    end

    Screens --> Trial["Trial: TrialRun, TrialMap, Room"]
    Screens --> Combat["Combat: Battle, Combatant, CombatAI"]
    Screens --> Meta["Meta: Profile, Progression, Quests"]
    Trial --> Data["Data: *Db content tables"]
    Combat --> Data
    Meta --> Data
    Screens --> Core["Core: Input, Draw, Assets, Sprites"]
    Screens --> UI["UI: Figures, Icons"]
```

**One run, start to finish:** Main menu → Hub (pick party, upgrade, equip, choose a Trial) →
`TrialRun.Start` generates a `TrialMap` → Exploration (walk rooms, loot, events, NPCs) → entering a combat
room pushes a `CombatScreen` over exploration → the `Battle` result is applied and the screen pops →
Extract (loot is secured into the `Profile`) or the party falls (unsecured loot is lost) → Result → Hub.

---

## 2. Core engine

```mermaid
classDiagram
    class Game1 {
        +int Width = 1280
        +int Height = 720
        +Game1 Instance$
        +Profile Profile
        -ScreenManager _screens
        -ScriptRunner _script
        +SetFullscreen(bool on)
        #LoadContent()
        #Update(GameTime)
        #Draw(GameTime)
    }
    class Screen {
        <<abstract>>
        +ScreenManager Screens
        +bool IsOverlay
        +OnEnter()
        +OnResume()
        +Update(GameTime)*
        +Draw(SpriteBatch, GameTime)*
    }
    class ScreenManager {
        -List~Screen~ _stack
        +Screen Top
        +bool Transitioning
        +Replace(Screen)
        +Push(Screen)
        +Pop()
        +PushInstant(Screen)
        +PopInstant()
        +Update(GameTime)
        +Draw(SpriteBatch, GameTime)
    }
    class Input {
        <<static>>
        +HashSet~Keys~ Injected
        +bool ScriptOnly
        +Update()
        +Down(Keys) bool
        +Pressed(Keys) bool
        +bool Up, DownKey, Left, Right
        +bool Confirm, Cancel, Interact
        +bool FastForward
        +Vector2 Move
    }
    class ScriptRunner {
        +string OutputDir
        +string PendingShot
        +bool Done
        +Tick()
    }
    class SelfTest {
        <<static>>
        +Run() int
    }
    class Assets {
        <<static>>
        +UiFont Title, Header, Body, Small
        +Texture2D Pixel
        +Load(GraphicsDevice, ContentManager)
        +Art(string id) ArtSprite
    }
    class ArtSprite {
        +Texture2D Texture
        +int Frames
        +float Fps
        +Source(double time) Rectangle
    }
    class UiFont {
        +Load(ContentManager, string)$ UiFont
        +MeasureString(string) Vector2
        +float LineSpacing
        +Draw(SpriteBatch, string, Vector2, Color)
    }
    class Draw {
        <<static>>
        +float Scale
        +Rect()
        +Border()
        +Panel()
        +Line()
        +Text()
        +TextCentered()
        +Wrapped()
        +Bar()
        +Diamond()
        +Art(SpriteBatch, string id, Rectangle, double) bool
    }
    class Palette {
        <<static>>
        +Color Background, Panel, Border, Text, Accent, Danger, Heal
    }
    class Sprites {
        <<static>>
        +Init(GraphicsDevice)
        +Get(string id) SpriteSet
        +Recolored(string id, Color) SpriteSet
        +Draw(SpriteBatch, SpriteSet, string pose, Vector2 feet, float h, Color, bool flip)
        +DrawFit(SpriteBatch, SpriteSet, string pose, Rectangle, Color)
    }
    class SpriteSet {
        +string Id
        +string Reference
        +float DisplayScale
        +Dictionary~string,SpriteFrame~ Frames
        +Has(string pose) bool
        +Get(string pose) SpriteFrame
    }
    class SpriteFrame {
        +string Name
        +int W, H
        +Vector2 Pivot
        +Scaled(GraphicsDevice, int height) Texture2D
    }

    Game1 *-- ScreenManager
    Game1 o-- ScriptRunner : --script
    ScreenManager o-- "0..*" Screen : stack
    ScriptRunner ..> Input : injects keys
    Assets *-- UiFont
    Assets ..> ArtSprite : caches
    Sprites ..> SpriteSet : caches
    SpriteSet *-- "1..*" SpriteFrame
    Draw ..> Assets
    Draw ..> Palette
```

---

## 3. Screens and UI helpers

```mermaid
classDiagram
    class Screen {
        <<abstract>>
    }
    class MainMenuScreen {
        -List items
        +HelpText$
        +Modal(SpriteBatch, string, string)$
    }
    class HubScreen {
        -Tab _tab
        -Embark(AreaDef, TrialDef)
        +SkillRow(...)$
        +SlotName(int)$ string
    }
    class ExplorationScreen {
        -TrialRun _run
        -Vector2 _pos
        -Modal _modal
        -List~Combatant~ _pendingEnemies
        -Walk(float dt)
        -ArriveIn(Room, Dir)
        -StartCombat()
        -OnCombatFinished(Room, Battle, bool)
        -Interact()
        -StartDialogue(NpcDef)
    }
    class CombatScreen {
        -TrialRun _run
        -Battle _battle
        -Phase _phase
        -Dictionary~Combatant,Visual~ _vis
        -Queue~BattleEvent~ _events
        -Action~Battle~ _onFinished
        -PlayEvent(BattleEvent) float
    }
    class ResultScreen {
        -TrialRun _run
        -bool _extracted
    }
    class ArtViewerScreen {
        -List~string~ _pages
    }
    class Figures {
        <<static>>
        +Unit(...)
        +Portrait(...)
        +RankPips(...)
        +Statuses(...)
        +HpBar(...)
    }
    class Icons {
        <<static>>
        +Draw(SpriteBatch, string id, Rectangle)
    }

    Screen <|-- MainMenuScreen
    Screen <|-- HubScreen
    Screen <|-- ExplorationScreen
    Screen <|-- CombatScreen
    Screen <|-- ResultScreen
    Screen <|-- ArtViewerScreen

    ExplorationScreen --> TrialRun
    ExplorationScreen ..> CombatScreen : pushes
    CombatScreen --> TrialRun
    CombatScreen *-- Battle
    ResultScreen --> TrialRun
    HubScreen ..> Profile
    HubScreen ..> Progression
    CombatScreen ..> Figures
    ExplorationScreen ..> Figures
    HubScreen ..> Figures
    HubScreen ..> Icons
```

---

## 4. Trial (one dive)

```mermaid
classDiagram
    class TrialRun {
        +AreaDef Area
        +TrialDef Trial
        +TrialMap Map
        +Room Current
        +Random Rng
        +List~PartyMember~ Party
        +int TrialShards
        +List~string~ FoundEquipment
        +List~string~ Consumables
        +bool ObjectiveCompleted
        +HashSet~string~ Flags
        +Start(Profile, AreaDef, TrialDef, int seed)$ TrialRun
        +EncounterFor(Room) List~EnemyDef~
        +BuildEnemyCombatants(Room) List~Combatant~
        +BuildPartyCombatants() List~Combatant~
        +ApplyBattleResult(Battle)
        +OnCombatWon(Room, Profile)
        +SpawnLoot(Room)
        +Collect(LootPickup) string
        +UseConsumable(string) bool
        +HealParty(float, bool)
        +Extract(Profile)
        +Fail(Profile)
    }
    class PartyMember {
        +CharacterDef Def
        +int MaxHp, Damage, Speed, Hp
        +bool Fallen
    }
    class TrialMap {
        +Dictionary~Point,Room~ Rooms
        +Room Entrance
        +int MaxDepth
        +At(Point) Room
        +Generate(TrialDef, Random, bool withNpc)$ TrialMap
    }
    class Room {
        +int Id
        +Point Grid
        +RoomType Type
        +int Depth
        +RoomLayout Layout
        +Dictionary~Dir,Room~ Links
        +bool Visited, Cleared, FeatureUsed
        +List~LootPickup~ Pickups
        +EventDef Event
        +NpcDef Npc
        +bool IsCombatRoom
    }
    class RoomType {
        <<enumeration>>
        Entrance
        Normal
        Elite
        Treasure
        Rest
        Event
        Extraction
        Objective
        Npc
    }
    class RoomLayout {
        +Tile[,] Tiles
        +List~Point~ LootSpots
        +Point Feature
        +Templates$ string[][]
        +Build(int template, bool mirror, Dir[] doors)$ RoomLayout
        +Solid(int x, int y) bool
        +Reachable(Point) HashSet~Point~
    }
    class LootPickup {
        +Vector2 Tile
        +LootKind Kind
        +int Amount
        +string ItemId
        +bool Collected
    }

    TrialRun *-- TrialMap
    TrialRun *-- "1..4" PartyMember
    TrialRun --> Room : Current
    TrialMap *-- "1..*" Room
    Room --> "0..4" Room : Links
    Room *-- RoomLayout
    Room *-- "0..*" LootPickup
    Room --> RoomType
    TrialRun --> AreaDef
    TrialRun --> TrialDef
    PartyMember --> CharacterDef
```

---

## 5. Combat (the rules, no drawing)

```mermaid
classDiagram
    class Battle {
        +Combatant[4] Party
        +Combatant[4] Enemies
        +List~Combatant~ Fallen
        +int Turn
        +BattleOutcome Outcome
        +ValidTargets(Combatant, ActionDef) List~Combatant~
        +Unusable(Combatant, ActionDef) string
        +CanUse(Combatant, ActionDef) bool
        +ResolveOrder() List~Combatant~
        +PredictedOrder() List~Combatant~
        +Execute(Combatant actor) List~BattleEvent~
        +EndTurn() List~BattleEvent~
        +PositionBonus(Combatant, bool)$ float
    }
    class Combatant {
        +string Name
        +bool IsParty
        +int MaxHp, Hp, Damage, Speed
        +int Slot
        +int Sp, MaxSp, Feast
        +List~ActionDef~ Actions
        +List~StatusInstance~ Statuses
        +List~PassiveDef~ Passives
        +PlannedAction Plan
        +PartyMember Member
        +EnemyDef Enemy
        +bool Alive
        +AddStatus(StatusKind, int, int)
        +FromMember(PartyMember)$ Combatant
        +FromEnemy(EnemyDef, float)$ Combatant
    }
    class PlannedAction {
        +ActionDef Action
        +Combatant Target
    }
    class StatusInstance {
        +StatusKind Kind
        +int Turns
        +int Power
    }
    class BattleEvent {
        +BattleEventType Type
        +Combatant Actor
        +Combatant Target
        +int Amount
        +bool Crit
        +string Text
        +ActionDef Action
    }
    class CombatAI {
        <<static>>
        +PlanAll(Battle, Combatant[] units, Random)
        +Choose(Battle, Combatant, Random) PlannedAction
    }

    Battle o-- "2..8" Combatant
    Battle ..> BattleEvent : produces
    Combatant *-- "0..*" StatusInstance
    Combatant --> PlannedAction : Plan
    PlannedAction --> ActionDef
    PlannedAction --> Combatant : Target
    Combatant --> PartyMember : party side
    Combatant --> EnemyDef : enemy side
    CombatAI ..> Battle
    CombatAI ..> PlannedAction : creates
```

---

## 6. Data (content tables)

Every `*Db` is a static class holding `static readonly` definitions. Changing content means editing these
tables and rebuilding.

```mermaid
classDiagram
    class ActionDef {
        +string Id, Name, Description
        +ActionKind Kind
        +TargetSide Side
        +Ranks UsableFrom
        +Ranks TargetRanks
        +bool AreaEffect
        +float Power
        +int SpCost, Hits, KnockBack, MoveBy
        +StatusKind Applies
        +string Anim
    }
    class Ranks {
        <<struct>>
        +Of(string)$ Ranks
        +Has(int slot) bool
    }
    class CommonActions {
        <<static>>
        +Defend, Advance, Retreat
    }
    class CharacterDef {
        +string Id, Name, Role, Blurb
        +int BaseHp, BaseDamage, BaseSpeed, MaxSp
        +List~ActionDef~ Skills
        +List~PassiveDef~ Passives
        +bool StartsUnlocked
        +AllActions
    }
    class PassiveDef {
        +PassiveKind Kind
        +string Name, Description
    }
    class EnemyDef {
        +string Id, Name
        +int Hp, Damage, Speed
        +float Scale
        +EnemyRow Row
        +List~ActionDef~ Skills
    }
    class AreaDef {
        +string Id, Name, ObjectiveId, UnlockedBy
        +List~EnemyDef~ FrontPool, BackPool
        +EnemyDef Elite, Boss
        +List~TrialDef~ Trials
    }
    class TrialDef {
        +string Id, Name
        +int RoomCount, Tier, ExtractionPoints
        +float LootMultiplier
        +bool HasMainObjective
    }
    class EquipmentDef {
        +string Id, Name
        +EquipSlot Slot
        +int Hp, Damage, Speed, MinTier
    }
    class ConsumableDef {
        +string Id, Name
        +ConsumableEffect Effect
        +float Amount
    }
    class EventDef {
        +string Id, Title, Text
        +List~EventOption~ Options
    }
    class EventOption {
        +string Label, Hint
        +EventOutcome Outcome
        +float Amount
        +int Shards
    }
    class NpcDef {
        +string Id, Name, SpriteId
        +CanAppear(DialogueContext) bool
        +Entry(DialogueContext) string
        +Dictionary~string,DialogueNode~ Nodes
    }
    class DialogueNode {
        +string Text
        +OnEnter(DialogueContext)
        +List~DialogueChoice~ Choices
    }
    class DialogueChoice {
        +string Label
        +string Next
        +If(DialogueContext) bool
    }
    class DialogueContext {
        +Profile Profile
        +TrialRun Run
        +Notify(string)
    }
    class QuestDef {
        +string Id, Name, Giver
        +QuestGoal Goal
        +int Target
        +string GoalCharacter
    }
    class CharacterDb {
        <<static>>
        +All
        +Get(id)
    }
    class EnemyDb {
        <<static>>
        +All
        +Get(id)
    }
    class WorldDb {
        <<static>>
        +Areas
        +Area(id)
        +Trial(id)
    }
    class ItemDb {
        <<static>>
        +Equipment
        +Consumables
    }
    class EventDb {
        <<static>>
        +All
    }
    class NpcDb {
        <<static>>
        +All
        +PickFor(ctx, rng)
    }
    class QuestDb {
        <<static>>
        +All
        +Get(id)
    }

    ActionDef *-- Ranks
    CharacterDef *-- "1..*" ActionDef
    CharacterDef *-- "0..*" PassiveDef
    EnemyDef *-- "1..*" ActionDef
    AreaDef *-- "1..*" TrialDef
    AreaDef --> EnemyDef
    EventDef *-- "1..*" EventOption
    NpcDef *-- "1..*" DialogueNode
    DialogueNode *-- "0..*" DialogueChoice
    CharacterDb o-- CharacterDef
    EnemyDb o-- EnemyDef
    WorldDb o-- AreaDef
    ItemDb o-- EquipmentDef
    ItemDb o-- ConsumableDef
    EventDb o-- EventDef
    NpcDb o-- NpcDef
    QuestDb o-- QuestDef
```

---

## 7. Meta (save file and permanent progress)

```mermaid
classDiagram
    class Profile {
        +int SecuredShards
        +Dictionary~string,CharacterProgress~ Characters
        +List~string~ Stash
        +List~string~ PartyOrder
        +List~string~ CompletedObjectives
        +List~string~ UnlockedCharacters
        +Dictionary~string,QuestState~ Quests
        +int TrialsStarted, Extractions, Defeats
        +CreateNew()$ Profile
        +Progress(string charId) CharacterProgress
        +IsAreaUnlocked(AreaDef) bool
        +IsUnlocked(string) bool
        +Quest(string) QuestState
    }
    class CharacterProgress {
        +int HpLevel, DamageLevel, SpeedLevel
        +string Weapon
        +string Charm
        +Level(UpgradeStat) int
    }
    class QuestState {
        +int Progress
        +bool Claimed
    }
    class ProfileStore {
        <<static>>
        +Load() Profile
        +Save(Profile)
        +Delete()
    }
    class Progression {
        <<static>>
        +UpgradeCost(int)$ int
        +Base(CharacterDef, CharacterProgress) Stats
        +Final(CharacterDef, CharacterProgress) Stats
        +TryBuyUpgrade(Profile, string, UpgradeStat) bool
        +Equip(Profile, string, EquipSlot, string)
    }
    class Stats {
        <<struct>>
        +int Hp, Damage, Speed
    }
    class Quests {
        <<static>>
        +Start(Profile, QuestDef)
        +Claim(Profile, QuestDef)
        +OnVictory(Profile, Room, Battle) List~string~
    }

    Profile *-- "0..*" CharacterProgress
    Profile *-- "0..*" QuestState
    ProfileStore ..> Profile : saves as JSON
    Progression ..> Profile
    Progression ..> Stats
    Quests ..> Profile
    Quests ..> QuestDef
```

---

## 8. How it runs: the crucial scripts

### MonoGame, and how the game uses it

The game is built on **MonoGame 3.8 (DesktopGL)**, a C# framework that gives the game loop, graphics, input and content loading. Everything else is our own code.

- **`Game` loop:** `Game1` inherits from MonoGame's `Game`. MonoGame calls `LoadContent` once, then `Update` and `Draw` about 60 times a second. `Game1` hands both to the screen stack.
- **`GraphicsDeviceManager`:** opens the window and switches fullscreen. The game is laid out at 1280x720 and scaled to any screen with black bars.
- **`SpriteBatch`:** draws everything: sprites, rectangles, text. There are no 3D models or shaders in this build.
- **Content Pipeline (`Content.mgcb`):** compiles the fonts only (`.spritefont` to `.xnb`). Art is not in the pipeline. PNGs are read at runtime from the `Art/` folder (`Texture2D.FromFile`, and StbImageSharp for the big sprite sheets), so artists can drop files in without a build.
- **`Keyboard`:** read once per frame by `Input`.
- **MonoGame.Extended** is referenced in the project but not used by any code yet.

### The scripts that make the game work

| Script | Why it matters |
|---|---|
| `Game1.cs` | The heart of the program: runs MonoGame's loop, loads assets and the save, scales the frame to the screen. |
| `Core/Screen.cs` | The screen stack. Every part of the game (menu, hub, exploring, combat) is a screen; combat is pushed on top of exploring and popped when done. |
| `Screens/ExplorationScreen.cs` | The main gameplay: walking through rooms, loot, events, NPCs, extraction. Starts combat. |
| `Screens/CombatScreen.cs` | Lets the player choose actions and plays back the fight as animation. |
| `Combat/Battle.cs` | The combat rules: positions, speed order, damage, statuses. Returns a list of events for the combat screen to show. |
| `Trial/TrialRun.cs` + `Trial/TrialMap.cs` | One dive: generates the room map, builds the encounters, holds the loot at risk, extract or lose it. |
| `Meta/Profile.cs` | The save file: what the player keeps between dives (shards, upgrades, gear, unlocks). |
| `Data/*.cs` | All the content (characters, enemies, areas, items, NPCs, quests) as plain tables. Change the numbers here to tune the game. |

Everything else is support: drawing helpers (`Core/Draw.cs`, `UI/Figures.cs`), art loading (`Core/Assets.cs`, `Core/Sprites.cs`), and dev tools (`Core/SelfTest.cs` for automated checks and balance numbers, `Core/ScriptRunner.cs` for scripted screenshots, `Screens/ArtViewerScreen.cs` for checking art).
