---
type: gdd-scope
version: 0.1
date: [7/7/2569]
---
# [Jo's game] — Scope & Feature List

## Unique Selling Point (USP)

1. [Vessel & Weapon Co-op]
2. [Synergy Gameplay]
3. [Split-Screen Combat]
4. [Turn-Based Combat]
5. [Microgames Combat]

## In Scope — สิ่งที่ "จะทำ" ในภาคการศึกษานี้

| # | Feature                          | Priority | หมายเหตุ                 |
| - | -------------------------------- | -------- | -------------------------------- |
| 1 | [Turn-Based Combat]                  | Must   | ระบบต่อสู้หลัก                       |
| 2 | [Vessel & Weapon System] | Must     | ผู้เล่นแต่ละคนควบคุมคนละส่วนของตัวละคร         |
| 3 | [3x3 Grid]                     | Must     | ใช้สำหรับการเคลื่อนที่และวางตำแหน่งในการต่อสู้         |
| 4 | [Enemy]        | Must    | ศัตรูมีรูปแบบการโจมตี |
| 5 | [Microgames]                | Should     | Microgame ที่แตกต่างกันสำหรับ Vessel และ Weapon         |
| 6 | [Roguelike Progression]                     | Should   | เส้นทาง/Encounter และ Progression แบบสุ่ม                       |

## Out of Scope — สิ่งที่ "จะไม่ทำ"

- Online Multiplayer - ทำ Couch Co-op ก่อน เพราะ Online ทำได้ยากเกินขอบเขตของโปรเจกต์
- ระบบ Combat ที่ซับซ้อนเกินไป - เช่น Parry ที่ต้องใช้ Timing สูง
- ระบบที่ต้องใช้ Content จำนวนมาก - เช่น ตัวละครหรือศัตรูที่เยอะเกินไป




## Risks & Assumptions

| ความเสี่ยง                             | ผลกระทบ                                                                | แผนรับมือ                                                |
| ------------------------------------------------ | ----------------------------------------------------------------------------- | ----------------------------------------------------------------- |
| [ทีมยังไม่เคยทำเกมแนวนี้] | [อาจใช้เวลาเรียนรู้นาน และระบบ Co-op มีความซับซ้อน] | [ทำ Prototype ของ Combat ก่อน แล้วค่อยเพิ่มระบบอื่น] |
| [ระบบผู้เล่น 2 คนควบคุมตัวละครเดียวกัน] | [อาจทำให้การออกแบบ Gameplay และ UI ซับซ้อนเกินไป] | [จำกัด Action ของแต่ละ Player ให้ชัดเจน] |
| [Split-Screen + Microgame] | [อาจใช้เวลาในการพัฒนามากกว่าที่คาด] | [เริ่มจาก Microgame รูปแบบง่าย ๆ ก่อน] |
| [Roguelike + Randomization] | [อาจทำให้ Content และระบบ Generation เยอะเกินไป] | [ใช้ Preset Rooms/Events ที่สร้างไว้ล่วงหน้าแทนการสุ่มทุกอย่าง] |
