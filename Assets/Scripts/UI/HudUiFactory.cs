using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace GhostHunter.UI
{
    /// <summary>HUD의 Canvas와 도형 스프라이트를 만들고 생성한 자원의 수명을 관리한다.</summary>
    internal sealed class HudUiFactory : IDisposable
    {
        private readonly List<Object> _resources = new();

        /// <summary>지정한 정렬 순서와 기준 해상도로 화면 HUD를 만든다.</summary>
        public Canvas CreateCanvas(string name, Transform parent, int sortingOrder, Vector2 referenceResolution)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            _resources.Add(root);
            Canvas canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            CanvasScaler scaler = root.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = referenceResolution;
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            return canvas;
        }

        /// <summary>부모 안에 UI 사각형을 만든다.</summary>
        public static RectTransform CreateRect(string name, Transform parent)
        {
            var root = new GameObject(name, typeof(RectTransform));
            var rect = (RectTransform)root.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>사각형을 부모 전체에 맞춘다.</summary>
        public static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.pivot = new Vector2(0.5f, 0.5f);
        }

        /// <summary>입력을 가리지 않는 HUD 이미지를 만든다.</summary>
        public static Image CreateImage(string name, Transform parent, Sprite sprite)
        {
            RectTransform rect = CreateRect(name, parent);
            Stretch(rect);
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.raycastTarget = false;
            return image;
        }

        /// <summary>채워진 원을 만든다.</summary>
        public Sprite CreateCircleSprite(int resolution, float radius, string name) =>
            CreateCircularSprite(resolution, radius, -1f, name);

        /// <summary>지정한 반경과 두께의 링을 만든다.</summary>
        public Sprite CreateRingSprite(int resolution, float radius, float thickness, string name)
        {
            float inner = Mathf.Max(0f, radius - thickness);
            return CreateCircularSprite(resolution, radius, inner * inner, name);
        }

        /// <summary>화면 오버레이에 쓰는 흰색 한 픽셀을 만든다.</summary>
        public Sprite CreateWhiteSprite(string name) =>
            CreateSprite(1, name, new Color32[] { Color.white }, FilterMode.Point, 1f);

        /// <summary>12시 방향을 가리키는 삼각형을 만든다.</summary>
        public Sprite CreateArrowSprite(int resolution, string name)
        {
            var pixels = new Color32[resolution * resolution];
            float center = (resolution - 1) * 0.5f;
            for (int y = 0; y < resolution; y++)
            {
                float halfWidth = (1f - (float)y / (resolution - 1)) * resolution * 0.5f;
                for (int x = 0; x < resolution; x++)
                    pixels[y * resolution + x] = Mathf.Abs(x - center) <= halfWidth ? Color.white : Color.clear;
            }
            return CreateSprite(resolution, name, pixels);
        }

        private Sprite CreateCircularSprite(int resolution, float radius, float innerSquared, string name)
        {
            var pixels = new Color32[resolution * resolution];
            float center = (resolution - 1) * 0.5f;
            float outerSquared = radius * radius;
            for (int y = 0; y < resolution; y++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float dx = x - center;
                    float dy = y - center;
                    float distanceSquared = dx * dx + dy * dy;
                    pixels[y * resolution + x] = distanceSquared <= outerSquared && distanceSquared >= innerSquared
                        ? Color.white : Color.clear;
                }
            }
            return CreateSprite(resolution, name, pixels);
        }

        private Sprite CreateSprite(int resolution, string name, Color32[] pixels,
            FilterMode filter = FilterMode.Bilinear, float pixelsPerUnit = 100f)
        {
            var texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false)
            {
                name = name,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
            };
            _resources.Add(texture);
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, resolution, resolution),
                new Vector2(0.5f, 0.5f), pixelsPerUnit);
            _resources.Add(sprite);
            return sprite;
        }

        /// <summary>이 HUD가 만든 자원을 해제한다. 외부에서 받은 아이콘은 소유하지 않는다.</summary>
        public void Dispose()
        {
            // 스프라이트 → 텍스처 → Canvas 순서. 다른 HUD의 자원을 함께 해제하지 않는다.
            for (int i = _resources.Count - 1; i >= 0; i--)
            {
                Object resource = _resources[i];
                if (resource == null)
                    continue;
                if (Application.isPlaying)
                    Object.Destroy(resource);
                else
                    Object.DestroyImmediate(resource);
            }
            _resources.Clear();
        }
    }
}
