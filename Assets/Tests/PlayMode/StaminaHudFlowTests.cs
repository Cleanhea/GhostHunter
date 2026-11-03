using System.Collections;
using System.Reflection;
using GhostHunter.Gameplay.Player;
using GhostHunter.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>스태미나 HUD 바의 배치와 런타임 도형의 해제를 검증한다.</summary>
    public sealed class StaminaHudFlowTests
    {
        private GameObject _root;
        private StaminaSettings _settings;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null)
                Object.Destroy(_root);
            yield return null;
            if (_settings != null)
                Object.DestroyImmediate(_settings);
        }

        [Test]
        public void 흰색_바를_화면_아래_가운데에_왼쪽부터_채운다()
        {
            Canvas canvas = BuildCanvas(0.5f);

            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            Assert.AreEqual(130, canvas.sortingOrder);

            var bar = (RectTransform)canvas.transform.Find("StaminaBar");
            Assert.AreEqual(new Vector2(0.5f, 0f), bar.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 0f), bar.anchorMax);
            Assert.AreEqual(new Vector2(0.5f, 0f), bar.pivot);
            Assert.AreEqual(new Vector2(_settings.BarWidth, _settings.BarHeight), bar.sizeDelta);
            Assert.AreEqual(new Vector2(0f, _settings.BottomMargin), bar.anchoredPosition);

            var fill = (RectTransform)bar.Find("Fill");
            Assert.AreEqual(Vector2.zero, fill.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 1f), fill.anchorMax, "비율만큼 왼쪽에서 채워진다.");
            Image fillImage = fill.GetComponent<Image>();
            Assert.AreEqual(_settings.BarColor, fillImage.color);
            Assert.AreEqual(Color.white, _settings.BarColor, "기본 바 색은 흰색이다.");
            Assert.IsFalse(fillImage.raycastTarget);
            Assert.IsFalse(bar.GetComponent<Image>().raycastTarget);
        }

        [Test]
        public void 가득_찬_채_스폰하면_바는_숨은_상태로_시작한다()
        {
            Canvas canvas = BuildCanvas(1f);

            Assert.AreEqual(0f, canvas.GetComponent<CanvasGroup>().alpha);
        }

        [Test]
        public void 덜_찬_채_스폰하면_바는_보이는_상태로_시작한다()
        {
            Canvas canvas = BuildCanvas(0.4f);

            Assert.AreEqual(1f, canvas.GetComponent<CanvasGroup>().alpha);
        }

        [UnityTest]
        public IEnumerator HUD를_제거하면_만든_도형을_해제한다()
        {
            Canvas canvas = BuildCanvas(0.5f);
            Sprite sprite = canvas.GetComponentInChildren<Image>(true).sprite;
            Texture texture = sprite.texture;
            Assert.IsNotNull(sprite);

            Object.Destroy(_root);
            // OnDestroy 안에서 요청한 스프라이트·텍스처 해제까지 기다린다.
            yield return null;
            yield return null;

            Assert.IsTrue(sprite == null && texture == null, "HUD가 만든 스프라이트나 텍스처가 남았습니다.");
        }

        private Canvas BuildCanvas(float ratio)
        {
            _settings = ScriptableObject.CreateInstance<StaminaSettings>();
            _root = new GameObject("StaminaHudTest");
            _root.SetActive(false);
            var stamina = _root.AddComponent<PlayerStamina>();
            Set(stamina, "_settings", _settings);
            var hud = _root.AddComponent<StaminaHud>();
            Set(hud, "_stamina", stamina);
            // 로컬 소유 플레이어에서만 지연 생성하므로, 표시부 생성만 독립적으로 검사한다.
            hud.enabled = false;
            _root.SetActive(true);

            typeof(StaminaHud).GetMethod("BuildCanvas", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(hud, new object[] { _settings, ratio });
            return _root.GetComponentInChildren<Canvas>(true);
        }

        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
