---
type: gdd-class-diagram
version: 0.1
date: [วันที่]
---

# Class Diagram — [ชื่อเกม]

```mermaid
classDiagram
    class Game1 {
        -SpriteBatch _spriteBatch
        -Player _player
        +Initialize()
        +LoadContent()
        +Update(GameTime)
        +Draw(GameTime)
    }
    class Player {
        -Vector2 _position
        -float _speed
        +bool IsAlive
        +HandleInput()
        +Update(GameTime)
        +Draw(SpriteBatch)
    }
    class ICollidable {
        <<interface>>
        +Rectangle Bounds
        +OnCollide(ICollidable)
    }
    Game1 --> Player
    Player ..|> ICollidable
```
