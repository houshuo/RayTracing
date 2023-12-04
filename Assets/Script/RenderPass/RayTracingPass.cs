using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
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
            public int randomSeed;
            public ComputeShader RayTracingShader;
            public Texture SkyBoxTextures;
            public bool SkyboxEnabled = true;
            
            public int MaxReflections = 2;
            public int maxReflectionsLocked = 4;
            public int maxReflectionsUnlocked = 2;

            public float RTDownScaling;
        }
        public RayTraceSettings settings = new RayTraceSettings();
        //PerObjects BVH
        private ComputeBuffer ObjectsBVHOffsetBuffer;
        private ComputeBuffer ObjectsBVHBuffer;
        //PerObjects Vertices
        private ComputeBuffer ObjectsVerticesOffsetBuffer;
        private ComputeBuffer ObjectsVerticesBuffer;
        //PerObjects Normals
        private ComputeBuffer ObjectsNormalsOffsetBuffer;
        private ComputeBuffer ObjectsNormalsBuffer;
        //PerObjects Triangle Indices
        private ComputeBuffer ObjectsTrianglesOffsetBuffer;
        private ComputeBuffer ObjectsTrianglesBuffer;
        //PerObjects Properties
        private ComputeBuffer ObjectsMaterialBuffer;
        private ComputeBuffer ObjectsWorldToLocalBuffer;
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
                cmd.SetComputeTextureParam(pass.settings.RayTracingShader, 0, "_SkyboxTexture", pass.settings.SkyBoxTextures);
                PrepareComputeBuffer(ref pass.TLASBuffer, buildAccelerateStructureSystem.TLASBuffer);
                cmd.SetComputeBufferParam(pass.settings.RayTracingShader, 0, "_TALSBuffer", pass.TLASBuffer);
            }
            
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var buildAccelerateStructureSystemHandle = World.DefaultGameObjectInjectionWorld.GetExistingSystem<BuildAccelerateStructureSystem>();
                if (buildAccelerateStructureSystemHandle == SystemHandle.Null)
                    return;
                var buildAccelerateStructureSystem =
                    World.DefaultGameObjectInjectionWorld.Unmanaged.GetUnsafeSystemRef<BuildAccelerateStructureSystem>(
                        buildAccelerateStructureSystemHandle);
                if (buildAccelerateStructureSystem.EmptyScene)
                    return;
                
                CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
                // Set the target and dispatch the compute shader
                SetShaderParametersPerUpdate(buildAccelerateStructureSystem, cmd);
                cmd.SetComputeTextureParam(pass.settings.RayTracingShader, 0, "Result", ShaderIDs.RayTracingResult); 
                // cmd.DispatchCompute(settings.RayTracingShader, 0, RTWidth, RTHeight, 1);
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
            scriptablePass.pass = this;
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

        protected override void Dispose(bool disposing)
        {
            if (disposing) return;
            DestroyComputeBuffer(ref ObjectsBVHOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsBVHBuffer);
            DestroyComputeBuffer(ref ObjectsVerticesOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsVerticesBuffer);
            DestroyComputeBuffer(ref ObjectsNormalsOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsNormalsBuffer);
            DestroyComputeBuffer(ref ObjectsTrianglesOffsetBuffer);
            DestroyComputeBuffer(ref ObjectsTrianglesBuffer);
            DestroyComputeBuffer(ref ObjectsMaterialBuffer);
            DestroyComputeBuffer(ref ObjectsWorldToLocalBuffer);
            DestroyComputeBuffer(ref TLASBuffer);
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
