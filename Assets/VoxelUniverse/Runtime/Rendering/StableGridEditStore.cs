using System;
using System.Collections.Generic;
using System.IO;
using DoctorWho.VoxelUniverse.Core;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Rendering
{
    public sealed class StableGridEditStore : MonoBehaviour
    {
        [Serializable]
        private sealed class EditRecord
        {
            public int x;
            public int y;
            public int z;
            public uint packed;
        }

        [Serializable]
        private sealed class SaveDocument
        {
            public int formatVersion = 2;
            public int generatorVersion;
            public string bodyId;
            public long savedUtcTicks;
            public List<EditRecord> edits = new List<EditRecord>();
        }

        private readonly Dictionary<Int3, uint> edits = new Dictionary<Int3, uint>();
        private VoxelUniverseWorld world;
        private string savePath;
        private bool configured;
        private bool dirty;

        public int EditCount { get { return edits.Count; } }

        public void Configure(VoxelUniverseWorld voxelWorld)
        {
            if (voxelWorld == null) return;
            world = voxelWorld;
            string body = world.BodyId.ToString();
            string safeBody = body.Replace(':', '_').Replace('/', '_').Replace('\\', '_');
            savePath = Path.Combine(Application.persistentDataPath, "VoxelUniverse",
                "stable-grid-" + safeBody + "-v2.json");
            configured = true;
            Load();
        }

        public bool TryGet(Int3 cell, out uint packed)
        {
            return edits.TryGetValue(cell, out packed);
        }

        public void Set(Int3 cell, uint packed)
        {
            edits[cell] = packed;
            dirty = true;
        }

        // Called only on the Unity main thread before a worker job is scheduled.
        // The worker receives its own immutable dictionary and never touches this store.
        public Dictionary<Int3, uint> CaptureRegion(Int3 minInclusive, Int3 maxInclusive)
        {
            Dictionary<Int3, uint> result = new Dictionary<Int3, uint>();
            foreach (KeyValuePair<Int3, uint> pair in edits)
            {
                Int3 p = pair.Key;
                if (p.x < minInclusive.x || p.y < minInclusive.y || p.z < minInclusive.z) continue;
                if (p.x > maxInclusive.x || p.y > maxInclusive.y || p.z > maxInclusive.z) continue;
                result.Add(p, pair.Value);
            }
            return result;
        }

        public void SaveNow()
        {
            if (!configured || string.IsNullOrEmpty(savePath)) return;
            SaveDocument document = new SaveDocument();
            document.generatorVersion = world != null && world.Settings != null
                ? world.Settings.generatorVersion : 1;
            document.bodyId = world != null ? world.BodyId.ToString() : string.Empty;
            document.savedUtcTicks = DateTime.UtcNow.Ticks;
            foreach (KeyValuePair<Int3, uint> pair in edits)
            {
                document.edits.Add(new EditRecord
                {
                    x = pair.Key.x,
                    y = pair.Key.y,
                    z = pair.Key.z,
                    packed = pair.Value
                });
            }

            string directory = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string temporary = savePath + ".tmp";
            string backup = savePath + ".bak";
            File.WriteAllText(temporary, JsonUtility.ToJson(document, true));
            if (File.Exists(savePath))
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(savePath, backup);
            }
            File.Move(temporary, savePath);
            dirty = false;
        }

        private void Load()
        {
            edits.Clear();
            dirty = false;
            if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath)) return;
            try
            {
                SaveDocument document = JsonUtility.FromJson<SaveDocument>(File.ReadAllText(savePath));
                if (document == null || document.edits == null) return;
                for (int i = 0; i < document.edits.Count; i++)
                {
                    EditRecord record = document.edits[i];
                    edits[new Int3(record.x, record.y, record.z)] = record.packed;
                }
            }
            catch (Exception exception)
            {
                string corrupt = savePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(savePath, corrupt, true); }
                catch { }
                Debug.LogError("[Stable Voxel Grid] Edit save could not be loaded. Preserved: " + corrupt);
                Debug.LogException(exception);
            }
        }

        private void OnApplicationQuit()
        {
            if (dirty) SaveNow();
        }

        private void OnDisable()
        {
            if (Application.isPlaying && dirty) SaveNow();
        }
    }
}
