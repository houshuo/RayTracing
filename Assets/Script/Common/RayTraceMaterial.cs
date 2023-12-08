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
        public Vector4 albedo;
        public Vector4 specular;
        public Vector4 emission;
    }
    
    public struct FrameCBuffer
    {
        public float4x4 CameraToWorld;
        public float4x4 CameraInverseProjection;
        public float2 PixelOffset;
        public float Seed;
        public int ReflectionNum;
    };
}