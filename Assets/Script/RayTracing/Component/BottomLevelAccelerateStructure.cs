using Unity.Entities;

namespace Script.RayTracing
{
    public struct BottomLevelAccelerateStructure
    {
        public BlobArray<BoundingVolumeHierarchy.Node> Nodes; // The nodes of the bounding volume

        public Aabb Aabb
        {
            get => Nodes[1].Bounds.GetCompoundAabb();
        }
    }
}