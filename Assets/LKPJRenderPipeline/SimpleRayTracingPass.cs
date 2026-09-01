using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.UnifiedRayTracing;

using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

public class SimpleRayTracingPass
{
    // Must match in shader
    private const uint RayMask = 0xFF;
    private const int MaxPointLights = 128;

    // donno what this is for tbh
    private RayTracingResources resources;
    private RayTracingContext rayTracingContext;

    private IRayTracingShader shader;
    private IRayTracingAccelStruct accelerationStructure;

    // CPU side data
    private readonly List<Vector3> vertices =
        new List<Vector3>();

    private readonly List<int> indices =
        new List<int>();


    // 1 geometric world space normal per triangle
    private readonly List<Vector4> triangleNormals =
        new List<Vector4>();


    // instanceID -> first triangle for that RT instance.
    private readonly List<uint> triangleBases =
        new List<uint>();


    // xyz = position
    // w   = range
    private readonly List<Vector4> pointLights =
        new List<Vector4>(MaxPointLights);

    // BS definitions that I need ;(
    private static readonly int RenderWidth =
        Shader.PropertyToID("_RenderWidth");

    private static readonly int RenderHeight =
        Shader.PropertyToID("_RenderHeight");

    private static readonly int CameraFrustum =
        Shader.PropertyToID("_CameraFrustum");

    private static readonly int CameraToWorld =
        Shader.PropertyToID("_CameraToWorldMatrix");

    private static readonly int OutputTexture =
        Shader.PropertyToID("_OutputTexture");

    private static readonly int TriangleNormals =
        Shader.PropertyToID("_TriangleNormals");

    private static readonly int InstanceTriangleBase =
        Shader.PropertyToID("_InstanceTriangleBase");

    private static readonly int PointLights =
        Shader.PropertyToID("_PointLights");

    private static readonly int PointLightCount =
        Shader.PropertyToID("_PointLightCount");

    // Constructor
    public SimpleRayTracingPass(string shaderPath)
    {
#if UNITY_EDITOR
        // RT code I copied from documentation
        resources =
            new RayTracingResources();
        resources.Load();

        RayTracingBackend backend =
            RayTracingContext.IsBackendSupported(
                RayTracingBackend.Hardware)
            ? RayTracingBackend.Hardware
            : RayTracingBackend.Compute;

        // Steven handjobs don't have hardware RT acceleration lmao
        Debug.Log(
            $"Unified RT backend: {backend}");


        rayTracingContext =
            new RayTracingContext(
                backend,
                resources);

        UnityEngine.Object shaderAsset =
            LoadShader(
                shaderPath,
                backend);


        if (shaderAsset == null)
        {
            Debug.LogError(
                $"Could not load Unified RT shader: {shaderPath}");

            return;
        }

        shader =
            rayTracingContext.CreateRayTracingShader(
                shaderAsset);

        accelerationStructure =
            rayTracingContext.CreateAccelerationStructure(
                new AccelerationStructureOptions
                {
                    buildFlags =
                        BuildFlags.PreferFastBuild
                });

#endif
    }

    // some normal render code to put stuff on screen
    public void Render(
        ScriptableRenderContext renderContext,
        Camera camera,
        CullingResults cullingResults,
        RenderTexture output)
    {
        if (shader == null ||
            accelerationStructure == null ||
            output == null)
        {
            renderContext.Submit();
            return;
        }


        int width =
            Mathf.Max(output.width, 1);

        int height =
            Mathf.Max(output.height, 1);


        // one line costing a LOT of performance lmao
        BuildScene();


        if (triangleNormals.Count == 0)
        {
            renderContext.Submit();
            return;
        }

        BuildPointLights(
            cullingResults);

        GraphicsBuffer normalBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                triangleNormals.Count,
                sizeof(float) * 4);

        normalBuffer.SetData(
            triangleNormals);

