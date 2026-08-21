using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace GhostHunter.EditorTools
{
    /// <summary>
    /// 맵에 놓을 가구·문의 원본 목록. 생성 도구는 이 목록에서 <see cref="Place"/> 로 인스턴스를
    /// 찍어 낸다 — 더 이상 배치할 때마다 큐브를 새로 조립하지 않는다.
    ///
    /// <b>원본이 프리팹 에셋인지 씬 오브젝트인지 신경 쓰지 않는다.</b>
    /// 실제 생성(<see cref="PrototypeSceneSetup"/>)은 <c>Assets/Prefabs/</c> 의 프리팹 에셋을
    /// 등록하고, 테스트는 메모리에 조립한 오브젝트를 등록한다. 프리팹 저장은 AssetDatabase 가
    /// 필요해 batchmode 에서 돌지 않으므로(→ workflow/testing.md §5.3), 이 이음매가 없으면
    /// 맵 생성 로직을 자동으로 검증할 방법이 사라진다.
    /// </summary>
    internal sealed class FurnitureCatalog
    {
        private readonly Dictionary<string, GameObject> _sources = new();
        private readonly List<string> _keys = new();

        /// <summary>등록 순서를 유지한 키 목록. 가구 라이브러리의 진열 순서가 된다.</summary>
        internal IReadOnlyList<string> Keys => _keys;

        internal void Register(string key, GameObject source)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("가구 키가 비어 있습니다.", nameof(key));
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            if (_sources.ContainsKey(key))
                throw new ArgumentException($"'{key}' 가 이미 등록되어 있습니다.", nameof(key));

            _sources.Add(key, source);
            _keys.Add(key);
        }

        internal bool Contains(string key) => _sources.ContainsKey(key);

        /// <summary>
        /// 원본을 하나 찍어 <paramref name="parent"/> 아래 놓는다.
        /// </summary>
        /// <param name="floorPosition">놓을 자리의 바닥 점. 정확한 높이는 지지면이 정한다.</param>
        /// <param name="yaw">Y 회전(도). 벽에 붙이는 가구의 앞면 방향을 여기서 정한다.</param>
        /// <param name="supportHeight">
        /// 가구가 앉을 지지면 높이. 바닥은 0, 책상 위 소품은 상판 윗면을 넘긴다.
        /// </param>
        /// <param name="instanceName">
        /// 씬에 남길 이름. 같은 원본을 역할만 달리해 두 번 놓을 때 쓴다(머리맡/발치 협탁).
        /// 비우면 키를 그대로 쓴다.
        /// </param>
        internal Transform Place(
            string key,
            Vector3 floorPosition,
            float yaw,
            Transform parent,
            float supportHeight = 0f,
            string instanceName = null)
        {
            Transform instance = Instantiate(key, parent);
            instance.name = string.IsNullOrEmpty(instanceName) ? key : instanceName;
            instance.SetPositionAndRotation(floorPosition, Quaternion.Euler(0f, yaw, 0f));

            // 도면 좌표는 눈으로 잡은 값이라 몇 cm 씩 떠 있는 것들이 있다. 그대로 물리를 켜면
            // 세션 시작과 동시에 전부 떨어진다.
            HousePrototypeBuilder.RestOnSupport(instance, supportHeight);
            return instance;
        }

        /// <summary>
        /// 지지면 보정 없이 그대로 놓는다. 문처럼 경첩 위치가 곧 기준점인 것에 쓴다 —
        /// 문짝을 바닥에 앉히면 문틀 아래로 내려간다.
        /// </summary>
        internal Transform PlaceExact(string key, Vector3 position, float yaw, Transform parent)
        {
            Transform instance = Instantiate(key, parent);
            instance.name = key;
            instance.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            return instance;
        }

        private Transform Instantiate(string key, Transform parent)
        {
            if (!_sources.TryGetValue(key, out GameObject source))
            {
                throw new KeyNotFoundException(
                    $"가구 목록에 '{key}' 가 없습니다. 등록된 것: {string.Join(", ", _keys)}");
            }

            // 프리팹 에셋이면 연결된 인스턴스로, 메모리 원본이면 그냥 복제한다.
            GameObject instance = PrefabUtility.IsPartOfPrefabAsset(source)
                ? (GameObject)PrefabUtility.InstantiatePrefab(source)
                : Object.Instantiate(source);

            instance.transform.SetParent(parent, false);
            instance.SetActive(true);
            return instance.transform;
        }
    }
}
