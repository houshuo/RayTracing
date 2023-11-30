using Script.RayTracing;
using Unity.Burst;
using Unity.Burst.Intrinsics;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace Script.RayTracing
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct BuildTopLevelAccelerateStructureSystem : Unity.Entities.ISystem
    {
        private TopLevelAccelerateStructure TLAS;
        
        [Unity.Burst.BurstCompile]
        public void OnCreate(ref Unity.Entities.SystemState state)
        {
            TLAS = new TopLevelAccelerateStructure();
            TLAS.Init();
        }

        [Unity.Burst.BurstCompile]
        public void OnUpdate(ref Unity.Entities.SystemState state)
        {
            NativeArray<Aabb> aabbs = new NativeArray<Aabb>(1, Allocator.TempJob);
            NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndices =
                new NativeArray<BoundingVolumeHierarchy.PointAndIndex>(1, Allocator.TempJob);
            //Query RayTraceable
            EntityQuery rayTraceableQuery = SystemAPI.QueryBuilder()
                .WithAll<RayTraceableComponent, LocalToWorld>()
                .Build();
            var rayTraceables =
                rayTraceableQuery.ToComponentDataArray<RayTraceableComponent>(state.WorldUpdateAllocator);
            //Calculate total nodes and build BLAS offsets
            int totalNodeCount = 0;
            NativeArray<int> blasOffsets = new NativeArray<int>(rayTraceables.Length, Allocator.TempJob);
            for(int i = 0; i < rayTraceables.Length; i++)
            {
                var rayTraceableComponent = rayTraceables[i];
                ref BottomLevelAccelerateStructure blas = ref rayTraceableComponent.BLAS.Value;
                blasOffsets[i] = totalNodeCount;
                totalNodeCount += blas.NodeCount;
            }
            
            //set buildTLASJob Properties
            BuildTLASJob buildTLASJob = new BuildTLASJob();
            buildTLASJob.aabbs = aabbs;
            buildTLASJob.pointAndIndex = pointAndIndices;
            
            
            
            
            state.Dependency = buildTLASJob.ScheduleParallel(rayTraceableQuery, state.Dependency);
            //build TLAS
            state.Dependency = TLAS.ScheduleBuildTree(aabbs, pointAndIndices, state.Dependency);
            //set TLAS to compute shader
        }

        [Unity.Burst.BurstCompile]
        public void OnDestroy(ref Unity.Entities.SystemState state)
        {
            TLAS.Dispose();
        }
    }
    
    [BurstCompile]
    public partial struct BuildTLASJob : IJobEntity
    {
        public NativeArray<Aabb> aabbs;
        public NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndex;
        public readonly NativeArray<int> blasOffsets;
        void Execute([EntityIndexInQuery] int entityIndexInQuery, in LocalToWorld transform, in RayTraceableComponent rayTraceable)
        {
            ref BottomLevelAccelerateStructure blas = ref rayTraceable.BLAS.Value;
            //build aabbs and index for build TLAS
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
            //set blas to array by offset
            int blasOffset = blasOffsets[entityIndexInQuery];
            
        }
    }
}