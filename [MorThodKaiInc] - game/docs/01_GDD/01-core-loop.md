---
type: gdd-core-loop
version: 0.1
date: [7/7/2026]
---
# [Jo's Game] — Core Loop & Gameplay ?

## Core Loop

```mermaid
flowchart LR
A[START RUN] --> B[Explore Dungeon]
B --> C[Choose Path]
C --> D[Encounter]
D --> E[Turn-Based Combat]
E --> F[Enemy Defeated?]
F -->|No| E
F -->|Yes| G[Reward / Progress]
G --> B
```

## Core Mechanics

1. [Roguelike Progression]
2. [Vessel & Weapon Synergy]
3. [Turn-Based Combat]
4. [3×3 Grid Combat]
5. [Split-Screen Co-op]

## Controls

| Key               | Action               |
| ----------------- | -------------------- |
| W, A, S, D        | Movement, Microgames |
| Space             | Microgames           |
| Left Mouse Button | Microgames           |
| E                 | Interact             |

## Win / Lose Condition

- **ชนะเมื่อ:** เอาชนะศัตรูและผ่าน Dungeon / ทำ Run สำเร็จ
- **แพ้เมื่อ:** Vessel HP หมด หรือผู้เล่นทั้งสองไม่สามารถต่อสู้ต่อได้
