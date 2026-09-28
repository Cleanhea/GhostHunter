using System;
using UnityEngine;

namespace GhostHunter.Data.Scenes
{
    /// <summary>
    /// 씬을 에디터에서 에셋으로 지정하고 런타임에는 이름 문자열로 쓰는 참조.
    /// 손으로 친 씬 이름이 리네임 뒤 조용히 깨지는 것을 막는 게 목적이다.
    ///
    /// 이름 굽기는 소유 에셋의 OnValidate 가 <see cref="BakeName"/> 을 불러 수행한다.
    /// 직렬화 콜백에서 AssetDatabase 를 건드리지 않기 위해서다.
    /// </summary>
    [Serializable]
    public sealed class SceneReference
    {
#if UNITY_EDITOR
        [SerializeField] private UnityEditor.SceneAsset _sceneAsset;
#endif

        [SerializeField, HideInInspector] private string _sceneName = string.Empty;

        /// <summary>런타임에 쓰는 씬 이름. 지정되지 않았으면 빈 문자열.</summary>
        public string SceneName
        {
            get
            {
#if UNITY_EDITOR
                // Domain Reload 를 끈 에디터에서 외부 파일 변경 직후 Play 하면,
                // 기존 ScriptableObject 인스턴스의 구운 이름이 잠시 낡아 있을 수 있다.
                // 에디터에는 원본 SceneAsset 이 있으므로 실제 에셋 이름을 우선한다.
                if (_sceneAsset != null)
                    return _sceneAsset.name;
#endif
                return _sceneName;
            }
        }

        public bool IsAssigned => !string.IsNullOrEmpty(SceneName);

#if UNITY_EDITOR
        /// <summary>
        /// 지정된 씬 에셋의 이름을 <see cref="_sceneName"/> 에 굽는다.
        /// 변경이 있었으면 true 를 돌려준다(호출부가 에셋을 dirty 로 표시하도록).
        /// </summary>
        public bool BakeName()
        {
            string baked = _sceneAsset != null ? _sceneAsset.name : string.Empty;

            if (_sceneName == baked)
                return false;

            _sceneName = baked;
            return true;
        }
#endif
    }
}
