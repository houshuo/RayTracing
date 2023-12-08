using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Script.RayTracing
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    [UpdateAfter(typeof(BuildAccelerateStructureSystem))]
    public partial struct DebugBuildAccelerateStructureSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            // state.Enabled = false;
        }

        // [BurstCompile]
        public unsafe void OnUpdate(ref SystemState state)
        {
            //Query RayTraceable
            SystemHandle buildAccelerateStructureSystemHandle =
                state.WorldUnmanaged.GetExistingUnmanagedSystem<BuildAccelerateStructureSystem>();
            if (buildAccelerateStructureSystemHandle == SystemHandle.Null)
                return;

            BuildAccelerateStructureSystem buildAccelerateStructureSystem =
                state.WorldUnmanaged.GetUnsafeSystemRef<BuildAccelerateStructureSystem>(buildAccelerateStructureSystemHandle);
            if (buildAccelerateStructureSystem.ObjectsCount == 0)
                return;

            
            var TLASNodes = buildAccelerateStructureSystem.TLASBuffer;
            DrawBVH(TLASNodes, 0, float4x4.identity);
            // for (int i = 0; i < buildAccelerateStructureSystem.ObjectsCount; i++)
            // {
            //     DrawBVHTriangle(buildAccelerateStructureSystem.ObjectsBVHOffsetBuffer[i],buildAccelerateStructureSystem.ObjectsBVHBuffer, 
            //         buildAccelerateStructureSystem.ObjectsTrianglesOffsetBuffer[i], buildAccelerateStructureSystem.ObjectsTrianglesBuffer,
            //         buildAccelerateStructureSystem.ObjectsVerticesOffsetBuffer[i], buildAccelerateStructureSystem.ObjectsVerticesBuffer,
            //         buildAccelerateStructureSystem.ObjectsLocalToWorldBuffer[i]);
            // }
        }

        private static void DrawAABB(Aabb bbox, Color c, float4x4 localToWorld)
        {
            float3 top = bbox.Max;
            float3 bot = bbox.Min;
            float3 
                v0 = math.transform(localToWorld, bot),
                v1 = math.transform(localToWorld, new float3(top.x, bot.y, bot.z)),
                v2 = math.transform(localToWorld, new float3(top.x, top.y, bot.z)),
                v3 = math.transform(localToWorld, new float3(bot.x, top.y, bot.z)),

                v4 = math.transform(localToWorld, new float3(bot.x, bot.y, top.z)),
                v5 = math.transform(localToWorld, new float3(top.x, bot.y, top.z)),
                v7 = math.transform(localToWorld, new float3(bot.x, top.y, top.z)),
                v6 = math.transform(localToWorld, top);


            Debug.DrawLine(v0, v1, c);
            Debug.DrawLine(v1, v2, c);
            Debug.DrawLine(v2, v3, c);
            Debug.DrawLine(v3, v0, c);

            Debug.DrawLine(v4, v5, c);
            Debug.DrawLine(v5, v6, c);
            Debug.DrawLine(v6, v7, c);
            Debug.DrawLine(v7, v4, c);

            Debug.DrawLine(v0, v4, c);
            Debug.DrawLine(v1, v5, c);
            Debug.DrawLine(v2, v6, c);
            Debug.DrawLine(v3, v7, c);
        }
        
        private static void DrawTriangle(float3 v0, float3 v1, float3 v2, Color c, float4x4 localToWorld)
        {
            v0 = math.transform(localToWorld, v0);
            v1 = math.transform(localToWorld, v1);
            v2 = math.transform(localToWorld, v2);
            Debug.DrawLine(v0, v1, c);
            Debug.DrawLine(v1, v2, c);
            Debug.DrawLine(v2, v0, c);
        }

        private static unsafe void DrawBVH(NativeArray<BoundingVolumeHierarchy.Node> nodes, int bvhOffset, 
            float4x4 localToWorld)
        {
            int* stack = stackalloc int[BoundingVolumeHierarchy.Constants.UnaryStackSize];
            int top = 0;
            stack[top++] = 1;
            do
            {
                int index = stack[--top];
                BoundingVolumeHierarchy.Node node = nodes[index + bvhOffset];

                if (!node.IsLeaf)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (node.Data[i] != 0)
                        {
                            stack[top++] = node.Data[i];
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (node.Data[i] != -1)
                        {
                            DrawAABB(node.Bounds.GetAabb(i), Color.green, localToWorld);
                        }
                    }
                }
            } while (top > 0);
        }
        
        private static unsafe void DrawBVHTriangle(int bvhOffset, NativeArray<BoundingVolumeHierarchy.Node> nodes, 
            int trianglesOffset, NativeArray<int3> trianglesArray,
            int verticesOffset, NativeArray<Vertex> verticesArray,
            float4x4 localToWorld)
        {
            int* stack = stackalloc int[BoundingVolumeHierarchy.Constants.UnaryStackSize];
            int top = 0;
            stack[top++] = 1;
            do
            {
                int index = stack[--top];
                BoundingVolumeHierarchy.Node node = nodes[index + bvhOffset];

                if (!node.IsLeaf)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (node.Data[i] != 0)
                        {
                            stack[top++] = node.Data[i];
                        }
                    }
                }
                else
                {
                    for (int i = 0; i < 4; i++)
                    {
                        if (node.Data[i] != -1)
                        {
                            int triangleIndex = node.Data[i] + trianglesOffset;
                            int3 triangle = trianglesArray[triangleIndex];
                            var v0 = verticesArray[triangle.x + verticesOffset];
                            var v1 = verticesArray[triangle.y + verticesOffset];
                            var v2 = verticesArray[triangle.z + verticesOffset];
                            
                            float3 v0v1 = v1.position - v0.position;
                            float3 v0v2 = v2.position - v0.position;
                            
                            float3 pvec = math.cross(Camera.current.transform.forward, v0v2);
                            float det = math.dot(v0v1, pvec);
                            // culling
                            if (det < 0)
                                continue;
                            
                            DrawTriangle(v0.position, v1.position, v2.position,  Color.yellow, localToWorld);
                        }
                    }
                }
            } while (top > 0);
        }
        

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {

        }
    }
}