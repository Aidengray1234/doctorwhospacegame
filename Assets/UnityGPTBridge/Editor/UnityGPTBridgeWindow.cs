using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace UnityGPTBridge.Editor
{
    internal sealed class UnityGPTBridgeWindow : EditorWindow
    {
        private Vector2 _scroll;
        private Vector2 _changesScroll;
        private List<UnityGPTGitChange> _changes = new List<UnityGPTGitChange>();
        private string _gitMessage = "Not checked yet.";
        private string _remoteCommit = string.Empty;
        private bool _allowDeletes;
        private bool _forceLocalOverwrite;
        private bool _showAdvanced;
        private string _commitMessage = "Apply reviewed Unity GPT changes";

        [MenuItem("Tools/Unity GPT Bridge")]
        public static void OpenWindow()
        {
            UnityGPTBridgeWindow window = GetWindow<UnityGPTBridgeWindow>();
            window.titleContent = new GUIContent("Unity GPT Bridge");
            window.minSize = new Vector2(640f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            UnityGPTPaths.EnsureDirectories();
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            DrawHeader();
            EditorGUILayout.Space(8f);
            DrawUnityStatus();
            EditorGUILayout.Space(8f);
            DrawSnapshotControls();
            EditorGUILayout.Space(8f);
            DrawGitSettings();
            EditorGUILayout.Space(8f);
            DrawWorkBranchControls();
            EditorGUILayout.Space(8f);
            DrawCommandControls();
            EditorGUILayout.Space(8f);
            DrawAdvanced();

            EditorGUILayout.EndScrollView();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Unity GPT Bridge", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "This bridge exports Unity state to .unity-gpt/status and safely applies reviewed text changes from a GitHub work branch. " +
                "It does not expose unrestricted CMD access and never reads outside this Unity project.",
                MessageType.Info);
        }

        private void DrawUnityStatus()
        {
            EditorGUILayout.LabelField("Unity status", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Project", new DirectoryInfo(UnityGPTPaths.ProjectRoot).Name);
                EditorGUILayout.LabelField("Unity", Application.unityVersion);
                EditorGUILayout.LabelField("Compiling", EditorApplication.isCompiling ? "Yes" : "No");
                EditorGUILayout.LabelField("Play Mode", EditorApplication.isPlaying ? (EditorApplication.isPaused ? "Paused" : "Running") : "Stopped");
                EditorGUILayout.LabelField("Pending command files", UnityGPTCommandRunner.PendingCommandFileCount.ToString());

                UnityGPTCompileReport compile = UnityGPTSnapshotExporter.CurrentCompileReport;
                EditorGUILayout.LabelField("Last compile errors", compile.errorCount.ToString());
                EditorGUILayout.LabelField("Last compile warnings", compile.warningCount.ToString());
            }
        }

        private void DrawSnapshotControls()
        {
            EditorGUILayout.LabelField("Project reports", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Export Snapshot", GUILayout.Height(28f)))
                {
                    UnityGPTSnapshotExporter.ExportAll("Manual export");
                    ShowNotification(new GUIContent("Snapshot exported"));
                }

                if (GUILayout.Button("Open Status Folder", GUILayout.Height(28f)))
                {
                    EditorUtility.RevealInFinder(UnityGPTPaths.StatusDirectory);
                }

                if (GUILayout.Button("Start Git Relay", GUILayout.Height(28f)))
                {
                    StartRelay();
                }
            }

            bool autoExport = EditorGUILayout.ToggleLeft("Automatically export after hierarchy, selection, Play Mode, errors, and compilation changes", UnityGPTBridgeSettings.AutoExport);
            if (autoExport != UnityGPTBridgeSettings.AutoExport)
            {
                UnityGPTBridgeSettings.AutoExport = autoExport;
            }
        }

        private void DrawGitSettings()
        {
            EditorGUILayout.LabelField("GitHub bridge settings", EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                string remote = EditorGUILayout.TextField("Remote", UnityGPTBridgeSettings.RemoteName);
                string workBranch = EditorGUILayout.TextField("GPT work branch", UnityGPTBridgeSettings.WorkBranch);
                string statusBranch = EditorGUILayout.TextField("Unity status branch", UnityGPTBridgeSettings.StatusBranch);

                if (remote != UnityGPTBridgeSettings.RemoteName)
                {
                    UnityGPTBridgeSettings.RemoteName = remote.Trim();
                }

                if (workBranch != UnityGPTBridgeSettings.WorkBranch)
                {
                    UnityGPTBridgeSettings.WorkBranch = workBranch.Trim();
                }

                if (statusBranch != UnityGPTBridgeSettings.StatusBranch)
                {
                    UnityGPTBridgeSettings.StatusBranch = statusBranch.Trim();
                }

                string repositoryMessage;
                bool isRepository = UnityGPTGit.IsRepository(out repositoryMessage);
                EditorGUILayout.LabelField("Local repository", isRepository ? repositoryMessage : "Not initialized");

                if (!isRepository)
                {
                    if (GUILayout.Button("Initialize Git Repository"))
                    {
                        UnityGPTGitResult result = UnityGPTGit.Run("init");
                        _gitMessage = result.Success ? "Git repository initialized." : result.stderr;
                    }
                }

                EditorGUILayout.HelpBox(
                    "The relay pushes only .unity-gpt/status to the status branch. GPT code changes should be made on the work branch. " +
                    "This window previews and backs up those changes before applying them.",
                    MessageType.None);
            }
        }

        private void DrawWorkBranchControls()
        {
            EditorGUILayout.LabelField("GPT changes", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Fetch & Preview", GUILayout.Height(28f)))
                {
                    FetchAndPreview();
                }

                GUI.enabled = _changes.Count > 0;
                if (GUILayout.Button("Apply Selected", GUILayout.Height(28f)))
                {
                    ApplySelected();
                }

                if (GUILayout.Button("Revert Last Apply", GUILayout.Height(28f)))
                {
                    string message;
                    bool success = UnityGPTGit.RevertLastApply(out message);
                    EditorUtility.DisplayDialog(success ? "Reverted" : "Revert failed", message, "OK");
                    UnityGPTSnapshotExporter.ExportAll("Last GPT apply reverted");
                }
                GUI.enabled = true;
            }

            EditorGUILayout.HelpBox(_gitMessage, _gitMessage.IndexOf("error", StringComparison.OrdinalIgnoreCase) >= 0 ? MessageType.Error : MessageType.None);
            if (!string.IsNullOrEmpty(_remoteCommit))
            {
                EditorGUILayout.LabelField("Remote work commit", _remoteCommit);
            }

            EditorGUILayout.Space(4f);
            _commitMessage = EditorGUILayout.TextField("Accepted-change commit", _commitMessage);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Commit & Push Project Changes"))
                {
                    CommitAndPushProjectChanges();
                }

                if (GUILayout.Button("Sync Work Branch to Current HEAD"))
                {
                    SyncWorkBranch();
                }
            }
            EditorGUILayout.HelpBox(
                "After Unity compiles and you accept the result, commit and push it. Syncing the work branch gives GPT the latest accepted project before the next task.",
                MessageType.None);

            if (_changes.Count == 0)
            {
                EditorGUILayout.LabelField("No previewed changes.");
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select Safe"))
                {
                    for (int i = 0; i < _changes.Count; i++)
                    {
                        _changes[i].selected = !_changes[i].blocked && !_changes[i].hasLocalChanges
                                               && !_changes[i].status.StartsWith("D", StringComparison.OrdinalIgnoreCase);
                    }
                }

                if (GUILayout.Button("Select None"))
                {
                    for (int i = 0; i < _changes.Count; i++)
                    {
                        _changes[i].selected = false;
                    }
                }
            }

            _changesScroll = EditorGUILayout.BeginScrollView(_changesScroll, GUILayout.MinHeight(150f), GUILayout.MaxHeight(300f));
            for (int i = 0; i < _changes.Count; i++)
            {
                UnityGPTGitChange change = _changes[i];
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUI.enabled = !change.blocked;
                    change.selected = EditorGUILayout.Toggle(change.selected, GUILayout.Width(20f));
                    GUI.enabled = true;

                    GUILayout.Label(change.status, GUILayout.Width(48f));
                    GUILayout.Label(change.path, EditorStyles.wordWrappedLabel);
                }

                if (!string.IsNullOrEmpty(change.oldPath))
                {
                    EditorGUILayout.LabelField("    from: " + change.oldPath, EditorStyles.miniLabel);
                }

                if (!string.IsNullOrEmpty(change.blockReason))
                {
                    EditorGUILayout.HelpBox(change.blockReason, change.blocked || change.hasLocalChanges ? MessageType.Warning : MessageType.None);
                }
            }
            EditorGUILayout.EndScrollView();

            _allowDeletes = EditorGUILayout.ToggleLeft("Allow selected deletions and rename-source deletion", _allowDeletes);
            _forceLocalOverwrite = EditorGUILayout.ToggleLeft("Force overwrite files with local uncommitted changes (dangerous)", _forceLocalOverwrite);
        }

        private void DrawCommandControls()
        {
            EditorGUILayout.LabelField("Unity command inbox", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Process Inbox Now", GUILayout.Height(26f)))
                {
                    UnityGPTCommandRunner.ProcessInboxNow();
                }

                if (GUILayout.Button("Open Inbox Folder", GUILayout.Height(26f)))
                {
                    EditorUtility.RevealInFinder(UnityGPTPaths.InboxDirectory);
                }

                if (GUILayout.Button("Open Results Folder", GUILayout.Height(26f)))
                {
                    EditorUtility.RevealInFinder(UnityGPTPaths.ResultsDirectory);
                }
            }

            EditorGUILayout.HelpBox(
                "Command JSON files can safely create GameObjects, add components, set serialized properties, create/open scenes, create materials, " +
                "control Play Mode, refresh assets, save the active scene, select objects, and request a Game View screenshot.",
                MessageType.None);
        }

        private void DrawAdvanced()
        {
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced safety and export settings", true);
            if (!_showAdvanced)
            {
                return;
            }

            EditorGUI.indentLevel++;
            bool allowYaml = EditorGUILayout.ToggleLeft(
                "Allow direct .unity/.prefab/.asset/.mat YAML changes from Git (not recommended; object commands are safer)",
                UnityGPTBridgeSettings.AllowUnityYaml);
            if (allowYaml != UnityGPTBridgeSettings.AllowUnityYaml)
            {
                UnityGPTBridgeSettings.AllowUnityYaml = allowYaml;
            }

            int maxObjects = EditorGUILayout.IntField("Maximum exported hierarchy objects", UnityGPTBridgeSettings.MaxHierarchyObjects);
            if (maxObjects != UnityGPTBridgeSettings.MaxHierarchyObjects)
            {
                UnityGPTBridgeSettings.MaxHierarchyObjects = maxObjects;
            }

            int logKilobytes = EditorGUILayout.IntField("Editor.log tail (KB)", UnityGPTBridgeSettings.EditorLogTailKilobytes);
            if (logKilobytes != UnityGPTBridgeSettings.EditorLogTailKilobytes)
            {
                UnityGPTBridgeSettings.EditorLogTailKilobytes = logKilobytes;
            }
            EditorGUI.indentLevel--;
        }

        private void FetchAndPreview()
        {
            string repoMessage;
            if (!UnityGPTGit.IsRepository(out repoMessage))
            {
                _gitMessage = "This project is not a Git repository yet.";
                _changes.Clear();
                return;
            }

            if (!UnityGPTSafety.IsBranchNameSafe(UnityGPTBridgeSettings.RemoteName)
                || !UnityGPTSafety.IsBranchNameSafe(UnityGPTBridgeSettings.WorkBranch))
            {
                _gitMessage = "Remote or branch name contains unsupported characters.";
                return;
            }

            UnityGPTGitResult fetch = UnityGPTGit.FetchWorkBranch();
            if (!fetch.Success)
            {
                _gitMessage = "Fetch error: " + fetch.stderr.Trim();
                _changes.Clear();
                return;
            }

            string error;
            _changes = UnityGPTGit.ListWorkChanges(out error);
            _remoteCommit = UnityGPTGit.GetRemoteWorkCommit();
            _gitMessage = string.IsNullOrEmpty(error)
                ? (_changes.Count == 0 ? "The work branch has no changes relative to local HEAD." : "Review the files below before applying.")
                : "Preview error: " + error;
        }

        private void ApplySelected()
        {
            List<UnityGPTGitChange> selected = new List<UnityGPTGitChange>();
            for (int i = 0; i < _changes.Count; i++)
            {
                if (_changes[i].selected)
                {
                    selected.Add(_changes[i]);
                }
            }

            if (selected.Count == 0)
            {
                EditorUtility.DisplayDialog("Nothing selected", "Select one or more safe changes first.", "OK");
                return;
            }

            string warning = "Apply " + selected.Count + " selected change(s)?\n\n" +
                             "A full per-file backup will be saved under .unity-gpt/backups before anything is written.";
            if (_allowDeletes)
            {
                warning += "\n\nDeletion approval is ENABLED.";
            }
            if (_forceLocalOverwrite)
            {
                warning += "\n\nLocal overwrite is ENABLED.";
            }

            if (!EditorUtility.DisplayDialog("Apply GPT changes", warning, "Apply", "Cancel"))
            {
                return;
            }

            UnityGPTApplyRecord record;
            string error;
            bool success = UnityGPTGit.ApplyChanges(selected, _allowDeletes, _forceLocalOverwrite, out record, out error);
            if (!success)
            {
                EditorUtility.DisplayDialog("Apply failed", error + "\n\nThe bridge attempted to restore the backup automatically.", "OK");
                return;
            }

            UnityGPTSnapshotExporter.ExportAll("GPT work branch applied");
            EditorUtility.DisplayDialog(
                "Changes applied",
                "Applied files from " + record.remoteCommit + ".\n\nBackup: " + record.backupDirectory +
                "\n\nUnity has refreshed the Asset Database. Watch the compile report for errors.",
                "OK");
            FetchAndPreview();
        }

        private void CommitAndPushProjectChanges()
        {
            string summary = UnityGPTGit.GetProjectChangeSummary();
            if (string.IsNullOrWhiteSpace(summary))
            {
                EditorUtility.DisplayDialog("No project changes", "There are no changes under Assets, Packages, ProjectSettings, Tools, AGENTS.md, or .gitignore.", "OK");
                return;
            }

            string preview = summary.Length > 5000 ? summary.Substring(0, 5000) + "\n..." : summary;
            bool approved = EditorUtility.DisplayDialog(
                "Commit project changes",
                "This commits every listed project change, including manual edits you made yourself. Review this list first:\n\n" + preview,
                "Commit & Push",
                "Cancel");
            if (!approved)
            {
                return;
            }

            string message;
            bool success = UnityGPTGit.CommitProjectChangesAndPush(_commitMessage, out message);
            EditorUtility.DisplayDialog(success ? "Committed" : "Commit failed", message, "OK");
            if (success)
            {
                UnityGPTSnapshotExporter.ExportAll("Accepted project changes committed");
                FetchAndPreview();
            }
        }

        private void SyncWorkBranch()
        {
            bool approved = EditorUtility.DisplayDialog(
                "Sync GPT work branch",
                "This force-updates the remote work branch to your current accepted Git commit using --force-with-lease. Do this only after reviewing and committing the project, and only when no unaccepted GPT work remains.",
                "Sync Work Branch",
                "Cancel");
            if (!approved)
            {
                return;
            }

            string message;
            bool success = UnityGPTGit.SyncWorkBranchToCurrentHead(out message);
            EditorUtility.DisplayDialog(success ? "Work branch synced" : "Sync failed", message, "OK");
            if (success)
            {
                FetchAndPreview();
            }
        }

        private void StartRelay()
        {
            string script = Path.Combine(UnityGPTPaths.ProjectRoot, "Tools", "UnityGPTBridge", "Start-UnityGPT-Relay.bat");
            if (!File.Exists(script))
            {
                EditorUtility.DisplayDialog("Relay missing", "Could not find " + script, "OK");
                return;
            }

            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = script;
                info.WorkingDirectory = UnityGPTPaths.ProjectRoot;
                info.UseShellExecute = true;
                Process.Start(info);
            }
            catch (Exception exception)
            {
                EditorUtility.DisplayDialog("Unable to start relay", exception.Message, "OK");
            }
        }
    }
}
