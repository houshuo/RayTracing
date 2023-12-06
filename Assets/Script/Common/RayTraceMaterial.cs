using Unity.Mathematics;
using UnityEngine;

namespace Script.RayTracing
{
    public struct Vertex
    {
        public float3 position;
        public float3 normal;
    }
    
    public struct RayTraceMaterial
    {
        public Color albedo;
        public Color specular;
        public Color emission;
    }
}