using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Entities;
using DH2.Algorithm;
using Script.RayTracing;
using Unity.Collections;
using Unity.Mathematics;

public struct RayTracable : IComponentData
{
    public Color albedo;
    public Color specular;
    public Color emission;

    public BlobAssetReference<TriangleMesh> mesh;
    public BlobAssetReference<BottomLevelAccelerateStructure> BLAS;
}
