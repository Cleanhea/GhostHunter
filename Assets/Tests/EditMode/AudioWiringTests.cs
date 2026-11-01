using System.Collections.Generic;
using System.IO;
using GhostHunter.Core.Audio;
using GhostHunter.Data.Audio;
using GhostHunter.Gameplay.Audio;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Player;
using GhostHunter.Gameplay.Revival;
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
        private const string LighterSettingsPath = "Assets/Settings/Gameplay/LighterSettings_Default.asset";
        private const string HeadlampSettingsPath = "Assets/Settings/Gameplay/HeadlampSettings_Default.asset";
        private const string DriverSettingsPath = "Assets/Settings/Gameplay/FurnitureDriverSettings_Default.asset";
        private const string DetectSettingsPath = "Assets/Settings/Gameplay/DetectionSkillSettings_Default.asset";
        private const string BurrowSettingsPath = "Assets/Settings/Gameplay/MoleBurrowSettings_Default.asset";
        private const string RevivalSettingsPath = "Assets/Settings/Gameplay/RevivalRitualSettings_Default.asset";

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

        [Test]
        public void 라이터_헤드라이트_드라이버_스킬_부활_의식_설정의_효과음_큐는_클립을_가진다()
        {
            var lighter = AssetDatabase.LoadAssetAtPath<LighterSettings>(LighterSettingsPath);
            var revival = AssetDatabase.LoadAssetAtPath<RevivalRitualSettings>(RevivalSettingsPath);
            Assert.NotNull(lighter, LighterSettingsPath);
            Assert.NotNull(revival, RevivalSettingsPath);
            var headlamp = AssetDatabase.LoadAssetAtPath<HeadlampSettings>(HeadlampSettingsPath);
            Assert.NotNull(headlamp, HeadlampSettingsPath);
            var driver = AssetDatabase.LoadAssetAtPath<FurnitureDriverSettings>(DriverSettingsPath);
            Assert.NotNull(driver, DriverSettingsPath);
            var detect = AssetDatabase.LoadAssetAtPath<DetectionSkillSettings>(DetectSettingsPath);
            Assert.NotNull(detect, DetectSettingsPath);
            var burrow = AssetDatabase.LoadAssetAtPath<MoleBurrowSettings>(BurrowSettingsPath);
            Assert.NotNull(burrow, BurrowSettingsPath);

            var cues = new (string Name, SoundCue Cue)[]
            {
                ("라이터 OnCue", lighter.OnCue),
                ("라이터 OffCue", lighter.OffCue),
                ("라이터 BrokenCue", lighter.BrokenCue),
                ("헤드라이트 OnCue", headlamp.OnCue),
                ("헤드라이트 OffCue", headlamp.OffCue),
                ("헤드라이트 BrokenCue", headlamp.BrokenCue),
                ("드라이버 사용 중(>임계)", driver.UseCueHighDurability),
                ("드라이버 사용 중(≤임계)", driver.UseCueLowDurability),
                ("드라이버 중단", driver.CancelCue),
                ("드라이버 완료", driver.CompleteCue),
                ("탐지 CastCue", detect.CastCue),
                ("굴착 HideCue", burrow.HideCue),
                ("굴착 PopCue", burrow.PopCue),
                ("의식 IgniteStartCue", revival.IgniteStartCue),
                ("의식 IgniteFinishCue", revival.IgniteFinishCue),
            };
            foreach ((string name, SoundCue cue) in cues)
            {
                Assert.NotNull(cue, $"{name} 이 배선되지 않았다.");
                Assert.IsTrue(cue.HasClips, $"{name}({cue.name}) 에 클립이 없다.");
            }

            // 든 사람에게만 들리는 라이터 소리는 2D, 촛대 위치에서 나는 의식 소리는 3D 다.
            Assert.AreEqual(0f, lighter.OnCue.SpatialBlend);
            Assert.AreEqual(1f, revival.IgniteFinishCue.SpatialBlend);
        }
    }
}
