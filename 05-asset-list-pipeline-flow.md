## Asset Pipeline Flow

```mermaid
flowchart TD
    subgraph SOURCES["แหล่ง Asset ฟรี + ทำเอง"]
        K1[itch.io] --> C1[MTGgameprojectxD/morthodkai-inc-aac-rip-off-/]
        K2[Freesound.org/ youtube] --> C1
        K3[www.1001fonts.com / Fontesk] --> C1
	K4[aseprite] --> C1
    end

    subgraph REPO["Git Repository (Staging)"]
        C1 --> R1[MTGgameprojectxD/morthodkai-inc-aac-rip-off-/sprites/]
        C1 --> R2[MTGgameprojectxD/morthodkai-inc-aac-rip-off-/soundeffect/]
        C1 --> R3[MTGgameprojectxD/morthodkai-inc-aac-rip-off-/BGM/]
        C1 --> R4[MTGgameprojectxD/morthodkai-inc-aac-rip-off-/fonts/]
    end

    subgraph PIPELINE["MonoGame Content Pipeline (Lab ถัดไป)"]
        R1 --> CP[คัดเลือก แล้วย้ายเข้า Content/]
        R2 --> CP
        R3 --> CP
        R4 --> CP
        CP --> P1[Content Builder / MGCB]
        P1 --> XNB[.xnb files]
    end

    XNB --> GAME[Runtime Game]
```
