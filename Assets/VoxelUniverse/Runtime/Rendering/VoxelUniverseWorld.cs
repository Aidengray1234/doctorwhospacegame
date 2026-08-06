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
        private bool nearTerrainStreamingActive;

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
        public bool NearTerrainStreamingActive { get { return nearTerrainStreamingActive; } }
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

        private void Awake() { InitializeRuntime(); }
        private void OnEnable() { InitializeRuntime(); }

        private void InitializeRuntime()
        {
            if (settings == null || bodyDefinition == null) return;
            settings.ClampValues();
            bodyId = bodyDefinition.BodyId;
            if (saveSystem != null) saveSystem.Configure(settings.saveVersion, settings.generatorVersion);
            generator = new VoxelTerrainGenerator(settings, bodyId);
            mesher = new VoxelSectionMesher(settings);
            if (Application.isPlaying && scheduler == null)
                scheduler = new VoxelJobScheduler(settings.workerCount);
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
                nearTerrainStreamingActive = GetAltitude(observer.position) <= settings.nearTerrainMaxAltitude;
                if (nearTerrainStreamingActive) RefreshStreaming();
            }
            else nearTerrainStreamingActive = false;

            UnloadExpiredSections();
            TrimSectionBudget();
        }

        public float GetAltitude(Vector3 worldPosition)
        {
            if (settings == null) return float.PositiveInfinity;
            return (worldPosition - Center).magnitude - settings.groundRadius;
        }

        public bool ShouldStreamNearTerrain(Vector3 worldPosition)
        {
            return settings != null && GetAltitude(worldPosition) <= settings.nearTerrainMaxAltitude;
        }

        private void RefreshStreaming()
        {
            Double3 local = Double3.FromVector3(observer.position - Center);
            if (local.Magnitude <= 0.001d) return;

            VoxelAddress observerAddress = CubeSphereMapper.PositionToAddress(
                bodyId, local, settings.groundRadius, settings.faceCellResolution);
            int surfaceRadial = generator.GetSurfaceHeight(
                observerAddress.face, observerAddress.u, observerAddress.v);
            VoxelAddress surfaceAddress = new VoxelAddress(
                bodyId, observerAddress.face, observerAddress.u, observerAddress.v, surfaceRadial);

            int centerSectionU = IntegerMath.FloorDiv(surfaceAddress.u, VoxelConstants.SectionSize);
            int centerSectionV = IntegerMath.FloorDiv(surfaceAddress.v, VoxelConstants.SectionSize);
            int centerSectionR = IntegerMath.FloorDiv(surfaceAddress.radial, VoxelConstants.SectionSize);

            FaceBasis tangent = CubeSphereMapper.GetCellTangentBasis(
                surfaceAddress.face, surfaceAddress.u, surfaceAddress.v, settings.faceCellResolution);
            int leadU = Mathf.RoundToInt(Vector3.Dot(observerVelocity, tangent.east.ToVector3())
                                         * settings.predictiveSectionLead / VoxelConstants.SectionSize);
            int leadV = Mathf.RoundToInt(Vector3.Dot(observerVelocity, tangent.north.ToVector3())
                                         * settings.predictiveSectionLead / VoxelConstants.SectionSize);
            leadU = Mathf.Clamp(leadU, -settings.predictiveSectionLead, settings.predictiveSectionLead);
            leadV = Mathf.Clamp(leadV, -settings.predictiveSectionLead, settings.predictiveSectionLead);

            float now = Time.unscaledTime;
            int radius = settings.nearSectionRadius;
            for (int dr = -settings.verticalSectionRadius; dr <= settings.verticalSectionRadius; dr++)
            {
                for (int dv = -radius; dv <= radius; dv++)
                {
                    for (int du = -radius; du <= radius; du++)
                    {
                        if (du * du + dv * dv > radius * radius + 1) continue;
                        int rawU = (centerSectionU + du + leadU) * VoxelConstants.SectionSize
                                   + VoxelConstants.SectionSize / 2;
                        int rawV = (centerSectionV + dv + leadV) * VoxelConstants.SectionSize
                                   + VoxelConstants.SectionSize / 2;
                        int rawR = (centerSectionR + dr) * VoxelConstants.SectionSize;
                        VoxelAddress canonical = CubeSphereMapper.Canonicalize(
                            new VoxelAddress(bodyId, surfaceAddress.face, rawU, rawV, rawR),
                            settings.faceCellResolution);
                        int distanceSq = du * du + dv * dv + dr * dr * 2;
                        int priority = distanceSq * 100;
                        if (dr == 0 && du == 0 && dv == 0) priority = 0;
                        if (du == Math.Sign(leadU) && dv == Math.Sign(leadV)) priority -= 20;
                        RequestSection(canonical.SectionKey, priority, now);
                    }
                }
            }

            RequestSection(surfaceAddress.SectionKey, -2000, now);
            RequestSection(observerAddress.SectionKey, -1900, now);
            VoxelAddress below = new VoxelAddress(bodyId, observerAddress.face,
                observerAddress.u, observerAddress.v, observerAddress.radial - 1);
            RequestSection(CubeSphereMapper.Canonicalize(below, settings.faceCellResolution).SectionKey,
                -1800, now);

            if (safeSpawnReadyTime < 0f && IsSectionReady(surfaceAddress.SectionKey))
                safeSpawnReadyTime = Time.realtimeSinceStartup;
        }

        private void RequestSection(SectionKey key, int priority, float requiredTime)
        {
            if (scheduler == null) return;
            SectionRecord record;
            if (!sections.TryGetValue(key, out record))
            {
                if (sections.Count >= settings.maximumLoadedSections)
                    RemoveOldestSection(false);
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
            for (int z = 0; z < VoxelConstants.SectionSize; z++)
            for (int x = 0; x < VoxelConstants.SectionSize; x++)
            {
                VoxelAddress address = section.ToAddress(x, y, z);
                BlockState edit;
                if (saveSystem != null && saveSystem.TryGetEdit(address, out edit))
                    section.SetLocal(x, y, z, edit);
            }
        }

        private BlockState SampleLogicalForWorker(VoxelAddress rawAddress)
        {
            VoxelAddress address = CubeSphereMapper.Canonicalize(
                rawAddress, settings.faceCellResolution);
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
            SectionKey protectedKey = default(SectionKey);
            bool hasProtected = false;
            if (observer != null && nearTerrainStreamingActive)
            {
                VoxelAddress a = GetAddress(observer.position);
                int surface = generator.GetSurfaceHeight(a.face, a.u, a.v);
                protectedKey = new VoxelAddress(bodyId, a.face, a.u, a.v, surface).SectionKey;
                hasProtected = true;
            }

            foreach (KeyValuePair<SectionKey, SectionRecord> pair in sections)
            {
                SectionRecord record = pair.Value;
                if (record.pending) continue;
                if (hasProtected && pair.Key == protectedKey) continue;
                float delay = nearTerrainStreamingActive
                    ? settings.unloadDelaySeconds
                    : Mathf.Min(2.5f, settings.unloadDelaySeconds);
                if (now - record.lastRequiredTime < delay) continue;
                if (remove == null) remove = new List<SectionKey>();
                remove.Add(pair.Key);
            }

            if (remove == null) return;
            for (int i = 0; i < remove.Count; i++) RemoveSection(remove[i]);
        }

        private void TrimSectionBudget()
        {
            while (sections.Count > settings.maximumLoadedSections)
                if (!RemoveOldestSection(true)) break;
        }

        private bool RemoveOldestSection(bool allowRecent)
        {
            bool found = false;
            SectionKey oldestKey = default(SectionKey);
            float oldest = float.MaxValue;
            foreach (KeyValuePair<SectionKey, SectionRecord> pair in sections)
            {
                SectionRecord record = pair.Value;
                if (record.pending) continue;
                if (!allowRecent && Time.unscaledTime - record.lastRequiredTime < 0.25f) continue;
                if (record.lastRequiredTime >= oldest) continue;
                oldest = record.lastRequiredTime;
                oldestKey = pair.Key;
                found = true;
            }
            if (!found) return false;
            RemoveSection(oldestKey);
            return true;
        }

        private void RemoveSection(SectionKey key)
        {
            SectionRecord record;
            if (!sections.TryGetValue(key, out record)) return;
            if (record.renderer != null)
            {
                if (Application.isPlaying) Destroy(record.renderer.gameObject);
                else DestroyImmediate(record.renderer.gameObject);
            }
            sections.Remove(key);
        }

        public BlockState GetBlock(VoxelAddress rawAddress)
        {
            if (settings == null || generator == null) return BlockState.Air;
            VoxelAddress address = CubeSphereMapper.Canonicalize(
                rawAddress, settings.faceCellResolution);
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
            VoxelAddress address = CubeSphereMapper.Canonicalize(
                rawAddress, settings.faceCellResolution);
            if (saveSystem != null) saveSystem.SetEdit(address, state);

            SectionRecord record;
            if (sections.TryGetValue(address.SectionKey, out record) && record.section != null)
            {
                Int3 local = address.Local;
                record.section.SetLocal(local.x, local.y, local.z, state);
            }

            RequestRemesh(address.SectionKey);
            RequestNeighborRemesh(address, -1, 0, 0);
            RequestNeighborRemesh(address, 1, 0, 0);
            RequestNeighborRemesh(address, 0, -1, 0);
            RequestNeighborRemesh(address, 0, 1, 0);
            RequestNeighborRemesh(address, 0, 0, -1);
            RequestNeighborRemesh(address, 0, 0, 1);
        }

        private void RequestNeighborRemesh(VoxelAddress address, int du, int dv, int dr)
        {
            VoxelAddress neighbor = CubeSphereMapper.Canonicalize(
                new VoxelAddress(bodyId, address.face, address.u + du,
                    address.v + dv, address.radial + dr),
                settings.faceCellResolution);
            RequestRemesh(neighbor.SectionKey);
        }

        private void RequestRemesh(SectionKey key)
        {
            if (scheduler == null) return;
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
            if (scheduler != null)
                RequestSection(address.SectionKey, -2500, Time.unscaledTime);
        }

        public VoxelAddress GetAddress(Vector3 worldPosition)
        {
            return CubeSphereMapper.PositionToAddress(bodyId,
                Double3.FromVector3(worldPosition - Center),
                settings.groundRadius, settings.faceCellResolution);
        }

        public VoxelBlockFrame GetBlockFrame(VoxelAddress address)
        {
            VoxelBlockFrame frame = VoxelBlockGeometry.Calculate(address, settings);
            frame.center += Center;
            return frame;
        }

        public Vector3 GetBlockCenter(VoxelAddress address)
        {
            return GetBlockFrame(address).center;
        }

        public FaceBasis GetBlockBasis(VoxelAddress address)
        {
            VoxelAddress canonical = CubeSphereMapper.Canonicalize(
                address, settings.faceCellResolution);
            return CubeSphereMapper.GetCellTangentBasis(canonical.face,
                canonical.u, canonical.v, settings.faceCellResolution);
        }

        public VoxelAddress FindSurfaceAddress(Vector3 direction)
        {
            if (generator == null) InitializeRuntime();
            return generator.FindSurfaceAddress(Double3.FromVector3(direction).Normalized);
        }

        public int GetSurfaceHeight(CubeSphereFace face, int u, int v)
        {
            if (generator == null) InitializeRuntime();
            return generator != null ? generator.GetSurfaceHeight(face, u, v) : 0;
        }

        public BlockState SampleGeneratedBlock(VoxelAddress address)
        {
            if (generator == null) InitializeRuntime();
            return generator != null ? generator.SampleBaseBlock(address) : BlockState.Air;
        }

        public void ClearGeneratedRenderers()
        {
            uploadQueue.Clear();
            sections.Clear();
            EnsureSectionRoot();
            for (int i = sectionRoot.childCount - 1; i >= 0; i--)
            {
                GameObject child = sectionRoot.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(child);
                else DestroyImmediate(child);
            }
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

        private void OnDestroy()
        {
            if (scheduler != null)
            {
                scheduler.Dispose();
                scheduler = null;
            }
        }
    }
}
