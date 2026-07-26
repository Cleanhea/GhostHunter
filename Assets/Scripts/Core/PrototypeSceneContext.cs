using UnityEngine;

namespace GhostHunter.Core
{
    /// <summary>
    /// 세션 시작 전에도 경기장을 보여주는 씬 카메라를 관리한다. 플레이어 코드가 씬 오브젝트를
    /// 검색하지 않도록 명시적인 씬 컨텍스트 하나만 정적 진입점으로 둔다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PrototypeSceneContext : MonoBehaviour
    {
        [SerializeField] private Camera _overviewCamera;
        [SerializeField] private AudioListener _overviewAudioListener;

        private static PrototypeSceneContext _instance;

        private void Awake()
        {
            _instance = this;
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        public static void SetGameplayCameraActive(bool active)
        {
            if (_instance == null)
                return;

            if (_instance._overviewCamera != null)
                _instance._overviewCamera.enabled = !active;

            if (_instance._overviewAudioListener != null)
                _instance._overviewAudioListener.enabled = !active;
        }
    }
}
