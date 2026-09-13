using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace MadeInArizona
{
    /// <summary>Far depth blur that uses correct linear orthographic depth, injected before URP post processing.</summary>
    public sealed class OrthographicDepthBlurFeature : ScriptableRendererFeature
    {
        Material material;
        BlurPass pass;

        public override void Create()
        {
            if (material == null) {
                var shader = Shader.Find("MadeInArizona/OrthographicDepthBlur");
                if (shader != null) material = CoreUtils.CreateEngineMaterial(shader);
            }
            pass ??= new BlurPass();
        }
        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(material);
            material = null;
        }
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            bool run = material != null && camera != null && camera.orthographic && camera.cameraType == CameraType.Game && DevTuning.Current.depthOfField > .001f;
            if (!run) return;
            pass.renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            pass.ConfigureInput(ScriptableRenderPassInput.Depth);
            pass.Setup(material);
            renderer.EnqueuePass(pass);
        }

        sealed class BlurPass : ScriptableRenderPass
        {
            Material material;

            public BlurPass() { profilingSampler = new ProfilingSampler("Orthographic depth blur"); requiresIntermediateTexture = true; }
            public void Setup(Material source) { material = source; }
            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (material == null) return;
                var resources = frameData.Get<UniversalResourceData>();
                var cameraData = frameData.Get<UniversalCameraData>();
                if (!cameraData.camera.orthographic || !resources.cameraColor.IsValid() || !resources.cameraDepthTexture.IsValid()) return;
                var desc = renderGraph.GetTextureDesc(resources.cameraColor); desc.name = "_ArizonaOrthoDofSource"; desc.clearBuffer = false;
                TextureHandle source = resources.cameraColor, copy = renderGraph.CreateTexture(desc), horizontal = renderGraph.CreateTexture(desc);
                renderGraph.AddBlitPass(source, copy, Vector2.one, Vector2.zero, passName: "Copy orthographic depth blur source");
                AddGaussianPass(renderGraph, copy, horizontal, resources.cameraDepthTexture, 0, "Orthographic depth blur horizontal");
                AddGaussianPass(renderGraph, horizontal, resources.activeColorTexture, resources.cameraDepthTexture, 1, "Orthographic depth blur vertical");
            }
            void AddGaussianPass(RenderGraph graph, TextureHandle source, TextureHandle destination, TextureHandle depth, int shaderPass, string passName)
            {
                using (var builder = graph.AddRasterRenderPass<PassData>(passName, out var data, profilingSampler)) {
                    data.material = material; data.source = source; data.pass = shaderPass;
                    builder.UseTexture(source, AccessFlags.Read); builder.UseTexture(depth, AccessFlags.Read); builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                    builder.SetRenderFunc(static (PassData d, RasterGraphContext context) => Blitter.BlitTexture(context.cmd, d.source, new Vector4(1,1,0,0), d.material, d.pass));
                }
            }
            sealed class PassData { internal Material material; internal TextureHandle source; internal int pass; }
        }
    }
}
