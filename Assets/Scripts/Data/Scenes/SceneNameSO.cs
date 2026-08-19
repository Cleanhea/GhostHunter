using GhostHunter.Core.Scenes;
using UnityEngine;

namespace GhostHunter.Data.Scenes
{
    /// <summary>
    /// SceneId 와 실제 씬 이름을 잇는 유일한 표. 씬 이름 문자열은 여기 말고 어디에도 두지 않는다.
    ///
    /// 여기 등록한 씬은 빌드 씬 목록에도 등록해야 한다. 누락은 런타임 로드 실패로만 드러난다.
    /// </summary>
    [CreateAssetMenu(menuName = "GhostHunter/Scenes/Scene Name SO", fileName = "SceneNameSO")]
    public sealed class SceneNameSO : ScriptableObject
    {
        [Tooltip("빌드 인덱스 0. 언로드하지 않는다.")]
        [SerializeField] private SceneReference _bootstrap = new();

        [SerializeField] private SceneReference _title = new();
        [SerializeField] private SceneReference _lobby = new();
        [SerializeField] private SceneReference _game = new();
        [SerializeField] private SceneReference _result = new();

        /// <summary>지정되지 않았으면 빈 문자열. 호출부가 검사한다.</summary>
        public string GetSceneName(SceneId id)
        {
            return Resolve(id).SceneName;
        }

        public bool IsAssigned(SceneId id) => Resolve(id).IsAssigned;

        /// <summary>
        /// 이미 로드되어 있는 씬 이름으로 SceneId 를 되찾는다.
        /// 에디터에서 Bootstrap 없이 특정 씬만 열고 플레이할 때 현재 위치를 알아내는 데 쓴다.
        /// </summary>
        public bool TryResolve(string sceneName, out SceneId id)
        {
            foreach (SceneId candidate in AllIds)
            {
                if (Resolve(candidate).SceneName == sceneName)
                {
                    id = candidate;
                    return true;
                }
            }

            id = SceneId.Bootstrap;
            return false;
        }

        private static readonly SceneId[] AllIds =
        {
            SceneId.Bootstrap, SceneId.Title, SceneId.Lobby, SceneId.Game, SceneId.Result,
        };

        private SceneReference Resolve(SceneId id)
        {
            switch (id)
            {
                case SceneId.Bootstrap: return _bootstrap;
                case SceneId.Title: return _title;
                case SceneId.Lobby: return _lobby;
                case SceneId.Game: return _game;
                case SceneId.Result: return _result;
                default:
                    Debug.LogError($"{nameof(SceneNameSO)}: 알 수 없는 SceneId {id}", this);
                    return _title;
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            bool changed = false;

            foreach (SceneReference reference in new[] { _bootstrap, _title, _lobby, _game, _result })
                changed |= reference.BakeName();

            if (changed)
                UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
