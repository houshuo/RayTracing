using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

namespace Script.RayTracing
{
    public struct BottomLevelAccelerateStructure
    {
        public BlobArray<BoundingVolumeHierarchy.Node> Nodes; // The nodes of the bounding volume
        public BlobArray<float3> vertices;
        public BlobArray<float3> normals;
        public BlobArray<int3> triangles;
        public Aabb Aabb
        {
            get => Nodes[1].Bounds.GetCompoundAabb();
        }
    }
    
    public struct RayTraceableBLASComponent : IComponentData
    {
        public BlobAssetReference<BottomLevelAccelerateStructure> BLAS;
    }

    public struct RayTraceableMaterialComponent : IComponentData
    {
        public Color albedo;
        public Color specular;
        public Color emission;
    }
    
}
