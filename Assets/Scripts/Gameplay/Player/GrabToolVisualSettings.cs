using UnityEngine;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>가구 흡착 장비의 화면 배치와 잡기 표시 색을 정한다.</summary>
    [CreateAssetMenu(menuName = "GhostHunter/Gameplay/Grab Tool Visual Settings")]
    public sealed class GrabToolVisualSettings : ScriptableObject
    {
        [SerializeField] private Vector3 _firstPersonPosition = new(0.34f, -0.4f, 0.42f);
        [SerializeField] private Vector3 _firstPersonEuler = new(0f, -10f, 0f);
        [SerializeField] private Vector3 _handOffset = new(0f, 0f, 0.02f);
        [SerializeField] private Vector3 _fallbackPosition = new(0.3f, 0.72f, 0.15f);
        [SerializeField] private Color _idleColor = new(0.04f, 0.06f, 0.015f, 1f);
        [SerializeField, ColorUsage(false, true)] private Color _grabEmission = new(0.45f, 2f, 0.04f, 1f);
        [SerializeField, Min(0f)] private float _lightIntensity = 0.5f;
        [SerializeField, Min(0f)] private float _lightRange = 0.45f;

        public Vector3 FirstPersonPosition => _firstPersonPosition;
        public Quaternion FirstPersonRotation => Quaternion.Euler(_firstPersonEuler);
        public Vector3 HandOffset => _handOffset;
        public Vector3 FallbackPosition => _fallbackPosition;
        public Color IdleColor => _idleColor;
        public Color GrabEmission => _grabEmission;
        public float LightIntensity => _lightIntensity;
        public float LightRange => _lightRange;
    }
}
