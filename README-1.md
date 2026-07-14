---
type: doc-index
version: 0.1
date: [วันที่]
---
# [ชื่อเกม] — Documentation Index

## 📖 เอกสารในโปรเจกต์นี้

| ไฟล์                                | เนื้อหา                   | สถานะ |
| --------------------------------------- | -------------------------------- | ---------- |
| [00-concept.md](00-concept.md)             | Game concept, core loop, scope   | ✅         |
| [01-mechanics.md](01-mechanics.md)         | Mechanic flow (state diagram)    | ✅         |
| [02-asset-list.md](02-asset-list.md)       | Asset list + asset pipeline flow | ✅         |
| [03-class-diagram.md](03-class-diagram.md) | Class diagram เบื้องต้น | ✅         |

## 🏷️ Naming Convention

**Asset:** ดูตารางเต็มใน [00-concept.md](00-concept.md#asset-naming-convention)

| Prefix   | ประเภท     |
| -------- | ---------------- |
| `spr_` | Sprite / Texture |
| `sfx_` | Sound Effect     |
| `bgm_` | Background Music |
| `fnt_` | Font             |
| `dat_` | Data / Config    |

**เอกสาร:** ไฟล์ใน `docs/01_GDD/` เรียงลำดับด้วย prefix ตัวเลข 2 หลัก (`00-`, `01-`, ...) ตามลำดับที่สร้างขึ้นในแต่ละ Lab — ห้ามสลับเลขไฟล์ที่มีอยู่แล้ว เพิ่มไฟล์ใหม่ให้ต่อเลขถัดไป

## Asset Naming Convention

| Prefix   | ประเภท     | ตัวอย่าง        |
| -------- | ---------------- | ----------------------- |
| `spr_` | Sprite / Texture | `spr_player_idle.png` |
| `sfx_` | Sound Effect     | `sfx_jump.wav`        |
| `bgm_` | Background Music | `bgm_stage_01.mp3`    |
| `fnt_` | Font             | `fnt_ui_main.ttf`     |
| `dat_` | Data / Config    | `dat_enemies.json`    |

## 📁 ใครดูแลส่วนไหน

| คนที่ | รับผิดชอบ  | โฟลเดอร์ staging                |
| ---------- | ------------------- | --------------------------------------- |
| 1          | Sprites / Textures  | `docs/02_Assets/_candidates/sprites/` |
| 2          | Sound Effects (SFX) | `docs/02_Assets/_candidates/sfx/`     |
| 3          | Music / BGM         | `docs/02_Assets/_candidates/music/`   |
| 4          | Fonts + Data        | `docs/02_Assets/_candidates/fonts/`   |
