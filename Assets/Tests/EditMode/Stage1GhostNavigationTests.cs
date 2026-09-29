using System.Collections.Generic;
using System.Linq;
using GhostHunter.Gameplay.Ghost;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 실제 Stage1 집으로 귀신 NavMesh 를 구워 계단마다 아래층 ↔ 위층이 이어지는지 본다(MAP-11).
    /// 경사 콜라이더 끝과 위층 슬래브 사이 틈(0.24m)은 플레이어 캡슐은 넘지만 NavMesh 는 낭떠러지로 깎아
    /// 층이 끊겼다 — 합성 기하 테스트(<see cref="GhostNavigationTests"/>)로는 잡히지 않는다.
    /// </summary>
    public sealed class Stage1GhostNavigationTests
    {
        private const string ScenePath = "Assets/Scenes/Stage1.unity";
        private const string HouseName = "House_Prototype_PlanB";
        private const string SettingsPath = "Assets/Settings/Gameplay/GhostPrototypeSettings_Default.asset";
        private const string GhostPrefabPath = "Assets/Prefabs/Ghost/Ghost_Prototype.prefab";
        private const string RampName = "Stair_Ramp_Collider";
        private const float ApproachDistance = 1f;
        // 계단 한 개로 오르면 경사 길이 + 접근 거리 정도다. 다른 계단으로 돌아가면 이보다 훨씬 길다.
        private const float DetourSlack = 3f;

        private Scene _scene;
        private bool _openedHere;
        private GameObject _house;
        private NavMeshData _data;
        private NavMeshDataInstance _instance;

        [OneTimeSetUp]
        public void OpenSceneAndBake()
        {
            _scene = SceneManager.GetSceneByPath(ScenePath);
            if (!_scene.isLoaded)
            {
                _scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                _openedHere = true;
            }

            _house = _scene.GetRootGameObjects().FirstOrDefault(x => x.name == HouseName);
            if (_house == null)
                return;

            Physics.SyncTransforms();
            var settings = AssetDatabase.LoadAssetAtPath<GhostPrototypeSettings>(SettingsPath);
            var ghost = AssetDatabase.LoadAssetAtPath<GameObject>(GhostPrefabPath);
            var controller = ghost != null ? ghost.GetComponent<CharacterController>() : null;
            if (settings == null || controller == null)
                return;

            int layers = ~0;
            foreach (string excluded in new[] { "Player", "Furniture", "GhostPrototype" })
            {
                int layer = LayerMask.NameToLayer(excluded);
                if (layer >= 0)
                    layers &= ~(1 << layer);
            }

            _data = GhostPrototypeController.BuildHouseNavMesh(
                _house.transform,
                layers,
                settings.NavAgentRadius,
                controller.height,
                Mathf.Max(0.3f, controller.stepOffset));
            if (_data != null)
                _instance = NavMesh.AddNavMeshData(_data);
        }

        [OneTimeTearDown]
        public void RemoveAndClose()
        {
            if (_instance.valid)
                _instance.Remove();
            if (_data != null)
                Object.DestroyImmediate(_data);
            if (_openedHere && _scene.isLoaded)
                EditorSceneManager.CloseScene(_scene, true);
        }

        [Test]
        public void 집_NavMesh_를_굽는다()
        {
            Assert.IsNotNull(_house, $"{ScenePath} 에 {HouseName} 이 없습니다.");
            Assert.IsNotNull(_data, "귀신 설정·프리팹을 찾지 못했거나 NavMesh 를 굽지 못했습니다.");
        }

        [Test]
        public void 계단마다_아래층과_위층이_바로_이어진다()
        {
            Assume.That(_data, Is.Not.Null);
            List<BoxCollider> ramps = _house.GetComponentsInChildren<BoxCollider>()
                .Where(x => x.name == RampName)
                .ToList();
            Assert.AreEqual(4, ramps.Count, "1→2층·2층→다락 계단 A/B 경사 콜라이더 4개가 있어야 합니다.");

            var failures = new List<string>();
            foreach (BoxCollider ramp in ramps)
            {
                Transform t = ramp.transform;
                Vector3 center = t.TransformPoint(ramp.center);
                Vector3 along = t.forward * (ramp.size.z * 0.5f);
                Vector3 low = along.y < 0f ? center + along : center - along;
                Vector3 high = along.y < 0f ? center - along : center + along;
                Vector3 flat = Vector3.ProjectOnPlane(high - low, Vector3.up).normalized;
                Vector3 bottom = low - flat * ApproachDistance;
                Vector3 top = high + flat * ApproachDistance;
                string label = t.parent.name;

                if (!NavMesh.SamplePosition(bottom, out NavMeshHit start, 1f, NavMesh.AllAreas)
                    || !NavMesh.SamplePosition(top, out NavMeshHit end, 1f, NavMesh.AllAreas))
                {
                    failures.Add($"{label}: 계단 아래 {bottom} 또는 위 {top} 근처에 NavMesh 가 없다");
                    continue;
                }

                var path = new NavMeshPath();
                NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, path);
                float length = PathLength(path);
                float direct = Vector3.Distance(low, high) + ApproachDistance * 2f + DetourSlack;
                if (path.status != NavMeshPathStatus.PathComplete)
                    failures.Add($"{label}: 위층까지 경로가 끊겼다 ({path.status})");
                else if (length > direct)
                    failures.Add($"{label}: 이 계단으로 못 오르고 {length:0.0}m 를 돌아간다 (기대 ≤ {direct:0.0}m)");
            }

            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        private static float PathLength(NavMeshPath path)
        {
            Vector3[] corners = path.corners;
            float length = 0f;
            for (int i = 1; i < corners.Length; i++)
                length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }
    }
}
