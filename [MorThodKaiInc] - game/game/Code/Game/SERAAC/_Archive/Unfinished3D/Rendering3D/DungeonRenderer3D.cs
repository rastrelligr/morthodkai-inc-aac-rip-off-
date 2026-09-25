using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace SERAAC.Rendering3D
{
    public struct Billboard
    {
        public Vector3 Center;
        public float Width;
        public float Height;
        public Color Tint;
    }

    // Draws the dungeon mesh with real lit 3D geometry, plus camera-facing billboards
    // for enemies and a simple viewmodel quad for the Weapon in the player's hand.
    public class DungeonRenderer3D
    {
        private readonly BasicEffect _surfaceEffect;
        private readonly BasicEffect _billboardEffect;
        private readonly Texture2D _floorTex;
        private readonly Texture2D _ceilingTex;
        private readonly Texture2D _wallTex;
        private readonly Texture2D _enemyTex;
        private readonly Texture2D _weaponTex;

        public DungeonRenderer3D(GraphicsDevice device)
        {
            _surfaceEffect = new BasicEffect(device) { LightingEnabled = true, TextureEnabled = true };
            _surfaceEffect.EnableDefaultLighting();
            _surfaceEffect.SpecularColor = Vector3.Zero;

            _billboardEffect = new BasicEffect(device) { TextureEnabled = true, VertexColorEnabled = true };

            _floorTex = ProceduralTextures.CreateChecker(device, new Color(90, 78, 64), new Color(72, 62, 50));
            _ceilingTex = ProceduralTextures.CreateChecker(device, new Color(40, 40, 48), new Color(32, 32, 40));
            _wallTex = ProceduralTextures.CreateChecker(device, new Color(120, 118, 110), new Color(96, 94, 88), 64, 16);
            _enemyTex = ProceduralTextures.CreateSolid(device, Color.White);
            _weaponTex = ProceduralTextures.CreateSolid(device, Color.White);
        }

        public void DrawDungeon(GraphicsDevice device, DungeonMesh mesh, FirstPersonCamera camera, float aspect)
        {
            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = BlendState.Opaque;
            device.RasterizerState = RasterizerState.CullCounterClockwise;

            _surfaceEffect.World = Matrix.Identity;
            _surfaceEffect.View = camera.View;
            _surfaceEffect.Projection = camera.GetProjection(aspect);

            DrawPart(device, mesh.Floor, _floorTex, Color.White);
            DrawPart(device, mesh.Ceiling, _ceilingTex, Color.White);
            DrawPart(device, mesh.Walls, _wallTex, Color.White);
        }

        private void DrawPart(GraphicsDevice device, MeshPart part, Texture2D texture, Color tint)
        {
            if (part.PrimitiveCount <= 0) return;
            _surfaceEffect.Texture = texture;
            _surfaceEffect.DiffuseColor = tint.ToVector3();
            foreach (var pass in _surfaceEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                part.Draw(device);
            }
        }

        public void DrawEnemies(GraphicsDevice device, FirstPersonCamera camera, float aspect, IReadOnlyList<Billboard> enemies)
        {
            if (enemies.Count == 0) return;

            device.DepthStencilState = DepthStencilState.Default;
            device.BlendState = BlendState.AlphaBlend;

            _billboardEffect.View = camera.View;
            _billboardEffect.Projection = camera.GetProjection(aspect);
            _billboardEffect.Texture = _enemyTex;

            foreach (var b in enemies)
                DrawBillboard(device, b.Center, b.Width, b.Height, camera.Position, b.Tint);

            device.BlendState = BlendState.Opaque;
        }

        private void DrawBillboard(GraphicsDevice device, Vector3 center, float width, float height, Vector3 cameraPos, Color tint)
        {
            Vector3 toCamera = cameraPos - center;
            toCamera.Y = 0;
            if (toCamera.LengthSquared() < 0.0001f) toCamera = Vector3.Forward;
            toCamera.Normalize();
            Vector3 right = Vector3.Cross(Vector3.Up, toCamera);

            Vector3 bl = center - right * (width / 2f);
            Vector3 br = center + right * (width / 2f);
            Vector3 tl = bl + Vector3.Up * height;
            Vector3 tr = br + Vector3.Up * height;

            var verts = new[]
            {
                new VertexPositionColorTexture(bl, tint, new Vector2(0, 1)),
                new VertexPositionColorTexture(tl, tint, new Vector2(0, 0)),
                new VertexPositionColorTexture(tr, tint, new Vector2(1, 0)),
                new VertexPositionColorTexture(bl, tint, new Vector2(0, 1)),
                new VertexPositionColorTexture(tr, tint, new Vector2(1, 0)),
                new VertexPositionColorTexture(br, tint, new Vector2(1, 1)),
            };

            _billboardEffect.World = Matrix.Identity;
            foreach (var pass in _billboardEffect.CurrentTechnique.Passes)
            {
                pass.Apply();
                device.DrawUserPrimitives(PrimitiveType.TriangleList, verts, 0, 2);
            }
        }
    }
}
