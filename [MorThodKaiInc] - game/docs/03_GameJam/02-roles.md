---
type: jam-roles
version: 0.1
date: [7/21/2026]
team: [MTG]
---
# Team Roles & Pipeline Ownership — [ชื่อทีม]

## แบ่งความรับผิดชอบตาม Pipeline (4 คน)

| คน | ชื่อ     | Module ที่รับผิดชอบ         | Phase ที่ต้องเสร็จ |
| ---- | ------------ | --------------------------------------- | ------------------------------ |
| 1    | [Peachy]     | `GameStateManager` + `InputManager` | Phase 2 (Hour 6–14)           |
| 2    | [Peachy]     | `InputManager`                        | Phase 2 (Hour 6–14)          |
| 3    | [Peachy]     | Core mechanic `Update()` logic        | Phase 2–3 (Hour 6–24)        |
| 4    | [Peachy]     | Collision / interaction พื้นฐาน  | Phase 2–3 (Hour 6–24)        |
| 5    | [Peachy]     | render + camera/viewport                | Phase 2-3 (Hour 6–24)         |
| 6    | [Peachy]     | Win / Lose condition                    | Phase 3-4 (Hour 14–34)        |
| 7    | [Peachy]     | Basic Enemy AI                          | Phase 3-4 (Hour 14–34)        |
| 8    | [Fish & tap] | Model & Texturing                       | Phase 3–4 (Hour 14–34)       |

> คนละไฟล์/module = ชนกันน้อยที่สุด ปรับ module ตามตาราง [01-pipeline-checklist.md](01-pipeline-checklist.md) ให้ตรงกับเกมจริงของทีม

## File Ownership Rule

> ไฟล์ที่ต้องแตะร่วมกันบ่อย เช่น `Game1.cs`, ไฟล์ scene หลัก — ตกลงล่วงหน้าว่าใครแก้ก่อน-หลัง เพื่อลด merge conflict

| ไฟล์/พื้นที่ที่เสี่ยงชนกัน | เจ้าของหลัก | กติกาการแก้ไข                                                            |
| --------------------------------------------------- | ---------------------- | ------------------------------------------------------------------------------------- |
| `Game1.cs`                                        | [Peachy]               | [เช่น "คนอื่นแจ้งใน chat ก่อนแก้ แล้ว pull ก่อน push"] |

## เมื่อคนใดคนหนึ่งเสร็จงานตัวเองก่อน

- ลำดับความช่วยเหลือถัดไป: [เช่น ไปช่วย QA/playtest → ไปช่วย content → ไปช่วย polish]
- ช่วยคนอื่นเท่าที่ได้หรืแถามว่าใช้ได้ยังเย้้้้ ิ
- ให้คนอื่นเป็นหน่วยให้กำลังใจจำเป็น
- หา sound หา BGM (พวกอ้าตติสที่ว่าง)
- นอนพัก
