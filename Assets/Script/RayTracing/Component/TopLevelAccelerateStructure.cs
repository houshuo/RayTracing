using System;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;
using Unity.Jobs;

namespace DH2.Algorithm
{
    public struct TopLevelAccelerateStructure : IDisposable
    {
        private NativeArray<BoundingVolumeHierarchy.Node> Nodes; // The nodes of the bounding volume
        private BoundingVolumeHierarchy BoundingVolumeHierarchy;
        
        private int m_BodyCount;
        public int BodyCount
        {
            get { return m_BodyCount; }
            set { m_BodyCount = value; NodeCount = value + BoundingVolumeHierarchy.Constants.MaxNumTreeBranches; }
        }

        private int m_NodeCount;
        private int NodeCount
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
                    
                    BoundingVolumeHierarchy = new BoundingVolumeHierarchy(Nodes);
                }
            }
        }

        private NativeArray<int> m_BranchCount;
        public int BranchCount { get => m_BranchCount[0]; set => m_BranchCount[0] = value; }

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

        public JobHandle ScheduleBuildTree(NativeArray<Aabb> treeNodes, JobHandle deps = new JobHandle())
        {
            BodyCount = treeNodes.Length;
            NativeArray<BoundingVolumeHierarchy.PointAndIndex> pointAndIndex = new NativeArray<BoundingVolumeHierarchy.PointAndIndex>(BodyCount, Allocator.TempJob);
            NativeArray<Aabb> aabbs = new NativeArray<Aabb>(BodyCount, Allocator.TempJob);
            for (int i = 0; i < BodyCount; i++)
            {
                pointAndIndex[i] = new BoundingVolumeHierarchy.PointAndIndex() { Index = i, Position = treeNodes[i].Center };
                aabbs[i] = treeNodes[i];
            }
            
            return BoundingVolumeHierarchy.ScheduleBuildJobs(pointAndIndex, aabbs, 8, deps, NodeCount, m_BranchCount);
        }
    }
}
