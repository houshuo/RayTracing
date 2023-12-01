using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Script.RayTracing
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class BuildAccelerateStructureSystem : SystemBase
    {
        public bool EmptyScene = false;
        
        private TopLevelAccelerateStructure TLAS;
        //PerObjects BVH
        public ComputeBuffer ObjectsBVHOffsetBuffer;
        public ComputeBuffer ObjectsBVHBuffer;
        //PerObjects Vertices
        public ComputeBuffer ObjectsVerticesOffsetBuffer;
        public ComputeBuffer ObjectsVerticesBuffer;
        //PerObjects Normals
        public ComputeBuffer ObjectsNormalsOffsetBuffer;
        public ComputeBuffer ObjectsNormalsBuffer;
        //PerObjects Triangle Indices
        public ComputeBuffer ObjectsTrianglesOffsetBuffer;
        public ComputeBuffer ObjectsTrianglesBuffer;
        //PerObjects Properties
        public ComputeBuffer ObjectsMaterialBuffer;
        public ComputeBuffer ObjectsWorldToLocalBuffer;
        //TLAS Buffer
        public ComputeBuffer TLASBuffer;
        
        protected override void OnCreate()
        {
            TLAS = new TopLevelAccelerateStructure();
            TLAS.Init();
        }
        
        protected override void OnUpdate()
        {
            //Query RayTraceable
            EntityQuery rayTraceableQuery = SystemAPI.QueryBuilder()
                .WithAll<RayTraceableComponent, LocalToWorld>()
                .Build();
            var rayTraceables =
                rayTraceableQuery.ToComponentDataArray<RayTraceableComponent>(WorldUpdateAllocator);

            EmptyScene = rayTraceables.Length == 0;
            if (EmptyScene)
                return;
            
            //Calculate total nodes and build BLAS offsets
            int totalBVHNodeCount = 0;
            int totalVerticesCount = 0;
            int totalNormalsCount = 0;
            int totalTrianglesCount = 0;
            NativeArray<int> blasBVHOffsets = new NativeArray<int>(rayTraceables.Length, Allocator.TempJob);
            NativeArray<int> blasVerticesOffsets = new NativeArray<int>(rayTraceables.Length, Allocator.TempJob);
            NativeArray<int> blasNormalsOffsets = new NativeArray<int>(rayTraceables.Length, Allocator.TempJob);
            NativeArray<int> blasTriangleOffsets = new NativeArray<int>(rayTraceables.Length, Allocator.TempJob);
            for(int i = 0; i < rayTraceables.Length; i++)
            {
                var rayTraceableComponent = rayTraceables[i];
                ref BottomLevelAccelerateStructure blas = ref rayTraceableComponent.BLAS.Value;
                ref TriangleMesh mesh = ref rayTraceableComponent.mesh.Value;
                blasBVHOffsets[i] = totalBVHNodeCount;
                totalBVHNodeCount += blas.Nodes.Length;

                blasVerticesOffsets[i] = totalVerticesCount;
                totalVerticesCount += mesh.vertices.Length;
                
                blasNormalsOffsets[i] = totalNormalsCount;
                totalNormalsCount += mesh.normals.Length;

                blasTriangleOffsets[i] = totalTrianglesCount;
                totalTrianglesCount += mesh.triangles.Length;
            }
            
            //set buildTLASJob Properties
            NativeArray<Aabb> aabbs = new NativeArray<Aabb>(rayTraceables.Length, Allocator.TempJob);
            NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndices =
                new NativeArray<BoundingVolumeHierarchy.PointAndIndex>(rayTraceables.Length, Allocator.TempJob);
            BuildTLASJob buildTLASJob = new BuildTLASJob();
            buildTLASJob.aabbs = aabbs;
            buildTLASJob.pointAndIndex = pointAndIndices;
            
            buildTLASJob.blasBVHOffsets = blasBVHOffsets;
            PrepareComputeBuffer(ref ObjectsBVHOffsetBuffer, rayTraceables.Length, sizeof(int));
            buildTLASJob.blasBVHOffsetsCopy = ObjectsBVHOffsetBuffer.BeginWrite<int>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsBVHBuffer, totalBVHNodeCount, System.Runtime.InteropServices.Marshal.SizeOf(typeof(BoundingVolumeHierarchy.Node)));
            buildTLASJob.blasBVHArray = ObjectsBVHBuffer.BeginWrite<BoundingVolumeHierarchy.Node>(0, totalBVHNodeCount);
            
            buildTLASJob.blasVerticesOffsets = blasVerticesOffsets;
            PrepareComputeBuffer(ref ObjectsVerticesOffsetBuffer, rayTraceables.Length, sizeof(int));
            buildTLASJob.blasVerticesOffsetsCopy = ObjectsVerticesOffsetBuffer.BeginWrite<int>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsVerticesBuffer, totalVerticesCount, 3 * sizeof(float));
            buildTLASJob.blasVerticesArray = ObjectsVerticesBuffer.BeginWrite<float3>(0, totalVerticesCount);
            
            buildTLASJob.blasNormalsOffsets = blasNormalsOffsets;
            PrepareComputeBuffer(ref ObjectsNormalsOffsetBuffer, rayTraceables.Length, sizeof(int));
            buildTLASJob.blasNormalsOffsetsCopy = ObjectsNormalsOffsetBuffer.BeginWrite<int>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsNormalsBuffer, totalNormalsCount, 3 * sizeof(float));
            buildTLASJob.blasNormalsArray = ObjectsNormalsBuffer.BeginWrite<float3>(0, totalNormalsCount);
            
            buildTLASJob.blasTriangleOffsets = blasTriangleOffsets;
            PrepareComputeBuffer(ref ObjectsTrianglesOffsetBuffer, totalTrianglesCount, sizeof(int));
            buildTLASJob.blasTriangleOffsetsCopy = ObjectsTrianglesOffsetBuffer.BeginWrite<int>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsTrianglesBuffer, totalTrianglesCount, 3 * sizeof(int));
            buildTLASJob.blasTriangleArray = ObjectsTrianglesBuffer.BeginWrite<int3>(0, totalTrianglesCount);
            
            PrepareComputeBuffer(ref ObjectsMaterialBuffer, rayTraceables.Length, System.Runtime.InteropServices.Marshal.SizeOf(typeof(RayTraceMaterial)));
            buildTLASJob.blasMaterials = ObjectsMaterialBuffer.BeginWrite<RayTraceMaterial>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsWorldToLocalBuffer, rayTraceables.Length, 16 * sizeof(float));
            buildTLASJob.blasWorldToLocal = ObjectsWorldToLocalBuffer.BeginWrite<float4x4>(0, rayTraceables.Length);
            var jobHandle = buildTLASJob.ScheduleParallel(rayTraceableQuery, new JobHandle());
            //build TLAS
            TLAS.ScheduleBuildTree(aabbs, pointAndIndices, jobHandle).Complete();
            Dependency.Complete();
            //set TLAS to compute shader
            ObjectsBVHOffsetBuffer.EndWrite<int>(rayTraceables.Length);
            ObjectsBVHBuffer.EndWrite<BoundingVolumeHierarchy.Node>(totalBVHNodeCount);
            
            ObjectsVerticesOffsetBuffer.EndWrite<int>(rayTraceables.Length);
            ObjectsVerticesBuffer.EndWrite<float3>(totalVerticesCount);
            
            ObjectsNormalsOffsetBuffer.EndWrite<int>(rayTraceables.Length);
            ObjectsNormalsBuffer.EndWrite<float3>(totalNormalsCount);
            
            ObjectsTrianglesOffsetBuffer.EndWrite<int>(rayTraceables.Length);
            ObjectsTrianglesBuffer.EndWrite<int3>(totalTrianglesCount);
            
            ObjectsMaterialBuffer.EndWrite<RayTraceMaterial>(rayTraceables.Length);
            ObjectsWorldToLocalBuffer.EndWrite<float4x4>(rayTraceables.Length);
            
            PrepareComputeBuffer(ref TLASBuffer, TLAS.NodeCount, System.Runtime.InteropServices.Marshal.SizeOf(typeof(BoundingVolumeHierarchy.Node)));
            NativeArray<BoundingVolumeHierarchy.Node> tlasNativeArray = TLASBuffer.BeginWrite<BoundingVolumeHierarchy.Node>(0, TLAS.NodeCount);
            NativeArray<BoundingVolumeHierarchy.Node>.Copy(TLAS.Nodes, tlasNativeArray, TLAS.NodeCount);
            TLASBuffer.EndWrite<BoundingVolumeHierarchy.Node>(TLAS.NodeCount);
        }

        protected override void OnDestroy()
        {
            TLAS.Dispose();
            DestroyComputeBuffer(ref ObjectsBVHOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsBVHBuffer);
            DestroyComputeBuffer(ref ObjectsVerticesOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsVerticesBuffer);
            DestroyComputeBuffer(ref ObjectsNormalsOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsNormalsBuffer);
            DestroyComputeBuffer(ref ObjectsTrianglesOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsTrianglesBuffer);
            DestroyComputeBuffer(ref ObjectsMaterialBuffer);
            DestroyComputeBuffer(ref ObjectsWorldToLocalBuffer);
            DestroyComputeBuffer(ref TLASBuffer);
        }

        private static void PrepareComputeBuffer(ref ComputeBuffer buffer, int count, int stride)
        {
            if (buffer != null && (buffer.stride != stride || buffer.count < count))
            {
                buffer.Dispose();
                buffer = null;
            }
            
            if (buffer == null)
            {
                buffer = new ComputeBuffer(count, stride, ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
            }
        }

        private static void DestroyComputeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer == null)
                return;
            buffer.Dispose();
            buffer = null;
        }
    }
    
    [BurstCompile]
    public partial struct BuildTLASJob : IJobEntity
    {
        public NativeArray<Aabb> aabbs;
        public NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndex;
        //BVH
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasBVHOffsets;
        public NativeArray<int> blasBVHOffsetsCopy;
        [NativeDisableContainerSafetyRestriction] public NativeArray<BoundingVolumeHierarchy.Node> blasBVHArray;
        //Vertices
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasVerticesOffsets;
        public NativeArray<int> blasVerticesOffsetsCopy;
        [NativeDisableContainerSafetyRestriction] public NativeArray<float3> blasVerticesArray;
        //Normals
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasNormalsOffsets;
        public NativeArray<int> blasNormalsOffsetsCopy;
        [NativeDisableContainerSafetyRestriction] public NativeArray<float3> blasNormalsArray;
        //Indices
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasTriangleOffsets;
        public NativeArray<int> blasTriangleOffsetsCopy;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int3> blasTriangleArray;
        //Per Object Properties
        public NativeArray<RayTraceMaterial> blasMaterials;
        public NativeArray<float4x4> blasWorldToLocal;
        void Execute([EntityIndexInQuery] int entityIndexInQuery, in LocalToWorld transform, in RayTraceableComponent rayTraceable)
        {
            ref BottomLevelAccelerateStructure blas = ref rayTraceable.BLAS.Value;
            ref TriangleMesh mesh = ref rayTraceable.mesh.Value;
            //1.build aabbs and index for build TLAS
            Aabb aabb = blas.Aabb;
            float3 center = aabb.Center;
            pointAndIndex[entityIndexInQuery] = new BoundingVolumeHierarchy.PointAndIndex()
                { Position = center, Index = entityIndexInQuery };
            float3 extents = aabb.Extents;
            float3 c0 = transform.Value.TransformPoint(center + extents * new float3(1, 1, 1));
            float3 c1 = transform.Value.TransformPoint(center + extents * new float3(1, 1, -1));
            float3 c2 = transform.Value.TransformPoint(center + extents * new float3(1, -1, 1));
            float3 c3 = transform.Value.TransformPoint(center + extents * new float3(1, -1, -1));
            float3 c4 = transform.Value.TransformPoint(center + extents * new float3(-1, 1, 1));
            float3 c5 = transform.Value.TransformPoint(center + extents * new float3(-1, 1, -1));
            float3 c6 = transform.Value.TransformPoint(center + extents * new float3(-1, -1, 1));
            float3 c7 = transform.Value.TransformPoint(center + extents * new float3(-1, -1, -1));
            float3 max = c0, min = c0;
            max = math.max(max, c1); min = math.min(min, c1);
            max = math.max(max, c2); min = math.min(min, c2);
            max = math.max(max, c3); min = math.min(min, c3);
            max = math.max(max, c4); min = math.min(min, c4);
            max = math.max(max, c5); min = math.min(min, c5);
            max = math.max(max, c6); min = math.min(min, c6);
            max = math.max(max, c7); min = math.min(min, c7);
            aabbs[entityIndexInQuery] = new Aabb(){Max = max, Min = min};
            
            //2.set blas and triangle to array by offset
            blasBVHOffsetsCopy[entityIndexInQuery] = blasBVHOffsets[entityIndexInQuery];
            int blasOffset = blasBVHOffsets[entityIndexInQuery];
            for (int i = 0; i < blas.Nodes.Length; i++)
            {
                blasBVHArray[i + blasOffset] = blas.Nodes[i];
            }
            
            blasVerticesOffsetsCopy[entityIndexInQuery] = blasVerticesOffsets[entityIndexInQuery];
            int verticesOffset = blasVerticesOffsets[entityIndexInQuery];
            for (int i = 0; i < mesh.vertices.Length; i++)
            {
                blasVerticesArray[i + verticesOffset] = mesh.vertices[i];
            }
            
            blasNormalsOffsetsCopy[entityIndexInQuery] = blasNormalsOffsets[entityIndexInQuery];
            int normalsOffset = blasNormalsOffsets[entityIndexInQuery];
            for (int i = 0; i < mesh.normals.Length; i++)
            {
                blasNormalsArray[i + normalsOffset] = mesh.normals[i];
            }
            
            blasTriangleOffsetsCopy[entityIndexInQuery] = blasTriangleOffsets[entityIndexInQuery];
            int triangleOffset = blasTriangleOffsets[entityIndexInQuery];
            for (int i = 0; i < mesh.triangles.Length; i++)
            {
                blasTriangleArray[i + triangleOffset] = mesh.triangles[i];
            }
            
            //3.set materials and worldToLocal
            blasMaterials[entityIndexInQuery] = new RayTraceMaterial()
            {
                albedo = rayTraceable.albedo,
                specular = rayTraceable.specular,
                emission = rayTraceable.emission
            };
            blasWorldToLocal[entityIndexInQuery] = math.inverse(transform.Value);
        }
    }
}