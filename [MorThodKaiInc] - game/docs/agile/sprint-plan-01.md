<!-- Template เต็มไฟล์สำหรับสร้าง docs/agile/sprint-plan-[NN].md ของ Sprint ไหนก็ได้ -->

<!-- ดึง Story ของ Sprint นี้มาจาก docs/agile/02-sprint-backlog.md -->

<!-- Sprint 1: เปลี่ยนชื่อ sprint-01.md จาก Lab 07 เป็น sprint-plan-01.md แล้วแทนที่เนื้อหาด้วย template นี้ -->

<!-- Sprint 2-4 ในแลปถัดไป: คัดลอกไฟล์นี้ทั้งไฟล์ไปสร้าง sprint-plan-02.md, sprint-plan-03.md, sprint-plan-04.md ตามลำดับ -->

# Sprint [1]

**Sprint Goal:** [ทำ Core Mechanic ให้เล่นได้]
**ระยะเวลา:** [2026-09-01] — [2026-09-14]
**Team:** [MTG]

---

## Sprint Backlog

| # | User Story                                                                                    | รับผิดชอบ | MoSCoW       | Estimate (SP) | Status         |
| - | --------------------------------------------------------------------------------------------- | ------------------ | ------------ | ------------- | -------------- |
| 1 | As a player, I want to level up, so that I can be stronger                                    | Peachy & Jo        | Must Have    | 2             | 🔄 In Progress |
| 2 | As a player, I want to multiple options, so that I can choose my own path                     | Peachy & Jo        | Must Have    | 8             | 🔲 Todo        |
| 3 | As a player, I want to defeat enemies, so that I can get loots and level up                   | Peachy & Jo        | Must Have    | 4             | 🔲 Todo        |
| 4 | As an Artist, I want to create compact spritesheet, so that I can flexibly change the texture | Tap & Fishe        | Nice to Have | 3             | 🔄 In Progress |
| 5 | As a designer i want level design so 3D can layout map for code generate                      | Jo mama            | Must Have    | 10            | 🔄 In Progress |

## Status Legend

- 🔲 Todo
- 🔄 In Progress
- ✅ Done
- ❌ Blocked

---

## Tasks

### Story[1]— Level Progression System

[As a player, I want to level up, so that I can be stronger]

- [ ] [Progression Tracking (Exp, Level)))]  [owner:: Peachy] [domain::programmer] [estimate:: 10] [actual:: 3h] [status:: Done] [note::  เหลือเพิ่ม method คำนวณ Level เพิ่มเติม]
- [ ] [Design Level Up UI&Sprite]  [owner:: Fishe&Tap] [domain::artist] [estimate:: 4]  [status:: In Progress]
- [ ] [ออกแบบ Stats ของตัวละคร]  [owner:: Jo] [domain::designer] [estimate:: 2]  [status:: In Progress]git status

### Story[2]— Multiple Choice/Outcome

[As a player, I want to multiple options, so that I can choose my own path]

- [ ] [Events Design]  [owner:: Fishe&Jo] [domain::artist & designer] [estimate:: 2]  [status:: In Progress]
- [ ] [Flagging system]  [owner:: Peachy] [domain::artist] [estimate:: 6]  [status:: In Progress]
- [ ] [Player status]  [owner:: Peachy & Jo] [domain::programmer] [estimate:: 4]  [status:: In Progress]

### Story[3]— Item & Enemy data

[As a player, I want to defeat enemies, so that I can get loots and level up]

- [ ] [Item & Enemies Sprite]  [owner:: Fishe&Tap] [domain::artist] [estimate:: 10]  [status:: In Progress]
- [ ] [Item & Enemies Design]  [owner:: Fishe & Tap & Jo] [domain::artist] [estimate:: 3]  [status:: In Progress]
- [ ] [Item & Enemies Data]  [owner:: Jo & Peach] [domain::artist] [estimate:: 3]  [status:: In Progress]

### Story[4] — Game Visual & File organizing V1

[As an Artist, I want to create compact spritesheet, so that I can flexibly change the texture]

- [ ] [Concept art]  [owner:: Fishe&Tap] [domain::artist] [estimate:: 3]  [status:: In Progress]
- [ ] [Art mock up]  [owner:: Fishe&Tap] [domain::artist] [estimate:: 5]  [status:: In Progress]

### Story[5] — Level design

[As a designer, I want to make cool level design :3]

- [ ] [Draw Level]  [owner:: jo] [domain::designer] [estimate:: 2]  [status:: In Progress]
- [ ] [Level Mock up in blender]  [owner:: Tap] [domain::artist] [estimate:: 2]  [status:: In Progress]
- [ ] [3D import integration]  [owner:: Fishe&Tap] [domain::program] [estimate:: 10]  [status:: In Progress]

## Daily Notes

so much work

### [วันที่]

**เมื่อวาน:** 07/09/2026
**วันนี้:** 08/09/2026
**Blocked:** -

---

## Links

- [[docs/gdd/00-concept|GDD Concept]]
- [[docs/agile/01-product-backlog|Product Backlog]]
- [[docs/agile/02-sprint-backlog|Sprint Backlog]]
