#if UNITY_EDITOR
using System.Collections;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>
    /// 플레이어 몸 메시가 몸 콜라이더(CharacterController)와 같은 키인지 — player-controller.md "캐릭터 모델" 크기.
    /// 2026-10-04: 바인드 포즈 키(1.30m)만 보고 임포트 배율 1로 두어 실제 Idle 자세 키가 1.57m 였고, 머리가 캡슐 위로
    /// 27cm 나와 낮은 천장을 뚫었다. Idle 클립을 실제로 샘플해 정점을 구워 잰다.
    /// </summary>
    public sealed class PlayerBodyFitTests
    {
        private const float HeightTolerance = 0.03f;

        [UnityTest]
        public IEnumerator 서_있는_몸_메시는_캡슐_높이와_같고_머리가_캡슐_위로_나오지_않는다()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Player.prefab");
            Assert.IsNotNull(prefab);
            GameObject player = Object.Instantiate(prefab, new Vector3(50f, 0f, 50f), Quaternion.identity);
            try
            {
                yield return null;
                var controller = player.GetComponent<CharacterController>();
                Animator animator = player.GetComponentInChildren<Animator>(true);
                AnimationClip idle = System.Array.Find(animator.runtimeAnimatorController.animationClips,
                    clip => clip.name.ToLowerInvariant().Contains("idle"));
                Assert.IsNotNull(idle, "Idle 클립이 없습니다.");

                AnimationMode.StartAnimationMode();
                try
                {
                    AnimationMode.BeginSampling();
                    AnimationMode.SampleAnimationClip(animator.gameObject, idle, 0.2f);
                    AnimationMode.EndSampling();

                    SkinnedMeshRenderer skin = player.GetComponentInChildren<SkinnedMeshRenderer>(true);
                    var baked = new Mesh();
                    skin.BakeMesh(baked, true);
                    float bottom = float.PositiveInfinity;
                    float top = float.NegativeInfinity;
                    foreach (Vector3 vertex in baked.vertices)
                    {
                        float y = skin.transform.TransformPoint(vertex).y - player.transform.position.y;
                        bottom = Mathf.Min(bottom, y);
                        top = Mathf.Max(top, y);
                    }
                    Object.Destroy(baked);

                    float capsuleTop = controller.center.y + controller.height * 0.5f;
                    Assert.AreEqual(0f, bottom, HeightTolerance, "발이 바닥(캡슐 밑)에 닿아야 합니다.");
                    Assert.AreEqual(capsuleTop, top, HeightTolerance,
                        $"몸 메시 키 {top:F3}m 가 캡슐 {capsuleTop:F3}m 와 다릅니다 — MainCharacter.fbx 임포트 배율을 맞추세요.");
                }
                finally
                {
                    AnimationMode.StopAnimationMode();
                }
            }
            finally
            {
                Object.Destroy(player);
            }
        }
    }
}
#endif
