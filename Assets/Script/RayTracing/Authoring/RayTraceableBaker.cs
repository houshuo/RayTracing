using System.Collections;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Hash128 = UnityEngine.Hash128;
using UnityEditor;

namespace Script.RayTracing
{
    public class RayTraceableBaker
    {
        class BLASBaker : Baker<MeshFilter>
        {
            public static GUID UnityEditorResources = new GUID("0000000000000000d000000000000000");
            public static GUID UnityBuiltinResources = new GUID("0000000000000000e000000000000000");
            public static GUID UnityBuiltinExtraResources = new GUID("0000000000000000f000000000000000");
            public static bool IsBuiltin(in GUID g) =>
                g == UnityEditorResources ||
                g == UnityBuiltinResources ||
                g == UnityBuiltinExtraResources;

            public static Hash128 GetAssetHash128(Object asset)
            {
                var assetPath = AssetDatabase.GetAssetPath(asset);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset.GetInstanceID(), out string guid, out long localId);
                Hash128 hash = default;

                // Here we get a hash from the mesh to use it later to de-duplicate the blob assets
                // mesh.GetHashCode() will not update if the mesh is changed outside of the editor
                // So we get the hash from the asset path through the asset database.
                // However, trying to get a hash from the Default Resources (cube, capsule eg.) will always give a hash of 0s
                // In addition, it won't change (except for a Unity version update) so we can use GetHashCode
                if (IsBuiltin(new GUID(guid)))
                {
                    hash = new Hash128((uint)localId, (uint)asset.GetHashCode(), 0, 0);
                }
                else if (UnityEditor.EditorUtility.IsPersistent(asset))
                {
                    hash = AssetDatabase.GetAssetDependencyHash(assetPath);
                }
                else
                {
                    Debug.LogError("This sample does not support procedural assets stored in the scene");
                }

                return hash;
            }
            
            public override void Bake(MeshFilter authoring)
            {
                DependsOn(authoring.sharedMesh);
                //Get Mesh
                Mesh mesh = authoring.sharedMesh;
                if (mesh == null)
                    return;
                
                Hash128 hash = GetAssetHash128(mesh);
                if (!TryGetBlobAssetReference<BottomLevelAccelerateStructure>(hash,
                        out var blasBlobReference))
                {
                    blasBlobReference = BuildBLASBlobAssetReference(mesh);
                    AddBlobAssetWithCustomHash(ref blasBlobReference, hash);
                }

                var entity = GetEntity(TransformUsageFlags.Renderable);
                //Add Entity
                AddComponent(entity, new RayTraceableBLASComponent()
                {
                    BLAS = blasBlobReference
                });
            }

            private BlobAssetReference<BottomLevelAccelerateStructure> BuildBLASBlobAssetReference(Mesh mesh)
            {
                var vertices = mesh.vertices;
                var verticesCount = vertices.Length;
                var normals = mesh.normals;
                var normalCount = normals.Length;
                //Build TriangleMesh
                BlobBuilder blasBlobBuilder = new BlobBuilder(Allocator.Temp);
                ref BottomLevelAccelerateStructure blas =
                    ref blasBlobBuilder.ConstructRoot<BottomLevelAccelerateStructure>();
                BlobBuilderArray<float3> verticesBuilder =
                    blasBlobBuilder.Allocate(ref blas.vertices, verticesCount);
                for (int i = 0; i < verticesCount; i++)
                {
                    verticesBuilder[i] = vertices[i];
                }

                BlobBuilderArray<float3> normalBuilder =
                    blasBlobBuilder.Allocate(ref blas.normals, normalCount);
                for (int i = 0; i < normalCount; i++)
                {
                    normalBuilder[i] = normals[i];
                }

                var indices = mesh.GetIndices(0);
                var trianglesCount = indices.Length / 3;
                BlobBuilderArray<int3> triangleBuilder =
                    blasBlobBuilder.Allocate(ref blas.triangles, trianglesCount);
                for (int i = 0; i < trianglesCount; i++)
                {
                    triangleBuilder[i] = new int3(
                        indices[i * 3],
                        indices[i * 3 + 1],
                        indices[i * 3 + 2]
                    );
                }

                //Build BLAS Inputs
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

                //Build BVH
                int sketchNodeCount = trianglesCount + BoundingVolumeHierarchy.Constants.MaxNumTreeBranches;
                NativeArray<BoundingVolumeHierarchy.Node> sketch =
                    new NativeArray<BoundingVolumeHierarchy.Node>(sketchNodeCount, Allocator.Temp);
                var bvh = new BoundingVolumeHierarchy(sketch);
                bvh.Build(pointAndIndex, aabbs, out int nodeCount);

                //Build BLAS BlobAssetReference
                BlobBuilderArray<BoundingVolumeHierarchy.Node> blasNodesBuilder =
                    blasBlobBuilder.Allocate(ref blas.Nodes, nodeCount);
                for (int i = 0; i < nodeCount; i++)
                {
                    blasNodesBuilder[i] = sketch[i];
                }

                var blasResult =
                    blasBlobBuilder.CreateBlobAssetReference<BottomLevelAccelerateStructure>(Allocator.Persistent);
                blasBlobBuilder.Dispose();
                sketch.Dispose();

                return blasResult;
            }
        }
        
        class MaterialBaker : Baker<MeshRenderer>
        {
            public override void Bake(MeshRenderer authoring)
            {
                DependsOn(authoring.sharedMaterial);
                if (authoring.sharedMaterial == null)
                    return;
                if (authoring.sharedMaterial.shader != Shader.Find("Universal Render Pipeline/Lit"))
                    return;
                
                Material material = authoring.sharedMaterial;
                var entity = GetEntity(TransformUsageFlags.Renderable);
                //Add Entity
                AddComponent(entity, new RayTraceableMaterialComponent()
                {
                    albedo = material.GetColor("_Color"),
                    specular = material.GetColor("_SpecColor"),
                    emission = material.GetColor("_EmissionColor"),
                });
            }
        }
    }
}