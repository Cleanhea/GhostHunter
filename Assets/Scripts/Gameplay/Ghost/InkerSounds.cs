using UnityEngine;

namespace GhostHunter.Gameplay.Ghost
{
    /// <summary>
    /// 인커 현상의 임시 합성음. 원문 첨부 음원(문 삐걱·긁는 소리·글씨 긁기·목 회전)이 저장소에 아직 없어
    /// 경고 심장 박동음처럼 코드로 만든다(project/ghost-inker.md §7). 음원이 들어오면 이 클래스를 교체한다.
    /// 각 피어에서 로컬로 재생하며 오디오 서비스·믹서를 거치지 않는다.
    /// </summary>
    public static class InkerSounds
    {
        private const int SampleRate = 22050;

        private static AudioClip s_creak;
        private static AudioClip s_slam;
        private static AudioClip s_scratch;
        private static AudioClip s_write;
        private static AudioClip s_neck;
        private static AudioClip s_scream;

        /// <summary>문이 천천히 열리는 삐걱 소리(2초).</summary>
        public static AudioClip Creak => s_creak ??= Build("InkerCreak", 2f, CreakSample);

        /// <summary>문이 세게 닫히는 소리.</summary>
        public static AudioClip Slam => s_slam ??= Build("InkerSlam", 0.6f, SlamSample);

        /// <summary>문 바깥에서 긁는 소리 — 원문 메모대로 피치를 낮게 잡았다.</summary>
        public static AudioClip Scratch => s_scratch ??= Build("InkerScratch", 2f, t => ScratchSample(t, 9f, 0.55f));

        /// <summary>
        /// 젖은 피를 손가락으로 벽에 문질러 글자를 쓰는 소리(반복 재생) — 낮게 깔린 끈적한 마찰 + 획마다 질척임 + 가끔 떨어지는 방울
        /// (사용자 요청 2026-11-15 "피로 쓰이는 느낌").
        /// </summary>
        public static AudioClip Write => s_write ??= BuildWetSmear("InkerWrite", 2f);

        /// <summary>목이 돌아가는 소리(뼈 꺾이는 딸깍임).</summary>
        public static AudioClip Neck => s_neck ??= Build("InkerNeck", 1f, NeckSample);

        /// <summary>귀신이 화낼 때(갑툭튀) 비명.</summary>
        public static AudioClip Scream => s_scream ??= Build("InkerScream", 0.9f, ScreamSample);

        /// <summary>월드 좌표 3D 재생. 끝나면 스스로 지운다.</summary>
        public static AudioSource PlayAt(AudioClip clip, Vector3 position, float volume, bool loop = false)
        {
            if (clip == null)
                return null;

            var host = new GameObject("InkerSound");
            host.transform.position = position;
            AudioSource source = host.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = loop;
            source.volume = volume;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 1f;
            source.maxDistance = 18f;
            source.dopplerLevel = 0f;
            source.Play();
            if (!loop)
                Object.Destroy(host, clip.length + 0.1f);
            return source;
        }

        /// <summary>위치 없는 2D 재생(내 머릿속 소리 — 갑툭튀 비명).</summary>
        public static void Play2D(AudioClip clip, float volume)
        {
            AudioSource source = PlayAt(clip, Vector3.zero, volume);
            if (source != null)
                source.spatialBlend = 0f;
        }

        public static void Stop(AudioSource source)
        {
            if (source != null)
                Object.Destroy(source.gameObject);
        }

        private delegate float SampleFunction(float time);

