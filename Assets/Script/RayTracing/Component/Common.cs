using DH2.Algorithm;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Script.RayTracing
{
    public struct TriangleMesh
    {
        public BlobArray<float3> vertices;
        public BlobArray<int3> triangles;
    }
    
    
}