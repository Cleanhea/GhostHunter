using System.Reflection;
using GhostHunter.Core.Audio;
using GhostHunter.Data.Audio;
using NUnit.Framework;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>오디오 테스트가 쓰는 큐·설정 ScriptableObject 를 메모리에서 만든다.</summary>
    internal static class AudioTestFactory
    {
        public static void Set(object target, string field, object value)
        {
            FieldInfo info = target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(info, $"{target.GetType().Name}.{field}");
            info.SetValue(target, value);
        }

        public static AudioClip CreateClip(string name = "clip") => AudioClip.Create(name, 64, 1, 44100, false);

        public static SoundCue CreateCue(string key, int clipCount = 1, bool loop = false)
        {
            var cue = ScriptableObject.CreateInstance<SoundCue>();
            cue.name = key;
            var clips = new AudioClip[clipCount];
            for (int i = 0; i < clipCount; i++)
                clips[i] = CreateClip($"{key}_{i}");
            Set(cue, "_key", key);
            Set(cue, "_clips", clips);
            Set(cue, "_loop", loop);
            return cue;
        }

        public static SoundLibrary CreateLibrary(params SoundCue[] cues)
        {
            var library = ScriptableObject.CreateInstance<SoundLibrary>();
            Set(library, "_cues", cues);
            return library;
        }

        public static GameAudioSettings CreateSettings(SoundLibrary library, int voiceCount = 4)
        {
            var settings = ScriptableObject.CreateInstance<GameAudioSettings>();
            Set(settings, "_library", library);
            Set(settings, "_voiceCount", voiceCount);
            return settings;
        }
    }
}
