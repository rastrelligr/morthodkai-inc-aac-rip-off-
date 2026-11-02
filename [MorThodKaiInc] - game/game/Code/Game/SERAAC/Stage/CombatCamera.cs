using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using MonoGame.Extended;
using MonoGame.Extended.ViewportAdapters;
using SERAAC.Core;

namespace SERAAC.Stage
{
    // Numbers for the combat camera, from Art/Stages/_camera.json (hot-reloaded).
    public sealed class CameraTuning
    {
        // Idle sway: the camera drifts and breathes so the scene never looks frozen.
        public float SwayAmount = 5f;         // layout pixels
        public float SwaySpeed = 0.35f;       // cycles per second (roughly)
        public float SwayRoll = 0.25f;        // degrees of tilt (characters only; the background never tilts)

        // Zoom levels (1 = no zoom).
        public float IntroZoom = 1.35f;       // the fight opens close and pulls back
        public float IntroTime = 1.1f;        // seconds of the pull-back
        public float TargetingZoom = 1.04f;   // while choosing a target
        public float ActionZoom = 1.16f;      // while someone acts
        public float MeleeZoom = 1.24f;       // while a melee attacker is in the enemy's face
        public float VictoryZoom = 1.12f;
        public float DefeatZoom = 0.94f;

        // Pan toward whoever is acting: 0 = stay centred, 1 = centre exactly on the action.
        public float ActionPan = 0.55f;

        // Parallax: how much of the zoom, pan, shake and sway each background layer follows.
        // 0 = never moves, 1 = moves exactly with the characters, over 1 = in front of them.
        public float BackdropFollow = 0.6f;     // a background that is one single picture
        public float BackdropDepth = 0.1f;      // <name>_Backdrop.png     (farthest: the sky)
        public float BackgroundDepth = 0.35f;   // <name>_Background.png   (far: buildings)
        public float MiddlegroundDepth = 0.8f;  // <name>_Middleground.png (the ground the characters stand on)
        public float ForegroundDepth = 1.3f;    // <name>_Foreground.png   (drawn over the characters)

        // Easing speeds (higher = snappier).
        public float ZoomSpeed = 3.5f;
        public float PanSpeed = 3.5f;

        // Impacts.
        public float ShakeHit = 5f;           // layout pixels
        public float ShakeCrit = 12f;
        public float ShakeDeath = 8f;
        public float ShakeDecay = 5f;         // per second
        public float CritZoomKick = 0.07f;    // extra zoom for a moment on a crit
        public float HitStop = 0.035f;        // seconds the action freezes on a hit
        public float CritHitStop = 0.1f;
        public float HitZoomKick = 0.025f;    // small punch-in on every hit

        // Action-cam (CombatScreen): slow motion, flashes, skill cut-ins, letterbox bars.
        public float SlowMoScale = 0.3f;      // game speed during slow motion
        public float CritSlowMo = 0.22f;      // seconds (real time) of slow motion on a crit
        public float KillSlowMo = 0.4f;       // ... on a killing blow
        public float BigMoveSlowMo = 0.55f;   // ... on a big moment (boss attack, interrupt, enrage, Feast crit)
        public float CritFlash = 0.35f;       // white screen flash strength on a crit (0 = off)
        public float CutInTime = 0.7f;        // seconds a skill cut-in banner stays (skills of CutInMinSp SP or more)
        public int CutInMinSp = 3;
        public float LetterboxHeight = 46f;   // cinematic bars during big moves (layout pixels, 0 = off)
    }

    // A 2D cinematic camera for combat. The characters' layer gets the full zoom, pan, shake, sway and
    // tilt; the pre-rendered background gets a fraction of it (BackdropFollow) and is kept covering the
    // whole screen, so its edges never show.
    public sealed class CombatCamera
    {
        public static readonly LiveFile<CameraTuning> Tuning = new("Stages/_camera.json");
        private static CameraTuning T => Tuning.Value;

        public const float FocusY = 320;     // layout y the camera zooms around (characters' chests)

        private float _zoom = 1f, _zoomTarget = 1f;
        private float _pan, _panTarget;
        private float _kick;                    // decaying extra zoom
        private float _shake;                   // layout pixels, decays
        private float _time;
        private readonly Random _rng = new();
        private Vector2 _shakeOffset;

        public float Zoom => _zoom + _kick;

        // Jumps straight there (the intro starts close, then Focus eases out).
        public void Snap(float zoom, float pan)
        {
            _zoom = _zoomTarget = zoom;
            _pan = _panTarget = pan;
        }

        // Eases toward this zoom and horizontal pan (layout pixels from the screen centre).
        public void Focus(float zoom, float pan)
        {
            _zoomTarget = zoom;
            _panTarget = pan;
        }

