using System;
using System.Collections.Generic;
using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Core;
using DoctorWho.VoxelUniverse.Generation;
using DoctorWho.VoxelUniverse.Meshing;
using DoctorWho.VoxelUniverse.Saves;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class VoxelUniverseWorld : MonoBehaviour
    {
        private sealed class SectionRecord
        {
            public VoxelSection section;
            public VoxelSectionRenderer renderer;
            public int requestVersion;
            public float lastRequiredTime;
            public bool pending;
        }

        [SerializeField] private VoxelUniverseSettings settings;
        [SerializeField] private CelestialBodyDefinition bodyDefinition;
        [SerializeField] private Transform observer;
        [SerializeField] private Material opaqueMaterial;
        [SerializeField] private Material waterMaterial;
        [SerializeField] private VoxelSaveSystem saveSystem;

        private readonly Dictionary<SectionKey, SectionRecord> sections =
            new Dictionary<SectionKey, SectionRecord>();
        private readonly Queue<SectionMeshData> uploadQueue = new Queue<SectionMeshData>();
        private readonly object editSync = new object();

        private VoxelJobScheduler scheduler;
        private VoxelTerrainGenerator generator;
        private VoxelSectionMesher mesher;
        private CelestialBodyId bodyId;
        private Transform sectionRoot;
        private Vector3 previousObserverPosition;
        private Vector3 observerVelocity;
        private int nextRequestVersion;
        private int meshUploadsThisFrame;
        private float safeSpawnStartTime;
        private float safeSpawnReadyTime = -1f;

        public VoxelUniverseSettings Settings { get { return settings; } }
        public CelestialBodyDefinition BodyDefinition { get { return bodyDefinition; } }
        public CelestialBodyId BodyId { get { return bodyId; } }
        public Vector3 Center { get { return transform.position; } }
        public int LoadedSectionCount { get { return sections.Count; } }
        public int CachedSectionCount { get { return sections.Count; } }
        public int QueuedJobCount { get { return scheduler != null ? scheduler.QueuedCount : 0; } }
        public int ActiveWorkerCount { get { return scheduler != null ? scheduler.ActiveWorkerCount : 0; } }
        public int PendingUploadCount { get { return uploadQueue.Count; } }
        public int MeshUploadsThisFrame { get { return meshUploadsThisFrame; } }
        public int EstimatedSectionBytes
        {
            get
            {
                int total = 0;
                foreach (KeyValuePair<SectionKey, SectionRecord> pair in sections)
                    if (pair.Value.section != null) total += pair.Value.section.EstimatedBytes;
                return total;
            }
        }
        public float TimeToSafeSpawn
        {
            get { return safeSpawnReadyTime < 0f ? -1f : safeSpawnReadyTime - safeSpawnStartTime; }
        }

        public void Configure(
            VoxelUniverseSettings runtimeSettings,
            CelestialBodyDefinition body,
            Transform trackingObserver,
            Material opaque,
            Material water,
            VoxelSaveSystem saves)
        {
            settings = runtimeSettings;
            bodyDefinition = body;
            observer = trackingObserver;
            opaqueMaterial = opaque;
            waterMaterial = water;
            saveSystem = saves;
            InitializeRuntime();
        }

        public void SetObserver(Transform value)
        {
            observer = value;
            previousObserverPosition = observer != null ? observer.position : Vector3.zero;
        }

        private void Awake()
        {
            InitializeRuntime();
        }

        private void OnEnable()
        {
            InitializeRuntime();
        }

        private void InitializeRuntime()
        {
            if (settings == null || bodyDefinition == null) return;
            settings.ClampValues();
            bodyId = bodyDefinition.BodyId;
            if (saveSystem != null) saveSystem.Configure(settings.saveVersion, settings.generatorVersion);
            generator = new VoxelTerrainGenerator(settings, bodyId);
            mesher = new VoxelSectionMesher(settings);
            if (Application.isPlaying && scheduler == null) scheduler = new VoxelJobScheduler(settings.workerCount);
            EnsureSectionRoot();
            safeSpawnStartTime = Time.realtimeSinceStartup;
            if (observer != null) previousObserverPosition = observer.position;
        }

        private void Update()
        {
            meshUploadsThisFrame = 0;
            if (settings == null || generator == null || scheduler == null) return;

            scheduler.PumpMainThread(settings.mainThreadCallbacksPerFrame);
            ApplyCompletedMeshes(settings.meshUploadsPerFrame);

            if (observer != null)
            {
                float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                observerVelocity = (observer.position - previousObserverPosition) / dt;
                previousObserverPosition = observer.position;
                RefreshStreaming();
            }

            UnloadExpiredSections();
        }

        private void RefreshStreaming()
        {
            Double3 local = Double3.FromVector3(observer.position - Center);
            if (local.Magnitude <= 0.001d) return;
            VoxelAddress centerAddress = CubeSphereMapper.PositionToAddress(
                bodyId, local, settings.groundRadius, settings.faceCellResolution);

            int centerSectionU = IntegerMath.FloorDiv(centerAddress.u, VoxelConstants.SectionSize);
            int centerSectionV = IntegerMath.FloorDiv(centerAddress.v, VoxelConstants.SectionSize);
            int centerSectionR = IntegerMath.FloorDiv(centerAddress.radial, VoxelConstants.SectionSize);
            Vector3 localVelocity = observerVelocity;
            FaceBasis tangent = CubeSphereMapper.GetCellTangentBasis(
                centerAddress.face, centerAddress.u, centerAddress.v, settings.faceCellResolution);
            int leadU = Mathf.RoundToInt(Vector3.Dot(localVelocity, tangent.east.ToVector3())
                                         * settings.predictiveSectionLead / VoxelConstants.SectionSize);
            int leadV = Mathf.RoundToInt(Vector3.Dot(localVelocity, tangent.north.ToVector3())
                                         * settings.predictiveSectionLead / VoxelConstants.SectionSize);
            leadU = Mathf.Clamp(leadU, -settings.predictiveSectionLead, settings.predictiveSectionLead);
            leadV = Mathf.Clamp(leadV, -settings.predictiveSectionLead, settings.predictiveSectionLead);

            float now = Time.unscaledTime;
            for (int dr = -settings.verticalSectionRadius; dr <= settings.verticalSectionRadius; dr++)
            {
                for (int dv = -settings.nearSectionRadius; dv <= settings.nearSectionRadius; dv++)
                {
                    for (int du = -settings.nearSectionRadius; du <= settings.nearSectionRadius; du++)
                    {
                        int distanceSq = du * du + dv * dv + dr * dr * 2;
                        if (du * du + dv * dv > settings.nearSectionRadius * settings.nearSectionRadius + 1)
                            continue;

                        int rawU = (centerSectionU + du + leadU) * VoxelConstants.SectionSize + VoxelConstants.SectionSize / 2;
                        int rawV = (centerSectionV + dv + leadV) * VoxelConstants.SectionSize + VoxelConstants.SectionSize / 2;
                        int rawR = (centerSectionR + dr) * VoxelConstants.SectionSize;
                        VoxelAddress canonical = CubeSphereMapper.Canonicalize(
                            new VoxelAddress(bodyId, centerAddress.face, rawU, rawV, rawR),
                            settings.faceCellResolution);
                        SectionKey key = canonical.SectionKey;
                        int priority = distanceSq * 100;
                        if (dr == 0 && du == 0 && dv == 0) priority = 0;
                        RequestSection(key, priority, now);
                    }
                }
            }

            SectionKey supportKey = centerAddress.SectionKey;
            RequestSection(supportKey, -1000, now);
            if (safeSpawnReadyTime < 0f && IsSectionReady(supportKey))
                safeSpawnReadyTime = Time.realtimeSinceStartup;
        }

        private void RequestSection(SectionKey key, int priority, float requiredTime)
        {
            if (scheduler == null) return;
            SectionRecord record;
            if (!sections.TryGetValue(key, out record))
            {
                record = new SectionRecord();
                sections.Add(key, record);
            }

            record.lastRequiredTime = requiredTime;
            if (record.section != null || record.pending) return;
            record.pending = true;
            record.requestVersion = ++nextRequestVersion;
            int capturedVersion = record.requestVersion;

            scheduler.Schedule(priority,
                delegate
                {
                    VoxelSection section = generator.GenerateSection(key);
                    ApplySavedEdits(section);
                    return mesher.Build(section, capturedVersion, SampleLogicalForWorker);
                },
                delegate(object result)
                {
                    SectionMeshData data = result as SectionMeshData;
                    if (data == null) return;
                    SectionRecord current;
                    if (!sections.TryGetValue(data.key, out current)) return;
                    if (current.requestVersion != data.requestVersion) return;
                    current.pending = false;
                    current.section = data.section;
                    uploadQueue.Enqueue(data);
                });
        }

        private void ApplySavedEdits(VoxelSection section)
        {
            for (int y = 0; y < VoxelConstants.SectionSize; y++)
            {
                for (int z = 0; z < VoxelConstants.SectionSize; z++)
                {
                    for (int x = 0; x < VoxelConstants.SectionSize; x++)
                    {
                        VoxelAddress address = section.ToAddress(x, y, z);
                        BlockState edit;
                        if (saveSystem != null && saveSystem.TryGetEdit(address, out edit))
                            section.SetLocal(x, y, z, edit);
                    }
                }
            }
        }

        private BlockState SampleLogicalForWorker(VoxelAddress rawAddress)
        {
            VoxelAddress address = CubeSphereMapper.Canonicalize(rawAddress, settings.faceCellResolution);
            BlockState edit;
            if (saveSystem != null && saveSystem.TryGetEdit(address, out edit)) return edit;
            return generator.SampleBaseBlock(address);
        }

        private void ApplyCompletedMeshes(int budget)
        {
            EnsureSectionRoot();
            while (meshUploadsThisFrame < budget && uploadQueue.Count > 0)
            {
                SectionMeshData data = uploadQueue.Dequeue();
                SectionRecord record;
                if (!sections.TryGetValue(data.key, out record)) continue;
                if (record.requestVersion != data.requestVersion) continue;

                if (record.renderer == null)
                {
                    GameObject sectionObject = new GameObject("Section " + data.key);
                    sectionObject.transform.SetParent(sectionRoot, false);
                    record.renderer = sectionObject.AddComponent<VoxelSectionRenderer>();
                    record.renderer.Configure(data.key, opaqueMaterial, waterMaterial);
                }
                record.renderer.Apply(data);
                meshUploadsThisFrame++;
            }
        }

        private void UnloadExpiredSections()
        {
            if (!Application.isPlaying) return;
            float now = Time.unscaledTime;
            List<SectionKey> remove = null;
            foreach (KeyValuePair<SectionKey, SectionRecord> pair in sections)
            {
                SectionRecord record = pair.Value;
                if (record.pending) continue;
                if (now - record.lastRequiredTime < settings.unloadDelaySeconds) continue;
                if (observer != null)
                {
                    VoxelAddress observerAddress = CubeSphereMapper.PositionToAddress(
                        bodyId,
                        Double3.FromVector3(observer.position - Center),
                        settings.groundRadius,
                        settings.faceCellResolution);
                    if (pair.Key == observerAddress.SectionKey) continue;
                }
                if (remove == null) remove = new List<SectionKey>();
                remove.Add(pair.Key);
            }

            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++)
            {
                SectionRecord record = sections[remove[i]];
                if (record.renderer != null) Destroy(record.renderer.gameObject);
                sections.Remove(remove[i]);
            }
        }

        public BlockState GetBlock(VoxelAddress rawAddress)
        {
            if (settings == null || generator == null) return BlockState.Air;
            VoxelAddress address = CubeSphereMapper.Canonicalize(rawAddress, settings.faceCellResolution);
            BlockState edit;
            if (saveSystem != null && saveSystem.TryGetEdit(address, out edit)) return edit;

            SectionRecord record;
            if (sections.TryGetValue(address.SectionKey, out record) && record.section != null)
            {
                Int3 local = address.Local;
                return record.section.GetLocal(local.x, local.y, local.z);
            }
            return generator.SampleBaseBlock(address);
        }

        public void SetBlock(VoxelAddress rawAddress, BlockState state)
        {
            VoxelAddress address = CubeSphereMapper.Canonicalize(rawAddress, settings.faceCellResolution);
            if (saveSystem != null) saveSystem.SetEdit(address, state);

            SectionRecord record;
            if (sections.TryGetValue(address.SectionKey, out record) && record.section != null)
            {
                Int3 local = address.Local;
                record.section.SetLocal(local.x, local.y, local.z, state);
            }

            RequestRemesh(address.SectionKey);
            RequestRemesh(new VoxelAddress(bodyId, address.face, address.u - 1, address.v, address.radial).SectionKey);
            RequestRemesh(new VoxelAddress(bodyId, address.face, address.u + 1, address.v, address.radial).SectionKey);
            RequestRemesh(new VoxelAddress(bodyId, address.face, address.u, address.v - 1, address.radial).SectionKey);
            RequestRemesh(new VoxelAddress(bodyId, address.face, address.u, address.v + 1, address.radial).SectionKey);
            RequestRemesh(new VoxelAddress(bodyId, address.face, address.u, address.v, address.radial - 1).SectionKey);
            RequestRemesh(new VoxelAddress(bodyId, address.face, address.u, address.v, address.radial + 1).SectionKey);
        }

        private void RequestRemesh(SectionKey rawKey)
        {
            VoxelAddress representative = CubeSphereMapper.Canonicalize(
                new VoxelAddress(bodyId, rawKey.face,
                    rawKey.sectionU * VoxelConstants.SectionSize + 8,
                    rawKey.sectionV * VoxelConstants.SectionSize + 8,
                    rawKey.sectionRadial * VoxelConstants.SectionSize),
                settings.faceCellResolution);
            SectionKey key = representative.SectionKey;
            SectionRecord record;
            if (!sections.TryGetValue(key, out record) || record.section == null) return;
            record.requestVersion = ++nextRequestVersion;
            int version = record.requestVersion;
            VoxelSection section = record.section;
            scheduler.Schedule(-500,
                delegate { return mesher.Build(section, version, SampleLogicalForWorker); },
                delegate(object result)
                {
                    SectionMeshData data = result as SectionMeshData;
                    SectionRecord current;
                    if (data != null && sections.TryGetValue(data.key, out current)
                        && current.requestVersion == data.requestVersion)
                        uploadQueue.Enqueue(data);
                });
        }

        public bool IsSectionReady(SectionKey key)
        {
            SectionRecord record;
            return sections.TryGetValue(key, out record) && record.section != null;
        }

        public void PrioritizeAddress(VoxelAddress address)
        {
            if (scheduler != null) RequestSection(address.SectionKey, -2000, Time.unscaledTime);
        }

        public VoxelAddress GetAddress(Vector3 worldPosition)
        {
            return CubeSphereMapper.PositionToAddress(
                bodyId,
                Double3.FromVector3(worldPosition - Center),
                settings.groundRadius,
                settings.faceCellResolution);
        }

        public Vector3 GetBlockCenter(VoxelAddress address)
        {
            return Center + CubeSphereMapper.AddressCenterToPosition(
                CubeSphereMapper.Canonicalize(address, settings.faceCellResolution),
                settings.groundRadius,
                settings.faceCellResolution).ToVector3();
        }

        public FaceBasis GetBlockBasis(VoxelAddress address)
        {
            VoxelAddress canonical = CubeSphereMapper.Canonicalize(address, settings.faceCellResolution);
            return CubeSphereMapper.GetCellTangentBasis(
                canonical.face, canonical.u, canonical.v, settings.faceCellResolution);
        }

        public VoxelAddress FindSurfaceAddress(Vector3 direction)
        {
            return generator.FindSurfaceAddress(Double3.FromVector3(direction).Normalized);
        }

        private void EnsureSectionRoot()
        {
            if (sectionRoot != null) return;
            Transform found = transform.Find("Near Voxel Sections");
            if (found != null) sectionRoot = found;
            else
            {
                GameObject root = new GameObject("Near Voxel Sections");
                root.transform.SetParent(transform, false);
                sectionRoot = root.transform;
            }
        }

        private void OnDisable()
        {
            if (scheduler != null)
            {
                scheduler.Dispose();
                scheduler = null;
            }
        }
    }
}
