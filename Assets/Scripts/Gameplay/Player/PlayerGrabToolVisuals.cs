using GhostHunter.Gameplay.Interaction;
using GhostHunter.Gameplay.Sanity;
using UnityEngine;
using UnityEngine.Rendering;

namespace GhostHunter.Gameplay.Player
{
    /// <summary>플레이어의 1인칭·3인칭 흡착 장비를 표시하고 서버가 승인한 가구 잡기 동안 발광시킨다.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerGrabToolVisuals : MonoBehaviour
    {
        [SerializeField] private GrabToolView _toolPrefab;
        [SerializeField] private GrabToolVisualSettings _settings;
        [SerializeField] private GrabController _grab;
        [SerializeField] private PlayerLook _look;
        [SerializeField] private PlayerMotor _motor;
        [SerializeField] private SanityNetworkState _sanity;
        [SerializeField] private Animator _animator;

        private Transform _root;
        private Transform _rightHand;
        private Camera _ownerCamera;
        private PlayerCleaningController _cleaning;
        private PlayerFurnitureDriverController _driver;
        private PlayerLighter _lighter;
        private GrabToolView _firstPerson;
        private GrabToolView _thirdPerson;
        private bool _visible;
        private bool _lit;

        private void Awake()
        {
            if (_toolPrefab == null || _settings == null || _grab == null || _look == null
                || _motor == null || _sanity == null || _look.PlayerCamera == null)
            {
                Debug.LogError($"{nameof(PlayerGrabToolVisuals)}: 장비 프리팹·설정·플레이어 참조가 없습니다.", this);
                enabled = false;
                return;
            }

            _root = transform;
            _ownerCamera = _look.PlayerCamera;
            _cleaning = GetComponent<PlayerCleaningController>();
            _driver = GetComponent<PlayerFurnitureDriverController>();
            _lighter = GetComponent<PlayerLighter>();
            if (_animator != null && _animator.isHuman)
                _rightHand = _animator.GetBoneTransform(HumanBodyBones.RightHand);

            _firstPerson = Instantiate(_toolPrefab, _ownerCamera.transform);
            _firstPerson.name = "GrabTool_FirstPerson";
            _firstPerson.transform.SetLocalPositionAndRotation(
                _settings.FirstPersonPosition, _settings.FirstPersonRotation);
            _firstPerson.Configure(_settings, true);

            // 시체 모델 밖에 둬서 사망 시 장비가 시체와 함께 복제되지 않게 한다.
            _thirdPerson = Instantiate(_toolPrefab, _root);
            _thirdPerson.name = "GrabTool_ThirdPerson";
            _thirdPerson.Configure(_settings, false);
        }

        private void OnEnable()
        {
            RenderPipelineManager.beginCameraRendering += HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += HandleEndCameraRendering;
        }

        private void LateUpdate()
        {
            if (_firstPerson == null || _thirdPerson == null)
                return;

            _visible = _grab.IsSpawned && _sanity.HasSanity
                && _motor.VisualBody != null && _motor.VisualBody.gameObject.activeInHierarchy;
            bool lit = _visible && _grab.IsHolding;
            if (lit != _lit)
            {
                _lit = lit;
                _firstPerson.SetLit(lit);
                _thirdPerson.SetLit(lit);
            }

            Quaternion rotation = _root.rotation * Quaternion.Euler(_look.Pitch, 0f, 0f);
            Vector3 grip = _rightHand != null
                ? _rightHand.position
                : _root.TransformPoint(_settings.FallbackPosition);
            _thirdPerson.transform.SetPositionAndRotation(grip + rotation * _settings.HandOffset, rotation);
            RestoreRendering();
        }

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= HandleBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= HandleEndCameraRendering;
            _visible = false;
            _lit = false;
            if (_firstPerson != null)
            {
                _firstPerson.SetLit(false);
                _firstPerson.SetRendered(false);
            }
            if (_thirdPerson != null)
            {
                _thirdPerson.SetLit(false);
                _thirdPerson.SetRendered(false);
            }
        }

        private void HandleBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (_firstPerson == null || _thirdPerson == null)
                return;
            bool ownerView = _grab.IsOwner && camera == _ownerCamera;
            bool otherTool = _grab.IsOwner && ((_cleaning != null && _cleaning.IsMopEquipped)
                || (_driver != null && _driver.IsDriverEquipped)
                || (_lighter != null && _lighter.IsEquipped));
            _firstPerson.SetRendered(_visible && ownerView && !otherTool);
            _thirdPerson.SetRendered(_visible && !ownerView);
        }

        private void HandleEndCameraRendering(ScriptableRenderContext context, Camera camera)
            => RestoreRendering();

        private void RestoreRendering()
        {
            if (_firstPerson != null)
                _firstPerson.SetRendered(false);
            if (_thirdPerson != null)
                _thirdPerson.SetRendered(_visible);
        }
    }
}
