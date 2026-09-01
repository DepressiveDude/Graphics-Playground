using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif


public class LKPJRenderPipeline : RenderPipeline
{
    private LKPJRenderPipelineAsset renderPipelineAsset;

    private SimpleRayTracingPass rayTracing;

    private RenderTexture rayTracingOutput;


    public LKPJRenderPipeline(LKPJRenderPipelineAsset asset)
    {
        renderPipelineAsset = asset;

        rayTracing = new SimpleRayTracingPass(
            "Assets/LKPJRenderPipeline/LightVisibility.urtshader");
    }


    protected override void Render(
        ScriptableRenderContext context,
        List<Camera> cameras)
    {
        foreach (Camera camera in cameras)
        {
            
            // Cull
            

            if (!camera.TryGetCullingParameters(
                    out var cullingParameters))
            {
                continue;
            }

            CullingResults cullingResults =
                context.Cull(ref cullingParameters);


            
            // Setup camera
            

            context.SetupCameraProperties(camera);


            
            // Clear
            

            CommandBuffer clearCmd =
                CommandBufferPool.Get("Clear");

            clearCmd.ClearRenderTarget(
                true,
                true,
                Color.clear);

            context.ExecuteCommandBuffer(clearCmd);

            CommandBufferPool.Release(clearCmd);


            
            // Rasturize
            

            ShaderTagId shaderTagId =
                new ShaderTagId("ExampleLightModeTag");

            SortingSettings sortingSettings =
                new SortingSettings(camera);

            DrawingSettings drawingSettings =
                new DrawingSettings(
                    shaderTagId,
                    sortingSettings);

            FilteringSettings filteringSettings =
                FilteringSettings.defaultValue;


            context.DrawRenderers(
                cullingResults,
                ref drawingSettings,
                ref filteringSettings);


            
            // Ray tracing
            //
            // This ultimately replaces the camera image with
            // the ray traced red/black visibility image.
            

            EnsureRayTracingOutput(
                camera.pixelWidth,
                camera.pixelHeight);


            rayTracing.Render(
                context,
                camera,
                cullingResults,
                rayTracingOutput);


            
            // Put the ray traced image onto the camera.
            

            CommandBuffer blitCmd =
                CommandBufferPool.Get(
                    "Ray Tracing Output");

            blitCmd.Blit(
                rayTracingOutput,
                BuiltinRenderTextureType.CameraTarget);

            context.ExecuteCommandBuffer(
                blitCmd);

            CommandBufferPool.Release(
                blitCmd);

            if (camera.clearFlags == CameraClearFlags.Skybox &&
                RenderSettings.skybox != null)
            {
                context.DrawSkybox(camera);
            }


            context.Submit();
        }
    }


    private void EnsureRayTracingOutput(
        int width,
        int height)
    {
        width =
            Mathf.Max(width, 1);

        height =
            Mathf.Max(height, 1);


        if (rayTracingOutput != null &&
            rayTracingOutput.width == width &&
            rayTracingOutput.height == height)
        {
            return;
        }


        if (rayTracingOutput != null)
        {
            rayTracingOutput.Release();

#if UNITY_EDITOR
            Object.DestroyImmediate(
                rayTracingOutput);
#else
            Object.Destroy(
                rayTracingOutput);
#endif
        }


        rayTracingOutput =
            new RenderTexture(
                width,
                height,
                0,
                RenderTextureFormat.DefaultHDR);

        rayTracingOutput.name =
            "Ray Traced Visibility";

        // Ray tracing shader writes directly into this texture.
        rayTracingOutput.enableRandomWrite =
            true;

        rayTracingOutput.Create();
    }


    protected override void Dispose(bool disposing)
    {
        rayTracing?.Dispose();


        if (rayTracingOutput != null)
        {
            rayTracingOutput.Release();

#if UNITY_EDITOR
            Object.DestroyImmediate(
                rayTracingOutput);
#else
            Object.Destroy(
                rayTracingOutput);
#endif

            rayTracingOutput =
                null;
        }


        base.Dispose(disposing);
    }
}