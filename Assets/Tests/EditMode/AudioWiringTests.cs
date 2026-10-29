using System.Collections.Generic;
using System.IO;
using GhostHunter.Data.Audio;
using GhostHunter.Gameplay.Audio;
using GhostHunter.Gameplay.Furniture;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>저장된 오디오 설정 에셋과 Bootstrap 씬 배선이 이어져 있는지 확인한다.</summary>
    public sealed class AudioWiringTests
    {
        private const string SettingsPath = "Assets/Settings/Audio/GameAudioSettings.asset";
        private const string LibraryPath = "Assets/Settings/Audio/SoundLibrary.asset";
        private const string ImpactPath = "Assets/Settings/Audio/ImpactSoundProfile_Default.asset";
        private const string BootstrapScenePath = "Assets/Scenes/Bootstrap.unity";

        [Test]
        public void 오디오_설정은_라이브러리와_효과음_믹서_그룹에_이어져_있다()
        {
            var settings = AssetDatabase.LoadAssetAtPath<GameAudioSettings>(SettingsPath);

            Assert.NotNull(settings, SettingsPath);
            Assert.NotNull(settings.Library, "Library");
            Assert.AreEqual(AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath), settings.Library);
            Assert.NotNull(settings.SfxGroup, "SfxGroup");
            Assert.AreEqual("SFX", settings.SfxGroup.name);
            Assert.AreEqual(32, settings.VoiceCount);
        }

        [Test]
        public void 저장된_라이브러리와_충돌_프로파일이_로드되고_문제가_없다()
        {
            var library = AssetDatabase.LoadAssetAtPath<SoundLibrary>(LibraryPath);

            Assert.NotNull(library, LibraryPath);
            Assert.IsEmpty(library.FindProblems());
            Assert.NotNull(AssetDatabase.LoadAssetAtPath<ImpactSoundProfile>(ImpactPath), ImpactPath);
        }

        [Test]
        public void Bootstrap_씬의_인스톨러가_오디오_설정을_가리킨다()
        {
            string guid = AssetDatabase.AssetPathToGUID(SettingsPath);

            Assert.IsNotEmpty(guid);
            StringAssert.Contains($"_audioSettings: {{fileID: 11400000, guid: {guid}",
                File.ReadAllText(BootstrapScenePath));
        }

        [Test]
        public void 가구_프리팹은_모두_충돌음_컴포넌트와_프로파일을_가진다()
        {
            var profile = AssetDatabase.LoadAssetAtPath<ImpactSoundProfile>(ImpactPath);
            var missing = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || prefab.GetComponent<FurnitureNetworkPhysics>() == null)
                    continue;

                bool wired = prefab.GetComponent<NetworkSoundEmitter>() != null
                    && prefab.TryGetComponent(out FurnitureImpactSound impact)
                    && new SerializedObject(impact).FindProperty("_profile").objectReferenceValue == profile;
                if (!wired)
                    missing.Add(path);
            }

            Assert.IsEmpty(missing, "NetworkSoundEmitter + FurnitureImpactSound(_profile) 가 빠진 가구 프리팹: " + string.Join(", ", missing));
        }
    }
}