        public void Shake(float amount) => _shake = Math.Max(_shake, amount);
        public void Kick(float zoom) => _kick = Math.Max(_kick, zoom);

        public void Update(float dt)
        {
            _time += dt;
            _zoom = MathHelper.Lerp(_zoom, _zoomTarget, 1 - MathF.Exp(-T.ZoomSpeed * dt));
            _pan = MathHelper.Lerp(_pan, _panTarget, 1 - MathF.Exp(-T.PanSpeed * dt));
            _kick = Math.Max(0, _kick - dt * 0.5f);
            _shake = Math.Max(0, _shake - _shake * T.ShakeDecay * dt - dt);
            _shakeOffset = _shake > 0.01f
                ? new Vector2((float)(_rng.NextDouble() * 2 - 1), (float)(_rng.NextDouble() * 2 - 1)) * _shake
                : Vector2.Zero;
        }

        // Layered sines: smooth, never repeating exactly.
        private Vector2 Sway()
        {
            float s = T.SwaySpeed * MathHelper.TwoPi, t = _time;
            return new Vector2(
                MathF.Sin(t * s * 0.83f) * 0.7f + MathF.Sin(t * s * 1.71f + 1.3f) * 0.3f,
                MathF.Sin(t * s * 0.61f + 0.4f) * 0.5f) * T.SwayAmount;
        }

        private float Roll() => MathHelper.ToRadians(MathF.Sin(_time * T.SwaySpeed * MathHelper.TwoPi * 0.53f) * T.SwayRoll);

        // The characters' layer is viewed through a MonoGame.Extended OrthographicCamera. This class decides
        // where it looks (eased zoom / pan, shake, sway, tilt); the Extended camera builds the matrix.
        // Its viewport is the 1280x720 layout frame; Draw.FrameTransform still scales that to the real screen.
        private OrthographicCamera _view;

        private sealed class LayoutViewport : ViewportAdapter
        {
            public LayoutViewport(GraphicsDevice gd) : base(gd) { }
            public override int VirtualWidth => Game1.Width;
            public override int VirtualHeight => Game1.Height;
            public override int ViewportWidth => Game1.Width;
            public override int ViewportHeight => Game1.Height;
            public override Matrix GetScaleMatrix() => Matrix.Identity;
        }

        // Aims the Extended camera for this frame. It zooms and tilts around the characters' chests
        // (Origin); the pan, shake and sway are folded into Position so they don't zoom along.
        private OrthographicCamera View
        {
            get
            {
                _view ??= new OrthographicCamera(new LayoutViewport(Game1.Instance.GraphicsDevice));
                var center = new Vector2(Game1.Width / 2f, FocusY);
                float roll = Roll();
                var move = Vector2.Transform(_shakeOffset + Sway(), Matrix.CreateRotationZ(roll)) / Zoom;
                _view.Origin = center;
                _view.Zoom = Zoom;
                _view.Rotation = -roll;
                _view.Position = new Vector2(_pan, 0) - move;
                return _view;
            }
        }

        // Transform for the characters' layer, in layout units (multiply by Draw.FrameTransform after).
        public Matrix Layer => View.GetViewMatrix();

        // Layout position of a point drawn in the layer (labels, mouse hit-testing).
        public Vector2 LayerToScreen(Vector2 p) => Vector2.Transform(p, Layer);

        // Draws one background layer that follows the camera by `depth` (see CameraTuning). It is scaled to
        // cover the 1280x720 frame times `overscan`, and its position is clamped so it always fills the frame.
        // Wider art (e.g. 1920x720) leaves room to pan sideways without the clamp kicking in.
        public void DrawBackdrop(SpriteBatch sb, Texture2D tex, float overscan, float offsetY, float depth)
        {
            float f = MathHelper.Clamp(depth, 0, 2);
            float zoom = Math.Max(1f, 1f + (Zoom - 1f) * f);     // never smaller than the frame
            float cover = Math.Max(Game1.Width / (float)tex.Width, Game1.Height / (float)tex.Height);
            float scale = cover * Math.Max(1f, overscan) * zoom;
            var size = new Vector2(tex.Width, tex.Height) * scale;

            // Same idea as Layer: zoom around the (followed) focus, then add pan, shake and sway.
            var center = new Vector2(Game1.Width / 2f, FocusY);
            var focus = center + new Vector2(_pan * f, 0);
            var rest = new Vector2(Game1.Width / 2f, Game1.Height / 2f + offsetY);   // image centre at zoom 1
            var pos = (rest - focus) * zoom + center + (_shakeOffset + Sway()) * f - size / 2f;

            // Keep the frame covered.
            pos.X = MathHelper.Clamp(pos.X, Game1.Width - size.X, 0);
            pos.Y = MathHelper.Clamp(pos.Y, Game1.Height - size.Y, 0);
            sb.Draw(tex, pos, null, Color.White, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
        }
    }
}
