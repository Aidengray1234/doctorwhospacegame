using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace UnityGPTBridge.Editor
{
    [InitializeOnLoad]
    internal static class UnityGPTSnapshotExporter
    {
        private const int MaxRecentLogs = 500;
        private static readonly object LogLock = new object();
        private static readonly List<UnityGPTLogEntry> RecentLogs = new List<UnityGPTLogEntry>();
        private static UnityGPTCompileReport CompileReport = new UnityGPTCompileReport();
        private static bool _exportQueued;
        private static double _nextAllowedExportTime;

        static UnityGPTSnapshotExporter()
        {
            UnityGPTPaths.EnsureDirectories();

            Application.logMessageReceived -= OnLogMessageReceived;
            Application.logMessageReceived += OnLogMessageReceived;

            CompilationPipeline.compilationStarted -= OnCompilationStarted;
            CompilationPipeline.compilationStarted += OnCompilationStarted;
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
            CompilationPipeline.compilationFinished -= OnCompilationFinished;
            CompilationPipeline.compilationFinished += OnCompilationFinished;

            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.hierarchyChanged -= QueueExport;
            EditorApplication.hierarchyChanged += QueueExport;
            Selection.selectionChanged -= QueueExport;
            Selection.selectionChanged += QueueExport;

            EditorApplication.delayCall += delegate
            {
                if (UnityGPTBridgeSettings.AutoExport)
                {
                    ExportAll("Editor loaded");
                }
            };
        }

        public static UnityGPTCompileReport CurrentCompileReport
        {
            get { return CompileReport; }
        }

        public static void QueueExport()
        {
            if (!UnityGPTBridgeSettings.AutoExport || _exportQueued)
            {
                return;
            }

            _exportQueued = true;
            _nextAllowedExportTime = EditorApplication.timeSinceStartup + 0.75d;
            EditorApplication.update -= DelayedExportUpdate;
            EditorApplication.update += DelayedExportUpdate;
        }

        public static void ExportAll(string reason)
        {
            UnityGPTPaths.EnsureDirectories();

            UnityGPTSnapshot snapshot = BuildSnapshot(reason);
            string snapshotPath = Path.Combine(UnityGPTPaths.StatusDirectory, "snapshot.json");
            UnityGPTJson.WritePretty(snapshotPath, snapshot);

            CompileReport.compiling = EditorApplication.isCompiling;
            UnityGPTJson.WritePretty(Path.Combine(UnityGPTPaths.StatusDirectory, "compile.json"), CompileReport);

            ExportEditorLogTail();
            ExportPackageManifest();
            File.WriteAllText(Path.Combine(UnityGPTPaths.StatusDirectory, "ready.flag"), UnityGPTJson.UtcNow() + Environment.NewLine);
        }

        public static List<UnityGPTLogEntry> GetRecentLogsCopy()
        {
            lock (LogLock)
            {
                return new List<UnityGPTLogEntry>(RecentLogs);
            }
        }

        private static UnityGPTSnapshot BuildSnapshot(string reason)
        {
            UnityGPTSnapshot snapshot = new UnityGPTSnapshot();
            snapshot.generatedUtc = UnityGPTJson.UtcNow();
            snapshot.notes = reason;

            string projectRoot = UnityGPTPaths.ProjectRoot;
            BuildTarget activeTarget = EditorUserBuildSettings.activeBuildTarget;
            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(activeTarget);

            snapshot.project.name = new DirectoryInfo(projectRoot).Name;
            snapshot.project.rootPath = ".";
            snapshot.project.unityVersion = Application.unityVersion;
            snapshot.project.buildTarget = activeTarget.ToString();
            snapshot.project.productName = PlayerSettings.productName;
            snapshot.project.companyName = PlayerSettings.companyName;
            snapshot.project.colorSpace = PlayerSettings.colorSpace.ToString();
            snapshot.project.renderPipeline = GraphicsSettings.currentRenderPipeline == null
                ? "Built-in Render Pipeline"
                : GraphicsSettings.currentRenderPipeline.GetType().FullName;

            try
            {
                snapshot.project.scriptingBackend = PlayerSettings.GetScriptingBackend(targetGroup).ToString();
                snapshot.project.apiCompatibilityLevel = PlayerSettings.GetApiCompatibilityLevel(targetGroup).ToString();
            }
            catch (Exception exception)
            {
                snapshot.project.scriptingBackend = "Unavailable: " + exception.Message;
                snapshot.project.apiCompatibilityLevel = "Unavailable";
            }

            snapshot.editor.isPlaying = EditorApplication.isPlaying;
            snapshot.editor.isPaused = EditorApplication.isPaused;
            snapshot.editor.isCompiling = EditorApplication.isCompiling;
            snapshot.editor.isUpdating = EditorApplication.isUpdating;
            snapshot.editor.hasUnsavedScenes = HasUnsavedScenes();
            snapshot.editor.compileStartedUtc = CompileReport.startedUtc;
            snapshot.editor.compileFinishedUtc = CompileReport.finishedUtc;
            snapshot.editor.compileErrorCount = CompileReport.errorCount;
            snapshot.editor.compileWarningCount = CompileReport.warningCount;

            UnityGPTApplyRecord applyRecord = UnityGPTJson.Read<UnityGPTApplyRecord>(UnityGPTPaths.LastApplyRecordPath);
            if (applyRecord != null)
            {
                snapshot.editor.lastApplyUtc = applyRecord.appliedUtc;
                snapshot.editor.lastAppliedRemoteCommit = applyRecord.remoteCommit;
            }

            Scene activeScene = SceneManager.GetActiveScene();
            snapshot.scene.name = activeScene.name;
            snapshot.scene.path = activeScene.path;
            snapshot.scene.isLoaded = activeScene.isLoaded;
            snapshot.scene.isDirty = activeScene.isDirty;
            snapshot.scene.rootObjectCount = activeScene.IsValid() && activeScene.isLoaded ? activeScene.rootCount : 0;
            snapshot.scene.loadedSceneCount = SceneManager.sceneCount;

            int objectBudget = UnityGPTBridgeSettings.MaxHierarchyObjects;
            int visited = 0;
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount && visited < objectBudget; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded)
                {
                    continue;
                }

                GameObject[] roots = scene.GetRootGameObjects();
                for (int rootIndex = 0; rootIndex < roots.Length && visited < objectBudget; rootIndex++)
                {
                    AddHierarchyRecursive(roots[rootIndex].transform, scene.name, snapshot.hierarchy, ref visited, objectBudget);
                }
            }

            UnityEngine.Object[] selected = Selection.objects;
            for (int i = 0; i < selected.Length; i++)
            {
                UnityEngine.Object item = selected[i];
                UnityGPTSelectionInfo selectedInfo = new UnityGPTSelectionInfo();
                selectedInfo.name = item == null ? "<null>" : item.name;
                selectedInfo.type = item == null ? "null" : item.GetType().FullName;
                selectedInfo.instanceId = item == null ? 0 : item.GetInstanceID();
                selectedInfo.assetPath = item == null ? string.Empty : AssetDatabase.GetAssetPath(item);

                GameObject gameObject = item as GameObject;
                if (gameObject == null && item is Component)
                {
                    gameObject = ((Component)item).gameObject;
                }

                if (gameObject != null)
                {
                    selectedInfo.hierarchyPath = GetHierarchyPath(gameObject.transform);
                    Component[] components = gameObject.GetComponents<Component>();
                    for (int componentIndex = 0; componentIndex < components.Length; componentIndex++)
                    {
                        Component component = components[componentIndex];
                        selectedInfo.components.Add(component == null ? "<Missing Script>" : component.GetType().FullName);
                    }
                }

                snapshot.selection.Add(selectedInfo);
            }

            snapshot.recentLogs = GetRecentLogsCopy();
            snapshot.editorLogTailFile = ".unity-gpt/status/editor-log-tail.txt";
            snapshot.packageManifestFile = ".unity-gpt/status/packages-manifest.json";
            return snapshot;
        }

        private static void AddHierarchyRecursive(
            Transform transform,
            string sceneName,
            List<UnityGPTHierarchyNode> output,
            ref int visited,
            int budget)
        {
            if (transform == null || visited >= budget)
            {
                return;
            }

            GameObject gameObject = transform.gameObject;
            UnityGPTHierarchyNode node = new UnityGPTHierarchyNode();
            node.path = sceneName + ":/" + GetHierarchyPath(transform);
            node.name = gameObject.name;
            node.activeSelf = gameObject.activeSelf;
            node.activeInHierarchy = gameObject.activeInHierarchy;
            node.tag = SafeGetTag(gameObject);
            node.layer = gameObject.layer;
            node.localPosition = transform.localPosition;
            node.localEulerAngles = transform.localEulerAngles;
            node.localScale = transform.localScale;
            node.childCount = transform.childCount;

            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                node.components.Add(component == null ? "<Missing Script>" : component.GetType().FullName);
            }

            output.Add(node);
            visited++;

            for (int childIndex = 0; childIndex < transform.childCount && visited < budget; childIndex++)
            {
                AddHierarchyRecursive(transform.GetChild(childIndex), sceneName, output, ref visited, budget);
            }
        }

        private static string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder(transform.name);
            Transform parent = transform.parent;
            while (parent != null)
            {
                builder.Insert(0, parent.name + "/");
                parent = parent.parent;
            }

            return builder.ToString();
        }

        private static string SafeGetTag(GameObject gameObject)
        {
            try
            {
                return gameObject.tag;
            }
            catch
            {
                return "<Invalid Tag>";
            }
        }

        private static bool HasUnsavedScenes()
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (SceneManager.GetSceneAt(i).isDirty)
                {
                    return true;
                }
            }

            return false;
        }

        private static void OnLogMessageReceived(string condition, string stackTrace, LogType type)
        {
            UnityGPTLogEntry entry = new UnityGPTLogEntry();
            entry.timestampUtc = UnityGPTJson.UtcNow();
            entry.type = type.ToString();
            entry.message = SanitizePaths(condition);
            entry.stackTrace = SanitizePaths(stackTrace);

            lock (LogLock)
            {
                RecentLogs.Add(entry);
                while (RecentLogs.Count > MaxRecentLogs)
                {
                    RecentLogs.RemoveAt(0);
                }
            }

            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
            {
                QueueExport();
            }
        }

        private static void OnCompilationStarted(object context)
        {
            CompileReport = new UnityGPTCompileReport();
            CompileReport.startedUtc = UnityGPTJson.UtcNow();
            CompileReport.compiling = true;
            QueueExport();
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null)
            {
                return;
            }

            for (int i = 0; i < messages.Length; i++)
            {
                CompilerMessage message = messages[i];
                UnityGPTCompilerMessage item = new UnityGPTCompilerMessage();
                item.assembly = assemblyPath;
                item.type = message.type.ToString();
                item.message = SanitizePaths(message.message);
                item.file = SanitizePaths(message.file);
                item.line = message.line;
                item.column = message.column;
                CompileReport.messages.Add(item);

                if (message.type == CompilerMessageType.Error)
                {
                    CompileReport.errorCount++;
                }
                else if (message.type == CompilerMessageType.Warning)
                {
                    CompileReport.warningCount++;
                }
            }
        }

        private static void OnCompilationFinished(object context)
        {
            CompileReport.compiling = false;
            CompileReport.finishedUtc = UnityGPTJson.UtcNow();
            EditorApplication.delayCall += delegate { ExportAll("Compilation finished"); };
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            QueueExport();
        }

        private static void DelayedExportUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextAllowedExportTime)
            {
                return;
            }

            EditorApplication.update -= DelayedExportUpdate;
            _exportQueued = false;
            ExportAll("Automatic state change");
        }

        private static void ExportEditorLogTail()
        {
            string outputPath = Path.Combine(UnityGPTPaths.StatusDirectory, "editor-log-tail.txt");
            try
            {
                string logPath = Application.consoleLogPath;
                if (string.IsNullOrEmpty(logPath) || !File.Exists(logPath))
                {
                    File.WriteAllText(outputPath, "Editor log path is unavailable." + Environment.NewLine);
                    return;
                }

                int requestedBytes = UnityGPTBridgeSettings.EditorLogTailKilobytes * 1024;
                using (FileStream stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    long start = Math.Max(0L, stream.Length - requestedBytes);
                    stream.Seek(start, SeekOrigin.Begin);
                    using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true))
                    {
                        string text = SanitizePaths(reader.ReadToEnd());
                        File.WriteAllText(outputPath, text, new UTF8Encoding(false));
                    }
                }
            }
            catch (Exception exception)
            {
                File.WriteAllText(outputPath, "Unable to read Editor.log: " + exception + Environment.NewLine);
            }
        }

        private static void ExportPackageManifest()
        {
            string source = Path.Combine(UnityGPTPaths.ProjectRoot, "Packages", "manifest.json");
            string destination = Path.Combine(UnityGPTPaths.StatusDirectory, "packages-manifest.json");
            try
            {
                if (File.Exists(source))
                {
                    File.Copy(source, destination, true);
                }
                else
                {
                    File.WriteAllText(destination, "{}" + Environment.NewLine);
                }
            }
            catch (Exception exception)
            {
                File.WriteAllText(destination, "{\"error\":\"" + EscapeJson(exception.Message) + "\"}" + Environment.NewLine);
            }
        }

        private static string SanitizePaths(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            string sanitized = value;
            string projectRoot = UnityGPTPaths.ProjectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!string.IsNullOrEmpty(projectRoot))
            {
                sanitized = sanitized.Replace(projectRoot, "<PROJECT_ROOT>");
                sanitized = sanitized.Replace(projectRoot.Replace('\\', '/'), "<PROJECT_ROOT>");
            }

            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrEmpty(userHome))
            {
                sanitized = sanitized.Replace(userHome, "<USER_HOME>");
                sanitized = sanitized.Replace(userHome.Replace('\\', '/'), "<USER_HOME>");
            }

            return sanitized;
        }

        private static string EscapeJson(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "").Replace("\n", "\\n");
        }
    }
}
