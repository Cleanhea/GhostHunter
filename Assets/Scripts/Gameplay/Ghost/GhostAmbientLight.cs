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
        private Color _baseColor;
        private bool _baseEnabled;
        private bool _stageManaged;

        public Light Light { get; private set; }

        /// <summary>씬에 저장된 기본 밝기. 연출이 끝나면 이 값으로 되돌린다.</summary>
        public float BaseIntensity => _baseIntensity;
        public Color BaseColor => _baseColor;

        /// <summary>
        /// 스테이지 조명 컨트롤러가 켜짐 상태를 알려 준 천장등인가(<see cref="SetBase(float, Color, bool)"/>).
        /// 이런 등만 어택 깜빡임이 꺼진 등을 잠시 켠다 — 가구 램프처럼 다른 코드가 켜고 끄는 조명(풀에 숨은 가구 등)은
        /// 켜짐 상태를 건드리지 않고 밝기만 바꾼다.
        /// </summary>
        public bool StageManaged => _stageManaged;

        /// <summary>천장등의 평소 켜짐 상태(Stage1 은 기본 꺼짐 — HUD 스위치). 어택 깜빡임이 끝나면 이 값으로 돌린다.</summary>
        public bool BaseEnabled => _baseEnabled;

        /// <summary>깜빡임 무늬를 조명마다 다르게 하는 값. 위치로 정해 피어가 달라도 같은 조명은 같은 무늬다.</summary>
        public int FlickerSeed { get; private set; }

        private void Awake()
        {
            Light = GetComponent<Light>();
            _baseIntensity = Light.intensity;
            _baseColor = Light.color;
            Vector3 position = transform.position;
            FlickerSeed = Mathf.RoundToInt(position.x * 73.1f) ^ Mathf.RoundToInt(position.y * 19.7f) * 31
                ^ Mathf.RoundToInt(position.z * 41.3f) * 977;
        }

        /// <summary>
        /// 기준 밝기·색을 바꾼다(스테이지 조명 설정). 연출은 끝나면 이 값으로 돌아오고,
        /// 진행 중인 깜빡임은 다음 단계부터 새 기준을 곱한다.
        /// </summary>
        public void SetBase(float intensity, Color color)
        {
            Light ??= GetComponent<Light>();
            _baseIntensity = intensity;
            _baseColor = color;
        }

        /// <summary>기준 밝기·색과 켜짐 상태(HUD 스위치)를 함께 바꾼다 — 스테이지 조명 컨트롤러 전용.</summary>
        public void SetBase(float intensity, Color color, bool enabled)
        {
            SetBase(intensity, color);
            _baseEnabled = enabled;
            _stageManaged = true;
        }

        /// <summary>천장등이면 켜짐 상태를 정한다(그 밖의 조명은 건드리지 않는다).</summary>
        public void ApplyEnabled(bool enabled)
        {
            if (_stageManaged && Light != null && Light.enabled != enabled)
                Light.enabled = enabled;
        }

        /// <summary>
        /// 어택 깜빡임 밝기 배율(0 = 꺼짐, 켜지면 <paramref name="minLevel"/>~1). 시간을 1/<paramref name="stepsPerSecond"/> 초
        /// 단위로 끊어 단계마다 <paramref name="onChance"/> 확률로 켜진다 — 일정한 경고 깜빡임과 달리 불규칙하다.
        /// 같은 시각·같은 씨앗이면 같은 값이다(순수 계산, EditMode 검증).
        /// </summary>
        public static float FlickerLevel(float time, int seed, float stepsPerSecond, float onChance, float minLevel)
        {
            int step = Mathf.FloorToInt(time * Mathf.Max(0.1f, stepsPerSecond));
            if (Hash01(step, seed) >= onChance)
                return 0f;
            return Mathf.Lerp(Mathf.Clamp01(minLevel), 1f, Hash01(step, seed ^ 0x5bd1e995));
        }

        private static float Hash01(int step, int seed)
        {
            unchecked
            {
                uint h = (uint)step * 0x9E3779B1u ^ (uint)seed * 0x85EBCA77u;
                h ^= h >> 15;
                h *= 0x2C1B3C6Du;
                h ^= h >> 12;
                h *= 0x297A2D39u;
                h ^= h >> 15;
                return (h & 0x00FFFFFF) / 16777216f;
            }
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
            {
                Light.intensity = _baseIntensity;
                Light.color = _baseColor;
                ApplyEnabled(_baseEnabled);
            }
        }
    }
}
