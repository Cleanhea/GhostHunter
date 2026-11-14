using System;
using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 스포너가 고를 수 있는 귀신 종류 하나. 종류는 프리팹(= 설정 에셋 타입, ADR-0024)이다.
    /// Hub(Tab) 에서 이 목록으로 기존 귀신을 지우고 다른 종류를 스폰한다.
    /// </summary>
    [Serializable]
    public struct GhostTypeOption
    {
        [Tooltip("Hub 에 보이는 이름.")]
        public string Label;

        [Tooltip("귀신 프리팹(DefaultNetworkPrefabs 에 등록된 것).")]
        public GameObject Prefab;

        [Tooltip("스테이지 시작 시 무작위로 고르는 후보에 넣는다.")]
        public bool InRandomPool;
    }

    /// <summary>귀신 종류 고르기. 순수 계산이라 EditMode 로 검증한다.</summary>
    public static class GhostTypePicker
    {
        /// <summary>
        /// 무작위 후보(<see cref="GhostTypeOption.InRandomPool"/> + 프리팹 있음) 중 하나의 인덱스.
        /// <paramref name="roll"/> 은 0 이상 1 미만. 후보가 없으면 프리팹이 있는 첫 항목, 그것도 없으면 -1.
        /// </summary>
        public static int PickRandom(GhostTypeOption[] options, float roll)
        {
            if (options == null)
                return -1;

            int candidates = 0;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].InRandomPool && options[i].Prefab != null)
                    candidates++;
            }

            if (candidates == 0)
                return FirstWithPrefab(options);

            int pick = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(roll) * candidates), 0, candidates - 1);
            for (int i = 0; i < options.Length; i++)
            {
                if (!options[i].InRandomPool || options[i].Prefab == null)
                    continue;
                if (pick == 0)
                    return i;
                pick--;
            }

            return FirstWithPrefab(options);
        }

        public static int FirstWithPrefab(GhostTypeOption[] options)
        {
            if (options == null)
                return -1;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].Prefab != null)
                    return i;
            }

            return -1;
        }

        /// <summary>프리팹이 같은 첫 항목의 인덱스. 없으면 -1.</summary>
        public static int IndexOf(GhostTypeOption[] options, GameObject prefab)
        {
            if (options == null || prefab == null)
                return -1;
            for (int i = 0; i < options.Length; i++)
            {
                if (options[i].Prefab == prefab)
                    return i;
            }

            return -1;
        }
    }
}
