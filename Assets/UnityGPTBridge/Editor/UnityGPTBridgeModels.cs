using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace UnityGPTBridge.Editor
{
    [Serializable]
    internal sealed class UnityGPTSnapshot
    {
        public string schemaVersion = "1.0";
        public string generatedUtc;
        public UnityGPTProjectInfo project = new UnityGPTProjectInfo();
        public UnityGPTEditorState editor = new UnityGPTEditorState();
        public UnityGPTSceneInfo scene = new UnityGPTSceneInfo();
        public List<UnityGPTHierarchyNode> hierarchy = new List<UnityGPTHierarchyNode>();
        public List<UnityGPTSelectionInfo> selection = new List<UnityGPTSelectionInfo>();
        public List<UnityGPTLogEntry> recentLogs = new List<UnityGPTLogEntry>();
        public string editorLogTailFile;
        public string packageManifestFile;
        public string notes;
    }

    [Serializable]
    internal sealed class UnityGPTProjectInfo
    {
        public string name;
        public string rootPath;
        public string unityVersion;
        public string buildTarget;
        public string renderPipeline;
        public string productName;
        public string companyName;
        public string scriptingBackend;
        public string apiCompatibilityLevel;
        public string colorSpace;
    }

    [Serializable]
    internal sealed class UnityGPTEditorState
    {
        public bool isPlaying;
        public bool isPaused;
        public bool isCompiling;
        public bool isUpdating;
        public bool hasUnsavedScenes;
        public string compileStartedUtc;
        public string compileFinishedUtc;
        public int compileErrorCount;
        public int compileWarningCount;
        public string lastApplyUtc;
        public string lastAppliedRemoteCommit;
    }

    [Serializable]
    internal sealed class UnityGPTSceneInfo
    {
        public string name;
        public string path;
        public bool isLoaded;
        public bool isDirty;
        public int rootObjectCount;
        public int loadedSceneCount;
    }

    [Serializable]
    internal sealed class UnityGPTHierarchyNode
    {
        public string path;
        public string name;
        public bool activeSelf;
        public bool activeInHierarchy;
        public string tag;
        public int layer;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale;
        public int childCount;
        public List<string> components = new List<string>();
    }

    [Serializable]
    internal sealed class UnityGPTSelectionInfo
    {
        public string name;
        public string hierarchyPath;
        public string assetPath;
        public string type;
        public int instanceId;
        public List<string> components = new List<string>();
    }

    [Serializable]
    internal sealed class UnityGPTLogEntry
    {
        public string timestampUtc;
        public string type;
        public string message;
        public string stackTrace;
    }

    [Serializable]
    internal sealed class UnityGPTCompileReport
    {
        public string schemaVersion = "1.0";
        public string startedUtc;
        public string finishedUtc;
        public bool compiling;
        public int errorCount;
        public int warningCount;
        public List<UnityGPTCompilerMessage> messages = new List<UnityGPTCompilerMessage>();
    }

    [Serializable]
    internal sealed class UnityGPTCompilerMessage
    {
        public string assembly;
        public string type;
        public string message;
        public string file;
        public int line;
        public int column;
    }

    [Serializable]
    internal sealed class UnityGPTCommandBatch
    {
        public string schemaVersion = "1.0";
        public string requestId;
        public string description;
        public bool saveSceneAfter;
        public List<UnityGPTCommand> commands = new List<UnityGPTCommand>();
    }

    [Serializable]
    internal sealed class UnityGPTCommand
    {
        public string type;
        public string scenePath;
        public string objectPath;
        public string parentPath;
        public string name;
        public string componentType;
        public string propertyPath;
        public string valueType;
        public string stringValue;
        public string assetPath;
        public int intValue;
        public float floatValue;
        public bool boolValue;
        public Vector2 vector2Value;
        public Vector3 vector3Value;
        public Vector4 vector4Value;
        public Color colorValue = Color.white;
        public Vector3 position;
        public Vector3 rotationEuler;
        public Vector3 scale = Vector3.one;
        public bool active = true;
        public List<UnityGPTComponentSpec> components = new List<UnityGPTComponentSpec>();
        public List<UnityGPTPropertyAssignment> properties = new List<UnityGPTPropertyAssignment>();
    }

    [Serializable]
    internal sealed class UnityGPTComponentSpec
    {
        public string type;
        public List<UnityGPTPropertyAssignment> properties = new List<UnityGPTPropertyAssignment>();
    }

    [Serializable]
    internal sealed class UnityGPTPropertyAssignment
    {
        public string propertyPath;
        public string valueType;
        public string stringValue;
        public string assetPath;
        public int intValue;
        public float floatValue;
        public bool boolValue;
        public Vector2 vector2Value;
        public Vector3 vector3Value;
        public Vector4 vector4Value;
        public Color colorValue = Color.white;
    }

    [Serializable]
    internal sealed class UnityGPTCommandResult
    {
        public string schemaVersion = "1.0";
        public string requestId;
        public string executedUtc;
        public bool success;
        public string summary;
        public List<UnityGPTCommandResultItem> results = new List<UnityGPTCommandResultItem>();
    }

    [Serializable]
    internal sealed class UnityGPTCommandResultItem
    {
        public int index;
        public string type;
        public bool success;
        public string message;
    }

    [Serializable]
    internal sealed class UnityGPTApplyRecord
    {
        public string appliedUtc;
        public string remoteCommit;
        public string backupDirectory;
        public List<UnityGPTAppliedFile> files = new List<UnityGPTAppliedFile>();
    }

    [Serializable]
    internal sealed class UnityGPTAppliedFile
    {
        public string path;
        public string status;
        public bool existedBefore;
    }

    internal static class UnityGPTPaths
    {
        public static string ProjectRoot
        {
            get
            {
                return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            }
        }

        public static string BridgeRoot
        {
            get { return Path.Combine(ProjectRoot, ".unity-gpt"); }
        }

        public static string StatusDirectory
        {
            get { return Path.Combine(BridgeRoot, "status"); }
        }

        public static string InboxDirectory
        {
            get { return Path.Combine(BridgeRoot, "inbox"); }
        }

        public static string ProcessedDirectory
        {
            get { return Path.Combine(BridgeRoot, "processed"); }
        }

        public static string ResultsDirectory
        {
            get { return Path.Combine(BridgeRoot, "results"); }
        }

        public static string BackupsDirectory
        {
            get { return Path.Combine(BridgeRoot, "backups"); }
        }

        public static string LastApplyRecordPath
        {
            get { return Path.Combine(BridgeRoot, "last-apply.json"); }
        }

        public static string RelayStatePath
        {
            get { return Path.Combine(BridgeRoot, "relay-state.json"); }
        }

        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(BridgeRoot);
            Directory.CreateDirectory(StatusDirectory);
            Directory.CreateDirectory(InboxDirectory);
            Directory.CreateDirectory(ProcessedDirectory);
            Directory.CreateDirectory(ResultsDirectory);
            Directory.CreateDirectory(BackupsDirectory);
        }
    }

    internal static class UnityGPTJson
    {
        public static string UtcNow()
        {
            return DateTime.UtcNow.ToString("o");
        }

        public static void WritePretty<T>(string path, T value)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json = JsonUtility.ToJson(value, true);
            File.WriteAllText(path, json + Environment.NewLine);
        }

        public static T Read<T>(string path) where T : class
        {
            if (!File.Exists(path))
            {
                return null;
            }

            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<T>(json);
        }
    }
}
