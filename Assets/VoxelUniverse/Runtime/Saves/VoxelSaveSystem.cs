using System;
using System.Collections.Generic;
using System.IO;
using DoctorWho.VoxelUniverse.Celestial;
using DoctorWho.VoxelUniverse.Voxels;
using UnityEngine;

namespace DoctorWho.VoxelUniverse.Saves
{
    public sealed class VoxelSaveSystem : MonoBehaviour
    {
        [Serializable]
        public sealed class BlockEditRecord
        {
            public string bodyId;
            public int face;
            public int u;
            public int v;
            public int radial;
            public uint packedState;
        }

        [Serializable]
        public sealed class InventoryRecord
        {
            public int selectedSlot;
            public List<ItemStackRecord> slots = new List<ItemStackRecord>();
        }

        [Serializable]
        public sealed class ItemStackRecord
        {
            public int slot;
            public int blockId;
            public int count;
        }

        [Serializable]
        private sealed class SaveDocument
        {
            public int saveVersion;
            public int generatorVersion;
            public long savedUtcTicks;
            public List<BlockEditRecord> edits = new List<BlockEditRecord>();
            public InventoryRecord inventory = new InventoryRecord();
        }

        private readonly object sync = new object();
        private readonly Dictionary<VoxelAddress, BlockState> edits =
            new Dictionary<VoxelAddress, BlockState>();
        private InventoryRecord inventory = new InventoryRecord();
        private int saveVersion = 1;
        private int generatorVersion = 1;
        private string savePath;
        private bool dirty;

        public int EditCount
        {
            get { lock (sync) return edits.Count; }
        }

        public void Configure(int requestedSaveVersion, int requestedGeneratorVersion)
        {
            saveVersion = Math.Max(1, requestedSaveVersion);
            generatorVersion = Math.Max(1, requestedGeneratorVersion);
            savePath = Path.Combine(Application.persistentDataPath, "VoxelUniverse", "world-v1.json");
            Load();
        }

        public bool TryGetEdit(VoxelAddress address, out BlockState state)
        {
            lock (sync) return edits.TryGetValue(address, out state);
        }

        public void SetEdit(VoxelAddress address, BlockState state)
        {
            lock (sync)
            {
                edits[address] = state;
                dirty = true;
            }
        }

        public InventoryRecord GetInventoryCopy()
        {
            lock (sync)
            {
                InventoryRecord copy = new InventoryRecord();
                copy.selectedSlot = inventory.selectedSlot;
                for (int i = 0; i < inventory.slots.Count; i++)
                {
                    ItemStackRecord source = inventory.slots[i];
                    copy.slots.Add(new ItemStackRecord
                    {
                        slot = source.slot,
                        blockId = source.blockId,
                        count = source.count
                    });
                }
                return copy;
            }
        }

        public void SetInventory(InventoryRecord record)
        {
            lock (sync)
            {
                inventory = record ?? new InventoryRecord();
                dirty = true;
            }
        }

        public void SaveNow()
        {
            SaveDocument document = new SaveDocument();
            lock (sync)
            {
                if (string.IsNullOrEmpty(savePath))
                    savePath = Path.Combine(Application.persistentDataPath, "VoxelUniverse", "world-v1.json");
                document.saveVersion = saveVersion;
                document.generatorVersion = generatorVersion;
                document.savedUtcTicks = DateTime.UtcNow.Ticks;
                foreach (KeyValuePair<VoxelAddress, BlockState> pair in edits)
                {
                    document.edits.Add(new BlockEditRecord
                    {
                        bodyId = pair.Key.bodyId.ToString(),
                        face = (int)pair.Key.face,
                        u = pair.Key.u,
                        v = pair.Key.v,
                        radial = pair.Key.radial,
                        packedState = pair.Value.Packed
                    });
                }
                document.inventory = GetInventoryCopy();
            }

            string directory = Path.GetDirectoryName(savePath);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
            string temporary = savePath + ".tmp";
            string backup = savePath + ".bak";
            string json = JsonUtility.ToJson(document, true);
            File.WriteAllText(temporary, json);
            if (File.Exists(savePath))
            {
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(savePath, backup);
            }
            File.Move(temporary, savePath);
            lock (sync) dirty = false;
        }

        private void Load()
        {
            lock (sync)
            {
                edits.Clear();
                inventory = new InventoryRecord();
                dirty = false;
            }

            if (string.IsNullOrEmpty(savePath) || !File.Exists(savePath)) return;
            try
            {
                SaveDocument document = JsonUtility.FromJson<SaveDocument>(File.ReadAllText(savePath));
                if (document == null) return;
                lock (sync)
                {
                    if (document.edits != null)
                    {
                        for (int i = 0; i < document.edits.Count; i++)
                        {
                            BlockEditRecord edit = document.edits[i];
                            CelestialBodyId body;
                            if (!CelestialBodyId.TryParse(edit.bodyId, out body)) continue;
                            VoxelAddress address = new VoxelAddress(
                                body,
                                (CubeSphereFace)edit.face,
                                edit.u,
                                edit.v,
                                edit.radial);
                            edits[address] = BlockState.FromPacked(edit.packedState);
                        }
                    }
                    inventory = document.inventory ?? new InventoryRecord();
                }
            }
            catch (Exception exception)
            {
                string corruptPath = savePath + ".corrupt-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(savePath, corruptPath, true); }
                catch { }
                Debug.LogError("[Voxel Universe] Save could not be loaded. A copy was preserved at " + corruptPath);
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
