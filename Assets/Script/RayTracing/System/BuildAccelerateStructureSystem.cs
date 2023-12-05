using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;

namespace Script.RayTracing
{
    [UpdateInGroup(typeof(PresentationSystemGroup))]
    public partial struct BuildAccelerateStructureSystem : ISystem
    {
        public bool EmptyScene ;
        //TLAS
        private TopLevelAccelerateStructure TLAS;

        public NativeArray<BoundingVolumeHierarchy.Node> TLASBuffer
        {
            get { return TLAS.Nodes; }
        }
        //PerObjects BVH
        public NativeArray<int> ObjectsBVHOffsetBuffer;
        public NativeArray<BoundingVolumeHierarchy.Node> ObjectsBVHBuffer;
        //PerObjects Vertices
        public NativeArray<int> ObjectsVerticesOffsetBuffer;
        public NativeArray<float3> ObjectsVerticesBuffer;
        public NativeArray<float3> ObjectsNormalsBuffer;
        //PerObjects Triangle Indices
        public NativeArray<int> ObjectsTrianglesOffsetBuffer;
        public NativeArray<int3> ObjectsTrianglesBuffer;
        //PerObjects Properties
        public NativeArray<RayTraceMaterial> ObjectsMaterialBuffer;
        public NativeArray<float4x4> ObjectsWorldToLocalBuffer;
        
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            TLAS = new TopLevelAccelerateStructure();
            TLAS.Init();
        }
        
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            //Query RayTraceable
            EntityQuery rayTraceableQuery = SystemAPI.QueryBuilder()
                .WithAll<RayTraceableComponent, LocalToWorld>()
                .Build();
            var rayTraceables =
                rayTraceableQuery.ToComponentDataArray<RayTraceableComponent>(state.WorldUpdateAllocator);

            EmptyScene = rayTraceables.Length == 0;
            if (EmptyScene)
                return;
            
            //Calculate total nodes and build BLAS offsets
            int totalBVHNodeCount = 0;
            int totalVerticesCount = 0;
            int totalTrianglesCount = 0;
            PrepareNativeArray(ref ObjectsBVHOffsetBuffer, rayTraceables.Length);
            PrepareNativeArray(ref ObjectsVerticesOffsetBuffer, rayTraceables.Length);
            PrepareNativeArray(ref ObjectsTrianglesOffsetBuffer, rayTraceables.Length);
            for(int i = 0; i < rayTraceables.Length; i++)
            {
                var rayTraceableComponent = rayTraceables[i];
                ref BottomLevelAccelerateStructure blas = ref rayTraceableComponent.BLAS.Value;
                ref TriangleMesh mesh = ref rayTraceableComponent.mesh.Value;
                ObjectsBVHOffsetBuffer[i] = totalBVHNodeCount;
                totalBVHNodeCount += blas.Nodes.Length;

                ObjectsVerticesOffsetBuffer[i] = totalVerticesCount;
                totalVerticesCount += mesh.vertices.Length;

                ObjectsTrianglesOffsetBuffer[i] = totalTrianglesCount;
                totalTrianglesCount += mesh.triangles.Length;
            }
            
            //set buildTLASJob Properties
            NativeArray<Aabb> aabbs = new NativeArray<Aabb>(rayTraceables.Length, Allocator.TempJob);
            NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndices =
                new NativeArray<BoundingVolumeHierarchy.PointAndIndex>(rayTraceables.Length, Allocator.TempJob);
            BuildTLASJob buildTLASJob = new BuildTLASJob();
            buildTLASJob.aabbs = aabbs;
            buildTLASJob.pointAndIndex = pointAndIndices;
            
            buildTLASJob.blasBVHOffsets = ObjectsBVHOffsetBuffer;
            PrepareNativeArray(ref ObjectsBVHBuffer, totalBVHNodeCount);
            buildTLASJob.blasBVHArray = ObjectsBVHBuffer;
            
