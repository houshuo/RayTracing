using System;
using Unity.Collections;
using Unity.Jobs;

namespace Script.RayTracing
{
    public struct TopLevelAccelerateStructure : IDisposable
    {
        public NativeArray<BoundingVolumeHierarchy.Node> Nodes; // The nodes of the bounding volume
        private BoundingVolumeHierarchy bvh;
        
        private int m_BodyCount;
        public int BodyCount
        {
            get { return m_BodyCount; }
            set { m_BodyCount = value; NodeCount = value + BoundingVolumeHierarchy.Constants.MaxNumTreeBranches; }
        }

        private int m_NodeCount;
        public int NodeCount
        {
            get => m_NodeCount;
            set
            {
                m_NodeCount = value;
                if (value > Nodes.Length)
                {
                    if(Nodes.IsCreated) Nodes.Dispose();
                    Nodes = new NativeArray<BoundingVolumeHierarchy.Node>(value, Allocator.Persistent, NativeArrayOptions.UninitializedMemory)
                    {
                        // Always initialize first 2 nodes as empty, to gracefully return from queries on an empty tree
                        [0] = BoundingVolumeHierarchy.Node.Empty,
                        [1] = BoundingVolumeHierarchy.Node.Empty
                    };
                    
                    bvh = new BoundingVolumeHierarchy(Nodes);
                }
            }
        }

        private NativeArray<int> m_BranchCount;
        public int BranchCount { get => m_BranchCount[0]; }

        public void Init()
        {
            m_BodyCount = 0;
            m_BranchCount = new NativeArray<int>(1, Allocator.Persistent, NativeArrayOptions.ClearMemory);
        }
        
        public void Dispose()
        {
            if (Nodes.IsCreated)
            {
                Nodes.Dispose();
            }
            
            if (m_BranchCount.IsCreated)
            {
                m_BranchCount.Dispose();
            }
        }

        public JobHandle ScheduleBuildTree(NativeArray<Aabb> aabbs, NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndex, JobHandle deps = new JobHandle())
        {
            BodyCount = aabbs.Length;
            return bvh.ScheduleBuildJobs(pointAndIndex, aabbs, 8, deps, NodeCount, m_BranchCount);
        }
    }
}
