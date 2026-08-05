using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UnityGPTBridge.Editor
{
    [InitializeOnLoad]
    internal static class UnityGPTCommandRunner
    {
        private static double _nextPollTime;
        private static bool _processing;

        static UnityGPTCommandRunner()
        {
            UnityGPTPaths.EnsureDirectories();
            EditorApplication.update -= PollInbox;
            EditorApplication.update += PollInbox;
        }

        public static int PendingCommandFileCount
        {
            get
            {
                if (!Directory.Exists(UnityGPTPaths.InboxDirectory))
                {
                    return 0;
                }

                return Directory.GetFiles(UnityGPTPaths.InboxDirectory, "*.json", SearchOption.TopDirectoryOnly).Length;
            }
        }

        public static void ProcessInboxNow()
        {
            if (_processing || EditorApplication.isCompiling)
            {
                return;
            }

            _processing = true;
            try
            {
                string[] files = Directory.GetFiles(UnityGPTPaths.InboxDirectory, "*.json", SearchOption.TopDirectoryOnly);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    ProcessCommandFile(files[i]);
                }
            }
            finally
            {
                _processing = false;
            }
        }

        private static void PollInbox()
        {
            if (EditorApplication.timeSinceStartup < _nextPollTime)
            {
                return;
            }

            _nextPollTime = EditorApplication.timeSinceStartup + 2.0d;
            if (PendingCommandFileCount > 0)
            {
                ProcessInboxNow();
            }
        }

        private static void ProcessCommandFile(string path)
        {
            UnityGPTCommandResult result = new UnityGPTCommandResult();
            result.executedUtc = UnityGPTJson.UtcNow();

            try
            {
                string json = File.ReadAllText(path);
                UnityGPTCommandBatch batch = JsonUtility.FromJson<UnityGPTCommandBatch>(json);
                if (batch == null)
                {
                    throw new InvalidDataException("The command file could not be parsed.");
                }

                result.requestId = string.IsNullOrEmpty(batch.requestId) ? Path.GetFileNameWithoutExtension(path) : batch.requestId;
                bool allSucceeded = true;

                for (int i = 0; i < batch.commands.Count; i++)
                {
                    UnityGPTCommand command = batch.commands[i];
                    UnityGPTCommandResultItem item = new UnityGPTCommandResultItem();
                    item.index = i;
                    item.type = command == null ? "<null>" : command.type;

                    try
                    {
                        item.message = ExecuteCommand(command);
                        item.success = true;
                    }
                    catch (Exception exception)
                    {
                        item.success = false;
                        item.message = exception.Message;
                        allSucceeded = false;
                    }

                    result.results.Add(item);
                }

                if (batch.saveSceneAfter)
                {
                    Scene scene = SceneManager.GetActiveScene();
                    if (scene.IsValid() && scene.isLoaded && !string.IsNullOrEmpty(scene.path))
                    {
                        EditorSceneManager.SaveScene(scene);
                    }
                }

                result.success = allSucceeded;
                result.summary = allSucceeded ? "All commands completed." : "One or more commands failed.";
            }
            catch (Exception exception)
            {
                result.success = false;
                result.summary = exception.ToString();
            }

            string safeRequestId = MakeSafeFileName(string.IsNullOrEmpty(result.requestId)
                ? Path.GetFileNameWithoutExtension(path)
                : result.requestId);
            string resultPath = Path.Combine(UnityGPTPaths.ResultsDirectory, safeRequestId + "-result.json");
            UnityGPTJson.WritePretty(resultPath, result);

            string processedName = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Path.GetFileName(path);
            string processedPath = Path.Combine(UnityGPTPaths.ProcessedDirectory, processedName);
            MoveReplacing(path, processedPath);

            UnityGPTSnapshotExporter.ExportAll("Command batch processed: " + result.requestId);
        }

        private static string ExecuteCommand(UnityGPTCommand command)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.type))
            {
                throw new InvalidOperationException("Command type is missing.");
            }

            string type = command.type.Trim().ToLowerInvariant();
            switch (type)
            {
                case "refresh_assets":
                    AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                    return "Asset database refresh requested.";

                case "save_active_scene":
                    return SaveActiveScene();

                case "enter_play_mode":
                    EditorApplication.isPlaying = true;
                    return "Play Mode requested.";

                case "stop_play_mode":
                    EditorApplication.isPlaying = false;
                    return "Play Mode stop requested.";

                case "pause_play_mode":
                    EditorApplication.isPaused = command.boolValue;
                    return "Pause state set to " + command.boolValue + ".";

                case "create_game_object":
                    return CreateGameObject(command);

                case "add_component":
                    return AddComponent(command);

                case "set_component_property":
                case "set_property":
                    return SetComponentProperty(command);

                case "select_object":
                    return SelectObject(command);

                case "create_scene":
                    return CreateScene(command);

                case "open_scene":
                    return OpenScene(command);

                case "capture_game_view":
                    return CaptureGameView(command);

                case "create_material":
                    return CreateMaterial(command);

                default:
                    throw new NotSupportedException("Unsupported command type: " + command.type);
            }
        }

        private static string SaveActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded)
            {
                throw new InvalidOperationException("No valid active scene is loaded.");
            }

            if (string.IsNullOrEmpty(scene.path))
            {
                throw new InvalidOperationException("The active scene has never been saved. Save it manually once before remote commands can save it.");
            }

            bool saved = EditorSceneManager.SaveScene(scene);
            if (!saved)
            {
                throw new InvalidOperationException("Unity did not save the active scene.");
            }

            return "Saved " + scene.path;
        }

        private static string CreateGameObject(UnityGPTCommand command)
        {
            if (string.IsNullOrWhiteSpace(command.name))
            {
                throw new InvalidOperationException("create_game_object requires a name.");
            }

            Transform parent = null;
            if (!string.IsNullOrWhiteSpace(command.parentPath))
            {
                GameObject parentObject = FindGameObject(command.parentPath);
                if (parentObject == null)
                {
                    throw new InvalidOperationException("Parent object was not found: " + command.parentPath);
                }

                parent = parentObject.transform;
            }

            GameObject gameObject = new GameObject(command.name);
            Undo.RegisterCreatedObjectUndo(gameObject, "Unity GPT: Create " + command.name);
            if (parent != null)
            {
                Undo.SetTransformParent(gameObject.transform, parent, "Unity GPT: Parent " + command.name);
            }

            gameObject.transform.localPosition = command.position;
            gameObject.transform.localEulerAngles = command.rotationEuler;
            gameObject.transform.localScale = command.scale;
            gameObject.SetActive(command.active);

            for (int i = 0; i < command.components.Count; i++)
            {
                UnityGPTComponentSpec componentSpec = command.components[i];
                Component component = AddComponentByName(gameObject, componentSpec.type);
                ApplyProperties(component, componentSpec.properties);
            }

            Selection.activeGameObject = gameObject;
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            return "Created GameObject " + GetHierarchyPath(gameObject.transform) + ".";
        }

        private static string AddComponent(UnityGPTCommand command)
        {
            GameObject gameObject = RequireGameObject(command.objectPath);
            Component component = AddComponentByName(gameObject, command.componentType);
            ApplyProperties(component, command.properties);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            return "Added " + component.GetType().FullName + " to " + GetHierarchyPath(gameObject.transform) + ".";
        }

        private static string SetComponentProperty(UnityGPTCommand command)
        {
            GameObject gameObject = RequireGameObject(command.objectPath);
            Component component = FindComponent(gameObject, command.componentType);
            if (component == null)
            {
                throw new InvalidOperationException("Component not found on " + command.objectPath + ": " + command.componentType);
            }

            UnityGPTPropertyAssignment property = new UnityGPTPropertyAssignment();
            property.propertyPath = command.propertyPath;
            property.valueType = command.valueType;
            property.stringValue = command.stringValue;
            property.assetPath = command.assetPath;
            property.intValue = command.intValue;
            property.floatValue = command.floatValue;
            property.boolValue = command.boolValue;
            property.vector2Value = command.vector2Value;
            property.vector3Value = command.vector3Value;
            property.vector4Value = command.vector4Value;
            property.colorValue = command.colorValue;

            ApplyProperty(component, property);
            EditorSceneManager.MarkSceneDirty(gameObject.scene);
            return "Set " + command.componentType + "." + command.propertyPath + " on " + command.objectPath + ".";
        }

        private static string SelectObject(UnityGPTCommand command)
        {
            GameObject gameObject = RequireGameObject(command.objectPath);
            Selection.activeGameObject = gameObject;
            EditorGUIUtility.PingObject(gameObject);
            return "Selected " + GetHierarchyPath(gameObject.transform) + ".";
        }

        private static string CreateScene(UnityGPTCommand command)
        {
            string scenePath = command.scenePath;
            ValidateScenePath(scenePath);
            if (File.Exists(Path.Combine(UnityGPTPaths.ProjectRoot, scenePath)))
            {
                throw new IOException("Scene already exists: " + scenePath);
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, scenePath))
            {
                throw new IOException("Unity could not save the new scene to " + scenePath);
            }

            return "Created scene " + scenePath + ".";
        }

        private static string OpenScene(UnityGPTCommand command)
        {
            ValidateScenePath(command.scenePath);
            if (!File.Exists(Path.Combine(UnityGPTPaths.ProjectRoot, command.scenePath)))
            {
                throw new FileNotFoundException("Scene not found.", command.scenePath);
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                throw new OperationCanceledException("Opening the scene was canceled because current changes were not saved.");
            }

            EditorSceneManager.OpenScene(command.scenePath, OpenSceneMode.Single);
            return "Opened scene " + command.scenePath + ".";
        }

        private static string CaptureGameView(UnityGPTCommand command)
        {
            string capturesDirectory = Path.Combine(UnityGPTPaths.StatusDirectory, "captures");
            Directory.CreateDirectory(capturesDirectory);
            string requestedName = string.IsNullOrWhiteSpace(command.name)
                ? "game-view-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".png"
                : MakeSafeFileName(command.name);
            if (!requestedName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                requestedName += ".png";
            }

            string path = Path.Combine(capturesDirectory, requestedName);
            ScreenCapture.CaptureScreenshot(path);
            return "Screenshot requested at .unity-gpt/status/captures/" + requestedName + ". Unity writes it after the current frame.";
        }

        private static string CreateMaterial(UnityGPTCommand command)
        {
            string assetPath = command.assetPath;
            if (string.IsNullOrWhiteSpace(assetPath) || !assetPath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || !assetPath.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("create_material requires an Assets/.../*.mat assetPath.");
            }

            if (AssetDatabase.LoadAssetAtPath<Material>(assetPath) != null)
            {
                throw new IOException("A material already exists at " + assetPath);
            }

            string shaderName = string.IsNullOrWhiteSpace(command.stringValue) ? "Standard" : command.stringValue;
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException("Shader not found: " + shaderName);
            }

            string directory = Path.GetDirectoryName(Path.Combine(UnityGPTPaths.ProjectRoot, assetPath));
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            Material material = new Material(shader);
            material.name = string.IsNullOrWhiteSpace(command.name) ? Path.GetFileNameWithoutExtension(assetPath) : command.name;
            AssetDatabase.CreateAsset(material, assetPath);
            AssetDatabase.SaveAssets();
            return "Created material " + assetPath + " using shader " + shaderName + ".";
        }

        private static Component AddComponentByName(GameObject gameObject, string componentTypeName)
        {
            Type componentType = ResolveComponentType(componentTypeName);
            if (componentType == null)
            {
                throw new InvalidOperationException("Component type could not be resolved: " + componentTypeName);
            }

            if (componentType == typeof(Transform) || componentType == typeof(RectTransform))
            {
                throw new InvalidOperationException("Transform types cannot be added with this command.");
            }

            Component component = Undo.AddComponent(gameObject, componentType);
            if (component == null)
            {
                throw new InvalidOperationException("Unity failed to add " + componentType.FullName + ".");
            }

            return component;
        }

        private static Type ResolveComponentType(string componentTypeName)
        {
            if (string.IsNullOrWhiteSpace(componentTypeName))
            {
                return null;
            }

            Type direct = Type.GetType(componentTypeName, false);
            if (direct != null && typeof(Component).IsAssignableFrom(direct))
            {
                return direct;
            }

            foreach (Type type in TypeCache.GetTypesDerivedFrom<Component>())
            {
                if (string.Equals(type.FullName, componentTypeName, StringComparison.Ordinal)
                    || string.Equals(type.Name, componentTypeName, StringComparison.Ordinal))
                {
                    return type;
                }
            }

            return null;
        }

        private static Component FindComponent(GameObject gameObject, string componentTypeName)
        {
            Type type = ResolveComponentType(componentTypeName);
            if (type != null)
            {
                return gameObject.GetComponent(type);
            }

            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                if (component != null && (component.GetType().Name == componentTypeName || component.GetType().FullName == componentTypeName))
                {
                    return component;
                }
            }

            return null;
        }

        private static void ApplyProperties(Component component, List<UnityGPTPropertyAssignment> properties)
        {
            if (component == null || properties == null)
            {
                return;
            }

            for (int i = 0; i < properties.Count; i++)
            {
                ApplyProperty(component, properties[i]);
            }
        }

        private static void ApplyProperty(Component component, UnityGPTPropertyAssignment assignment)
        {
            if (assignment == null || string.IsNullOrWhiteSpace(assignment.propertyPath))
            {
                throw new InvalidOperationException("A property assignment is missing propertyPath.");
            }

            SerializedObject serializedObject = new SerializedObject(component);
            SerializedProperty property = serializedObject.FindProperty(assignment.propertyPath);
            if (property == null)
            {
                throw new InvalidOperationException("Serialized property not found: " + component.GetType().FullName + "." + assignment.propertyPath);
            }

            Undo.RecordObject(component, "Unity GPT: Set " + assignment.propertyPath);
            string valueType = string.IsNullOrWhiteSpace(assignment.valueType)
                ? InferValueType(property)
                : assignment.valueType.Trim().ToLowerInvariant();

            switch (valueType)
            {
                case "string":
                    property.stringValue = assignment.stringValue ?? string.Empty;
                    break;
                case "int":
                case "integer":
                    property.intValue = assignment.intValue;
                    break;
                case "float":
                    property.floatValue = assignment.floatValue;
                    break;
                case "bool":
                case "boolean":
                    property.boolValue = assignment.boolValue;
                    break;
                case "vector2":
                    property.vector2Value = assignment.vector2Value;
                    break;
                case "vector3":
                    property.vector3Value = assignment.vector3Value;
                    break;
                case "vector4":
                    property.vector4Value = assignment.vector4Value;
                    break;
                case "color":
                    property.colorValue = assignment.colorValue;
                    break;
                case "enum":
                    SetEnum(property, assignment);
                    break;
                case "object":
                case "objectreference":
                case "asset":
                    property.objectReferenceValue = string.IsNullOrWhiteSpace(assignment.assetPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assignment.assetPath);
                    if (!string.IsNullOrWhiteSpace(assignment.assetPath) && property.objectReferenceValue == null)
                    {
                        throw new FileNotFoundException("Referenced asset could not be loaded.", assignment.assetPath);
                    }
                    break;
                default:
                    throw new NotSupportedException("Unsupported valueType: " + assignment.valueType);
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(component);
        }

        private static string InferValueType(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.String: return "string";
                case SerializedPropertyType.Integer: return "int";
                case SerializedPropertyType.Float: return "float";
                case SerializedPropertyType.Boolean: return "bool";
                case SerializedPropertyType.Vector2: return "vector2";
                case SerializedPropertyType.Vector3: return "vector3";
                case SerializedPropertyType.Vector4: return "vector4";
                case SerializedPropertyType.Color: return "color";
                case SerializedPropertyType.Enum: return "enum";
                case SerializedPropertyType.ObjectReference: return "objectreference";
                default: return property.propertyType.ToString().ToLowerInvariant();
            }
        }

        private static void SetEnum(SerializedProperty property, UnityGPTPropertyAssignment assignment)
        {
            if (!string.IsNullOrWhiteSpace(assignment.stringValue))
            {
                string[] names = property.enumNames;
                for (int i = 0; i < names.Length; i++)
                {
                    if (string.Equals(names[i], assignment.stringValue, StringComparison.OrdinalIgnoreCase))
                    {
                        property.enumValueIndex = i;
                        return;
                    }
                }

                throw new InvalidOperationException("Enum value not found: " + assignment.stringValue);
            }

            property.enumValueIndex = assignment.intValue;
        }

        private static GameObject RequireGameObject(string objectPath)
        {
            GameObject gameObject = FindGameObject(objectPath);
            if (gameObject == null)
            {
                throw new InvalidOperationException("GameObject not found: " + objectPath);
            }

            return gameObject;
        }

        private static GameObject FindGameObject(string objectPath)
        {
            if (string.IsNullOrWhiteSpace(objectPath))
            {
                return null;
            }

            string sceneName = null;
            string hierarchyPath = objectPath.Trim();
            int sceneSeparator = hierarchyPath.IndexOf(":/", StringComparison.Ordinal);
            if (sceneSeparator >= 0)
            {
                sceneName = hierarchyPath.Substring(0, sceneSeparator);
                hierarchyPath = hierarchyPath.Substring(sceneSeparator + 2);
            }

            hierarchyPath = hierarchyPath.Trim('/');
            for (int sceneIndex = 0; sceneIndex < SceneManager.sceneCount; sceneIndex++)
            {
                Scene scene = SceneManager.GetSceneAt(sceneIndex);
                if (!scene.isLoaded || (!string.IsNullOrEmpty(sceneName) && !string.Equals(scene.name, sceneName, StringComparison.Ordinal)))
                {
                    continue;
                }

                GameObject found = FindInScene(scene, hierarchyPath);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static GameObject FindInScene(Scene scene, string hierarchyPath)
        {
            string[] segments = hierarchyPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                return null;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            for (int i = 0; i < roots.Length; i++)
            {
                if (!string.Equals(roots[i].name, segments[0], StringComparison.Ordinal))
                {
                    continue;
                }

                Transform current = roots[i].transform;
                bool found = true;
                for (int segmentIndex = 1; segmentIndex < segments.Length; segmentIndex++)
                {
                    Transform child = current.Find(segments[segmentIndex]);
                    if (child == null)
                    {
                        found = false;
                        break;
                    }

                    current = child;
                }

                if (found)
                {
                    return current.gameObject;
                }
            }

            return null;
        }

        private static string GetHierarchyPath(Transform transform)
        {
            string path = transform.name;
            Transform parent = transform.parent;
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }

            return path;
        }

        private static void ValidateScenePath(string scenePath)
        {
            if (string.IsNullOrWhiteSpace(scenePath)
                || !scenePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                || !scenePath.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)
                || scenePath.Contains(".."))
            {
                throw new InvalidOperationException("Scene path must be an Assets/.../*.unity path without parent traversal.");
            }
        }

        private static string MakeSafeFileName(string value)
        {
            string safe = value;
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < invalid.Length; i++)
            {
                safe = safe.Replace(invalid[i], '_');
            }

            return safe;
        }

        private static void MoveReplacing(string source, string destination)
        {
            string directory = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            if (File.Exists(destination))
            {
                File.Delete(destination);
            }

            File.Move(source, destination);
        }
    }
}
