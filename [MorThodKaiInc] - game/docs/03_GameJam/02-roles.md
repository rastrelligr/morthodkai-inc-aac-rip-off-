---
type: jam-roles
version: 0.1
date: [วันที่]
team: [ชื่อทีม]
---

# Team Roles & Pipeline Ownership — [ชื่อทีม]

## แบ่งความรับผิดชอบตาม Pipeline (4 คน)

| คน | ชื่อ | Module ที่รับผิดชอบ | Phase ที่ต้องเสร็จ |
|---|---|---|---|
| 1 | [ชื่อ] | `GameStateManager` + `InputManager` | Phase 2 (Hour 6–14) |
| 2 | [ชื่อ] | Core mechanic `Update()` logic | Phase 2–3 (Hour 6–24) |
| 3 | [ชื่อ] | Render/`SpriteBatch` + Collision | Phase 2–3 (Hour 6–24) |
| 4 | [ชื่อ] | Content pipeline (MGCB) + Audio/UI | Phase 3–4 (Hour 14–34) |

> คนละไฟล์/module = ชนกันน้อยที่สุด ปรับ module ตามตาราง [01-pipeline-checklist.md](01-pipeline-checklist.md) ให้ตรงกับเกมจริงของทีม

## File Ownership Rule

> ไฟล์ที่ต้องแตะร่วมกันบ่อย เช่น `Game1.cs`, ไฟล์ scene หลัก — ตกลงล่วงหน้าว่าใครแก้ก่อน-หลัง เพื่อลด merge conflict

| ไฟล์/พื้นที่ที่เสี่ยงชนกัน | เจ้าของหลัก | กติกาการแก้ไข |
|---|---|---|
| `Game1.cs` | [ชื่อ] | [เช่น "คนอื่นแจ้งใน chat ก่อนแก้ แล้ว pull ก่อน push"] |
| [ไฟล์อื่นที่คาดว่าจะชน] | [ชื่อ] | [กติกา] |

## เมื่อคนใดคนหนึ่งเสร็จงานตัวเองก่อน

- ลำดับความช่วยเหลือถัดไป: [เช่น ไปช่วย QA/playtest → ไปช่วย content → ไปช่วย polish]
- [กติกาเพิ่มเติมของทีม]
