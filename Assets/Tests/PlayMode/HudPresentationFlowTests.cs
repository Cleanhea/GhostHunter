using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using GhostHunter.Gameplay.FurnitureDriver;
using GhostHunter.Gameplay.Player;
using GhostHunter.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GhostHunter.Tests.PlayMode
{
    /// <summary>HUD 배치·게이지 방향과 런타임 도형의 소유·해제를 검증한다.</summary>
    public sealed class HudPresentationFlowTests
    {
        private readonly List<GameObject> _roots = new();
        private readonly List<Object> _assets = new();
        private Sprite _icon;

        [SetUp]
        public void SetUp()
        {
            var texture = new Texture2D(1, 1);
            _assets.Add(texture);
            _icon = Sprite.Create(texture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f);
            _assets.Add(_icon);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (GameObject root in _roots)
                if (root != null)
                    Object.Destroy(root);
            yield return null;
            foreach (Object asset in _assets)
                if (asset != null)
                    Object.DestroyImmediate(asset);
            _roots.Clear();
            _assets.Clear();
        }

        [Test]
        public void 원형_HUD의_배치와_채움_방향을_유지한다()
        {
            var settings = CreateSettings<MoleSkillUiSettings>();
            Canvas mole = Build<MoleSkillHud>(settings);
            Assert.AreEqual(200, mole.sortingOrder);
            CheckTopRight(mole, "DetectionIndicator", settings, 0);
            CheckTopRight(mole, "ExcavationIndicator", settings, 1);
            CheckRing(mole, "DetectionIndicator/ProgressRing", clockwise: false);
            CheckRing(mole, "ExcavationIndicator/ProgressRing", clockwise: false);

            Canvas headlamp = Build<HeadlampChargeHud>(settings);
            Assert.AreEqual(201, headlamp.sortingOrder);
            CheckTopRight(headlamp, "HeadlampChargeIndicator", settings, 2);
            CheckRing(headlamp, "HeadlampChargeIndicator/ChargeRing", clockwise: true);
            RectTransform icon = (RectTransform)headlamp.transform.Find("HeadlampChargeIndicator/Icon");
            Assert.AreEqual(new Vector2(0.31f, 0.16f), icon.anchorMin);
            Assert.AreEqual(new Vector2(0.69f, 0.54f), icon.anchorMax);

            Canvas lighter = Build<LighterFuelHud>(settings);
            Assert.AreEqual(201, lighter.sortingOrder);
            CheckTopRight(lighter, "LighterFuelIndicator", settings, 3);
            CheckRing(lighter, "LighterFuelIndicator/FuelRing", clockwise: true);

            var driverSettings = CreateSettings<FurnitureDriverUiSettings>();
            Canvas driver = Build<FurnitureDriverActionHud>(driverSettings);
            Assert.AreEqual(140, driver.sortingOrder);
            CheckRing(driver, "ActionGauge/Fill", clockwise: true);
            RectTransform gauge = (RectTransform)driver.transform.Find("ActionGauge");
            Assert.AreEqual(Vector2.one * 0.5f, gauge.anchorMin);
            Assert.AreEqual(Vector2.zero, gauge.anchoredPosition);
            Assert.AreEqual(Vector2.one * driverSettings.RingDiameter, gauge.sizeDelta);

            Canvas wheel = Build<QuickSlotWheelUi>(CreateSettings<QuickSlotUiSettings>());
            Assert.AreEqual(150, wheel.sortingOrder);
            Assert.AreEqual(Image.Type.Simple, wheel.transform.Find("Wheel/RingContainer/Ring").GetComponent<Image>().type);
        }

        [UnityTest]
        public IEnumerator HUD를_제거하면_도형을_해제하고_외부_아이콘을_보존한다()
        {
            var settings = CreateSettings<MoleSkillUiSettings>();
            Canvas[] canvases =
            {
                Build<MoleSkillHud>(settings), Build<HeadlampChargeHud>(settings), Build<LighterFuelHud>(settings),
                Build<FurnitureDriverActionHud>(CreateSettings<FurnitureDriverUiSettings>()),
                Build<QuickSlotWheelUi>(CreateSettings<QuickSlotUiSettings>()),
            };
            var generated = new HashSet<Object>();
            foreach (Canvas canvas in canvases)
                foreach (Image image in canvas.GetComponentsInChildren<Image>(true))
                    if (image.sprite != null && image.sprite != _icon)
                    {
                        generated.Add(image.sprite);
                        generated.Add(image.sprite.texture);
                    }
            Assert.Greater(generated.Count, 0);

            foreach (GameObject root in _roots)
                Object.Destroy(root);
            // OnDestroy 안에서 요청한 스프라이트·텍스처 해제까지 기다린다.
            yield return null;
            yield return null;
            foreach (Object resource in generated)
                Assert.IsTrue(resource == null, "HUD가 만든 스프라이트나 텍스처가 남았습니다.");
            Assert.IsTrue(_icon != null && _icon.texture != null, "외부 아이콘은 HUD의 소유가 아닙니다.");
        }

        private Canvas Build<T>(ScriptableObject settings) where T : MonoBehaviour
        {
            var root = new GameObject(typeof(T).Name);
            root.SetActive(false);
            _roots.Add(root);
            T hud = root.AddComponent<T>();
            Set(hud, "_uiSettings", settings);
            if (hud is MoleSkillHud)
                foreach (string name in new[] { "_detectionOnIcon", "_detectionOffIcon", "_excavationOnIcon", "_excavationOffIcon" })
                    Set(hud, name, _icon);
            if (hud is HeadlampChargeHud || hud is LighterFuelHud)
            {
                var player = new GameObject("InactivePlayerReference");
                player.SetActive(false);
                player.transform.SetParent(root.transform, false);
                if (hud is HeadlampChargeHud)
                {
                    Set(hud, "_headlamp", player.AddComponent<PlayerHeadlamp>());
                    Set(hud, "_onIcon", _icon);
                }
                else
                    Set(hud, "_lighter", player.AddComponent<PlayerLighter>());
            }
            if (hud is QuickSlotWheelUi)
                Set(hud, "_loadout", CreateSettings<QuickSlotLoadout>());
            hud.enabled = false;
            root.SetActive(true);
            Canvas canvas = root.GetComponentInChildren<Canvas>(true);
            // 장비 HUD는 로컬 소유자에서만 지연 생성한다. 표시부 생성만 독립적으로 검사한다.
            if (canvas == null)
            {
                typeof(T).GetMethod("BuildCanvas", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(hud, null);
                canvas = root.GetComponentInChildren<Canvas>(true);
            }
            Assert.AreEqual(RenderMode.ScreenSpaceOverlay, canvas.renderMode);
            return canvas;
        }

        private T CreateSettings<T>() where T : ScriptableObject
        {
            T settings = ScriptableObject.CreateInstance<T>();
            _assets.Add(settings);
            return settings;
        }

        private static void CheckTopRight(Canvas canvas, string path, MoleSkillUiSettings settings, int slot)
        {
            var rect = (RectTransform)canvas.transform.Find(path);
            Assert.AreEqual(Vector2.one, rect.anchorMin);
            Assert.AreEqual(Vector2.one, rect.anchorMax);
            Assert.AreEqual(Vector2.one, rect.pivot);
            Assert.AreEqual(Vector2.one * settings.IndicatorSize, rect.sizeDelta);
            Assert.AreEqual(new Vector2(-settings.RightMargin,
                -settings.TopMargin - slot * (settings.IndicatorSize + settings.VerticalSpacing)), rect.anchoredPosition);
        }

        private static void CheckRing(Canvas canvas, string path, bool clockwise)
        {
            Image ring = canvas.transform.Find(path).GetComponent<Image>();
            Assert.AreEqual(Image.Type.Filled, ring.type);
            Assert.AreEqual(Image.FillMethod.Radial360, ring.fillMethod);
            Assert.AreEqual((int)Image.Origin360.Top, ring.fillOrigin);
            Assert.AreEqual(clockwise, ring.fillClockwise);
            Assert.IsFalse(ring.raycastTarget);
        }

        private static void Set(object target, string name, object value) => target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
