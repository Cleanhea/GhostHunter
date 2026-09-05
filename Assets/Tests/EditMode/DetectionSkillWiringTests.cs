using System.IO;
using GhostHunter.Gameplay.Player;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>탐지 입력·대상 스캐폴드·설정 에셋의 파일 배선을 확인한다.</summary>
    public sealed class DetectionSkillWiringTests
    {
        private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
        private const string ControllerPath =
            "Assets/Scripts/Gameplay/Player/DetectionSkillController.cs";
        private const string MarkerPath =
            "Assets/Scripts/Gameplay/Player/DetectionTargetMarker.cs";

        private static readonly string[] IconPaths =
        {
            "Assets/Sprite/Skill_icon/ICON_ detection_on.png",
            "Assets/Sprite/Skill_icon/ICON_ detection_off.png",
            "Assets/Sprite/Skill_icon/ICON_excavation_on.png",
            "Assets/Sprite/Skill_icon/ICON_excavation_off.png",
        };

        [Test]
        public void Player_Detect_액션은_Q키에_바인딩되어_있다()
        {
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            Assert.IsNotNull(actions, $"{InputActionsPath} 를 찾지 못했습니다.");

            InputAction detect = actions.FindAction("Player/Detect", false);
            Assert.IsNotNull(detect, "Player/Detect 액션이 없습니다.");

            bool hasKeyboardQ = false;
            foreach (InputBinding binding in detect.bindings)
                hasKeyboardQ |= binding.path == "<Keyboard>/q";

            Assert.IsTrue(hasKeyboardQ, "Player/Detect 액션에 Q키 바인딩이 없습니다.");
        }

        [Test]
        public void 탐지_컨트롤러는_로컬_전용이고_네트워크_상태를_복제하지_않는다()
        {
            Assert.IsTrue(File.Exists(ControllerPath), $"{ControllerPath} 파일이 없습니다.");
            string source = File.ReadAllText(ControllerPath);

            Assert.IsTrue(source.Contains("IsOwner"), "탐지 컨트롤러에 소유자 게이팅이 없습니다.");
            Assert.IsFalse(source.Contains("NetworkVariable"), "탐지는 NetworkVariable을 사용하면 안 됩니다.");
            Assert.IsFalse(source.Contains("[Rpc("), "탐지는 RPC를 사용하면 안 됩니다.");
        }

        [Test]
        public void 탐지_대상은_활성_마커만_표시한다()
        {
            Assert.IsTrue(File.Exists(MarkerPath), $"{MarkerPath} 파일이 없습니다.");
            string source = File.ReadAllText(MarkerPath);

            Assert.IsTrue(source.Contains("_targetActive"), "마커 활성 상태 필드가 없습니다.");
            Assert.IsTrue(source.Contains("DetectionTargetKind"), "마커 종류 enum이 없습니다.");
            Assert.IsTrue(source.Contains("if (!_targetActive"), "비활성 마커를 건너뛰는 판정이 없습니다.");
        }

        [Test]
        public void 스킬_아이콘_4장은_Sprite_서브에셋으로_로드된다()
        {
            foreach (string path in IconPaths)
            {
                Assert.IsTrue(File.Exists(path), $"{path} 파일이 없습니다.");
                TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
                Assert.IsNotNull(importer, $"{path} TextureImporter를 읽지 못했습니다.");
                Assert.AreEqual(
                    SpriteImportMode.Single,
                    importer.spriteImportMode,
                    $"{path} 는 SpriteImportMode.Single이어야 합니다.");

                var sprite = AssetDatabase.LoadAssetAtPath<UnityEngine.Sprite>(path);
                Assert.IsNotNull(sprite, $"{path} Sprite 서브 에셋을 읽지 못했습니다.");
            }
        }

        [Test]
        public void 탐지_설정의_기획값은_5초_표시와_10초_쿨타임이다()
        {
            var settings = ScriptableObject.CreateInstance<DetectionSkillSettings>();
            try
            {
                Assert.AreEqual(5f, settings.DisplayDurationSeconds, 0.001f);
                Assert.AreEqual(10f, settings.CooldownSeconds, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(settings);
            }
        }
    }
}