        private static AudioClip Build(string name, float seconds, SampleFunction function)
        {
            int count = Mathf.CeilToInt(seconds * SampleRate);
            float[] samples = new float[count];
            Random.State saved = Random.state;
            Random.InitState(name.GetHashCode());
            for (int i = 0; i < count; i++)
                samples[i] = Mathf.Clamp(function(i / (float)SampleRate), -1f, 1f);
            Random.state = saved;

            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float Noise() => Random.value * 2f - 1f;

        /// <summary>
        /// 젖은 문지름: 저역 통과 잡음(끈적한 마찰)을 손가락 획 박자로 여닫고, 획 시작마다 낮은 질척임(음정이 떨어지는 짧은 울림),
        /// 사이사이 방울 떨어지는 소리를 섞는다. 반복 재생해도 이음매가 튀지 않게 끝을 처음과 맞춘다.
        /// </summary>
        private static AudioClip BuildWetSmear(string name, float seconds)
        {
            int count = Mathf.CeilToInt(seconds * SampleRate);
            float[] samples = new float[count];
            Random.State saved = Random.state;
            Random.InitState(name.GetHashCode());

            float low = 0f, lower = 0f;
            // 획 시작 시각(불규칙한 손가락 박자).
            var strokes = new System.Collections.Generic.List<float>();
            for (float at = 0.05f; at < seconds - 0.2f; at += Random.Range(0.22f, 0.45f))
                strokes.Add(at);
            var drips = new System.Collections.Generic.List<float>();
            for (int i = 0; i < 3; i++)
                drips.Add(Random.Range(0.2f, seconds - 0.15f));

            for (int i = 0; i < count; i++)
            {
                float t = i / (float)SampleRate;
                // 끈적한 마찰 — 두 번 거른 갈색 잡음.
                low += (Noise() - low) * 0.08f;
                lower += (low - lower) * 0.12f;

                float envelope = 0f;
                float squelch = 0f;
                foreach (float at in strokes)
                {
                    float dt = t - at;
                    if (dt < 0f || dt > 0.4f)
                        continue;
                    envelope = Mathf.Max(envelope, Mathf.Sin(Mathf.Clamp01(dt / 0.34f) * Mathf.PI));
                    if (dt < 0.09f)
                    {
                        float pitch = Mathf.Lerp(190f, 70f, dt / 0.09f);
                        squelch += Mathf.Sin(2f * Mathf.PI * pitch * dt) * Mathf.Exp(-dt * 38f) * 0.55f;
                    }
                }

                float drip = 0f;
                foreach (float at in drips)
                {
                    float dt = t - at;
                    if (dt < 0f || dt > 0.08f)
                        continue;
                    float pitch = Mathf.Lerp(900f, 420f, dt / 0.08f);
                    drip += Mathf.Sin(2f * Mathf.PI * pitch * dt) * Mathf.Exp(-dt * 70f) * 0.35f;
                }

                float smear = lower * 5.5f * (0.25f + 0.75f * envelope);
                float loopFade = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((seconds - t) / 0.03f);
                samples[i] = Mathf.Clamp((smear + squelch + drip) * loopFade, -1f, 1f);
            }

            Random.state = saved;
            AudioClip clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static float CreakSample(float t)
        {
            // 마찰이 걸렸다 풀리는 낮은 톱니파 + 거친 진폭 떨림.
            float frequency = Mathf.Lerp(150f, 95f, t / 2f) + Mathf.Sin(t * 23f) * 12f;
            float saw = 2f * Mathf.Repeat(t * frequency, 1f) - 1f;
            float stick = 0.5f + 0.5f * Mathf.Sin(t * 41f + Mathf.Sin(t * 7f) * 3f);
            float envelope = Mathf.SmoothStep(0f, 1f, t / 0.15f) * Mathf.SmoothStep(0f, 1f, (2f - t) / 0.3f);
            return (saw * 0.35f * stick + Noise() * 0.04f) * envelope;
        }

        private static float SlamSample(float t)
        {
            float thump = Mathf.Sin(2f * Mathf.PI * 55f * t) * Mathf.Exp(-t * 9f);
            float crack = Noise() * Mathf.Exp(-t * 40f);
            float rattle = Noise() * 0.15f * Mathf.Exp(-t * 6f) * (0.5f + 0.5f * Mathf.Sin(t * 180f));
            return thump * 0.9f + crack * 0.6f + rattle;
        }

        private static float ScratchSample(float t, float strokesPerSecond, float brightness)
        {
            float phase = Mathf.Repeat(t * strokesPerSecond, 1f);
            float stroke = phase < 0.7f ? Mathf.Sin(phase / 0.7f * Mathf.PI) : 0f;
            // 밝기를 낮출수록 거친 저역 성분이 많다(원문: 피치 낮추면 좋겠다).
            float grit = Noise() * brightness + Mathf.Sin(2f * Mathf.PI * (300f * brightness) * t) * 0.2f;
            return grit * stroke * 0.45f;
        }

        private static float NeckSample(float t)
        {
            float clicks = 0f;
            for (int i = 0; i < 7; i++)
            {
                float at = 0.08f + i * 0.12f + Mathf.Sin(i * 12.9898f) * 0.03f;
                float dt = t - at;
                if (dt >= 0f && dt < 0.03f)
                    clicks += Noise() * Mathf.Exp(-dt * 200f);
            }

            float groan = Mathf.Sin(2f * Mathf.PI * (70f + t * 20f) * t) * 0.12f * Mathf.Sin(Mathf.PI * Mathf.Clamp01(t));
            return clicks * 0.8f + groan;
        }

        private static float ScreamSample(float t)
        {
            float wobble = Mathf.Sin(t * 37f) * 40f;
            float a = 2f * Mathf.Repeat(t * (620f + wobble), 1f) - 1f;
            float b = 2f * Mathf.Repeat(t * (933f - wobble), 1f) - 1f;
            float envelope = Mathf.Clamp01(t / 0.02f) * Mathf.Exp(-t * 2.2f);
            return (a * 0.35f + b * 0.3f + Noise() * 0.35f) * envelope;
        }
    }
}
