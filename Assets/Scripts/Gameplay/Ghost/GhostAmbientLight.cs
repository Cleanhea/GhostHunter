using System.Collections.Generic;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 귀신 초자연현상 '조명 깜빡임/끄기'(§6.5 #5)가 건드릴 수 있는 방 조명 표식.
    /// 활성화된 인스턴스를 정적 레지스트리에 모아, 클라이언트 연출이 씬 전체를 훑지 않고
    /// 곧바로 현상 위치 근처 조명을 찾게 한다. <c>NetworkObject</c> 가 아니므로
    /// 씬 배치 해시 함정(CLAUDE.md §5)과 무관하고, 연출은 각 피어가 로컬로 재생한다(§10).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Light))]
    public sealed class GhostAmbientLight : MonoBehaviour
    {
        private static readonly List<GhostAmbientLight> RegistryList = new();

        /// <summary>지금 씬에 살아 있는 방 조명들.</summary>
        public static IReadOnlyList<GhostAmbientLight> Registry => RegistryList;

        private float _baseIntensity;

        public Light Light { get; private set; }

        /// <summary>씬에 저장된 기본 밝기. 연출이 끝나면 이 값으로 되돌린다.</summary>
        public float BaseIntensity => _baseIntensity;

        private void Awake()
        {
            Light = GetComponent<Light>();
            _baseIntensity = Light.intensity;
        }

        private void OnEnable()
        {
            if (!RegistryList.Contains(this))
                RegistryList.Add(this);
        }

        private void OnDisable()
        {
            RegistryList.Remove(this);

            // 연출 도중 비활성화되어도 다음 활성화 때 어두운 채로 남지 않게 복구한다.
            if (Light != null)
                Light.intensity = _baseIntensity;
        }
    }
}
