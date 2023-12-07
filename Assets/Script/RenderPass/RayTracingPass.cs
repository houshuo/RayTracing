using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Script.RayTracing
{
    public class RayTracingPass : ScriptableRendererFeature
    {
        static class ShaderIDs
        {
            internal static readonly int GoldenRot = Shader.PropertyToID("_GoldenRot");
            internal static readonly int Params = Shader.PropertyToID("_Params");
            internal static readonly int RayTracingResult = Shader.PropertyToID("RayTracingResult");
        }
        //settings
        [System.Serializable]
        public class RayTraceSettings
        {
            public int ReflectionNum;
            public int randomSeed;
            public ComputeShader RayTracingShader;
            public Texture SkyBoxTextures;
            public float RTDownScaling;
        }
        public RayTraceSettings settings = new RayTraceSettings();
        //Constant Buffer
        private ComputeBuffer FrameConsts;
        //PerObjects BVH
        private ComputeBuffer ObjectsBVHOffsetBuffer;
        private ComputeBuffer ObjectsBVHBuffer;
        //PerObjects Vertices
        private ComputeBuffer ObjectsVerticesOffsetBuffer;
        private ComputeBuffer ObjectsVerticesBuffer;
        //PerObjects Triangle Indices
        private ComputeBuffer ObjectsTrianglesOffsetBuffer;
        private ComputeBuffer ObjectsTrianglesBuffer;
        //PerObjects Properties
        private ComputeBuffer ObjectsMaterialBuffer;
        private ComputeBuffer ObjectsWorldToLocalBuffer;
        private ComputeBuffer ObjectsLocalToWorldBuffer;
        //TLAS Buffer
        private ComputeBuffer TLASBuffer;

        class CustomRenderPass : ScriptableRenderPass
        {
            public RayTracingPass pass;
            private string profilerTag;
            private int RTWidth, RTHeight;
            
            private RenderTargetIdentifier source { get; set; }

            public void Setup(RenderTargetIdentifier source) {
                this.source = source;
            }

            public CustomRenderPass(string profilerTag)
            {
                this.profilerTag = profilerTag;
            }

            public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
            {
                RTWidth = (int)(cameraTextureDescriptor.width / pass.settings.RTDownScaling);
                RTHeight = (int)(cameraTextureDescriptor.height / pass.settings.RTDownScaling);
                cmd.GetTemporaryRT(ShaderIDs.RayTracingResult, RTWidth, RTHeight, 0, FilterMode.Bilinear, RenderTextureFormat.Default, RenderTextureReadWrite.Default, 1, true);
                cmd.ClearRandomWriteTargets();
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
                var buildAccelerateStructureSystemHandle = World.DefaultGameObjectInjectionWorld.GetExistingSystem<BuildAccelerateStructureSystem>();
                if (buildAccelerateStructureSystemHandle != SystemHandle.Null)
                {
                    var buildAccelerateStructureSystem =
                        World.DefaultGameObjectInjectionWorld.Unmanaged.GetUnsafeSystemRef<BuildAccelerateStructureSystem>(
                            buildAccelerateStructureSystemHandle);
                    if (buildAccelerateStructureSystem.ObjectsCount > 0)
                    {
                        // Set the target and dispatch the compute shader
                        Camera camera = renderingData.cameraData.camera;
                        FrameCBuffer cbuffer = new FrameCBuffer()
                        {
                            CameraInverseProjection = camera.projectionMatrix.inverse,
                            CameraToWorld = camera.cameraToWorldMatrix,
                            PixelOffset = new Vector2(Random.value, Random.value),
                            Seed = Random.value,
                            ReflectionNum = pass.settings.ReflectionNum
                        };
                        var cBufferArray = pass.FrameConsts.BeginWrite<FrameCBuffer>(0, 1);
                        cBufferArray[0] = cbuffer;
                        pass.FrameConsts.EndWrite<FrameCBuffer>(1);
                        cmd.SetComputeConstantBufferParam(pass.settings.RayTracingShader, "FrameCBuffer", pass.FrameConsts, 0, Marshal.SizeOf<FrameCBuffer>());
                        //skybox
                        cmd.SetComputeTextureParam(pass.settings.RayTracingShader, 0, "_SkyboxTexture", pass.settings.SkyBoxTextures);
                        //TLAS
                        PrepareComputeBuffer(ref pass.TLASBuffer, buildAccelerateStructureSystem.TLASBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_TLAS", pass.TLASBuffer);
                        //BLAS
                        PrepareComputeBuffer(ref pass.ObjectsBVHOffsetBuffer, buildAccelerateStructureSystem.ObjectsBVHOffsetBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_BLASOffsets", pass.ObjectsBVHOffsetBuffer);
                        PrepareComputeBuffer(ref pass.ObjectsBVHBuffer, buildAccelerateStructureSystem.ObjectsBVHBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_BLAS", pass.ObjectsBVHBuffer);
                        //Triangle Mesh
                        PrepareComputeBuffer(ref pass.ObjectsVerticesOffsetBuffer, buildAccelerateStructureSystem.ObjectsVerticesOffsetBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_VerticesOffsets", pass.ObjectsVerticesOffsetBuffer);
                        PrepareComputeBuffer(ref pass.ObjectsVerticesBuffer, buildAccelerateStructureSystem.ObjectsVerticesBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_Vertices", pass.ObjectsVerticesBuffer);
                        PrepareComputeBuffer(ref pass.ObjectsTrianglesOffsetBuffer, buildAccelerateStructureSystem.ObjectsTrianglesOffsetBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_TrianglesOffsets", pass.ObjectsTrianglesOffsetBuffer);
                        PrepareComputeBuffer(ref pass.ObjectsTrianglesBuffer, buildAccelerateStructureSystem.ObjectsTrianglesBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_Triangles", pass.ObjectsTrianglesBuffer);
                        //Per Object Property
                        PrepareComputeBuffer(ref pass.ObjectsMaterialBuffer, buildAccelerateStructureSystem.ObjectsMaterialBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_TriMeshMats", pass.ObjectsMaterialBuffer);
                        PrepareComputeBuffer(ref pass.ObjectsWorldToLocalBuffer, buildAccelerateStructureSystem.ObjectsWorldToLocalBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_WorldToLocalMatrices", pass.ObjectsWorldToLocalBuffer);
                        PrepareComputeBuffer(ref pass.ObjectsLocalToWorldBuffer, buildAccelerateStructureSystem.ObjectsLocalToWorldBuffer);
                        cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_LocalToWorldMatrices", pass.ObjectsLocalToWorldBuffer);
                        //Output
                        cmd.SetComputeTextureParam(pass.settings.RayTracingShader, 0, "Result", ShaderIDs.RayTracingResult); 
                        //Dispatch
                        cmd.DispatchCompute(pass.settings.RayTracingShader, 0, RTWidth, RTHeight, 1);
                    }
                }
                
                cmd.Blit(ShaderIDs.RayTracingResult, source); 
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            public override void FrameCleanup(CommandBuffer cmd)
            {
                cmd.ReleaseTemporaryRT(ShaderIDs.RayTracingResult);
            }
        }

        CustomRenderPass scriptablePass;

        public override void Create()
        {
            scriptablePass = new CustomRenderPass("RayTracePass");
            scriptablePass.pass = this;
            scriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
            FrameConsts = new ComputeBuffer(1, Marshal.SizeOf<FrameCBuffer>(), ComputeBufferType.Constant, ComputeBufferMode.SubUpdates);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(scriptablePass);
        }
        
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            scriptablePass.Setup(renderer.cameraColorTarget);  // use of target after allocation
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) return;
            DestroyComputeBuffer(ref ObjectsBVHOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsBVHBuffer);
            DestroyComputeBuffer(ref ObjectsVerticesOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsVerticesBuffer);
            DestroyComputeBuffer(ref ObjectsTrianglesOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsTrianglesBuffer);
            DestroyComputeBuffer(ref ObjectsMaterialBuffer);
            DestroyComputeBuffer(ref ObjectsWorldToLocalBuffer);
            DestroyComputeBuffer(ref ObjectsLocalToWorldBuffer);
            DestroyComputeBuffer(ref TLASBuffer);
            DestroyComputeBuffer(ref FrameConsts);
        }
        
        private static void PrepareComputeBuffer<T>(ref ComputeBuffer buffer, in NativeArray<T> array) where T : struct
        {
            if (buffer != null && (buffer.stride != Marshal.SizeOf<T>() || buffer.count < array.Length))
            {
                buffer.Dispose();
                buffer = null;
            }
            
            if (buffer == null)
            {
                buffer = new ComputeBuffer(array.Length, Marshal.SizeOf<T>(), ComputeBufferType.Structured, ComputeBufferMode.SubUpdates);
            }
            
            var bufferArray = buffer.BeginWrite<T>(0, array.Length);
            NativeArray<T>.Copy(array, bufferArray, array.Length);
            buffer.EndWrite<T>(array.Length);
        }

        private static void DestroyComputeBuffer(ref ComputeBuffer buffer)
        {
            if (buffer == null)
                return;
            buffer.Dispose();
            buffer = null;
        }
    }
}
