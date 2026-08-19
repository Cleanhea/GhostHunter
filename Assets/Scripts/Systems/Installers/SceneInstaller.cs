using System;
using System.Collections.Generic;
using GhostHunter.Core;
using UnityEngine;

namespace GhostHunter.Systems.Installers
{
    /// <summary>
    /// 한 씬의 컴포지션 루트. 그 씬이 제공하는 서비스를 등록하고, 씬이 내려갈 때 함께 해제한다.
    ///
    /// 파생 클래스는 <see cref="InstallBindings"/> 안에서 <see cref="Bind{T}"/> 만 호출한다.
    /// Awake/OnDestroy 를 재정의하면 반드시 base 를 부른다.
    /// </summary>
    [DefaultExecutionOrder(ExecutionOrder)]
    public abstract class SceneInstaller : MonoBehaviour
    {
        /// <summary>
        /// 같은 씬의 다른 Awake 보다 먼저 돌아야 소비자가 Awake 에서 Services.Get 을 쓸 수 있다.
        /// **파생 클래스도 이 값으로 [DefaultExecutionOrder] 를 직접 달아야 한다** —
        /// Unity 의 실행 순서 속성은 상속을 보장하지 않는다.
        /// </summary>
        public const int ExecutionOrder = -10000;

        private readonly List<Action> _unbinds = new();

        protected virtual void Awake()
        {
            InstallBindings();
        }

        protected virtual void OnDestroy()
        {
            // 등록 역순으로 푼다. 서비스끼리 의존이 생겨도 순서가 뒤집히지 않는다.
            for (int i = _unbinds.Count - 1; i >= 0; i--)
                _unbinds[i]();

            _unbinds.Clear();
        }

        /// <summary>이 씬이 제공하는 서비스를 등록한다. 여기서만 Bind 를 부른다.</summary>
        protected abstract void InstallBindings();

        protected void Bind<T>(T service) where T : class
        {
            if (service == null)
            {
                Debug.LogError($"{GetType().Name}: {typeof(T).Name} 구현체가 배선되지 않았다.", this);
                return;
            }

            Services.Bind(service);
            _unbinds.Add(() => Services.Unbind(service));
        }
    }
}
