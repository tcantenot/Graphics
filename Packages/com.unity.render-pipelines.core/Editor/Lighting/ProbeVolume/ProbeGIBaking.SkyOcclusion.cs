using System.Runtime.InteropServices;
using UnityEngine.Rendering.Sampling;
using UnityEngine.Rendering.UnifiedRayTracing;
using Unity.Collections;

namespace UnityEngine.Rendering
{
    partial class ProbeGIBaking
    {
        struct SkyOcclusionBaking
        {
            private const float k_SkyOcclusionOffsetRay = 0.015f;
            private const int k_SampleCountPerStep = 16;

            static readonly int _SampleCount = Shader.PropertyToID("_SampleCount");
            static readonly int _SampleId = Shader.PropertyToID("_SampleId");
            static readonly int _ProbeOffset = Shader.PropertyToID("_ProbeOffset");
            static readonly int _MaxBounces = Shader.PropertyToID("_MaxBounces");
            static readonly int _OffsetRay = Shader.PropertyToID("_OffsetRay");
            static readonly int _ProbePositions = Shader.PropertyToID("_ProbePositions");
            static readonly int _SkyOcclusionOut = Shader.PropertyToID("_SkyOcclusionOut");
            static readonly int _SkyShadingPrecomputedDirection = Shader.PropertyToID("_SkyShadingPrecomputedDirection");
            static readonly int _SkyShadingOut = Shader.PropertyToID("_SkyShadingOut");
            static readonly int _SkyShadingDirectionIndexOut = Shader.PropertyToID("_SkyShadingDirectionIndexOut");
            static readonly int _AverageAlbedo = Shader.PropertyToID("_AverageAlbedo");
            static readonly int _BackFaceCulling = Shader.PropertyToID("_BackFaceCulling");
            static readonly int _BakeSkyShadingDirection = Shader.PropertyToID("_BakeSkyShadingDirection");
            static readonly int _SobolBuffer = Shader.PropertyToID("_SobolBuffer");
            static readonly int _CPRBuffer = Shader.PropertyToID("_CPRBuffer");

            public bool skyOcclusion;
            public bool skyDirection;

            private int skyOcclusionBackFaceCulling;
            private float skyOcclusionAverageAlbedo;
            private int probeCount;

            private BakeJob[] jobs;
            private int jobCount;
            private int currentJob;
            public int sampleIndex;

            // Output buffers
            private GraphicsBuffer occlusionOutputBuffer;
            private GraphicsBuffer skyShadingIndexBuffer;
            public Vector4[] occlusionResults;
            public uint[] directionResults;

            private IRayTracingAccelStruct m_AccelerationStructure;
            private GraphicsBuffer scratchBuffer;
            private GraphicsBuffer probePositionsBuffer;
            private GraphicsBuffer skyShadingBuffer;
            private ComputeBuffer precomputedShadingDirections;
            private GraphicsBuffer sobolBuffer;
            private GraphicsBuffer cprBuffer; // Cranley Patterson rotation

            public ulong currentStep;
            public ulong stepCount => (ulong)probeCount;

            public void Initialize(ProbeVolumeBakingSet bakingSet, BakeJob[] bakeJobs, int bakeJobCount, int probeCount)
            {
                // We have to copy the values from the baking set as they may get modified by the user while baking
                skyOcclusion = bakingSet.skyOcclusion;
                skyDirection = bakingSet.skyOcclusionShadingDirection && skyOcclusion;
                skyOcclusionAverageAlbedo = bakingSet.skyOcclusionAverageAlbedo;
                skyOcclusionBackFaceCulling = bakingSet.skyOcclusionBackFaceCulling ? 1 : 0;

                jobs = bakeJobs;
                jobCount = bakeJobCount;
                currentJob = 0;
                sampleIndex = 0;

                currentStep = 0;
                this.probeCount = skyOcclusion ? probeCount : 0;
            }

