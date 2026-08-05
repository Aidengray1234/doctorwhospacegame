using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace UnityGPTBridge.Editor
{
    internal sealed class UnityGPTGitResult
    {
        public int exitCode;
        public string stdout;
        public string stderr;
        public bool timedOut;

        public bool Success
        {
            get { return exitCode == 0 && !timedOut; }
        }
    }

    internal sealed class UnityGPTGitChange
    {
        public string status;
        public string path;
        public string oldPath;
        public bool selected = true;
        public bool blocked;
        public string blockReason;
        public bool hasLocalChanges;
    }

    internal static class UnityGPTGit
    {
        public static UnityGPTGitResult Run(string arguments, int timeoutMilliseconds)
        {
            UnityGPTGitResult result = new UnityGPTGitResult();

            try
            {
                ProcessStartInfo info = new ProcessStartInfo();
                info.FileName = "git";
                info.Arguments = arguments;
                info.WorkingDirectory = UnityGPTPaths.ProjectRoot;
                info.UseShellExecute = false;
                info.CreateNoWindow = true;
                info.RedirectStandardOutput = true;
                info.RedirectStandardError = true;
                info.StandardOutputEncoding = Encoding.UTF8;
                info.StandardErrorEncoding = Encoding.UTF8;

                using (Process process = new Process())
                {
                    process.StartInfo = info;
                    process.Start();

                    string stdout = process.StandardOutput.ReadToEnd();
                    string stderr = process.StandardError.ReadToEnd();
                    bool exited = process.WaitForExit(timeoutMilliseconds);

                    if (!exited)
                    {
                        try { process.Kill(); } catch { }
                        result.exitCode = -1;
                        result.timedOut = true;
                        result.stdout = stdout;
                        result.stderr = "Git command timed out. " + stderr;
                        return result;
                    }

                    result.exitCode = process.ExitCode;
                    result.stdout = stdout;
                    result.stderr = stderr;
                    return result;
                }
            }
            catch (Exception exception)
            {
                result.exitCode = -1;
                result.stderr = exception.ToString();
                return result;
            }
        }

        public static UnityGPTGitResult Run(string arguments)
        {
            return Run(arguments, 30000);
        }

        public static bool IsRepository(out string message)
        {
            UnityGPTGitResult result = Run("rev-parse --show-toplevel");
            message = result.Success ? result.stdout.Trim() : result.stderr.Trim();
            return result.Success;
        }

        public static UnityGPTGitResult FetchWorkBranch()
        {
            string remote = UnityGPTBridgeSettings.RemoteName;
            string branch = UnityGPTBridgeSettings.WorkBranch;

            if (!UnityGPTSafety.IsBranchNameSafe(remote) || !UnityGPTSafety.IsBranchNameSafe(branch))
            {
                return new UnityGPTGitResult
                {
                    exitCode = -1,
                    stderr = "Remote or work branch contains unsupported characters."
                };
            }

            return Run("fetch --prune " + remote + " refs/heads/" + branch + ":refs/remotes/" + remote + "/" + branch, 120000);
        }

        public static string GetCurrentCommit()
        {
            UnityGPTGitResult result = Run("rev-parse HEAD");
            return result.Success ? result.stdout.Trim() : string.Empty;
        }

        public static string GetRemoteWorkCommit()
        {
            string remoteRef = GetRemoteWorkRef();
            UnityGPTGitResult result = Run("rev-parse " + remoteRef);
            return result.Success ? result.stdout.Trim() : string.Empty;
        }

        public static List<UnityGPTGitChange> ListWorkChanges(out string error)
        {
            error = null;
            List<UnityGPTGitChange> changes = new List<UnityGPTGitChange>();
            string remoteRef = GetRemoteWorkRef();
            string remoteCommit = GetRemoteWorkCommit();
            UnityGPTApplyRecord lastApply = UnityGPTJson.Read<UnityGPTApplyRecord>(UnityGPTPaths.LastApplyRecordPath);
            bool remoteCommitAlreadyApplied = lastApply != null
                                               && !string.IsNullOrEmpty(remoteCommit)
                                               && string.Equals(lastApply.remoteCommit, remoteCommit, StringComparison.OrdinalIgnoreCase);
            UnityGPTGitResult ancestry = Run("merge-base --is-ancestor HEAD " + remoteRef);
            bool workBranchIsStale = ancestry.exitCode == 1;

            UnityGPTGitResult result = Run("diff --name-status --find-renames HEAD.." + remoteRef);
            if (!result.Success)
            {
                error = result.stderr.Trim();
                return changes;
            }

            string[] lines = result.stdout.Replace("\r\n", "\n").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split('\t');
                if (parts.Length < 2)
                {
                    continue;
                }

                string rawStatus = parts[0];
                UnityGPTGitChange change = new UnityGPTGitChange();
                change.status = rawStatus;

                if (rawStatus.StartsWith("R", StringComparison.OrdinalIgnoreCase) && parts.Length >= 3)
                {
                    change.oldPath = NormalizeRepoPath(parts[1]);
                    change.path = NormalizeRepoPath(parts[2]);
                }
                else
                {
                    change.path = NormalizeRepoPath(parts[1]);
                }

                string fullPath;
                string reason;
                if (!UnityGPTSafety.TryGetSafeProjectPath(change.path, out fullPath, out reason))
                {
                    change.blocked = true;
                    change.blockReason = reason;
                    change.selected = false;
                }

                if (!string.IsNullOrEmpty(change.oldPath))
                {
                    string oldFullPath;
                    string oldReason;
                    if (!UnityGPTSafety.TryGetSafeProjectPath(change.oldPath, out oldFullPath, out oldReason))
                    {
                        change.blocked = true;
                        change.blockReason = "Rename source blocked: " + oldReason;
                        change.selected = false;
                    }
                }

                change.hasLocalChanges = HasLocalChanges(change.path)
                                         || (!string.IsNullOrEmpty(change.oldPath) && HasLocalChanges(change.oldPath));
                if (change.hasLocalChanges)
                {
                    change.selected = false;
                    if (string.IsNullOrEmpty(change.blockReason))
                    {
                        change.blockReason = "Local uncommitted changes exist. Commit/stash them or explicitly force the apply.";
                    }
                }

                if (rawStatus.StartsWith("D", StringComparison.OrdinalIgnoreCase))
                {
                    change.selected = false;
                    if (string.IsNullOrEmpty(change.blockReason))
                    {
                        change.blockReason = "Deletion requires explicit approval.";
                    }
                }

                if (workBranchIsStale)
                {
                    change.blocked = true;
                    change.selected = false;
                    string staleMessage = "The GPT work branch is not based on the current project commit. Commit your current work and use 'Sync Work Branch to Current HEAD' before accepting new edits.";
                    change.blockReason = string.IsNullOrEmpty(change.blockReason)
                        ? staleMessage
                        : change.blockReason + " " + staleMessage;
                }

                if (remoteCommitAlreadyApplied)
                {
                    change.blocked = true;
                    change.selected = false;
                    string duplicateMessage = "This exact work-branch commit was already applied. Test it, commit accepted project changes, then sync the work branch before applying more work.";
                    change.blockReason = string.IsNullOrEmpty(change.blockReason)
                        ? duplicateMessage
                        : change.blockReason + " " + duplicateMessage;
                }

                changes.Add(change);
            }

            return changes;
        }

        public static bool ApplyChanges(
            IList<UnityGPTGitChange> changes,
            bool allowDeletes,
            bool forceLocalOverwrite,
            out UnityGPTApplyRecord record,
            out string error)
        {
            record = new UnityGPTApplyRecord();
            error = null;
            record.appliedUtc = UnityGPTJson.UtcNow();
            record.remoteCommit = GetRemoteWorkCommit();
            record.backupDirectory = Path.Combine(UnityGPTPaths.BackupsDirectory, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Directory.CreateDirectory(record.backupDirectory);

            try
            {
                for (int i = 0; i < changes.Count; i++)
                {
                    UnityGPTGitChange change = changes[i];
                    if (!change.selected)
                    {
                        continue;
                    }

                    if (change.blocked)
                    {
                        throw new InvalidOperationException(change.path + " is blocked: " + change.blockReason);
                    }

                    if (change.hasLocalChanges && !forceLocalOverwrite)
                    {
                        throw new InvalidOperationException(change.path + " has local changes. Enable force overwrite only after reviewing them.");
                    }

                    bool isDelete = change.status.StartsWith("D", StringComparison.OrdinalIgnoreCase);
                    bool isRename = change.status.StartsWith("R", StringComparison.OrdinalIgnoreCase);

                    if ((isDelete || isRename) && !allowDeletes)
                    {
                        throw new InvalidOperationException(change.path + " requires deletion approval.");
                    }

                    if (isRename && !string.IsNullOrEmpty(change.oldPath))
                    {
                        BackupPath(change.oldPath, record);
                    }

                    BackupPath(change.path, record);

                    if (isDelete)
                    {
                        DeleteProjectFile(change.path);
                    }
                    else
                    {
                        string text;
                        string readError;
                        if (!TryReadRemoteText(change.path, out text, out readError))
                        {
                            throw new InvalidOperationException("Unable to read " + change.path + " from the work branch: " + readError);
                        }

                        string fullPath;
                        string reason;
                        if (!UnityGPTSafety.TryGetSafeProjectPath(change.path, out fullPath, out reason))
                        {
                            throw new InvalidOperationException(reason);
                        }

                        string directory = Path.GetDirectoryName(fullPath);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.WriteAllText(fullPath, text, new UTF8Encoding(false));

                        if (isRename && !string.IsNullOrEmpty(change.oldPath))
                        {
                            DeleteProjectFile(change.oldPath);
                        }
                    }
                }

                UnityGPTJson.WritePretty(UnityGPTPaths.LastApplyRecordPath, record);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                RestoreRecord(record, out _);
                return false;
            }
        }

        public static bool RevertLastApply(out string message)
        {
            UnityGPTApplyRecord record = UnityGPTJson.Read<UnityGPTApplyRecord>(UnityGPTPaths.LastApplyRecordPath);
            if (record == null)
            {
                message = "No last-apply record exists.";
                return false;
            }

            bool success = RestoreRecord(record, out message);
            if (success)
            {
                if (File.Exists(UnityGPTPaths.LastApplyRecordPath))
                {
                    File.Delete(UnityGPTPaths.LastApplyRecordPath);
                }
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }

            return success;
        }

        public static string GetCurrentBranch()
        {
            UnityGPTGitResult result = Run("branch --show-current");
            return result.Success ? result.stdout.Trim() : string.Empty;
        }

        public static string GetProjectChangeSummary()
        {
            UnityGPTGitResult result = Run("status --short -- Assets Packages ProjectSettings Tools AGENTS.md .gitignore");
            return result.Success ? result.stdout.Trim() : result.stderr.Trim();
        }

        public static bool CommitProjectChangesAndPush(string commitMessage, out string message)
        {
            message = null;
            string branch = GetCurrentBranch();
            if (string.IsNullOrEmpty(branch))
            {
                message = "The current Git branch could not be determined.";
                return false;
            }

            if (string.Equals(branch, UnityGPTBridgeSettings.WorkBranch, StringComparison.OrdinalIgnoreCase)
                || string.Equals(branch, UnityGPTBridgeSettings.StatusBranch, StringComparison.OrdinalIgnoreCase))
            {
                message = "Do not commit accepted project work while checked out on a bridge branch. Switch to your normal project branch first.";
                return false;
            }

            string summary = GetProjectChangeSummary();
            if (string.IsNullOrWhiteSpace(summary))
            {
                message = "There are no project changes to commit.";
                return false;
            }

            UnityGPTGitResult add = Run("add -A -- Assets Packages ProjectSettings Tools AGENTS.md .gitignore", 120000);
            if (!add.Success)
            {
                message = add.stderr.Trim();
                return false;
            }

            UnityGPTGitResult staged = Run("diff --cached --quiet");
            if (staged.exitCode == 0)
            {
                message = "There are no staged changes to commit.";
                return false;
            }
            if (staged.exitCode != 1)
            {
                message = staged.stderr.Trim();
                return false;
            }

            if (string.IsNullOrWhiteSpace(commitMessage))
            {
                commitMessage = "Apply reviewed Unity GPT changes";
            }

            UnityGPTGitResult commit = Run("commit -m " + Quote(commitMessage), 120000);
            if (!commit.Success)
            {
                message = commit.stderr.Trim();
                return false;
            }

            string remote = UnityGPTBridgeSettings.RemoteName;
            UnityGPTGitResult push = Run("push -u " + remote + " " + branch, 120000);
            if (!push.Success)
            {
                message = "The commit was created locally, but push failed: " + push.stderr.Trim();
                return false;
            }

            message = "Committed and pushed accepted project changes on " + branch + ".";
            return true;
        }

        public static bool SyncWorkBranchToCurrentHead(out string message)
        {
            message = null;
            string remote = UnityGPTBridgeSettings.RemoteName;
            string workBranch = UnityGPTBridgeSettings.WorkBranch;
            if (!UnityGPTSafety.IsBranchNameSafe(remote) || !UnityGPTSafety.IsBranchNameSafe(workBranch))
            {
                message = "Remote or work branch contains unsupported characters.";
                return false;
            }

            UnityGPTGitResult fetch = Run("fetch --prune " + remote + " refs/heads/" + workBranch + ":refs/remotes/" + remote + "/" + workBranch, 120000);
            if (!fetch.Success)
            {
                message = fetch.stderr.Trim();
                return false;
            }

            UnityGPTGitResult push = Run("push --force-with-lease " + remote + " HEAD:refs/heads/" + workBranch, 120000);
            if (!push.Success)
            {
                message = push.stderr.Trim();
                return false;
            }

            message = "Synced " + workBranch + " to the current accepted project commit.";
            return true;
        }

        private static bool RestoreRecord(UnityGPTApplyRecord record, out string message)
        {
            try
            {
                for (int i = record.files.Count - 1; i >= 0; i--)
                {
                    UnityGPTAppliedFile file = record.files[i];
                    string destination;
                    string reason;
                    if (!UnityGPTSafety.TryGetSafeProjectPath(file.path, out destination, out reason))
                    {
                        continue;
                    }

                    string backup = Path.Combine(record.backupDirectory, file.path.Replace('/', Path.DirectorySeparatorChar));
                    if (file.existedBefore && File.Exists(backup))
                    {
                        string directory = Path.GetDirectoryName(destination);
                        if (!string.IsNullOrEmpty(directory))
                        {
                            Directory.CreateDirectory(directory);
                        }

                        File.Copy(backup, destination, true);
                    }
                    else if (!file.existedBefore && File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                }

                message = "Restored files from " + record.backupDirectory;
                return true;
            }
            catch (Exception exception)
            {
                message = exception.Message;
                return false;
            }
        }

        private static void BackupPath(string repositoryPath, UnityGPTApplyRecord record)
        {
            string fullPath;
            string reason;
            if (!UnityGPTSafety.TryGetSafeProjectPath(repositoryPath, out fullPath, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            UnityGPTAppliedFile applied = new UnityGPTAppliedFile();
            applied.path = repositoryPath;
            applied.status = "backup";
            applied.existedBefore = File.Exists(fullPath);
            record.files.Add(applied);

            if (!applied.existedBefore)
            {
                return;
            }

            string backupPath = Path.Combine(record.backupDirectory, repositoryPath.Replace('/', Path.DirectorySeparatorChar));
            string directory = Path.GetDirectoryName(backupPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(fullPath, backupPath, true);
        }

        private static void DeleteProjectFile(string repositoryPath)
        {
            string fullPath;
            string reason;
            if (!UnityGPTSafety.TryGetSafeProjectPath(repositoryPath, out fullPath, out reason))
            {
                throw new InvalidOperationException(reason);
            }

            if (File.Exists(fullPath))
            {
                File.Delete(fullPath);
            }
        }

        private static bool TryReadRemoteText(string repositoryPath, out string text, out string error)
        {
            text = null;
            error = null;
            string remoteRef = GetRemoteWorkRef();
            string spec = remoteRef + ":" + repositoryPath.Replace('\\', '/');
            UnityGPTGitResult result = Run("show " + Quote(spec), 30000);
            if (!result.Success)
            {
                error = result.stderr.Trim();
                return false;
            }

            text = result.stdout;
            return true;
        }

        private static bool HasLocalChanges(string repositoryPath)
        {
            if (string.IsNullOrEmpty(repositoryPath))
            {
                return false;
            }

            UnityGPTGitResult result = Run("status --porcelain -- " + Quote(repositoryPath));
            return result.Success && !string.IsNullOrWhiteSpace(result.stdout);
        }

        private static string GetRemoteWorkRef()
        {
            return UnityGPTBridgeSettings.RemoteName + "/" + UnityGPTBridgeSettings.WorkBranch;
        }

        private static string NormalizeRepoPath(string path)
        {
            return path.Replace('\\', '/').Trim();
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
