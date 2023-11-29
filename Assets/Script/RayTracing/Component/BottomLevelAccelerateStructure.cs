using DH2.Algorithm;
using Unity.Entities;

namespace Script.RayTracing
{
    public struct BottomLevelAccelerateStructure
    {
        public BlobArray<BoundingVolumeHierarchy.Node> Nodes; // The nodes of the bounding volume
    }
}