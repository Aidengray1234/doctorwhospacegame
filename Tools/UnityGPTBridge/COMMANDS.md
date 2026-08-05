# Unity GPT Bridge command format

A command file is a JSON object containing a request ID and an ordered command list:

```json
{
  "schemaVersion": "1.0",
  "requestId": "unique-request-id",
  "description": "Human-readable explanation",
  "saveSceneAfter": false,
  "commands": []
}
```

Commands execute in order. Every command writes a success/failure item to `.unity-gpt/results/<request-id>-result.json`. The original command file moves to `.unity-gpt/processed`.

## Common object path format

Use either:

```text
Root/Child/Grandchild
```

or include a scene name:

```text
Playground:/Root/Child/Grandchild
```

Names must match the Hierarchy exactly.

## create_game_object

```json
{
  "type": "create_game_object",
  "name": "VoxelPlanet",
  "parentPath": "World",
  "position": { "x": 0, "y": 0, "z": 0 },
  "rotationEuler": { "x": 0, "y": 0, "z": 0 },
  "scale": { "x": 1, "y": 1, "z": 1 },
  "active": true,
  "components": [
    {
      "type": "VoxelPlanetGenerator",
      "properties": [
        { "propertyPath": "seed", "valueType": "int", "intValue": 12345 }
      ]
    }
  ]
}
```

## add_component

```json
{
  "type": "add_component",
  "objectPath": "VoxelPlanet",
  "componentType": "VoxelPlanetGenerator",
  "properties": [
    { "propertyPath": "planetRadius", "valueType": "float", "floatValue": 1000 }
  ]
}
```

## set_component_property

```json
{
  "type": "set_component_property",
  "objectPath": "Player",
  "componentType": "Rigidbody",
  "propertyPath": "m_Mass",
  "valueType": "float",
  "floatValue": 80
}
```

Supported `valueType` values:

```text
string
int
float
bool
vector2
vector3
vector4
color
enum
asset
objectReference
```

For asset references:

```json
{
  "propertyPath": "material",
  "valueType": "asset",
  "assetPath": "Assets/Materials/Planet.mat"
}
```

## create_scene

```json
{
  "type": "create_scene",
  "scenePath": "Assets/Scenes/PlanetTest.unity"
}
```

## open_scene

```json
{
  "type": "open_scene",
  "scenePath": "Assets/Scenes/PlanetTest.unity"
}
```

Unity asks before discarding unsaved scene changes.

## create_material

`stringValue` contains the shader name.

```json
{
  "type": "create_material",
  "assetPath": "Assets/Materials/PlanetSurface.mat",
  "name": "Planet Surface",
  "stringValue": "Standard"
}
```

## Other commands

```json
{ "type": "refresh_assets" }
{ "type": "save_active_scene" }
{ "type": "enter_play_mode" }
{ "type": "stop_play_mode" }
{ "type": "pause_play_mode", "boolValue": true }
{ "type": "select_object", "objectPath": "VoxelPlanet" }
{ "type": "capture_game_view", "name": "planet-orbit.png" }
```

## Serialized field names

`propertyPath` uses Unity's serialized field path, which is sometimes different from the public C# property name. Examples include `m_Mass` for `Rigidbody.mass`. Custom scripts are simplest when they expose `[SerializeField]` fields with stable names.
