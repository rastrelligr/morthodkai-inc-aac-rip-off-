using Microsoft.Xna.Framework;

namespace SERAAC.Rendering3D
{
    // Thin math wrapper: whoever owns the camera writes Position/Yaw/Pitch each frame,
    // this just turns them into View/Projection matrices for the renderer.
    public class FirstPersonCamera
    {
        public Vector3 Position;
        public float Yaw;   // Vessel facing + Weapon aim offset, radians
        public float Pitch; // Weapon aim offset only, radians

        public float FieldOfView = MathHelper.PiOver4;
        public float NearPlane = 0.05f;
        public float FarPlane = 100f;

        public Vector3 Forward => new Vector3(
            (float)System.Math.Sin(Yaw) * (float)System.Math.Cos(Pitch),
            (float)System.Math.Sin(Pitch),
            (float)System.Math.Cos(Yaw) * (float)System.Math.Cos(Pitch));

        public Matrix View => Matrix.CreateLookAt(Position, Position + Forward, Vector3.Up);

        public Matrix GetProjection(float aspectRatio) =>
            Matrix.CreatePerspectiveFieldOfView(FieldOfView, aspectRatio, NearPlane, FarPlane);
    }
}