            buildTLASJob.blasVerticesOffsets = ObjectsVerticesOffsetBuffer;
            PrepareNativeArray(ref ObjectsVerticesBuffer, totalVerticesCount);
            buildTLASJob.blasVerticesArray = ObjectsVerticesBuffer;
            buildTLASJob.blasNormalsArray = ObjectsNormalsBuffer;
            
            buildTLASJob.blasTriangleOffsets = ObjectsTrianglesOffsetBuffer;
            PrepareNativeArray(ref ObjectsTrianglesBuffer, totalTrianglesCount);
            buildTLASJob.blasTriangleArray = ObjectsTrianglesBuffer;
            
            PrepareNativeArray(ref ObjectsMaterialBuffer, rayTraceables.Length);
            buildTLASJob.blasMaterials = ObjectsMaterialBuffer;
            PrepareNativeArray(ref ObjectsWorldToLocalBuffer, rayTraceables.Length);
            buildTLASJob.blasWorldToLocal = ObjectsWorldToLocalBuffer;
            var jobHandle = buildTLASJob.ScheduleParallel(rayTraceableQuery, new JobHandle());
            //build TLAS
            TLAS.ScheduleBuildTree(aabbs, pointAndIndices, jobHandle).Complete();
        }

        [BurstCompile]
        public void OnDestroy(ref SystemState state)
        {
            TLAS.Dispose();
            DestroyNativeArray(ref ObjectsBVHOffsetBuffer);
            DestroyNativeArray(ref ObjectsBVHBuffer);
            DestroyNativeArray(ref ObjectsVerticesOffsetBuffer);
            DestroyNativeArray(ref ObjectsVerticesBuffer);
            DestroyNativeArray(ref ObjectsNormalsBuffer);
            DestroyNativeArray(ref ObjectsTrianglesOffsetBuffer);
            DestroyNativeArray(ref ObjectsTrianglesBuffer);
            DestroyNativeArray(ref ObjectsMaterialBuffer);
            DestroyNativeArray(ref ObjectsWorldToLocalBuffer);
        }

        private static void PrepareNativeArray<T>(ref NativeArray<T> buffer, int count) where T : struct
        {
            if (buffer != null && buffer.Length < count)
            {
                buffer.Dispose();
            }
            
            if (!buffer.IsCreated)
            {
                buffer = new NativeArray<T>(count, Allocator.Persistent);
            }
        }

        private static void DestroyNativeArray<T>(ref NativeArray<T> buffer) where T : struct
        {
            if (!buffer.IsCreated)
                return;
            buffer.Dispose();
        }
    }
    
    [BurstCompile]
    public partial struct BuildTLASJob : IJobEntity
    {
        public NativeArray<Aabb> aabbs;
        public NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndex;
        //BVH
        [ReadOnly] public NativeArray<int> blasBVHOffsets;
        [NativeDisableContainerSafetyRestriction] public NativeArray<BoundingVolumeHierarchy.Node> blasBVHArray;
        //Vertices+Normals
        [ReadOnly] public NativeArray<int> blasVerticesOffsets;
        [NativeDisableContainerSafetyRestriction] public NativeArray<float3> blasVerticesArray;
        [NativeDisableContainerSafetyRestriction] public NativeArray<float3> blasNormalsArray;
        //Indices
        [ReadOnly] public NativeArray<int> blasTriangleOffsets;
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
            int blasOffset = blasBVHOffsets[entityIndexInQuery];
            for (int i = 0; i < blas.Nodes.Length; i++)
            {
                blasBVHArray[i + blasOffset] = blas.Nodes[i];
            }
            
            int verticesOffset = blasVerticesOffsets[entityIndexInQuery];
            for (int i = 0; i < mesh.vertices.Length; i++)
            {
                blasVerticesArray[i + verticesOffset] = mesh.vertices[i];
            }
            
            for (int i = 0; i < mesh.normals.Length; i++)
            {
                blasNormalsArray[i + verticesOffset] = mesh.normals[i];
            }
            
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