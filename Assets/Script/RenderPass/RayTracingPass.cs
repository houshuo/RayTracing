using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Script.RayTracing
{
    public class RayTracingPass : ScriptableRendererFeature
    {
        [System.Serializable]
        public class RayTraceSettings
        {
            public int randomSeed;
            public ComputeShader RayTracingShader;
            public Texture[] SkyBoxTextures;
            public bool SkyboxEnabled = true;
            
            public int MaxReflections = 2;
            public int maxReflectionsLocked = 4;
            public int maxReflectionsUnlocked = 2;

            public float RTDownScaling;
        }

        static class ShaderIDs
        {
            internal static readonly int GoldenRot = Shader.PropertyToID("_GoldenRot");
            internal static readonly int Params = Shader.PropertyToID("_Params");
            internal static readonly int RayTracingResult = Shader.PropertyToID("RayTracingResult");
        }

        public RayTraceSettings settings = new RayTraceSettings();

        class CustomRenderPass : ScriptableRenderPass
        {
            public RayTraceSettings settings;
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
                RTWidth = (int)(cameraTextureDescriptor.width / settings.RTDownScaling);
                RTHeight = (int)(cameraTextureDescriptor.height / settings.RTDownScaling);
                cmd.GetTemporaryRT(ShaderIDs.RayTracingResult, RTWidth, RTHeight, 0, FilterMode.Bilinear);
            }

            private void SetShaderParametersPerUpdate(BuildAccelerateStructureSystem buildAccelerateStructureSystem, CommandBuffer cmd)
            {
                // RayTracingShader.SetMatrix("_CameraToWorld", _camera.cameraToWorldMatrix);
                // RayTracingShader.SetMatrix("_CameraInverseProjection", _camera.projectionMatrix.inverse);
                // RayTracingShader.SetVector("_PixelOffset", new Vector2(Random.value, Random.value));
                // RayTracingShader.SetInt("_MaxReflections", MaxReflections);
                //
                // // lighting params
                // Vector3 l = directionalLight.transform.forward;
                // RayTracingShader.SetVector(
                //     "_DirectionalLight", new Vector4(l.x, l.y, l.z, directionalLight.intensity)
                // );
                //
                // // ground plane
                // RayTracingShader.SetVector("_GroundAlbedo", ColorToVec3(GroundAlbedo));
                // RayTracingShader.SetVector("_GroundSpecular", ColorToVec3(GroundSpecular));
                // RayTracingShader.SetVector("_GroundEmission", ColorToVec3(GroundEmission));
                //
                // // rng
                // RayTracingShader.SetFloat("_Seed", Random.value);
                //
                // // tri mesh mats
                // // update every frame to allow for hot reloading of material
                // UpdateTriMeshMats();
                
                cmd.SetComputeBufferParam(settings.RayTracingShader, 0, "_TALSBuffer", buildAccelerateStructureSystem.TLASBuffer);
            }
            
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var buildAccelerateStructureSystem = World.DefaultGameObjectInjectionWorld.GetExistingSystemManaged<BuildAccelerateStructureSystem>();
                if (buildAccelerateStructureSystem == null)
                    return;
                
                CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
                // Set the target and dispatch the compute shader
                SetShaderParametersPerUpdate(buildAccelerateStructureSystem, cmd);
                cmd.SetComputeTextureParam(settings.RayTracingShader, 0, "Result", ShaderIDs.RayTracingResult); 
                cmd.DispatchCompute(settings.RayTracingShader, 0, RTWidth, RTHeight, 1);
                // use _converged because destination is not destination is not HDR texture
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
            scriptablePass.settings = settings;
            scriptablePass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            renderer.EnqueuePass(scriptablePass);
        }
        
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            scriptablePass.Setup(renderer.cameraColorTarget);  // use of target after allocation
        }
    }
}
