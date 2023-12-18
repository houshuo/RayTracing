using System;
using System.Runtime.InteropServices;
using Unity.Burst;
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
        public ComputeBuffer ObjectsPropertyBuffer;
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
                .WithAll<RayTraceableBLASComponent, RayTraceableMaterialComponent, LocalToWorld>()
                .Build();
            var rayTraceableBLASs =
                rayTraceableQuery.ToComponentDataArray<RayTraceableBLASComponent>(WorldUpdateAllocator);
            var rayTraceableMaterials =
                rayTraceableQuery.ToComponentDataArray<RayTraceableMaterialComponent>(WorldUpdateAllocator);

            ObjectsCount = rayTraceableBLASs.Length;
            if (ObjectsCount == 0)
                return;
            
            //TLAS
            //set buildTLASJob Properties
            NativeArray<Aabb> aabbs = new NativeArray<Aabb>(rayTraceableBLASs.Length, Allocator.TempJob);
            NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndices =
                new NativeArray<BoundingVolumeHierarchy.PointAndIndex>(rayTraceableBLASs.Length, Allocator.TempJob);
            PrepareBuildTLASJob prepareBuildTLASJob = new PrepareBuildTLASJob();
            prepareBuildTLASJob.aabbs = aabbs;
            prepareBuildTLASJob.pointAndIndex = pointAndIndices;
            var jobHandle = prepareBuildTLASJob.ScheduleParallel(rayTraceableQuery, new JobHandle());
            //build TLAS
            jobHandle = TLAS.ScheduleBuildTree(aabbs, pointAndIndices, jobHandle);
            
            //BLAS
            //Give individual BLAS index
            NativeHashMap<BlobAssetReference<BottomLevelAccelerateStructure>, int> objectsBVHMap =
                new NativeHashMap<BlobAssetReference<BottomLevelAccelerateStructure>, int>(rayTraceableMaterials.Length,
                    Allocator.TempJob);
            int nextBLASIndex = 0;
            for (int i = 0; i < rayTraceableBLASs.Length; i++)
            {
                var rayTraceableComponent = rayTraceableBLASs[i];
                if (!objectsBVHMap.TryGetValue(rayTraceableComponent.BLAS, out int blasIndex))
                {
                    blasIndex = nextBLASIndex;
                    nextBLASIndex++;
                    objectsBVHMap.TryAdd(rayTraceableComponent.BLAS, blasIndex);
                }
            }
            //Get BlobAssetReference<BottomLevelAccelerateStructure>> Array by BLAS index
            var blasArrayTemp = objectsBVHMap.GetKeyArray(Allocator.Temp);
            var blasArray =
                new NativeArray<BlobAssetReference<BottomLevelAccelerateStructure>>(blasArrayTemp.Length,
                    Allocator.TempJob);
            for (int i = 0; i < blasArrayTemp.Length; i++)
            {
                var aBlas = blasArrayTemp[i];
                objectsBVHMap.TryGetValue(aBlas, out int index);
                blasArray[index] = aBlas;
            }
            blasArrayTemp.Dispose();
            
            //Calculate total nodes and build BLAS offsets
            int blasCount = blasArray.Length;
            int totalBVHNodeCount = 0;
            int totalVerticesCount = 0;
            int totalTrianglesCount = 0;
            NativeArray<int> objectsBVHOffsetBuffer = new NativeArray<int>(blasArray.Length, Allocator.TempJob);
            NativeArray<int> objectsVerticesOffsetBuffer = new NativeArray<int>(blasArray.Length, Allocator.TempJob);
            NativeArray<int> objectsTrianglesOffsetBuffer = new NativeArray<int>(blasArray.Length, Allocator.TempJob);
            for(int i = 0; i < blasArray.Length; i++)
            {
                var rayTraceableComponent = blasArray[i];
                ref BottomLevelAccelerateStructure blas = ref rayTraceableComponent.Value;
                objectsBVHOffsetBuffer[i] = totalBVHNodeCount;
                totalBVHNodeCount += blas.Nodes.Length;

                objectsVerticesOffsetBuffer[i] = totalVerticesCount;
                totalVerticesCount += blas.vertices.Length;

                objectsTrianglesOffsetBuffer[i] = totalTrianglesCount;
                totalTrianglesCount += blas.triangles.Length;
            }
            
            //build BLAS Compute Buffer
            BuildBLASComputeBufferJob buildComputeBufferJob = new BuildBLASComputeBufferJob();
            buildComputeBufferJob.BLASs = blasArray;
            buildComputeBufferJob.blasBVHOffsets = objectsBVHOffsetBuffer;
            PrepareComputeBuffer(ref ObjectsBVHOffsetBuffer, sizeof(int), blasArray.Length);
            ObjectsBVHOffsetBuffer.SetData(objectsBVHOffsetBuffer);
            PrepareComputeBuffer(ref ObjectsBVHBuffer, Marshal.SizeOf<BoundingVolumeHierarchy.Node>(), totalBVHNodeCount);
            buildComputeBufferJob.blasBVHBuffer = new NativeArray<BoundingVolumeHierarchy.Node>(totalBVHNodeCount, Allocator.TempJob);
            
            buildComputeBufferJob.blasVerticesOffsets = objectsVerticesOffsetBuffer;
            PrepareComputeBuffer(ref ObjectsVerticesOffsetBuffer, sizeof(int), blasArray.Length);
            ObjectsVerticesOffsetBuffer.SetData(objectsVerticesOffsetBuffer);
            PrepareComputeBuffer(ref ObjectsVerticesBuffer,Marshal.SizeOf<Vertex>(), totalVerticesCount);
            buildComputeBufferJob.blasVerticesBuffer = new NativeArray<Vertex>(totalVerticesCount, Allocator.TempJob);
            
            buildComputeBufferJob.blasTriangleOffsets = objectsTrianglesOffsetBuffer;
            PrepareComputeBuffer(ref ObjectsTrianglesOffsetBuffer, sizeof(int), blasArray.Length);
            ObjectsTrianglesOffsetBuffer.SetData(objectsTrianglesOffsetBuffer);
            PrepareComputeBuffer(ref ObjectsTrianglesBuffer,Marshal.SizeOf<int3>(), totalTrianglesCount);
            buildComputeBufferJob.blasTriangleBuffer = new NativeArray<int3>(totalTrianglesCount, Allocator.TempJob);
            jobHandle = buildComputeBufferJob.Schedule(blasCount,32, jobHandle);

            BuildPerObjectPropertyComputeBufferJob buildPerObjectPropertyComputeBufferJob =
                new BuildPerObjectPropertyComputeBufferJob();
            buildPerObjectPropertyComputeBufferJob.objectsBVHIndexMap = objectsBVHMap;
            PrepareComputeBuffer(ref ObjectsPropertyBuffer, Marshal.SizeOf<PerObjectProperty>(), rayTraceableMaterials.Length);
            buildPerObjectPropertyComputeBufferJob.objectsProperty = new NativeArray<PerObjectProperty>(rayTraceableMaterials.Length, Allocator.TempJob);
            buildPerObjectPropertyComputeBufferJob.ScheduleParallel(rayTraceableQuery, jobHandle).Complete();
            //TLAS
            PrepareComputeBuffer(ref TLASBuffer, Marshal.SizeOf<BoundingVolumeHierarchy.Node>(), TLAS.NodeCount);
            TLASBuffer.SetData(TLAS.Nodes, 0, 0, TLAS.NodeCount);
            //BLAS
            ObjectsBVHBuffer.SetData(buildComputeBufferJob.blasBVHBuffer);
            buildComputeBufferJob.blasBVHBuffer.Dispose();
            ObjectsVerticesBuffer.SetData(buildComputeBufferJob.blasVerticesBuffer);
            buildComputeBufferJob.blasVerticesBuffer.Dispose();
            ObjectsTrianglesBuffer.SetData(buildComputeBufferJob.blasTriangleBuffer);
            buildComputeBufferJob.blasTriangleBuffer.Dispose();
            //Per Object Property
            ObjectsPropertyBuffer.SetData(buildPerObjectPropertyComputeBufferJob.objectsProperty);
            buildPerObjectPropertyComputeBufferJob.objectsProperty.Dispose();
            objectsBVHMap.Dispose();
            
            
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
            DestroyComputeBuffer(ref ObjectsPropertyBuffer);
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
        
        void Execute([EntityIndexInQuery] int entityIndexInQuery, in LocalToWorld transform, in RayTraceableBLASComponent rayTraceable)
        {
            ref BottomLevelAccelerateStructure blas = ref rayTraceable.BLAS.Value;
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
    public struct BuildBLASComputeBufferJob : IJobParallelFor
    {
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<BlobAssetReference<BottomLevelAccelerateStructure>> BLASs;
        //BVH
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasBVHOffsets;
        [NativeDisableContainerSafetyRestriction] public NativeArray<BoundingVolumeHierarchy.Node> blasBVHBuffer;
        //Vertices+Normals
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasVerticesOffsets;
        [NativeDisableContainerSafetyRestriction] public NativeArray<Vertex> blasVerticesBuffer;
        //Indices
        [DeallocateOnJobCompletion] [ReadOnly] public NativeArray<int> blasTriangleOffsets;
        [NativeDisableContainerSafetyRestriction] public NativeArray<int3> blasTriangleBuffer;
        
        public void Execute(int entityIndexInQuery)
        {
            var rayTraceableBLAS = BLASs[entityIndexInQuery];
            ref BottomLevelAccelerateStructure blas = ref rayTraceableBLAS.Value;
        
            //set blas and triangle to array by offset
            int blasOffset = blasBVHOffsets[entityIndexInQuery]; 
            for (int i = 0; i < blas.Nodes.Length; i++)
            {
                blasBVHBuffer[i + blasOffset] = blas.Nodes[i];
            }
        
            int verticesOffset = blasVerticesOffsets[entityIndexInQuery];
            for (int i = 0; i < blas.vertices.Length; i++)
            {
                blasVerticesBuffer[i + verticesOffset] = new Vertex(){position = blas.vertices[i], normal = blas.normals[i]};
            }
        
            int triangleOffset = blasTriangleOffsets[entityIndexInQuery];
            for (int i = 0; i < blas.triangles.Length; i++)
            {
                blasTriangleBuffer[i + triangleOffset] = blas.triangles[i];
            }
        }
    }
    
    [BurstCompile]
    public partial struct BuildPerObjectPropertyComputeBufferJob : IJobEntity
    {
        [ReadOnly] public NativeHashMap<BlobAssetReference<BottomLevelAccelerateStructure>, int> objectsBVHIndexMap;
        //Per Object Properties
        public NativeArray<PerObjectProperty> objectsProperty;
        
        void Execute([EntityIndexInQuery] int entityIndexInQuery, in LocalToWorld transform, 
            in RayTraceableBLASComponent rayTraceableBLAS, in RayTraceableMaterialComponent rayTraceableMaterial)
        {
            BlobAssetReference<BottomLevelAccelerateStructure> blas = rayTraceableBLAS.BLAS;
            objectsBVHIndexMap.TryGetValue(blas, out int blasIndex);
            objectsProperty[entityIndexInQuery] = new PerObjectProperty()
            {
                BLASIndex = blasIndex,
                Material = new RayTraceMaterial()
                {
                    albedo = rayTraceableMaterial.albedo,
                    specular = rayTraceableMaterial.specular,
                    emission = rayTraceableMaterial.emission
                },
                WorldToLocal = math.inverse(transform.Value),
                LocalToWorld = transform.Value,
            };
        }
    }
}