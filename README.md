# Aetheris (SERAAC)

เกม RPG แนว turn-based dungeon-crawl พัฒนาด้วย MonoGame
พัฒนาโดยกลุ่ม **MTG (MorThodGame)** มหาวิทยาลัยเชียงใหม่ รายวิชา Game Production (Group 8)

**เวอร์ชันปัจจุบัน: Prototype build v0.1.3**

ผู้เล่นรับบทเป็น Relle นักเรียน Striker แห่ง Aetheris Academy เริ่มจากบทนำ (prologue) ที่สอนระบบทั้งหมดของเกม
ไปจนถึงการบุกเข้า Fallen Academy เพื่อกำจัด Wyvern ซึ่งเป็นจุดสิ้นสุดของเวอร์ชันทดสอบนี้

---

## สิ่งที่ต้องมี

| รายการ | หมายเหตุ |
|---|---|
| Windows 10 หรือ 11 (64-bit) | เกมใช้ MonoGame DesktopGL และเล่นวิดีโอเปิดเกมผ่าน Windows Media Foundation |
| [.NET SDK 9](https://dotnet.microsoft.com/download) ขึ้นไป | โปรเจกต์ target `net9.0` และรันบน .NET รุ่นใหม่กว่าได้ |
| Git | สำหรับ clone โปรเจกต์ |

แพ็กเกจ NuGet (MonoGame, MonoGame.Extended, NLayer, Concentus ฯลฯ) และเครื่องมือ MonoGame Content Builder
จะถูกดาวน์โหลดให้อัตโนมัติในขั้นตอน build

## วิธี Build และ Run

โค้ดของเกมอยู่ที่ `[MorThodKaiInc] - game/game/Code/Game/SERAAC`

```bash
git clone https://github.com/rastrelligr/morthodkai-inc-aac-rip-off-.git
cd "morthodkai-inc-aac-rip-off-/[MorThodKaiInc] - game/game/Code/Game/SERAAC"
dotnet tool restore
dotnet build -c Release
dotnet run -c Release
```

หรือเปิด `morthodkai-inc-aac-rip-off-.slnx` ด้วย Visual Studio 2022 / Rider แล้วกด Run

### สร้างไฟล์สำหรับแจกจ่าย (ไม่ต้องติดตั้ง .NET บนเครื่องผู้เล่น)

```bash
dotnet publish -c Release -r win-x64 --self-contained true -o publish
```

จากนั้นนำทั้งโฟลเดอร์ `publish` ไปให้ผู้เล่น แล้วเปิด `SERAAC.exe`
(โฟลเดอร์ `Art/` และ `Audio/` ต้องอยู่ข้างไฟล์ exe เสมอ เพราะเกมโหลดภาพและเสียงจากโฟลเดอร์เหล่านี้ตอนรัน)

## วิธีเล่น

กด **New Game** ที่หน้าเมนูเพื่อเริ่มบทนำ เกมจะบันทึกความคืบหน้าให้อัตโนมัติ กด **Continue** เพื่อเล่นต่อจากบทล่าสุด

| บท | สิ่งที่เกิดขึ้น |
|---|---|
| ห้องพักของ Relle | Naomi มาปลุก (บทสนทนา) จากนั้นเดินสำรวจห้อง หยิบดาบ แล้วออกจากห้อง |
| Staging Room | ซ้อมต่อสู้กับ Naomi ในเครื่องจำลอง สอนระบบต่อสู้ทีละขั้น |
| Airlift Platform | เดินสำรวจลานจอดบนยอดตึก แล้วคุยกับ Naomi |
| The Sanctum | Naomi สอนการจัดทีม อัปเกรด อุปกรณ์ และการออกปฏิบัติการ |
| Fallen Academy | ด่านเต็ม สำรวจห้อง ต่อสู้ เก็บของ และกำจัด Wyvern |

### ปุ่มควบคุม

| ที่ไหน | ปุ่ม |
|---|---|
| ทุกหน้าจอ | เมาส์ คลิกซ้ายเลือก คลิกขวาย้อนกลับ, F11 หรือ Alt+Enter สลับเต็มจอ |
| เมนู | ลูกศร / WASD เลื่อน, Enter ยืนยัน, Esc ย้อนกลับ |
| เดินสำรวจ | WASD / ลูกศร หรือคลิกเพื่อเดิน, E / Enter ใช้ของหรือคุย, Esc เปิด Options |
| ในด่าน (Trial) | P ดูทีม, I ไอเทม, M แผนที่, Esc หยุดเกม |
| ต่อสู้ | เลือกท่าแล้วคลิกเป้าหมาย หรือลูกศร + Enter, Backspace ยกเลิก, กด Space ค้างเพื่อเร่ง |
| บทสนทนา | คลิก / Enter / หมุนล้อลงเพื่อไปต่อ, L หรือหมุนล้อขึ้นดูประวัติ, F อัตโนมัติ, Tab หรือกด Ctrl ค้างเพื่อข้าม, H ซ่อนกล่องข้อความ |

ระดับเสียงเพลงและเอฟเฟกต์ปรับได้ที่ **Options** (หน้าเมนูหลัก, เมนูหยุดเกม หรือกด Esc ตอนเดินสำรวจ)

ไฟล์เซฟอยู่ที่ `%AppData%\SERAAC\profile.json` เลือก **New Game (erase save)** ที่หน้าเมนูเพื่อเริ่มใหม่

## สำหรับนักพัฒนา

| คำสั่ง | ใช้ทำอะไร |
|---|---|
| `dotnet run -- --dev` | แสดงปุ่ม Playtest, Mods และ Art Viewer ที่หน้าเมนู |
| `dotnet run -- --story airlift` | ข้ามไปยังบทที่ต้องการ (`dorm`, `spar`, `airlift`, `hub`, `academy`) |
| `dotnet run -- --selftest` | ตรวจข้อมูลเกมทั้งหมดและจำลองการเล่นเพื่อดูสมดุล |

รายละเอียดระบบภายใน โครงสร้างโค้ด และการปรับค่าต่าง ๆ อยู่ใน `[MorThodKaiInc] - game/game/Code/Game/SERAAC/PROTOTYPE.md`
คู่มือการแก้แผนที่ด้วย Tiled อยู่ใน `Art/Maps/README.md` และรายการเสียงอยู่ใน `Audio/Music/README.md`

## เครื่องมือที่ใช้

- **โค้ด:** C#, MonoGame 3.8, MonoGame.Extended, Visual Studio / Rider
- **ภาพและแผนที่:** Adobe Photoshop, Blender, Tiled
- **การสื่อสารในทีม:** Discord
- **Version control:** Git, GitHub
