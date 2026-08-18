using System;
using NUnit.Framework;

namespace GameBattle.Tests.EditMode.Buff
{
    [TestFixture]
    internal class CustomBuffHandlerRegistryTests
    {
        [Test]
        public void RegisterAndLookup_ReturnsExactHandler()
        {
            var registry = new CustomBuffHandlerRegistry();
            var handler = new RecordingHandler();

            registry.Register(7, handler);

            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet(7, out ICustomBuffHandler found));
            Assert.AreSame(handler, found);
        }

        [Test]
        public void DuplicateRegistration_IsRejectedWithoutReplacingFirstHandler()
        {
            var registry = new CustomBuffHandlerRegistry();
            var first = new RecordingHandler();
            registry.Register(7, first);

            Assert.Throws<InvalidOperationException>(() => registry.Register(7, new RecordingHandler()));
            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet(7, out ICustomBuffHandler found));
            Assert.AreSame(first, found);
        }

        [Test]
        public void NullHandlerAndNegativeType_AreRejected()
        {
            var registry = new CustomBuffHandlerRegistry();

            Assert.Throws<ArgumentNullException>(() => registry.Register(7, null));
            Assert.Throws<ArgumentOutOfRangeException>(() => registry.Register(-1, new RecordingHandler()));
            Assert.AreEqual(0, registry.Count);
        }

        [Test]
        public void MissingLookup_ReturnsFalseWithoutFallback()
        {
            var registry = new CustomBuffHandlerRegistry();

            Assert.IsFalse(registry.TryGet(99, out ICustomBuffHandler missing));
            Assert.IsNull(missing);
        }

        [Test]
        public void RecordingHandler_ReceivesSymmetricSynchronousLifecycle()
        {
            var registry = new CustomBuffHandlerRegistry();
            var handler = new RecordingHandler();
            var target = new FakeTarget();
            var instance = new BuffInstanceSnapshot(1, Request());
            registry.Register(7, handler);
            Assert.IsTrue(registry.TryGet(7, out ICustomBuffHandler found));

            found.ApplyOrRefresh(instance, target);
            found.Remove(instance, target);
            found.Clear(instance, target);

            Assert.AreEqual(1, handler.ApplyCount);
            Assert.AreEqual(1, handler.RemoveCount);
            Assert.AreEqual(1, handler.ClearCount);
            Assert.AreSame(instance, handler.LastInstance);
            Assert.AreSame(target, handler.LastTarget);
        }

        private static BuffApplyRequest Request()
        {
            return new BuffApplyRequest(
                7,
                new BuffTargetHandle(BuffEntityKind.Unit, 1, 1),
                new BuffSourceHandle(12),
                0,
                BuffValueMode.Flat,
                BuffTimeMode.Permanent,
                0,
                new byte[] { 8 });
        }

        private sealed class RecordingHandler : ICustomBuffHandler
        {
            internal int ApplyCount { get; private set; }
            internal int RemoveCount { get; private set; }
            internal int ClearCount { get; private set; }
            internal BuffInstanceSnapshot LastInstance { get; private set; }
            internal IBuffTarget LastTarget { get; private set; }

            public void ApplyOrRefresh(
                BuffInstanceSnapshot instance,
                IBuffTarget target)
            {
                ApplyCount++;
                LastInstance = instance;
                LastTarget = target;
            }

            public void Remove(
                BuffInstanceSnapshot instance,
                IBuffTarget target)
            {
                RemoveCount++;
                LastInstance = instance;
                LastTarget = target;
            }

            public void Clear(
                BuffInstanceSnapshot instance,
                IBuffTarget target)
            {
                ClearCount++;
                LastInstance = instance;
                LastTarget = target;
            }
        }

        private sealed class FakeTarget : IBuffTarget
        {
            internal FakeTarget()
            {
                Handle = new BuffTargetHandle(BuffEntityKind.Unit, 1, 1);
                Capabilities = new BuffTargetCapabilities(
                    Array.Empty<BuffNumericChannel>(),
                    Array.Empty<BuffStateChannel>());
            }

            public BuffTargetHandle Handle { get; }
            public bool IsAvailable => true;
            public BuffTargetCapabilities Capabilities { get; }

            public bool TryGetNumericBase(BuffNumericChannel channel, out double value)
            {
                value = 0;
                return false;
            }

            public void CommitNumericAggregate(
                BuffNumericChannel channel,
                double effectiveValue,
                BuffSourceHandle source)
            {
            }

            public void CommitStateAggregate(
                BuffStateChannel channel,
                bool active,
                BuffInstanceSnapshot payloadSource)
            {
            }

            public void ClearBuffAggregates()
            {
            }
        }
    }
}
