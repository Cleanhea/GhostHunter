using System;
using GhostHunter.Core;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GhostHunter.Tests.EditMode
{
    /// <summary>
    /// 서비스 로케이터의 계약을 고정한다. 여기서 지키려는 것은 "조용히 덮어쓰지 않는다"이다 —
    /// 두 인스톨러가 같은 서비스를 등록하면 어느 쪽이 살아 있는지 알 수 없어진다.
    /// </summary>
    public sealed class ServicesTests
    {
        private interface ITestService
        {
        }

        private sealed class TestService : ITestService
        {
        }

        private ITestService _bound;

        [TearDown]
        public void TearDown()
        {
            if (_bound != null)
            {
                Services.Unbind(_bound);
                _bound = null;
            }
        }

        private void Bind(ITestService service)
        {
            Services.Bind(service);
            _bound = service;
        }

        [Test]
        public void Get_등록한_인스턴스를_그대로_돌려준다()
        {
            var service = new TestService();
            Bind(service);

            Assert.AreSame(service, Services.Get<ITestService>());
        }

        [Test]
        public void Get_등록되지_않았으면_예외를_던진다()
        {
            Assert.Throws<InvalidOperationException>(() => Services.Get<ITestService>());
        }

        [Test]
        public void TryGet_등록되지_않았으면_false_와_null()
        {
            Assert.IsFalse(Services.TryGet(out ITestService service));
            Assert.IsNull(service);
        }

        [Test]
        public void Bind_다른_인스턴스로_덮어쓰지_않는다()
        {
            var first = new TestService();
            var second = new TestService();
            Bind(first);

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Services.Bind"));
            Services.Bind<ITestService>(second);

            Assert.AreSame(first, Services.Get<ITestService>());
        }

        [Test]
        public void Bind_null_은_등록되지_않는다()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("Services.Bind"));
            Services.Bind<ITestService>(null);

            Assert.IsFalse(Services.TryGet(out ITestService _));
        }

        /// <summary>
        /// 씬이 내려갈 때 인스톨러가 Unbind 한다. 다른 인스턴스로 부르면 지워지면 안 된다 —
        /// 늦게 내려간 씬이 새 씬의 서비스를 지우는 사고를 막는 규칙이다.
        /// </summary>
        [Test]
        public void Unbind_다른_인스턴스로는_해제되지_않는다()
        {
            var registered = new TestService();
            Bind(registered);

            Services.Unbind<ITestService>(new TestService());

            Assert.AreSame(registered, Services.Get<ITestService>());
        }

        [Test]
        public void Unbind_같은_인스턴스면_해제된다()
        {
            var service = new TestService();
            Bind(service);

            Services.Unbind<ITestService>(service);
            _bound = null;

            Assert.IsFalse(Services.TryGet(out ITestService _));
        }
    }
}