            static IRayTracingAccelStruct BuildAccelerationStructure()
            {
                var accelStruct = s_TracingContext.CreateAccelerationStructure();
                var contributors = m_BakingBatch.contributors;

                foreach (var renderer in contributors.renderers)
                {
                    if (renderer.component.isPartOfStaticBatch)
                    {
                        Debug.LogError("Static batching should be disabled when using sky occlusion support.");
                    }

                    var mesh = renderer.component.GetComponent<MeshFilter>().sharedMesh;
                    if (mesh == null)
                        continue;

                    var matIndices = GetMaterialIndices(renderer.component);
                    uint mask = GetInstanceMask(renderer.component.shadowCastingMode);
                    int subMeshCount = mesh.subMeshCount;

                    for (int i = 0; i < subMeshCount; ++i)
                    {
                        var instanceDesc = new MeshInstanceDesc(mesh, i);
                        instanceDesc.localToWorldMatrix = renderer.component.transform.localToWorldMatrix;
                        instanceDesc.mask = mask;
                        instanceDesc.materialID = matIndices[i];

                        instanceDesc.enableTriangleCulling = true;
                        instanceDesc.frontTriangleCounterClockwise = false;

                        accelStruct.AddInstance(instanceDesc);
                    }
                }

                foreach (var terrain in contributors.terrains)
                {
                    uint mask = GetInstanceMask(terrain.component.shadowCastingMode);

                    var terrainDesc = new TerrainDesc(terrain.component);
                    terrainDesc.localToWorldMatrix = terrain.component.transform.localToWorldMatrix;
                    terrainDesc.mask = mask;
                    terrainDesc.materialID = 0;

                    accelStruct.AddTerrain(terrainDesc);
                }

                return accelStruct;
            }

