---
type: gdd-mechanics
version: 0.1
date: [วันที่]
---

# Mechanic Design — [ชื่อ Mechanic]

## State Diagram

```mermaid
stateDiagram-v2
    [*] --> Idle
    Idle --> Move : กด Arrow/WASD
    Move --> Jump : กด Space
    Jump --> Fall : ตกจากจุดสูงสุด
    Fall --> Idle : แตะพื้น
    Idle --> Attack : กด Attack button
    Attack --> Idle : Animation จบ
```

## Rules

| State | เข้าเงื่อนไข | ออกเงื่อนไข | Note |
|---|---|---|---|
| Idle | เริ่มเกม / หยุดเคลื่อนที่ | กด input ใดๆ | Animation loop |
| Move | กดปุ่มทิศทาง | ปล่อยปุ่ม / กระโดด | Speed = [ค่า] |
| Jump | กด Space ขณะอยู่พื้น | ถึงจุดสูงสุด | Gravity = [ค่า] |
