using System.Numerics;
using System.Runtime.InteropServices;
using AstroCraft.Core.Rendering;

namespace AstroCraft.Client.Rendering;

[StructLayout(LayoutKind.Sequential)]
public struct GpuVertex
{
    public Vector3 Position;
    public Vector2 Uv;
    public float TextureIndex;
    public Vector4 Normal;
    public float Ao;

    public GpuVertex(Vector3 position, Vector2 uv, float textureIndex, Vector3 normal, float ao)
        : this(position, uv, textureIndex, new Vector4(normal, 0f), ao)
    {
    }

    public GpuVertex(Vector3 position, Vector2 uv, float textureIndex, Vector4 normal, float ao)
    {
        Position = position;
        Uv = uv;
        TextureIndex = textureIndex;
        Normal = normal;
        Ao = ao;
    }

    public static GpuVertex[] FromBlockVertices(BlockVertex[] vertices)
    {
        GpuVertex[] gpuVertices = new GpuVertex[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            BlockVertex vertex = vertices[i];
            gpuVertices[i] = new GpuVertex(
                new Vector3(vertex.X, vertex.Y, vertex.Z),
                new Vector2(vertex.U, vertex.V),
                vertex.TextureIndex,
                new Vector4(vertex.Nx, vertex.Ny, vertex.Nz, vertex.WindWeight),
                vertex.Ao);
        }

        return gpuVertices;
    }
}
