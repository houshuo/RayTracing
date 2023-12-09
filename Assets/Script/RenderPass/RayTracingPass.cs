using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Mathematics;
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

                var buildAccelerateStructureSystem =
                    World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BuildAccelerateStructureSystem>();
                if (buildAccelerateStructureSystem != null && buildAccelerateStructureSystem.ObjectsCount > 0)
                {
                    // Set the target and dispatch the compute shader
                    Camera camera = renderingData.cameraData.camera;
                    FrameCBuffer cbuffer = new FrameCBuffer()
                    {
                        CameraInverseProjection = camera.projectionMatrix.inverse,
                        CameraToWorld = camera.cameraToWorldMatrix,
                        PixelOffset = new float2(0, 0), //new Vector2(Random.value, Random.value),
                        Seed = UnityEngine.Random.value,
                        ReflectionNum = pass.settings.ReflectionNum
                    };
                    var cBufferArray = pass.FrameConsts.BeginWrite<FrameCBuffer>(0, 1);
                    cBufferArray[0] = cbuffer;
                    pass.FrameConsts.EndWrite<FrameCBuffer>(1);
                    cmd.SetComputeConstantBufferParam(pass.settings.RayTracingShader, "FrameCBuffer", pass.FrameConsts, 0, Marshal.SizeOf<FrameCBuffer>());
                    //skybox
                    cmd.SetComputeTextureParam(pass.settings.RayTracingShader, 0, "_SkyboxTexture", pass.settings.SkyBoxTextures);
                    //TLAS
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_TLAS", buildAccelerateStructureSystem.TLASBuffer);
                    //BLAS
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_BLASOffsets", buildAccelerateStructureSystem.ObjectsBVHOffsetBuffer);
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_BLAS", buildAccelerateStructureSystem.ObjectsBVHBuffer);
                    //Triangle Mesh
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_VerticesOffsets", buildAccelerateStructureSystem.ObjectsVerticesOffsetBuffer);
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_Vertices", buildAccelerateStructureSystem.ObjectsVerticesBuffer);
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_TrianglesOffsets", buildAccelerateStructureSystem.ObjectsTrianglesOffsetBuffer);
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_Triangles", buildAccelerateStructureSystem.ObjectsTrianglesBuffer);
                    //Per Object Property
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_TriMeshMats", buildAccelerateStructureSystem.ObjectsMaterialBuffer);
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_WorldToLocalMatrices", buildAccelerateStructureSystem.ObjectsWorldToLocalBuffer);
                    cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_LocalToWorldMatrices", buildAccelerateStructureSystem.ObjectsLocalToWorldBuffer);
                    //Output
                    cmd.SetComputeTextureParam(pass.settings.RayTracingShader, 0, "Result", ShaderIDs.RayTracingResult); 
                    //Dispatch
                    cmd.DispatchCompute(pass.settings.RayTracingShader, 0, RTWidth, RTHeight, 1);
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
            if(FrameConsts != null)
            {
                FrameConsts.Dispose();
                FrameConsts = null;
            };
        }
    }
}
