using System;
using GhostHunter.Gameplay.Furniture;
using GhostHunter.Gameplay.Player;
using UnityEngine;

namespace GhostHunter.Gameplay.Interaction
{
    /// <summary>
    /// 시체 조준(윤곽선)·누름·놓음의 로컬 상태. 가구와 같이 누르면 잡고 떼면 던진다.
    /// 서버 요청은 GrabController의 RPC로 보내고, 상태·차지·홀더는 서버가 정해 PlayerVisuals로 복제한다.
    /// </summary>
    internal sealed class CorpseInteraction
    {
        private readonly Camera _camera;
        private readonly FurnitureThrowSettings _settings;
        private readonly ulong _localClientId;
        private readonly Action<ulong, Vector3, Vector3> _grab;
        private readonly Action<ulong, Vector3, Vector3> _aim;
        private readonly Action<ulong, Vector3, bool> _release;
        private PlayerVisuals _target;
        private PlayerVisuals _hover;

        public bool IsActive => _target != null;

        /// <summary>지금 누르고 있는 시체. 없으면 null.</summary>
        public PlayerVisuals Target => _target;

        public CorpseInteraction(
            Camera camera, FurnitureThrowSettings settings, ulong localClientId,
            Action<ulong, Vector3, Vector3> grab, Action<ulong, Vector3, Vector3> aim,
            Action<ulong, Vector3, bool> release)
        {
            _camera = camera;
            _settings = settings;
            _localClientId = localClientId;
            _grab = grab;
            _aim = aim;
            _release = release;
        }

        /// <summary>조준선이 시체를 겨누고 있으면 윤곽선을 켜고, 아니면 끈다. 매 프레임 부른다.</summary>
        public void UpdateHover(bool enabled)
        {
            PlayerVisuals next = null;
            if (enabled && !IsActive)
                TryFindCorpse(out next);

            if (next == _hover)
                return;

            if (_hover != null)
                _hover.SetTargeted(false);
            _hover = next;
            if (_hover != null)
                _hover.SetTargeted(true);
        }

        public bool TryBegin(bool furnitureBusy)
        {
            if (furnitureBusy || IsActive || !TryFindCorpse(out PlayerVisuals corpse))
                return false;

            _target = corpse;
            _grab(corpse.NetworkObjectId, _camera.transform.position, _camera.transform.forward);
            return true;
        }

        public void Tick(bool released, float aimSendInterval, ref float nextAimSendAt)
        {
            if (_target == null)
                return;

            if (released)
            {
                _release(_target.NetworkObjectId, _camera.transform.forward, true);
                Clear();
                return;
            }

            // 서버가 잡기를 받아들여 내가 홀더로 복제된 뒤에만 조준을 보낸다(가구와 같다).
            if (_target.ContainsHolder(_localClientId) && Time.unscaledTime >= nextAimSendAt)
            {
                nextAimSendAt = Time.unscaledTime + aimSendInterval;
                _aim(_target.NetworkObjectId, _camera.transform.position, _camera.transform.forward);
            }
        }

        /// <summary>발사 없이 놓는다. 일시정지·장비 장착처럼 던질 의도가 없는 취소에 쓴다.</summary>
        public void Cancel(bool isSpawned)
        {
            if (_target != null && isSpawned)
                _release(_target.NetworkObjectId, _camera.transform.forward, false);
            Clear();
        }

        private void Clear()
        {
            _target = null;
        }

        private bool TryFindCorpse(out PlayerVisuals corpse)
        {
            corpse = null;
            return _settings != null
                && Physics.Raycast(_camera.transform.position, _camera.transform.forward,
                    out RaycastHit hit, _settings.MaxTargetDistance,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                && PlayerVisuals.TryGetCorpseOwner(hit.collider, out corpse);
        }
    }
}
