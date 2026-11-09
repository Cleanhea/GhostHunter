using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 말소리 크기 → 귀신이 듣는 반경(원문 0.4 소음표 "음성 채팅 — 보통~높음, 말소리 크기에 따라 감지"). 순수 계산이라 EditMode 로 검증한다.
    /// 입력 레벨(dBFS)이 조용한 값 이하면 최소(보통) 반경, 시끄러운 값 이상이면 최대(높음) 반경이고 그 사이는 선형 보간이다.
    /// 수치는 원문에 없어 설정의 [TEMP] 값이다(ghost-system.md G-27).
    /// </summary>
    internal static class GhostVoiceNoise
    {
        /// <summary>0(조용함) ~ 1(시끄러움). 두 기준이 같거나 뒤집혀 있으면 기준 위·아래로만 가른다.</summary>
        internal static float Loudness(float decibels, float quietDb, float loudDb)
        {
            if (loudDb <= quietDb)
                return decibels >= loudDb ? 1f : 0f;
            return Mathf.Clamp01((decibels - quietDb) / (loudDb - quietDb));
        }

        internal static float Radius(float decibels, float quietDb, float loudDb, float minRadius, float maxRadius)
        {
            return Mathf.Lerp(minRadius, Mathf.Max(minRadius, maxRadius), Loudness(decibels, quietDb, loudDb));
        }

        /// <summary>절반 이상 시끄러우면 '높음', 아니면 '보통'. 귀신 소음 기억의 우선순위에만 쓴다.</summary>
        internal static GhostNoiseLevel Level(float loudness)
        {
            return loudness >= 0.5f ? GhostNoiseLevel.High : GhostNoiseLevel.Medium;
        }
    }
}
