using Microsoft.Xna.Framework;
using SERAAC.Input;

namespace SERAAC.Combat
{
    // The "coordination" beat of an attack: once the Weapon commits to a target, both
    // players have a short window to confirm together (Vessel = Space, Weapon = LMB).
    // Landing both within the window is what actually lands the hit - a single player
    // acting alone is not enough, by design.
    public class AttackMinigame
    {
        public const double DefaultDuration = 1.4;

        public bool Active { get; private set; }
        public double Duration { get; private set; }
        public double Elapsed { get; private set; }
        public bool VesselReady { get; private set; }
        public bool WeaponReady { get; private set; }

        public double TimeRemaining => System.Math.Max(0, Duration - Elapsed);
        public bool BothReady => VesselReady && WeaponReady;

        public void Start(double duration = DefaultDuration)
        {
            Active = true;
            Duration = duration;
            Elapsed = 0;
            VesselReady = false;
            WeaponReady = false;
        }

        public void Update(GameTime gameTime, VesselInput vessel, WeaponInput weapon)
        {
            if (!Active) return;

            Elapsed += gameTime.ElapsedGameTime.TotalSeconds;
            if (vessel.Confirm) VesselReady = true;
            if (weapon.Attack) WeaponReady = true;
        }

        public bool IsComplete() => Active && (Elapsed >= Duration || BothReady);

        public bool Evaluate()
        {
            Active = false;
            return BothReady;
        }
    }
}
