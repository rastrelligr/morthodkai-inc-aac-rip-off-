---
type: jam-pipeline
version: 0.1
date: [7/21/2026]
team: [MTG]
---
# Pipeline Function Checklist — [ชื่อเกม]

> เกมทุกแนวต้องมี pipeline ขั้นต่ำนี้ก่อนถึงจะเรียกว่า "เล่นได้" — เติมชื่อผู้รับผิดชอบและติ๊กสถานะระหว่างลงมือทำจริง เพิ่มแถวได้ถ้าเกมของทีมต้องการ module อื่น

## Must-Have (ต้องมีก่อน Hour 24 — Playable Build)

| Module                                         | หน้าที่                                                                    | สถานะ     | ผู้รับผิดชอบ |
| ---------------------------------------------- | --------------------------------------------------------------------------------- | -------------- | ------------------------ |
| `GameStateManager`                           | สลับ state (Menu / Playing / Pause / GameOver)                                | 🔲 Not Started | [Peachy]                 |
| `InputManager`                               | รับ input keyboard/gamepad แบบรวมศูนย์                              | 🔲 Not Started | [Peachy]                 |
| Core `Update()` logic ของกลไกหลัก | logic ของ core mechanic 1 อย่างที่เป็นหัวใจเกม             | 🔲 Not Started | [Peachy]                 |
| Collision / interaction พื้นฐาน         | ตรวจชน/ตรวจ trigger ระหว่าง entity/ Interaction ทีจำเป็น | 🔲 Not Started | [Peachy]                 |
| render + camera/viewport                       | วาดผ่าน `GraphicsDevice.Viewport` (ห้าม hardcode resolution)         | 🔲 Not Started | [Peachy]                 |
| Win / Lose condition                           | เงื่อนไขจบเกม/จบด่าน                                           | 🔲 Not Started | [Peachy]                 |
| Basic Enemy AI                                 | เขียนเบสิก AI ให้ spawn/despawn และล่าผู้เล่นได้    | 🔲 Not Started | [Peachy]                 |
| Model & Texturing                              | โมเดลต้องมีเท็กเจอร์                                          | 🔲 Not Started | [fishe]                  |

## Nice-to-Have (ทำถ้าเหลือเวลา — Hour 24–34)

| Module                                | หน้าที่                                                                                 | สถานะ     | ผู้รับผิดชอบ |
| ------------------------------------- | ---------------------------------------------------------------------------------------------- | -------------- | ------------------------ |
| `AudioManager` (SFX พื้นฐาน) | เสียงตอบสนอง action หลัก (free source)                                         | 🔲 Not Started | [Peachy]                 |
| UI/HUD (score, timer)                 | แสดงสถานะระหว่างเล่น                                                       | 🔲 Not Started | [fishe]                  |
| Music / เพลงประกอบ          | เพลงใส่ไว้เพิ่มประสบการณ์ในการเล่น โดยใช้ free source | 🔲 Not Started | [TopTap]                 |
| Itch.io page                          | เป็นเพจโปรโมทเกม แสดง screenshot ต่างๆ และใส่ file              | 🔲 Not Started | [fishe]                  |
| Complex Animation                    | หนุน game art เกมดูมีชีวิตชีวาขึ้น                                     | 🔲 Not Started | [fishe]                  |
| VFX & Postprocessing                  | Spicy effect pew pew                                                                           | 🔲 Not Started | [Peachy]                 |

## Cut-List (ตัดทิ้งก่อนถ้าเวลาไม่พอ — ห้ามเริ่มก่อน Must-Have เสร็จ)

- ❌ Save/Load
- ❌ Settings menu
- ❌ Leaderboard
- ❌ [feature อื่นที่ทีมตกลงตัดก่อน]

> เมื่อถึง **Feature Freeze (Hour 34)** ทุก module ในตารางนี้ต้องมีสถานะ ✅ Done หรือถูกย้ายไป Cut-List อย่างชัดเจน — ห้ามค้างเป็น 🔲/🟡
