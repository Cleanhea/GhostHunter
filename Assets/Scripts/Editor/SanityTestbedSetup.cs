using System;
using GhostHunter.Gameplay.Sanity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 집 서쪽(플레이어 스폰 기준 왼쪽)에 정신력 감소를 바로 시험할 수 있는 테스트 공간을 만든다.
    ///
    /// **프로토타입 전용이다.** 정식 시체·귀신 이벤트 시스템이 생기면 이 도구와
    /// <see cref="SanityWitnessProp"/> 를 통째로 삭제한다 → roadmap M8-GS-2·M8-GS-3
    ///
    /// 세 칸은 서로 벽으로 막아 둔다. 한 칸을 볼 때 옆 칸 소품이 시야에 같이 들어오면
    /// 어느 소품이 정신력을 깎았는지 구분할 수 없기 때문이다.
    /// </summary>
    public static class SanityTestbedSetup
    {
        internal const string RootObjectName = "SanityTestbed";

        private const string ScenePath = "Assets/Scenes/Game.unity";
        private const string FloorMaterialPath = "Assets/Materials/M_SanityTestbedFloor.mat";
        private const string WallMaterialPath = "Assets/Materials/M_SanityTestbedWall.mat";
        private const string CorpseMaterialPath = "Assets/Materials/M_SanityCorpse.mat";
        private const string EventMaterialPath = "Assets/Materials/M_SanityGhostEvent.mat";

        // 씬 바닥 Plane 윗면이 y = -0.54 다. 1cm 만 띄워서 z-fighting 없이 이어 붙인다.
        private const float GroundTopY = -0.54f;
        private const float PadTopY = GroundTopY + 0.01f;
        private const float PadThickness = 0.2f;

        // 집 서쪽 바깥벽이 x = -9.6 이다. 2.4m 를 비우고 시작한다.
        private const float PadWestX = -22f;
        private const float PadEastX = -12f;
        private const float PadSouthZ = -5f;
        private const float PadNorthZ = 5f;

        private const float WallHeight = 2.2f;
        private const float WallThickness = 0.18f;
        private const float NorthDividerZ = 2.2f;
        private const float SouthDividerZ = -0.8f;

        private static readonly Color FloorColor = new(0.17f, 0.18f, 0.2f);
        private static readonly Color WallColor = new(0.3f, 0.31f, 0.34f);
        private static readonly Color CorpseColor = new(0.42f, 0.08f, 0.09f);
        private static readonly Color EventColor = new(0.62f, 0.3f, 0.95f);

        [MenuItem("GhostHunter/정신력 테스트베드 설치", priority = 5)]
        public static void InstallIntoActiveGameScene()
        {
            InstallIntoActiveGameScene(true);
        }

        internal static void InstallIntoActiveGameScene(bool logCompletion)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Exit Play Mode before installing the sanity testbed.");

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException(
                    $"Open {ScenePath} before installing the sanity testbed. " +
                    $"The active scene is '{scene.path}'.");
            }

            Material floor = LoadOrCreateMaterial(FloorMaterialPath, FloorColor, false);
            Material wall = LoadOrCreateMaterial(WallMaterialPath, WallColor, false);
            Material corpse = LoadOrCreateMaterial(CorpseMaterialPath, CorpseColor, false);
            Material eventMaterial = LoadOrCreateMaterial(EventMaterialPath, EventColor, true);

            GameObject root = FindOrCreateRoot(scene);

            // 매번 처음부터 다시 짓는다. 소품 위치를 손으로 옮겨도 도구가 정답을 되돌린다.
            ClearChildren(root.transform);

            BuildShell(root.transform, floor, wall);
            BuildCorpse(root.transform, "Corpse_A", new Vector3(-18.5f, PadTopY, 3.6f), 1UL, corpse);
            BuildCorpse(root.transform, "Corpse_B", new Vector3(-18.5f, PadTopY, 0.7f), 2UL, corpse);
            BuildGhostEvent(root.transform, new Vector3(-18f, PadTopY, -3f), eventMaterial);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateInstallation();

            if (logCompletion)
            {
                Debug.Log(
                    "[SanityTestbedSetup] 집 서쪽 정신력 테스트베드 설치 완료 — " +
                    "시체 2구(중복 방지 확인용)와 귀신 이벤트 1개.");
            }
        }

        internal static void ValidateInstallation()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
                return;

            GameObject root = FindSceneRoot(scene, RootObjectName);
            if (root == null)
                throw new MissingReferenceException("Game 씬에 정신력 테스트베드가 없습니다.");

            if (root.transform.localScale != Vector3.one)
                throw new InvalidOperationException("테스트베드 루트 스케일은 1이어야 합니다.");

            var props = root.GetComponentsInChildren<SanityWitnessProp>(true);
            if (props.Length != 3)
            {
                throw new InvalidOperationException(
                    $"테스트베드 소품이 3개여야 합니다. 현재 {props.Length}개입니다.");
            }

            int corpseCount = 0;
            int eventCount = 0;
            foreach (SanityWitnessProp prop in props)
            {
                var serialized = new SerializedObject(prop);
                var kind = (SanityWitnessKind)serialized.FindProperty("_kind").enumValueIndex;
                if (kind == SanityWitnessKind.Corpse)
                    corpseCount++;
                else
                    eventCount++;
            }

            if (corpseCount != 2 || eventCount != 1)
            {
                throw new InvalidOperationException(
                    $"시체 2 + 귀신 이벤트 1이어야 합니다. 현재 시체 {corpseCount}, 이벤트 {eventCount}입니다.");
            }
        }

        private static void BuildShell(Transform parent, Material floor, Material wall)
        {
            float width = PadEastX - PadWestX;
            float depth = PadNorthZ - PadSouthZ;
            float centerX = (PadWestX + PadEastX) * 0.5f;
            float centerZ = (PadSouthZ + PadNorthZ) * 0.5f;
            float wallCenterY = PadTopY + WallHeight * 0.5f;

            CreateBox(
                "Floor",
                new Vector3(centerX, PadTopY - PadThickness * 0.5f, centerZ),
                new Vector3(width, PadThickness, depth),
                floor,
                parent);

            CreateBox(
                "Wall_West",
                new Vector3(PadWestX + WallThickness * 0.5f, wallCenterY, centerZ),
                new Vector3(WallThickness, WallHeight, depth),
                wall,
                parent);

            CreateBox(
                "Wall_North",
                new Vector3(centerX, wallCenterY, PadNorthZ - WallThickness * 0.5f),
                new Vector3(width, WallHeight, WallThickness),
                wall,
                parent);

            CreateBox(
                "Wall_South",
                new Vector3(centerX, wallCenterY, PadSouthZ + WallThickness * 0.5f),
                new Vector3(width, WallHeight, WallThickness),
                wall,
                parent);

            // 동쪽은 막지 않는다. 집에서 걸어와 각 칸으로 바로 들어갈 수 있어야 한다.
            CreateBox(
                "Divider_North",
                new Vector3(centerX, wallCenterY, NorthDividerZ),
                new Vector3(width, WallHeight, WallThickness),
                wall,
                parent);

            CreateBox(
                "Divider_South",
                new Vector3(centerX, wallCenterY, SouthDividerZ),
                new Vector3(width, WallHeight, WallThickness),
                wall,
                parent);
        }

        private static void BuildCorpse(
            Transform parent,
            string name,
            Vector3 position,
            ulong corpseId,
            Material material)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            // 캡슐의 높이 축은 로컬 Y다. X로 90도 눕히면 그 축이 월드 Z를 향한다.
            body.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            body.transform.localPosition = new Vector3(0f, 0.31f, 0f);
            body.transform.localScale = new Vector3(0.62f, 0.9f, 0.62f);
            body.GetComponent<Renderer>().sharedMaterial = material;

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            head.transform.localPosition = new Vector3(0f, 0.31f, 1.05f);
            head.transform.localScale = Vector3.one * 0.42f;
            head.GetComponent<Renderer>().sharedMaterial = material;

            var sightPoint = new GameObject("SightPoint");
            sightPoint.transform.SetParent(root.transform, false);
            sightPoint.transform.localPosition = new Vector3(0f, 0.55f, 0f);

            SanityWitnessProp prop = root.AddComponent<SanityWitnessProp>();
            var serialized = new SerializedObject(prop);
            serialized.FindProperty("_kind").enumValueIndex = (int)SanityWitnessKind.Corpse;
            serialized.FindProperty("_corpseId").ulongValue = corpseId;
            serialized.FindProperty("_sightPoint").objectReferenceValue = sightPoint.transform;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prop);
        }

        private static void BuildGhostEvent(Transform parent, Vector3 position, Material material)
        {
            var root = new GameObject("GhostEvent");
            root.transform.SetParent(parent, false);
            root.transform.position = position;

            GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            orb.name = "Orb";
            orb.transform.SetParent(root.transform, false);
            orb.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            orb.transform.localScale = Vector3.one * 0.7f;
            // 콜라이더를 지운다. 공중에 뜬 연출이라 몸으로 막히면 안 되고, 가시 판정 선도 통과해야 한다.
            Object.DestroyImmediate(orb.GetComponent<Collider>());
            Renderer orbRenderer = orb.GetComponent<Renderer>();
            orbRenderer.sharedMaterial = material;

            var auraObject = new GameObject("Aura");
            auraObject.transform.SetParent(root.transform, false);
            auraObject.transform.localPosition = new Vector3(0f, 1.3f, 0f);
            Light aura = auraObject.AddComponent<Light>();
            aura.type = LightType.Point;
            aura.range = 7f;
            aura.intensity = 2.4f;
            aura.color = EventColor;
            aura.shadows = LightShadows.None;

            SanityWitnessProp prop = root.AddComponent<SanityWitnessProp>();
            var serialized = new SerializedObject(prop);
            serialized.FindProperty("_kind").enumValueIndex = (int)SanityWitnessKind.GhostEvent;
            serialized.FindProperty("_sightPoint").objectReferenceValue = orb.transform;
            serialized.FindProperty("_occurrenceLight").objectReferenceValue = aura;
            SerializedProperty renderers = serialized.FindProperty("_occurrenceRenderers");
            renderers.arraySize = 1;
            renderers.GetArrayElementAtIndex(0).objectReferenceValue = orbRenderer;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(prop);
        }

        private static void CreateBox(
            string name,
            Vector3 position,
            Vector3 size,
            Material material,
            Transform parent)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent, false);
            cube.transform.position = position;
            cube.transform.localScale = size;
            cube.GetComponent<Renderer>().sharedMaterial = material;
        }

        private static GameObject FindOrCreateRoot(Scene scene)
        {
            GameObject root = FindSceneRoot(scene, RootObjectName);
            if (root != null)
                return root;

            root = new GameObject(RootObjectName);
            SceneManager.MoveGameObjectToScene(root, scene);
            return root;
        }

        private static GameObject FindSceneRoot(Scene scene, string objectName)
        {
            foreach (GameObject candidate in scene.GetRootGameObjects())
            {
                if (candidate.name == objectName)
                    return candidate;
            }

            return null;
        }

        private static void ClearChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Object.DestroyImmediate(parent.GetChild(i).gameObject);
        }

        private static Material LoadOrCreateMaterial(string path, Color color, bool emissive)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                throw new MissingReferenceException("URP Lit 셰이더를 찾지 못했습니다.");

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Smoothness", emissive ? 0.55f : 0.15f);

            if (emissive)
            {
                material.SetColor("_EmissionColor", color * 2f);
                material.EnableKeyword("_EMISSION");
            }

            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
