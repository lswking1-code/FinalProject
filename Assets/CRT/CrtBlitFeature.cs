using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

public class CrtBlitFeature : ScriptableRendererFeature
{
    [SerializeField] Shader crtShader;
    [SerializeField] RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing;
    [SerializeField] [Range(0f, 0.2f)] float lensValue = 0.04f;
    [SerializeField] [Range(0f, 0.01f)] float rgbSplitOffset = 0.002f;
    [SerializeField] [Range(0f, 1f)] float scanlineIntensity = 0.28f;
    [SerializeField] [Range(2f, 8f)] float scanlinePixelSize = 3f;
    [SerializeField] [Range(0f, 2f)] float vignetteIntensity = 0.35f;

    CrtBlitPass blitPass;

    public override void Create()
    {
        if (crtShader == null)
            crtShader = Shader.Find("Hidden/CrtBlit");

        blitPass = new CrtBlitPass(crtShader);
        blitPass.renderPassEvent = renderPassEvent;
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (crtShader == null)
            crtShader = Shader.Find("Hidden/CrtBlit");

        if (blitPass == null || !blitPass.IsReady)
        {
            if (crtShader != null)
            {
                blitPass = new CrtBlitPass(crtShader);
                blitPass.renderPassEvent = renderPassEvent;
            }
        }

        if (blitPass == null || !blitPass.IsReady)
            return;

        if (renderingData.cameraData.cameraType != CameraType.Game)
            return;

        blitPass.requiresIntermediateTexture = true;
        blitPass.Setup(lensValue, rgbSplitOffset, scanlineIntensity, scanlinePixelSize, vignetteIntensity);
        renderer.EnqueuePass(blitPass);
    }

    protected override void Dispose(bool disposing)
    {
        blitPass?.Dispose();
    }
}

public class CrtBlitPass : ScriptableRenderPass
{
    const string PassName = "CrtBlitPass";
    static readonly int LensValueId = Shader.PropertyToID("_LensValue");
    static readonly int RgbSplitId = Shader.PropertyToID("_RGBSplitOffset");
    static readonly int ScanlineId = Shader.PropertyToID("_ScanlineIntensity");
    static readonly int ScanlineSizeId = Shader.PropertyToID("_ScanlinePixelSize");
    static readonly int VignetteId = Shader.PropertyToID("_VignetteIntensity");
    static readonly Vector4 ScaleBias = new Vector4(1f, 1f, 0f, 0f);

    readonly Material material;
    readonly ProfilingSampler profiler = new ProfilingSampler(PassName);
    RenderTextureDescriptor textureDescriptor;
    RTHandle textureHandle;

    public bool IsReady => material != null;

    public CrtBlitPass(Shader shader)
    {
        if (shader != null)
            material = CoreUtils.CreateEngineMaterial(shader);

        textureDescriptor = new RenderTextureDescriptor(2, 2, RenderTextureFormat.Default, 0);
        requiresIntermediateTexture = true;
        ConfigureInput(ScriptableRenderPassInput.Color);
    }

    public void Setup(float lensValue, float rgbSplitOffset, float scanlineIntensity, float scanlinePixelSize, float vignetteIntensity)
    {
        if (material == null)
            return;

        material.SetFloat(LensValueId, lensValue);
        material.SetFloat(RgbSplitId, rgbSplitOffset);
        material.SetFloat(ScanlineId, scanlineIntensity);
        material.SetFloat(ScanlineSizeId, scanlinePixelSize);
        material.SetFloat(VignetteId, vignetteIntensity);
        requiresIntermediateTexture = true;
    }

    [System.Obsolete]
    public override void Configure(CommandBuffer cmd, RenderTextureDescriptor cameraTextureDescriptor)
    {
        textureDescriptor.width = cameraTextureDescriptor.width;
        textureDescriptor.height = cameraTextureDescriptor.height;
        textureDescriptor.depthBufferBits = 0;
        textureDescriptor.msaaSamples = 1;
        RenderingUtils.ReAllocateIfNeeded(ref textureHandle, textureDescriptor);
    }

    [System.Obsolete]
    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (material == null || renderingData.cameraData.isSceneViewCamera)
            return;

        CommandBuffer cmd = CommandBufferPool.Get(PassName);
        RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;
        Blit(cmd, source, textureHandle);
        Blit(cmd, textureHandle, source, material, 0);
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
    {
        if (material == null)
            return;

        UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
        if (cameraData.isSceneViewCamera)
            return;

        UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
        TextureHandle source = resourceData.activeColorTexture;
        if (!source.IsValid())
            return;

        var destinationDesc = renderGraph.GetTextureDesc(source);
        destinationDesc.name = "CameraColor-CrtBlit";
        destinationDesc.clearBuffer = false;
        destinationDesc.msaaSamples = MSAASamples.None;
        TextureHandle destination = renderGraph.CreateTexture(destinationDesc);

        using (IRasterRenderGraphBuilder builder = renderGraph.AddRasterRenderPass(PassName, out PassData passData, profiler))
        {
            passData.source = source;
            passData.material = material;
            builder.UseTexture(source);
            builder.SetRenderAttachment(destination, 0);
            builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
            {
                Blitter.BlitTexture(context.cmd, data.source, ScaleBias, data.material, 0);
            });
        }

        resourceData.cameraColor = destination;
    }

    public void Dispose()
    {
        CoreUtils.Destroy(material);
        textureHandle?.Release();
    }

    class PassData
    {
        internal TextureHandle source;
        internal Material material;
    }
}