        GraphicsBuffer triangleBaseBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                triangleBases.Count,
                sizeof(uint));

        triangleBaseBuffer.SetData(
            triangleBases);

        GraphicsBuffer lightBuffer =
            new GraphicsBuffer(
                GraphicsBuffer.Target.Structured,
                MaxPointLights,
                sizeof(float) * 4);

        if (pointLights.Count > 0)
        {
            lightBuffer.SetData(
                pointLights,
                0,
                0,
                pointLights.Count);
        }

        GraphicsBuffer scratch =
            RayTracingHelper
                .CreateScratchBufferForBuildAndDispatch(
                    accelerationStructure,
                    shader,
                    (uint)width,
                    (uint)height,
                    1);

        CommandBuffer cmd =
            CommandBufferPool.Get(
                "Simple Ray Tracing");

        // I think this is so unoptimized it probably cost 50x the actual ray tracing cost I gotta stop doing ts
        accelerationStructure.Build(
            cmd,
            scratch);

        // Assign a whole buncha shi so the shader can work
        shader.SetAccelerationStructure(
            cmd,
            "_AccelStruct",
            accelerationStructure);

        shader.SetBufferParam(
            cmd,
            TriangleNormals,
            normalBuffer);

        shader.SetBufferParam(
            cmd,
            InstanceTriangleBase,
            triangleBaseBuffer);

        shader.SetBufferParam(
            cmd,
            PointLights,
            lightBuffer);

        shader.SetIntParam(
            cmd,
            PointLightCount,
            pointLights.Count);

        shader.SetIntParam(
            cmd,
            RenderWidth,
            width);

        shader.SetIntParam(
            cmd,
            RenderHeight,
            height);

        shader.SetVectorParam(
            cmd,
            CameraFrustum,
            GetCameraFrustum(camera));

        shader.SetMatrixParam(
            cmd,
            CameraToWorld,
            camera.cameraToWorldMatrix);

        shader.SetTextureParam(
            cmd,
            OutputTexture,
            output);
        
        // TRACE THEM RAYS!!!!!
        shader.Dispatch(
            cmd,
            scratch,
            (uint)width,
            (uint)height,
            1);


        renderContext.ExecuteCommandBuffer(cmd);

        CommandBufferPool.Release(cmd);


        // Actually issue GPU work before destroying temporary buffers.
        renderContext.Submit();

        // get rid of everything I js baked which is kinda dumb
        scratch?.Dispose();

        normalBuffer.Dispose();

        triangleBaseBuffer.Dispose();

        lightBuffer.Dispose();
    }

    private void BuildScene()
    {
        accelerationStructure.ClearInstances();

        triangleNormals.Clear();

        triangleBases.Clear();


        MeshRenderer[] renderers =
            Object.FindObjectsByType<MeshRenderer>(
                FindObjectsSortMode.None);

        // For each object process it to be added to the ray tracing acceleration structure
        foreach (MeshRenderer renderer in renderers)
        {
            if (!renderer.enabled ||
                !renderer.gameObject.activeInHierarchy)
            {
                continue;
            }


            MeshFilter filter =
                renderer.GetComponent<MeshFilter>();

            if (filter == null)
                continue;


            Mesh mesh =
                filter.sharedMesh;

            if (mesh == null)
                continue;


            AddMesh(
                mesh,
                renderer.localToWorldMatrix);
        }
    }

    private void AddMesh(
        Mesh mesh,
        Matrix4x4 localToWorld)
    {
        vertices.Clear();

        mesh.GetVertices(
            vertices);


        // Each submesh becomes one RT instance.
        for (int subMesh = 0;
             subMesh < mesh.subMeshCount;
             subMesh++)
        {
            if (mesh.GetTopology(subMesh) !=
                MeshTopology.Triangles)
            {
                continue;
            }


            indices.Clear();


            mesh.GetTriangles(
                indices,
                subMesh,
                true);


            if (indices.Count < 3)
                continue;

            uint instanceID =
                (uint)triangleBases.Count;


            triangleBases.Add(
                (uint)triangleNormals.Count);


            MeshInstanceDesc instance =
                new MeshInstanceDesc(
                    mesh,
                    subMesh);


            instance.localToWorldMatrix =
                localToWorld;

            instance.instanceID =
                instanceID;

            instance.mask =
                RayMask;

            instance.opaqueGeometry =
                true;

            instance.enableTriangleCulling =
                false;


            accelerationStructure.AddInstance(
                instance);


            for (int i = 0;
                 i + 2 < indices.Count;
                 i += 3)
            {
                Vector3 p0 =
                    localToWorld.MultiplyPoint3x4(
                        vertices[indices[i + 0]]);

                Vector3 p1 =
                    localToWorld.MultiplyPoint3x4(
                        vertices[indices[i + 1]]);

                Vector3 p2 =
                    localToWorld.MultiplyPoint3x4(
                        vertices[indices[i + 2]]);


                Vector3 normal =
                    Vector3.Cross(
                        p1 - p0,
                        p2 - p0);


                if (normal.sqrMagnitude > 1e-12f)
                {
                    normal.Normalize();
                }
                else
                {
                    normal = Vector3.zero;
                }


                triangleNormals.Add(
                    new Vector4(
                        normal.x,
                        normal.y,
                        normal.z,
                        0.0f));
            }
        }
    }

    private void BuildPointLights(
        CullingResults cullingResults)
    {
        pointLights.Clear();


        var visibleLights =
            cullingResults.visibleLights;


        for (int i = 0;
             i < visibleLights.Length;
             i++)
        {
            VisibleLight light =
                visibleLights[i];


            if (light.lightType != LightType.Point)
                continue;


            if (pointLights.Count >= MaxPointLights)
                break;


            Vector4 position =
                light.localToWorldMatrix.GetColumn(3);


            // xyz = world-space position
            // w   = light range
            pointLights.Add(
                new Vector4(
                    position.x,
                    position.y,
                    position.z,
                    light.range));
        }
    }

    private static Vector4 GetCameraFrustum(
        Camera camera)
    {
        Vector3[] corners =
            new Vector3[4];


        camera.CalculateFrustumCorners(
            new Rect(0, 0, 1, 1),
            1.0f,
            Camera.MonoOrStereoscopicEye.Mono,
            corners);


        return new Vector4(
            corners[0].x, // left
            corners[2].x, // right
            corners[0].y, // bottom
            corners[2].y  // top
        );
    }

#if UNITY_EDITOR

    private static UnityEngine.Object LoadShader(
        string path,
        RayTracingBackend backend)
    {
        UnityEngine.Object[] assets =
            AssetDatabase.LoadAllAssetsAtPath(
                path);


        foreach (UnityEngine.Object asset in assets)
        {
            if (asset == null)
                continue;


            if (backend == RayTracingBackend.Compute &&
                asset is ComputeShader)
            {
                return asset;
            }


            if (backend == RayTracingBackend.Hardware &&
                asset.GetType().Name == "RayTracingShader")
            {
                return asset;
            }
        }


        return null;
    }

#endif

    public void Dispose()
    {
        accelerationStructure?.Dispose();

        accelerationStructure = null;


        rayTracingContext?.Dispose();

        rayTracingContext = null;
    }
}