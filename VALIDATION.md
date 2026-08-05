# Validation notes

Checks performed before packaging v0.1.0:

- All example and metadata JSON files parse successfully.
- C# source delimiter and string/comment balance was checked across all Editor scripts.
- The explicit Git fetch refspec used by the bridge was tested against a temporary bare remote; work-branch diff and `git show` retrieval returned the expected changed C# file.
- The separate status-branch strategy was tested against a temporary bare remote; the first report committed and pushed, while an unchanged second pass produced no duplicate commit.
- File-application paths are restricted to the Unity project and approved text extensions.
- Duplicate application of the same remote work commit is blocked until the accepted project is committed and the work branch is synchronized.
- Local uncommitted file overwrites and deletions are disabled by default.
- Editor log and compiler paths are redacted to `<PROJECT_ROOT>` and `<USER_HOME>` before publication.

This environment does not contain the Unity Editor or Windows PowerShell, so the Unity 2021.3 compilation/import and Windows relay execution still require the first installation test inside the user's project. The bridge is designed to publish any resulting compiler errors through its status files after Unity imports it.
