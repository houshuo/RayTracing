using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

namespace Script.RayTracing
{
    public class RayTraceable : MonoBehaviour
    {
        public Color albedo, specular, emission;

        class Baker : Baker<RayTraceable>
        {
            public override unsafe void Bake(RayTraceable authoring)
            {
                //Get Mesh
                MeshFilter meshFilter = authoring.GetComponent<MeshFilter>();
                Mesh mesh = meshFilter.sharedMesh;
                var vertices = mesh.vertices;
                var verticesCount = vertices.Length;
                //Build TriangleMesh
                BlobBuilder meshBlobBuilder = new BlobBuilder(Allocator.Temp);
                ref TriangleMesh triangleMesh = ref meshBlobBuilder.ConstructRoot<TriangleMesh>();
                BlobBuilderArray<float3> verticesBuilder =
                    meshBlobBuilder.Allocate(ref triangleMesh.vertices, vertices.Length);
                for (int i = 0; i < verticesCount; i++)
                {
                    verticesBuilder[i] = vertices[i];
                }

                var indices = mesh.GetIndices(0);
                var trianglesCount = indices.Length / 3;
                BlobBuilderArray<int3> triangleBuilder =
                    meshBlobBuilder.Allocate(ref triangleMesh.triangles, trianglesCount);
                for (int i = 0; i < trianglesCount; i++)
                {
                    triangleBuilder[i] = new int3(
                        indices[i * 3],
                        indices[i * 3 + 1],
                        indices[i * 3 + 2]
                    );
                }

                var meshResult = meshBlobBuilder.CreateBlobAssetReference<TriangleMesh>(Allocator.Persistent);
                meshBlobBuilder.Dispose();

                //Build BLAS
                NativeArray<Aabb> aabbs = new NativeArray<Aabb>(trianglesCount, Allocator.TempJob);
                NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndex =
                    new NativeArray<BoundingVolumeHierarchy.PointAndIndex>(trianglesCount, Allocator.TempJob);
                for (int i = 0; i < trianglesCount; i++)
                {
                    float3 a = vertices[indices[i * 3]];
                    float3 b = vertices[indices[i * 3 + 1]];
                    float3 c = vertices[indices[i * 3 + 2]];
                    float3 max = math.max(math.max(a, b), c);
                    float3 min = math.min(math.min(a, b), c);
                    aabbs[i] = new Aabb() { Min = min, Max = max };
                    pointAndIndex[i] = new BoundingVolumeHierarchy.PointAndIndex()
                        { Index = i, Position = aabbs[i].Center };
                }

                BlobBuilder blasBlobBuilder = new BlobBuilder(Allocator.Temp);
                ref BottomLevelAccelerateStructure blas =
                    ref blasBlobBuilder.ConstructRoot<BottomLevelAccelerateStructure>();
                int nodeCount = trianglesCount + BoundingVolumeHierarchy.Constants.MaxNumTreeBranches;
                BlobBuilderArray<BoundingVolumeHierarchy.Node> blasNodesBuilder =
                    blasBlobBuilder.Allocate(ref blas.Nodes, nodeCount);
                var bvh = new BoundingVolumeHierarchy((BoundingVolumeHierarchy.Node*)blasNodesBuilder.GetUnsafePtr());
                bvh.Build(pointAndIndex, aabbs, out int nodeCountOutput, true);
                blas.NodeCount = nodeCountOutput;
                var blasResult =
                    blasBlobBuilder.CreateBlobAssetReference<BottomLevelAccelerateStructure>(Allocator.Persistent);
                blasBlobBuilder.Dispose();

                //Add Entity
                var entity = GetEntity(TransformUsageFlags.Renderable);
                AddComponent<RayTraceableComponent>(entity, new RayTraceableComponent()
                    {
                        albedo = authoring.albedo,
                        specular = authoring.specular,
                        emission = authoring.emission,
                        mesh = meshResult,
                        BLAS = blasResult,
                    }
                );
            }
        }
    }
}