# Product Backlog

**Version:** 1.0 | **Last Updated:** 2026-09-01

> รวม User Story ทั้งหมดของโปรเจกต์ — ยังไม่ได้แปลว่าต้องทำใน Sprint นี้ทั้งหมด
> โปรเจกต์นี้แบ่งงานตลอดเทอมเป็น **4 Sprint** (Sprint 1-4) — Sprint ไหนหยิบ Story ไปทำ ให้ใส่เลข Sprint นั้น (1-4) ลงคอลัมน์ `Sprint`

## Must Have (MVP)

| # | User Story                                                                  | Acceptance Criteria                                       | Estimate (SP) | Sprint |
| - | --------------------------------------------------------------------------- | --------------------------------------------------------- | ------------- | ------ |
| 1 | As a player, I want to loot item, so that I can craft item                  | Player can craft items from shared material pool          | 2             | 2      |
| 2 | As a player, I want to level up, so that I can be stronger                  | Level up from exp point when defeating enemies            | 2             | 1      |
| 3 | As a player, I want to multiple options, so that I can choose my own path   | Freedom of choices (eg. choosing class, next story path)) | 8             | 1      |
| 4 | As a player, I want to defeat enemies, so that I can get loots and level up | Defeating enemies is rewarding                            | 4             | 1      |

## Should Have

| # | User Story                                                                                | Acceptance Criteria                          | Estimate (SP) | Sprint |
| - | ----------------------------------------------------------------------------------------- | -------------------------------------------- | ------------- | ------ |
| 1 | As a player, I want to see my remaining lives, so that I know how close I am to game over | Health/Status display in real time           | 2             | 2      |
| 2 | As a player, I want to see complete visual, so that I know what item/object is            | Art & Sprite, visual for everything in game | 8             | 2      |
| 3 | As a player, I want to use controller , so that I can use multiple device                | basic controller support & navigation        | 6             | 3      |
| 4 | As a player, I expect the game to gave random loot, so that it can have more variety     | random loot drop rate                        | 4             | 2      |
| 5 | As a programmer, I want to integrage couch coop, so that I can play with friend           | multiplayer turn based & interaction         | 8             | 2      |

## Nice to Have

| # | User Story                                                                                                      | Acceptance Criteria                                                                                                                                      | Estimate (SP) | Sprint |
| - | --------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------- | ------ |
| 1 | As a designer, I want enemy spawn rate stored in a data file, so that I can tune difficulty without recompiling | ปรับค่า spawn rate ในไฟล์ data แล้วรันเกมใหม่ ค่าที่เปลี่ยนมีผลทันทีโดยไม่ต้อง build ใหม่ | 3             | 4      |
| 2 | As a player, I want to know in-game story and lore, so that I can engage with the game                          | Has few storylines to help player understand the purpose of the game/that outcome                                                                        | 2             | 3      |
| 3 | As an Artist, I want to create compact spritesheet, so that I can flexibly change the texture                   | Create spritesheet for ease of use in building and reduce messy work files                                                                               | 4             | 1      |
| 4 | As an Artist, I want to create satisfying animation, so that players can engage by the game visuals             | Hand-made visual effect, animation spritesheets                                                                                                          | 8             | 2      |
| 5 | As a player, I want to easily access the game, so that I can play the game at home                              | Web-build, available for download at home                                                                                                                | 2             | 4      |

## MoSCoW Legend

- **Must Have** — จำเป็นต่อ core gameplay loop เกมเล่นไม่ได้ถ้าขาด (MVP)
- **Should Have** — เพิ่มคุณภาพเกม แต่เกมเล่นได้โดยไม่มีก็ได้
- **Nice to Have** — ทำถ้ามีเวลาเหลือ

## Links

- [[docs/01_GDD/00-concept|GDD Concept]]
- [[docs/agile/02-sprint-backlog|Sprint Backlog]]
