---
type: gdd-mechanics
version: 0.1
date: [7/14/2026]
---
# Mechanic Design — [Player Actions]

## State Diagram

```mermaid
stateDiagram-v2
    [*] --> Idle

    Idle --> Attack : เลือก Attack
    Idle --> Defend : เลือก Defend
    Idle --> Item : เลือก Item
    Idle --> Taunt : เลือก Taunt

    Attack --> Roll : เริ่มการโจมตี

    state Roll {
        [*] --> Predict
        Predict --> DiceRoll : ทำนายผล
        DiceRoll --> Calculate : ทอยลูกเต๋า
        Calculate --> [*] : คำนวณ Modifier
    }

    Roll --> AttackAnimation : ใช้ผล Modifier

    AttackAnimation --> Resolve : Animation จบ

    Resolve --> Idle : จบ Turn

    Defend --> Idle : Action เสร็จสิ้น
    Item --> Idle : ใช้ไอเทมเสร็จ
    Taunt --> Idle : Action เสร็จสิ้น
```

## Rules

| State            | เข้าเงื่อนไข                  | ออกเงื่อนไข         | Note                                                                                                                     |
| ---------------- | ----------------------------------------- | ------------------------------ | ------------------------------------------------------------------------------------------------------------------------ |
| Idle             | เริ่มเกม / เริ่มเทิร์น | เลือก Action              | รอผู้เล่นเลือกคำสั่ง                                                                                 |
| Attack           | ผู้เล่นเลือก Attack           | เริ่ม Roll                | เตรียมการโจมตี                                                                                             |
| Roll             | เริ่มการโจมตี                | คำนวณ Modifier เสร็จ | ผู้เล่นทำนายผลก่อน ระบบทอยลูกเต๋า และสร้าง Modifier สำหรับการโจมตี |
| Attack Animation | ได้ผลจาก Roll แล้ว            | Animation จบ                 | เล่น Animation การโจมตีตามค่า Modifier                                                                 |
| Resolve          | Animation จบ                            | กลับ Idle                  | คำนวณ Damage และใช้ผลของ Modifier                                                                        |
| Defend           | ผู้เล่นเลือก Defend           | Action เสร็จ              | -                                                                                                                        |
| Item             | ผู้เล่นเลือก Item             | ใช้ไอเทมเสร็จ     | -                                                                                                                        |
| Taunt            | ผู้เล่นเลือก Taunt            | Action เสร็จ              | -                                                                                                                        |