            public void StartBaking(NativeArray<Vector3> positions)
            {
                if (!skyOcclusion)
                    return;

                // Create acceletation structure
                m_AccelerationStructure = BuildAccelerationStructure();
                var skyOcclusionShader = s_TracingContext.shaderSO;

                probePositionsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, probeCount, Marshal.SizeOf<Vector3>());
                occlusionOutputBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, probeCount, Marshal.SizeOf<Vector4>());
                skyShadingBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, skyDirection ? probeCount : 1, Marshal.SizeOf<Vector3>());
                skyShadingIndexBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, skyDirection ? probeCount : 1, Marshal.SizeOf<uint>());
                scratchBuffer = RayTracingHelper.CreateScratchBufferForBuildAndDispatch(m_AccelerationStructure, skyOcclusionShader, (uint)probeCount, 1, 1);

                var buildCmd = new CommandBuffer();
                m_AccelerationStructure.Build(buildCmd, scratchBuffer);
                Graphics.ExecuteCommandBuffer(buildCmd);
                buildCmd.Dispose();

                int sobolBufferSize = (int)(SobolData.SobolDims * SobolData.SobolSize);
                sobolBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, sobolBufferSize, Marshal.SizeOf<uint>());
                sobolBuffer.SetData(SobolData.SobolMatrices);

                cprBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SamplingResources.cranleyPattersonRotationBufferSize, Marshal.SizeOf<float>());
                cprBuffer.SetData(SamplingResources.GetCranleyPattersonRotations());

                if (skyDirection)
                {
                    DynamicSkyPrecomputedDirections.Initialize();
                    precomputedShadingDirections = ProbeReferenceVolume.instance.GetRuntimeResources().SkyPrecomputedDirections;
                }
                else
                {
                    precomputedShadingDirections = new ComputeBuffer(1, Marshal.SizeOf<Vector3>());
                }

                probePositionsBuffer.SetData(positions.GetSubArray(0, probeCount));
            }

            public void RunSkyOcclusionStep()
            {
                if (currentStep >= stepCount)
                    return;

                var cmd = new CommandBuffer();
                var skyOccShader = s_TracingContext.shaderSO;
                ref var job = ref jobs[currentJob];

                s_TracingContext.BindSamplingTextures(cmd);
                skyOccShader.SetAccelerationStructure(cmd, "_AccelStruct", m_AccelerationStructure);

                skyOccShader.SetIntParam(cmd, _BakeSkyShadingDirection, skyDirection ? 1 : 0);
                skyOccShader.SetIntParam(cmd, _BackFaceCulling, skyOcclusionBackFaceCulling);
                skyOccShader.SetFloatParam(cmd, _AverageAlbedo, skyOcclusionAverageAlbedo);

                skyOccShader.SetFloatParam(cmd, _OffsetRay, k_SkyOcclusionOffsetRay);
                skyOccShader.SetBufferParam(cmd, _ProbePositions, probePositionsBuffer);
                skyOccShader.SetBufferParam(cmd, _SkyOcclusionOut, occlusionOutputBuffer);
                skyOccShader.SetBufferParam(cmd, _SkyShadingPrecomputedDirection, precomputedShadingDirections);
                skyOccShader.SetBufferParam(cmd, _SkyShadingOut, skyShadingBuffer);
                skyOccShader.SetBufferParam(cmd, _SkyShadingDirectionIndexOut, skyShadingIndexBuffer);

                skyOccShader.SetBufferParam(cmd, _SobolBuffer, sobolBuffer);
                skyOccShader.SetBufferParam(cmd, _CPRBuffer, cprBuffer);

                skyOccShader.SetIntParam(cmd, _SampleCount, job.skyOcclusionBakingSamples);
                skyOccShader.SetIntParam(cmd, _MaxBounces, job.skyOcclusionBakingBounces);
                skyOccShader.SetIntParam(cmd, _ProbeOffset, job.startOffset);

                int jobSize = job.indices.Length;

                // Sample all paths (1 per probe) in one pass
                for (int i = 0; i < k_SampleCountPerStep; i++)
                {
                    skyOccShader.SetIntParam(cmd, _SampleId, sampleIndex++);

                    // TODO: fails if probeCount > 16 * 65536
                    skyOccShader.Dispatch(cmd, scratchBuffer, (uint)jobSize, 1, 1);

                    Graphics.ExecuteCommandBuffer(cmd);
                    cmd.Clear();
                }

                cmd.Dispose();

                // If we computed all the samples for this job, continue with the next one
                if (sampleIndex >= job.skyOcclusionBakingSamples)
                {
                    currentStep += (ulong)jobSize;
                    currentJob++;
                    sampleIndex = 0;
                }

                // If we executed all the jobs, fetch results back from GPU
                if (currentJob == jobCount)
                    FetchResults();
            }

            void FetchResults()
            {
                if (!skyOcclusion)
                    return;

                var sortedSkyOcclusion = new Vector4[probeCount];
                var sortedSkyDirection = skyDirection ? new uint[probeCount] : null;

                occlusionOutputBuffer.GetData(sortedSkyOcclusion);
                if (skyDirection)
                    skyShadingIndexBuffer.GetData(sortedSkyDirection);

                occlusionResults = new Vector4[probeCount];
                directionResults = skyDirection ? new uint[probeCount] : null;

                for (int j = 0; j < jobCount; j++)
                {
                    ref var job = ref jobs[j];
                    for (int i = 0; i < job.indices.Length; i++)
                    {
                        var dst = job.indices[i];
                        occlusionResults[dst] = sortedSkyOcclusion[job.startOffset + i];
                        if (skyDirection)
                            directionResults[dst] = sortedSkyDirection[job.startOffset + i];
                    }
                }
            }

            public void Dispose()
            {
                if (m_AccelerationStructure == null)
                    return;

                occlusionOutputBuffer?.Dispose();
                skyShadingBuffer?.Dispose();

                scratchBuffer?.Dispose();
                probePositionsBuffer?.Dispose();
                skyShadingIndexBuffer?.Dispose();
                sobolBuffer?.Dispose();
                cprBuffer?.Dispose();

                if (!skyDirection)
                    precomputedShadingDirections?.Dispose();

                m_AccelerationStructure.Dispose();
            }
        }

        internal static uint LinearSearchClosestDirection(Vector3[] precomputedDirections, Vector3 direction)
        {
            uint indexMax = 255;
            float bestDot = -10.0f;
            uint bestIndex = 0;

            for (uint index = 0; index < indexMax; index++)
            {
                float currentDot = Vector3.Dot(direction, precomputedDirections[index]);
                if (currentDot > bestDot)
                {
                    bestDot = currentDot;
                    bestIndex = index;
                }
            }
            return bestIndex;
        }
    }
}
