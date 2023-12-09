using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.VisualScripting;
using UnityEngine;

namespace Script.RayTracing
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial class BuildAccelerateStructureSystem : SystemBase
    {
        //TLAS
        private TopLevelAccelerateStructure TLAS;
        
        public int ObjectsCount { private set; get; }
        //PerObjects BVH
        public ComputeBuffer ObjectsBVHOffsetBuffer;
        public ComputeBuffer ObjectsBVHBuffer;
        //PerObjects Vertices
        public ComputeBuffer ObjectsVerticesOffsetBuffer;
        public ComputeBuffer ObjectsVerticesBuffer;
        //PerObjects Triangle Indices
        public ComputeBuffer ObjectsTrianglesOffsetBuffer;
        public ComputeBuffer ObjectsTrianglesBuffer;
        //PerObjects Properties
        public ComputeBuffer ObjectsMaterialBuffer;
        public ComputeBuffer ObjectsWorldToLocalBuffer;
        public ComputeBuffer ObjectsLocalToWorldBuffer;
        //TLAS Buffer
        public ComputeBuffer TLASBuffer;
        
        protected override void OnCreate()
        {
            TLAS = new TopLevelAccelerateStructure();
            TLAS.Init();
        }
        
        protected override unsafe void OnUpdate()
        {
            //Query RayTraceable
            EntityQuery rayTraceableQuery = SystemAPI.QueryBuilder()
                .WithAll<RayTraceableComponent, LocalToWorld>()
                .Build();
            var rayTraceables =
                rayTraceableQuery.ToComponentDataArray<RayTraceableComponent>(WorldUpdateAllocator);

            ObjectsCount = rayTraceables.Length;
            if (ObjectsCount == 0)
                return;
            
            //Calculate total nodes and build BLAS offsets
            int totalBVHNodeCount = 0;
            int totalVerticesCount = 0;
            int totalTrianglesCount = 0;
            NativeArray<int> objectsBVHOffsetBuffer = new NativeArray<int>(rayTraceables.Length, Allocator.TempJob);
            NativeArray<int> objectsVerticesOffsetBuffer = new NativeArray<int>(rayTraceables.Length, Allocator.TempJob);
            NativeArray<int> objectsTrianglesOffsetBuffer = new NativeArray<int>(rayTraceables.Length, Allocator.TempJob);
            for(int i = 0; i < rayTraceables.Length; i++)
            {
                var rayTraceableComponent = rayTraceables[i];
                ref BottomLevelAccelerateStructure blas = ref rayTraceableComponent.BLAS.Value;
                ref TriangleMesh mesh = ref rayTraceableComponent.mesh.Value;
                objectsBVHOffsetBuffer[i] = totalBVHNodeCount;
                totalBVHNodeCount += blas.Nodes.Length;

                objectsVerticesOffsetBuffer[i] = totalVerticesCount;
                totalVerticesCount += mesh.vertices.Length;

                objectsTrianglesOffsetBuffer[i] = totalTrianglesCount;
                totalTrianglesCount += mesh.triangles.Length;
            }
            
            //set buildTLASJob Properties
            NativeArray<Aabb> aabbs = new NativeArray<Aabb>(rayTraceables.Length, Allocator.TempJob);
            NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndices =
                new NativeArray<BoundingVolumeHierarchy.PointAndIndex>(rayTraceables.Length, Allocator.TempJob);
            PrepareBuildTLASJob prepareBuildTLASJob = new PrepareBuildTLASJob();
            prepareBuildTLASJob.aabbs = aabbs;
            prepareBuildTLASJob.pointAndIndex = pointAndIndices;
            var jobHandle = prepareBuildTLASJob.ScheduleParallel(rayTraceableQuery, new JobHandle());
            //build TLAS
            TLAS.ScheduleBuildTree(aabbs, pointAndIndices, jobHandle).Complete();
            //build Compute Buffer
            BuildComputeBufferJob buildComputeBufferJob = new BuildComputeBufferJob();
            buildComputeBufferJob.blasBVHOffsets = objectsBVHOffsetBuffer;
            PrepareComputeBuffer(ref ObjectsBVHOffsetBuffer, sizeof(int), rayTraceables.Length);
            buildComputeBufferJob.blasBVHOffsetsBuffer = ObjectsBVHOffsetBuffer.BeginWrite<int>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsBVHBuffer, Marshal.SizeOf<BoundingVolumeHierarchy.Node>(), totalBVHNodeCount);
            buildComputeBufferJob.blasBVHBuffer = ObjectsBVHBuffer.BeginWrite<BoundingVolumeHierarchy.Node>(0, totalBVHNodeCount);
            
            buildComputeBufferJob.blasVerticesOffsets = objectsVerticesOffsetBuffer;
            PrepareComputeBuffer(ref ObjectsVerticesOffsetBuffer, sizeof(int), rayTraceables.Length);
            buildComputeBufferJob.blasVerticesOffsetsBuffer = ObjectsVerticesOffsetBuffer.BeginWrite<int>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsVerticesBuffer,Marshal.SizeOf<Vertex>(), totalVerticesCount);
            buildComputeBufferJob.blasVerticesBuffer = ObjectsVerticesBuffer.BeginWrite<Vertex>(0, totalVerticesCount);
            
            buildComputeBufferJob.blasTriangleOffsets = objectsTrianglesOffsetBuffer;
            PrepareComputeBuffer(ref ObjectsTrianglesOffsetBuffer, sizeof(int), rayTraceables.Length);
            buildComputeBufferJob.blasTriangleOffsetsBuffer = ObjectsTrianglesOffsetBuffer.BeginWrite<int>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsTrianglesBuffer,Marshal.SizeOf<int3>(), totalTrianglesCount);
            buildComputeBufferJob.blasTriangleBuffer = ObjectsTrianglesBuffer.BeginWrite<int3>(0, totalTrianglesCount);
            
            PrepareComputeBuffer(ref ObjectsMaterialBuffer, Marshal.SizeOf<RayTraceMaterial>(), rayTraceables.Length);
            buildComputeBufferJob.blasMaterials = ObjectsMaterialBuffer.BeginWrite<RayTraceMaterial>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsWorldToLocalBuffer, Marshal.SizeOf<float4x4>(), rayTraceables.Length);
            buildComputeBufferJob.blasWorldToLocal = ObjectsWorldToLocalBuffer.BeginWrite<float4x4>(0, rayTraceables.Length);
            PrepareComputeBuffer(ref ObjectsLocalToWorldBuffer, Marshal.SizeOf<float4x4>(), rayTraceables.Length);
            buildComputeBufferJob.blasLocalToWorld = ObjectsLocalToWorldBuffer.BeginWrite<float4x4>(0, rayTraceables.Length);
            buildComputeBufferJob.ScheduleParallel(rayTraceableQuery, new JobHandle()).Complete();
            
            ObjectsBVHOffsetBuffer.EndWrite<int>(rayTraceables.Length);
            ObjectsBVHBuffer.EndWrite<BoundingVolumeHierarchy.Node>(totalBVHNodeCount);
            ObjectsVerticesOffsetBuffer.EndWrite<int>(rayTraceables.Length);
            ObjectsVerticesBuffer.EndWrite<Vertex>(totalVerticesCount);
            ObjectsTrianglesOffsetBuffer.EndWrite<int>(rayTraceables.Length);
            ObjectsTrianglesBuffer.EndWrite<int3>(totalTrianglesCount);
            ObjectsMaterialBuffer.EndWrite<RayTraceMaterial>(rayTraceables.Length);
            ObjectsWorldToLocalBuffer.EndWrite<float4x4>(rayTraceables.Length);
            ObjectsLocalToWorldBuffer.EndWrite<float4x4>(rayTraceables.Length);

            PrepareComputeBuffer(ref TLASBuffer, Marshal.SizeOf<BoundingVolumeHierarchy.Node>(), TLAS.NodeCount);
            NativeArray<BoundingVolumeHierarchy.Node>.Copy(TLAS.Nodes, TLASBuffer.BeginWrite<BoundingVolumeHierarchy.Node>(0, TLAS.NodeCount));
            TLASBuffer.EndWrite<BoundingVolumeHierarchy.Node>(TLAS.NodeCount);
        }
        
        protected override void OnDestroy()
        {
            TLAS.Dispose();
            DestroyComputeBuffer(ref TLASBuffer);
            DestroyComputeBuffer(ref ObjectsBVHOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsBVHBuffer);
            DestroyComputeBuffer(ref ObjectsVerticesOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsVerticesBuffer);
            DestroyComputeBuffer(ref ObjectsTrianglesOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsTrianglesBuffer);
            DestroyComputeBuffer(ref ObjectsMaterialBuffer);
            DestroyComputeBuffer(ref ObjectsLocalToWorldBuffer);
            DestroyComputeBuffer(ref ObjectsWorldToLocalBuffer);
        }
        
        private static void PrepareComputeBuffer(ref ComputeBuffer buffer, int stride, int count)
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
    public partial struct PrepareBuildTLASJob : IJobEntity
    {
        public NativeArray<Aabb> aabbs;
        public NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndex;
        
        void Execute([EntityIndexInQuery] int entityIndexInQuery, in LocalToWorld transform, in RayTraceableComponent rayTraceable)
        {
            ref BottomLevelAccelerateStructure blas = ref rayTraceable.BLAS.Value;
            ref TriangleMesh mesh = ref rayTraceable.mesh.Value;
            //build aabbs and index for build TLAS
            Aabb aabb = blas.Aabb;
            float3 center = aabb.Center;
            pointAndIndex[entityIndexInQuery] = new BoundingVolumeHierarchy.PointAndIndex()
                { Position = center, Index = entityIndexInQuery };
            float3 extents = aabb.Extents * 0.5f;
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
        }
    }
    
    [BurstCompile]
    public partial struct BuildComputeBufferJob : IJobEntity
    {
        //BVH
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasBVHOffsets;
        public NativeArray<int> blasBVHOffsetsBuffer;
        [NativeDisableContainerSafetyRestriction] public NativeArray<BoundingVolumeHierarchy.Node> blasBVHBuffer;
        //Vertices+Normals
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasVerticesOffsets;
        public NativeArray<int> blasVerticesOffsetsBuffer;
        [NativeDisableContainerSafetyRestriction] public NativeArray<Vertex> blasVerticesBuffer;
        //Indices
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasTriangleOffsets;
        public NativeArray<int> blasTriangleOffsetsBuffer;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int3> blasTriangleBuffer;
        //Per Object Properties
        public NativeArray<RayTraceMaterial> blasMaterials;
        public NativeArray<float4x4> blasWorldToLocal;
        public NativeArray<float4x4> blasLocalToWorld;
        
        void Execute([EntityIndexInQuery] int entityIndexInQuery, in LocalToWorld transform, in RayTraceableComponent rayTraceable)
        {
            ref BottomLevelAccelerateStructure blas = ref rayTraceable.BLAS.Value;
            ref TriangleMesh mesh = ref rayTraceable.mesh.Value;
        
            //set blas and triangle to array by offset
            int blasOffset = blasBVHOffsets[entityIndexInQuery]; 
            blasBVHOffsetsBuffer[entityIndexInQuery] = blasOffset;
            for (int i = 0; i < blas.Nodes.Length; i++)
            {
                blasBVHBuffer[i + blasOffset] = blas.Nodes[i];
            }
        
            int verticesOffset = blasVerticesOffsets[entityIndexInQuery];
            blasVerticesOffsetsBuffer[entityIndexInQuery] = verticesOffset;
            for (int i = 0; i < mesh.vertices.Length; i++)
            {
                blasVerticesBuffer[i + verticesOffset] = new Vertex(){position = mesh.vertices[i], normal = mesh.normals[i]};
            }
        
            int triangleOffset = blasTriangleOffsets[entityIndexInQuery];
            blasTriangleOffsetsBuffer[entityIndexInQuery] = triangleOffset;
            for (int i = 0; i < mesh.triangles.Length; i++)
            {
                blasTriangleBuffer[i + triangleOffset] = mesh.triangles[i];
            }
        
            blasMaterials[entityIndexInQuery] = new RayTraceMaterial()
            {
                albedo = rayTraceable.albedo,
                specular = rayTraceable.specular,
                emission = rayTraceable.emission
            };
            blasLocalToWorld[entityIndexInQuery] = transform.Value;
            blasWorldToLocal[entityIndexInQuery] = math.inverse(transform.Value);
        }
    }
}