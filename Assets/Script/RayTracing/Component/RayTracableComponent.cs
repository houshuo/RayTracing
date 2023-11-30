using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

namespace Script.RayTracing
{
    public struct TriangleMesh
    {
        public BlobArray<float3> vertices;
        public BlobArray<float3> normals;
        public BlobArray<int3> triangles;
    }
    
    public struct RayTraceableComponent : IComponentData
    {
        public Color albedo;
        public Color specular;
        public Color emission;

        public BlobAssetReference<TriangleMesh> mesh;
        public BlobAssetReference<BottomLevelAccelerateStructure> BLAS;
    }
}
