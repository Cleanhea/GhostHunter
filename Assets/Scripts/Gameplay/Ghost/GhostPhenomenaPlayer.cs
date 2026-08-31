using System.Collections.Generic;
using GhostHunter.Gameplay.Interaction;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 초자연현상의 **연출**만 담당한다(§10 — 판정은 서버, 재생은 각 클라이언트). 서버가
    /// <see cref="GhostPrototypeController"/> 의 현상 RPC를 전 피어에 보내면 호스트를 포함한
    /// 모든 피어가 여기서 같은 시드로 조명 점멸·서랍 여닫기·일시 출현을 로컬 재생한다.
    /// 물건 흔들기·문 여닫기 등 서버가 이미 물리·NetworkVariable 로 처리한 현상은 여기서
    /// 아무것도 하지 않는다(복제된 상태가 그대로 보인다). 소리 현상은 오디오 에셋 대기.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GhostPhenomenaPlayer : MonoBehaviour
    {
        private const float FlickerStepSeconds = 0.07f;

        [SerializeField] private GhostPrototypeSettings _settings;

        [Tooltip("'귀신 일시 출현'에 쓰는 반투명 머티리얼. 보통 M_GhostBody 를 재사용한다.")]
        [SerializeField] private Material _apparitionMaterial;

        private readonly List<GhostAmbientLight> _flickerLights = new(4);
        private System.Random _flickerRandom;
        private float _flickerRemaining;
        private float _flickerStepRemaining;

        private GameObject _apparition;
        private MeshRenderer _apparitionRenderer;
        private MaterialPropertyBlock _apparitionBlock;
        private Color _apparitionBaseColor = new(0.76f, 0.79f, 0.92f, 0.6f);
        private float _apparitionRemaining;
        private float _apparitionTotal;

        private void Update()
        {
            TickFlicker(Time.deltaTime);
            TickApparition(Time.deltaTime);
        }

        private void OnDisable()
        {
            StopFlicker();
            ClearApparition();
        }

        /// <summary>서버가 확정한 현상을 이 피어에서 재생한다.</summary>
        public void Play(GhostPhenomenonKind kind, Vector3 position, int seed)
        {
            switch (kind)
            {
                case GhostPhenomenonKind.LightFlicker:
                    StartFlicker(position, seed);
                    break;

                case GhostPhenomenonKind.DrawerOpen:
                    PulseNearestDrawer(position);
                    break;

                case GhostPhenomenonKind.Apparition:
                    SpawnApparition(position, seed);
                    break;

                // ObjectShake / SmallObjectDrop / DoorMove — 서버 물리·복제로 이미 보인다.
                // WallKnock / Footsteps — 오디오 에셋 대기(GDD §9).
            }
        }

        private void StartFlicker(Vector3 position, int seed)
        {
            StopFlicker();

            if (_settings == null)
                return;

            float radiusSqr = _settings.PhenomenonRadius * _settings.PhenomenonRadius;
            GhostAmbientLight nearest = null;
            float nearestSqr = float.MaxValue;

            IReadOnlyList<GhostAmbientLight> lights = GhostAmbientLight.Registry;
            for (int i = 0; i < lights.Count; i++)
            {
                GhostAmbientLight light = lights[i];
                if (light == null)
                    continue;

                float sqr = (light.transform.position - position).sqrMagnitude;
                if (sqr <= radiusSqr)
                    _flickerLights.Add(light);

                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = light;
                }
            }

            // 반경 안에 아무것도 없으면 가장 가까운 조명 하나라도 흔든다.
            if (_flickerLights.Count == 0 && nearest != null)
                _flickerLights.Add(nearest);

            if (_flickerLights.Count == 0)
                return;

            _flickerRandom = new System.Random(seed);
            _flickerRemaining = _settings.LightFlickerSeconds;
            _flickerStepRemaining = 0f;
        }

        private void TickFlicker(float deltaTime)
        {
            if (_flickerRemaining <= 0f)
                return;

            _flickerRemaining -= deltaTime;
            _flickerStepRemaining -= deltaTime;

            if (_flickerStepRemaining <= 0f)
            {
                _flickerStepRemaining = FlickerStepSeconds;

                // 0(꺼짐) / 0.15(희미) / 1(정상) 을 시드 난수로 오간다 — 모든 피어가 같은 패턴.
                int roll = _flickerRandom.Next(0, 10);
                float factor = roll < 5 ? 0f : roll < 7 ? 0.15f : 1f;
                ApplyFlickerFactor(factor);
            }

            if (_flickerRemaining <= 0f)
                StopFlicker();
        }

        private void ApplyFlickerFactor(float factor)
        {
            for (int i = 0; i < _flickerLights.Count; i++)
            {
                GhostAmbientLight light = _flickerLights[i];
                if (light != null && light.Light != null)
                    light.Light.intensity = light.BaseIntensity * factor;
            }
        }

        private void StopFlicker()
        {
            ApplyFlickerFactor(1f);
            _flickerLights.Clear();
            _flickerRemaining = 0f;
            _flickerStepRemaining = 0f;
        }

        private static void PulseNearestDrawer(Vector3 position)
        {
            GhostDrawer nearest = null;
            float nearestSqr = float.MaxValue;

            IReadOnlyList<GhostDrawer> drawers = GhostDrawer.Registry;
            for (int i = 0; i < drawers.Count; i++)
            {
                GhostDrawer drawer = drawers[i];
                if (drawer == null)
                    continue;

                float sqr = (drawer.transform.position - position).sqrMagnitude;
                if (sqr < nearestSqr)
                {
                    nearestSqr = sqr;
                    nearest = drawer;
                }
            }

            if (nearest != null)
                nearest.PulseOpen();
        }

        private void SpawnApparition(Vector3 position, int seed)
        {
            if (_settings == null)
                return;

            ClearApparition();

            _apparition = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _apparition.name = "GhostApparition";
            _apparition.layer = gameObject.layer;

            Collider collider = _apparition.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var random = new System.Random(seed);
            float yaw = (float)(random.NextDouble() * 360d);
            _apparition.transform.SetPositionAndRotation(
                position + Vector3.up * 0.9f,
                Quaternion.Euler(0f, yaw, 0f));
            _apparition.transform.localScale = new Vector3(0.7f, 0.9f, 0.7f);

            _apparitionRenderer = _apparition.GetComponent<MeshRenderer>();
            _apparitionRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _apparitionRenderer.receiveShadows = false;

            if (_apparitionMaterial != null)
            {
                _apparitionRenderer.sharedMaterial = _apparitionMaterial;
                _apparitionBaseColor = _apparitionMaterial.HasProperty("_BaseColor")
                    ? _apparitionMaterial.GetColor("_BaseColor")
                    : _apparitionBaseColor;
            }

            _apparitionBlock ??= new MaterialPropertyBlock();
            _apparitionTotal = Mathf.Max(0.1f, _settings.ApparitionSeconds);
            _apparitionRemaining = _apparitionTotal;
            ApplyApparitionAlpha(1f);
        }

        private void TickApparition(float deltaTime)
        {
            if (_apparition == null)
                return;

            _apparitionRemaining -= deltaTime;
            if (_apparitionRemaining <= 0f)
            {
                ClearApparition();
                return;
            }

            ApplyApparitionAlpha(_apparitionRemaining / _apparitionTotal);
        }

        private void ApplyApparitionAlpha(float t)
        {
            if (_apparitionRenderer == null)
                return;

            Color color = _apparitionBaseColor;
            color.a = _apparitionBaseColor.a * Mathf.Clamp01(t);
            _apparitionRenderer.GetPropertyBlock(_apparitionBlock);
            _apparitionBlock.SetColor("_BaseColor", color);
            _apparitionRenderer.SetPropertyBlock(_apparitionBlock);
        }

        private void ClearApparition()
        {
            if (_apparition != null)
                Destroy(_apparition);

            _apparition = null;
            _apparitionRenderer = null;
            _apparitionRemaining = 0f;
        }
    }
}
