# Unity GPT Bridge instructions

This repository is a Unity project controlled through a reviewed GitHub bridge.

## Branch roles

- `main` (or the user's normal project branch): accepted project code.
- `unity-gpt-status`: Unity-generated reports. Treat this branch as read-only.
- `unity-gpt-work`: propose code/configuration changes here. Never force-push unless the user explicitly approves it.

## Required workflow

1. Read `.unity-gpt/status/snapshot.json`, `.unity-gpt/status/compile.json`, `.unity-gpt/status/editor-log-tail.txt`, and `.unity-gpt/status/packages-manifest.json` from `unity-gpt-status` before diagnosing the project.
2. Read all related scripts before changing a system. Search references and assembly definitions.
3. Make the smallest coherent change on `unity-gpt-work`.
4. Do not edit files in `Library`, `Temp`, `Logs`, `obj`, `Build`, `Builds`, `UserSettings`, or generated IDE files.
5. Prefer C# and shader text changes. Do not directly edit `.unity`, `.prefab`, `.asset`, or `.mat` YAML unless the user explicitly enables advanced YAML changes.
6. Use `.unity-gpt/inbox/<request-id>.json` commands on `unity-gpt-work` for scene and GameObject operations.
7. Never place shell commands, executable files, secrets, tokens, or unrestricted computer-control instructions in the repository.
8. After proposing changes, tell the user to open **Tools > Unity GPT Bridge**, choose **Fetch & Preview**, review the diff, and choose **Apply Selected**.
9. Wait for a fresh status publish before claiming that code compiled, tests passed, or a visual result worked.
10. Add regression tests when a fixed bug can reasonably be tested.

## Safe command types

The Unity command inbox currently supports:

- `refresh_assets`
- `save_active_scene`
- `enter_play_mode`
- `stop_play_mode`
- `pause_play_mode`
- `create_game_object`
- `add_component`
- `set_component_property`
- `select_object`
- `create_scene`
- `open_scene`
- `capture_game_view`
- `create_material`

## Command batch example

```json
{
  "schemaVersion": "1.0",
  "requestId": "create-planet-root-001",
  "description": "Create the root object after the VoxelPlanetGenerator script compiles.",
  "saveSceneAfter": true,
  "commands": [
    {
      "type": "create_game_object",
      "name": "VoxelPlanet",
      "position": { "x": 0, "y": 0, "z": 0 },
      "rotationEuler": { "x": 0, "y": 0, "z": 0 },
      "scale": { "x": 1, "y": 1, "z": 1 },
      "active": true,
      "components": [
        {
          "type": "VoxelPlanetGenerator",
          "properties": [
            { "propertyPath": "planetRadius", "valueType": "float", "floatValue": 1000 },
            { "propertyPath": "seed", "valueType": "int", "intValue": 12345 }
          ]
        }
      ]
    }
  ]
}
```

Unity `SerializedProperty` paths must match serialized field names. Private fields require `[SerializeField]`. Object references should use `valueType: "asset"` and an `assetPath` under `Assets/`.

## Unity coding expectations

- Target the Unity version reported by the status branch.
- Keep editor-only code inside an `Editor` folder or editor-only assembly.
- Preserve `.meta` files when moving existing assets.
- Avoid per-frame allocations in performance-sensitive planet/chunk code.
- Procedural systems must expose deterministic seeds and useful debug statistics.
- Chunk generation must be cancellable, bounded, and safe across scene unload/domain reload.
- Use tests for deterministic generation, mesh index validity, chunk border agreement, save/load persistence, and LOD seams.
