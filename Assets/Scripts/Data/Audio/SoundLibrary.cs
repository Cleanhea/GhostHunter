using System.Collections.Generic;
using GhostHunter.Core.Audio;
using UnityEngine;

namespace GhostHunter.Data.Audio
{
    /// <summary>
    /// 네트워크로 주고받을 수 있는 큐 목록. 서버가 보낸 식별자를 받은 쪽이 이 목록에서 큐를 찾는다.
    /// 목록에 없는 큐는 로컬에서만 재생할 수 있다.
    /// </summary>
    [CreateAssetMenu(menuName = "GhostHunter/Audio/Sound Library", fileName = "SoundLibrary")]
    public sealed class SoundLibrary : ScriptableObject
    {
        [SerializeField] private SoundCue[] _cues = new SoundCue[0];

        private Dictionary<uint, SoundCue> _lookup;

        public IReadOnlyList<SoundCue> Cues => _cues;

        public bool TryGet(uint id, out SoundCue cue)
        {
            if (_lookup == null)
                BuildLookup();

            return _lookup.TryGetValue(id, out cue);
        }

        /// <summary>빈 칸·키 중복·클립 없음을 찾아 문장으로 돌려준다. 문제가 없으면 빈 목록.</summary>
        public List<string> FindProblems()
        {
            var problems = new List<string>();
            var seen = new Dictionary<uint, SoundCue>();
            for (int i = 0; i < _cues.Length; i++)
            {
                SoundCue cue = _cues[i];
                if (cue == null)
                {
                    problems.Add($"[{i}] 비어 있다.");
                    continue;
                }

                if (!cue.HasClips)
                    problems.Add($"[{i}] {cue.name}: 클립이 하나도 없다.");

                if (seen.TryGetValue(cue.Id, out SoundCue first))
                    problems.Add($"[{i}] {cue.name}: 키 '{cue.Key}' 가 {first.name} 와 같은 식별자다.");
                else
                    seen.Add(cue.Id, cue);
            }

            return problems;
        }

        private void BuildLookup()
        {
            _lookup = new Dictionary<uint, SoundCue>(_cues.Length);
            foreach (SoundCue cue in _cues)
            {
                // 겹치면 먼저 나온 것을 쓴다. 겹침은 FindProblems 와 OnValidate 가 알린다.
                if (cue != null && !_lookup.ContainsKey(cue.Id))
                    _lookup.Add(cue.Id, cue);
            }
        }

        private void OnEnable() => _lookup = null;

        private void OnValidate()
        {
            _lookup = null;
#if UNITY_EDITOR
            foreach (string problem in FindProblems())
                Debug.LogWarning($"{nameof(SoundLibrary)} {name}: {problem}", this);
#endif
        }
    }
}
