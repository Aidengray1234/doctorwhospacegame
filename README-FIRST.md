> Version 0.1.1 fixes a Windows PowerShell issue where normal Git clone progress on stderr could stop the relay with a false fatal error.

# Unity GPT Bridge Starter v0.1.0

This starter connects a Unity project to normal ChatGPT through a private GitHub repository. It does **not** require Codex messages, Work mode, OpenAI API credit, a public server, port forwarding, or unrestricted remote CMD access.

The first version provides a useful reviewed loop:

1. Unity exports the project state, active scene hierarchy, selected objects, recent logs, compiler messages, package manifest, and Editor.log tail.
2. A local Git relay pushes those reports to `unity-gpt-status`.
3. ChatGPT reads the reports and writes code/configuration changes to `unity-gpt-work`.
4. The Unity window previews every changed file and creates backups before applying anything.
5. Unity refreshes and publishes the new compile result.
6. JSON commands can create GameObjects, add components, set serialized fields, create/open scenes and materials, control Play Mode, and request screenshots.

## Install

Back up the Unity project first. Extract this ZIP **into the Unity project root**—the folder that already contains:

```text
Assets
Packages
ProjectSettings
```

After extraction it should also contain:

```text
Assets/UnityGPTBridge
Tools/UnityGPTBridge
AGENTS.md
README-FIRST.md
```

Open the project and let Unity compile. Then open:

```text
Tools > Unity GPT Bridge
```

Click **Export Snapshot**. The status should appear under `.unity-gpt/status`.

## Recommended Unity settings

In **Edit > Project Settings > Editor** set:

- Version Control Mode: **Visible Meta Files**
- Asset Serialization Mode: **Force Text**

The bridge blocks direct Unity YAML edits by default. Force Text still improves Git history and manual review.

## Create the private GitHub repository

On GitHub, create a new **private, empty repository**. Do not add a README, `.gitignore`, or license on GitHub because the setup script makes the first commit.

Run:

```text
Tools/UnityGPTBridge/Setup-UnityGPT-GitHub.bat
```

Paste the repository URL when prompted, for example:

```text
https://github.com/your-name/doctorwhospacegame.git
```

The script:

- initializes Git if needed;
- creates a Unity-safe `.gitignore` block;
- commits `Assets`, `Packages`, `ProjectSettings`, `Tools`, and `AGENTS.md`;
- pushes the normal project branch;
- creates and pushes `unity-gpt-work`.

Git may open a browser sign-in prompt. The relay uses your normal Git credentials; it does not store a GitHub token.

## Start the relay

In Unity, click **Start Git Relay**, or run:

```text
Tools/UnityGPTBridge/Start-UnityGPT-Relay.bat
```

Leave the relay window open while working. It performs outbound Git HTTPS requests only. It does not listen on a public network port.

The relay publishes reports to `unity-gpt-status` and fetches `unity-gpt-work`. It never applies code automatically. The Unity window also provides explicit buttons to commit accepted project changes and synchronize the work branch after review.

## Connect GitHub to ChatGPT

Connect the private repository through ChatGPT's GitHub app/plugin. Then use a normal GPT chat and say:

```text
Use the connected GitHub repository for my Unity project.
Read the unity-gpt-status branch first and inspect the current Unity project,
active scene, compile report, logs, and relevant scripts. Do not change anything yet.
```

For a change:

```text
Fix the current Unity compile errors using the smallest safe changes.
Put all edits on unity-gpt-work. Do not directly edit scene or prefab YAML.
Use a Unity command JSON file for any GameObject or component changes.
```

After GPT updates the work branch:

1. Open **Tools > Unity GPT Bridge**.
2. Click **Fetch & Preview**.
3. Review every file.
4. Click **Apply Selected**.
5. Let Unity compile and test the result.
6. Click **Commit & Push Project Changes** once you accept it.
7. Click **Sync Work Branch to Current HEAD** before the next GPT task.
8. Keep the relay running so every result reaches `unity-gpt-status`.

## Safety behavior

The bridge allows reviewed text changes only inside:

- `Assets/`
- `Packages/`
- `ProjectSettings/`
- `.unity-gpt/`
- `AGENTS.md`
- `.gitignore`

It blocks:

- `Library`, `Temp`, `Logs`, `obj`, builds, and unrelated computer folders;
- absolute paths and `..` traversal;
- unsupported/binary file types;
- deletions unless separately enabled;
- overwriting local uncommitted changes unless separately forced;
- direct `.unity`, `.prefab`, `.asset`, and `.mat` changes unless advanced YAML mode is enabled.

Every apply creates a backup in `.unity-gpt/backups`. **Revert Last Apply** restores that backup.

## Supported Unity commands

Place command batches in `.unity-gpt/inbox` on `unity-gpt-work`. When applied, Unity processes them automatically and writes results to `.unity-gpt/results`.

Supported commands in v0.1.0:

```text
refresh_assets
save_active_scene
enter_play_mode
stop_play_mode
pause_play_mode
create_game_object
add_component
set_component_property
select_object
create_scene
open_scene
capture_game_view
create_material
```

See `Tools/UnityGPTBridge/COMMANDS.md` and the `Examples` folder.

## Current limitations

- This first build does not yet run Unity Test Framework tests or extract full Profiler frames automatically.
- Game View screenshot requests work best while Play Mode is running and a Game View exists.
- The log report contains logs observed since the bridge loaded plus a tail of `Editor.log`; it is not an exact clone of every Unity Console filter state.
- Only text/code changes are applied from Git. Large binary assets should be added manually or through a later asset-transfer feature.
- GPT cannot honestly claim a visual or runtime fix until Unity publishes a fresh result.

## Troubleshooting

**Unity does not show the Tools menu:** check Console compile errors and confirm the files are under `Assets/UnityGPTBridge/Editor`.

**Relay says work branch is unavailable:** run the GitHub setup script, or create and push `unity-gpt-work` from the project repository.

**Relay cannot push:** open a terminal in the project and run `git push` once to complete GitHub authentication.

**A file is blocked:** use a supported text file, or perform scene/object changes through command JSON. Keep advanced YAML mode disabled unless necessary.

**Local changes warning:** commit or stash your work before applying GPT edits. Forced overwrite exists only as an emergency option.
