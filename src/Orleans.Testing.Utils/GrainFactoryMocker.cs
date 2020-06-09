using Moq;
using Orleans;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;

namespace Orleans.Testing.Utils
{
    public class GrainFactoryMocker
    {
        private readonly IGrainFactory _grainFactory;
        private readonly Dictionary<Type, Dictionary<NullObject<Guid?>, Mock>> _mockGrainWithGuidKeyRegistry = new Dictionary<Type, Dictionary<NullObject<Guid?>, Mock>>();
        private readonly Dictionary<Type, Dictionary<(Guid?, string), Mock>> _mockGrainWithGuidCompoundKeyRegistry = new Dictionary<Type, Dictionary<(Guid?, string), Mock>>();
        private readonly Dictionary<Type, Dictionary<(long?, string), Mock>> _mockGrainWithLongCompoundKeyRegistry = new Dictionary<Type, Dictionary<(long?, string), Mock>>();

        internal Mock<IGrainFactory> Mock { get; private set; }

        internal GrainFactoryMocker(IGrainFactory grainFactory)
        {
            Mock = new Mock<IGrainFactory>();
            _grainFactory = grainFactory;
        }

        public Task GetAwaiterForGuidGrainMethod<TGrain, TResult>(Guid? primaryKey, Expression<Func<TGrain, TResult>> expression, TResult result)
            where TGrain : class, IGrainWithGuidKey
        {
            var mockGrain = GetGuidGrain<TGrain>(primaryKey);
            var tcs = new TaskCompletionSource<object>();
            mockGrain
                .Setup(expression)
                .Callback(() => tcs.SetResult(null))
                .Returns(result);
            return tcs.Task;
        }

        public void VerifyGuidGrainActivated<TGrain>(Guid primaryKey)
            where TGrain : class, IGrainWithGuidKey
        {
            Mock.Verify(x => x.GetGrain<TGrain>(It.Is<Guid>(pkey => pkey == primaryKey), It.IsAny<string>()));
        }

        public void VerifyGuidGrainActivated<TGrain>(Guid primaryKey, Times times)
            where TGrain : class, IGrainWithGuidKey
        {
            Mock.Verify(x => x.GetGrain<TGrain>(It.Is<Guid>(pkey => pkey == primaryKey), It.IsAny<string>()), times);
        }

        public void VerifyGuidGrainActivated<TGrain>(IEnumerable<Guid> primaryKeys)
            where TGrain : class, IGrainWithGuidKey
        {
            foreach (Guid guid in primaryKeys)
            {
                Mock.Verify(x => x.GetGrain<TGrain>(It.Is<Guid>(pkey => pkey == guid), It.IsAny<string>()));
            }
        }

        public void VerifyGuidGrainActivated<TGrain>(IEnumerable<Guid> primaryKeys, Times times)
            where TGrain : class, IGrainWithGuidKey
        {
            foreach (Guid guid in primaryKeys)
            {
                Mock.Verify(x => x.GetGrain<TGrain>(It.Is<Guid>(pkey => pkey == guid), It.IsAny<string>()), times);
            }
        }

        public void VerifyGuidGrainCalled<TGrain>(Guid primaryKey, Expression<Action<TGrain>> expression, Times times)
            where TGrain : class, IGrainWithGuidKey
        {
            GetGuidGrain<TGrain>(primaryKey).Verify(expression, times);
        }

        public void VerifyGuidCompoundGrainActivated<TGrain>(Guid primaryKey, string compoundKey)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            Mock.Verify(x => x.GetGrain<TGrain>(
                It.Is<Guid>(guid => guid == primaryKey),
                It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                It.IsAny<string>()));
        }

        public void VerifyLongCompoundGrainActivated<TGrain>(long primaryKey, string compoundKey)
            where TGrain : class, IGrainWithIntegerCompoundKey
        {
            Mock.Verify(x => x.GetGrain<TGrain>(
                It.Is<long>(id => id == primaryKey),
                It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                It.IsAny<string>()));
        }

        public void VerifyGuidCompoundGrainActivated<TGrain>(Guid primaryKey, string compoundKey, Times times)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            Mock.Verify(x => x.GetGrain<TGrain>(
                It.Is<Guid>(guid => guid == primaryKey),
                It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                It.IsAny<string>()), times);
        }

        public void VerifyGuidCompoundGrainCalled<TGrain>(Guid primaryKey, string compoundKey, Expression<Action<TGrain>> expression, Times times)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            GetGuidCompoundGrain<TGrain>(primaryKey, compoundKey).Verify(expression, times);
        }

        public void VerifyGuidCompoundGrainActivated<TGrain>(IEnumerable<(Guid PrimaryKey, string CompoundKey)> keys)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            foreach (var (primaryKey, compoundKey) in keys)
            {
                Mock.Verify(x => x.GetGrain<TGrain>(
                    It.Is<Guid>(guid => guid == primaryKey),
                    It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                    It.IsAny<string>()));
            }
        }

        public void VerifyGuidCompoundGrainActivated<TGrain>(IEnumerable<(Guid PrimaryKey, string CompoundKey)> keys, Times times)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            foreach (var (primaryKey, compoundKey) in keys)
            {
                Mock.Verify(x => x.GetGrain<TGrain>(
                    It.Is<Guid>(guid => guid == primaryKey),
                    It.Is<string>(@string => @string.Equals(compoundKey, StringComparison.InvariantCulture)),
                    It.IsAny<string>()), times);
            }
        }

        public GrainFactoryMocker Clear()
        {
            foreach (var g in _mockGrainWithGuidKeyRegistry.SelectMany(d => d.Value.Values))
            {
                g.Reset();
            }
            foreach (var g in _mockGrainWithGuidCompoundKeyRegistry.SelectMany(d => d.Value.Values))
            {
                g.Reset();
            }

            _mockGrainWithGuidKeyRegistry.Clear();
            _mockGrainWithGuidCompoundKeyRegistry.Clear();

            return this;
        }

        public GrainFactoryMocker PassThruGuidGrain<TGrain>(Guid? primaryKey = null)
            where TGrain : class, IGrainWithGuidKey
        {
            Mock
                .Setup(x => x.GetGrain<TGrain>(
                    It.Is<Guid>(id => primaryKey.HasValue ? id == primaryKey : true),
                    It.IsAny<string>()))
                .Returns((Guid g, string c) => _grainFactory.GetGrain<TGrain>(g, c));
            return this;
        }

        public GrainFactoryMocker PassThruGuidCompoundGrain<TGrain>(Guid? guid = null, string id = null)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            Mock
                .Setup(x => x.GetGrain<TGrain>(
                    It.Is<Guid>(gid => guid.HasValue ? gid == guid : true),
                    It.Is<string>(sid => !string.IsNullOrEmpty(id) ? id.Equals(sid, StringComparison.InvariantCulture) : true),
                    It.IsAny<string>()))
                .Returns((Guid g, string s, string c) => _grainFactory.GetGrain<TGrain>(g, s, c));
            return this;
        }

        public Mock<TGrain> GetGuidGrain<TGrain>(Guid? guid = null)
            where TGrain : class, IGrainWithGuidKey
        {
            return _mockGrainWithGuidKeyRegistry[typeof(TGrain)]?[guid] as Mock<TGrain>;
        }

        public Mock<TGrain> GetGuidCompoundGrain<TGrain>(Guid? guid = null, string secondaryKey = null)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            return _mockGrainWithGuidCompoundKeyRegistry[typeof(TGrain)]?[(guid, secondaryKey)] as Mock<TGrain>;
        }

        public Task GetAwaiterForGrainCall<TGrain>(Expression<Func<TGrain, Task>> func, TimeSpan timeout, Guid? primaryKey = null)
            where TGrain : class, IGrainWithGuidKey
        {
            var semaphoreSlim = new SemaphoreSlim(0);

            if (_mockGrainWithGuidKeyRegistry.ContainsKey(typeof(TGrain)) && _mockGrainWithGuidKeyRegistry[typeof(TGrain)].ContainsKey(primaryKey) == true)
            {
                GetGuidGrain<TGrain>().Setup(func).Callback(() => semaphoreSlim.Release());
            }
            else
            {
                GuidGrain<TGrain>(primaryKey, grain => grain.Setup(func).Callback(() => semaphoreSlim.Release()).Returns(Task.CompletedTask));
            }

            return semaphoreSlim.WaitAsync(timeout);
        }

        public GrainFactoryMocker GuidGrain<TGrain>(Action<Mock<TGrain>> mocker = null, Action<Guid> callback = null)
            where TGrain : class, IGrainWithGuidKey
        {
            return GuidGrain(null, mocker, callback);
        }

        public GrainFactoryMocker ClearGuidGrain<TGrain>(Guid? guid = null)
            where TGrain : class, IGrainWithGuidKey
        {
            if (!_mockGrainWithGuidKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
            {
                return this;
            }

            registry.Remove(guid);

            return this;
        }

        public GrainFactoryMocker GuidGrain<TGrain>(Guid? guid, Action<Mock<TGrain>> mocker = null, Action<Guid> callback = null)
            where TGrain : class, IGrainWithGuidKey
        {
            var mockGrain = new Mock<TGrain>
            {
                DefaultValue = DefaultValue.Mock
            };

            if (!_mockGrainWithGuidKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
            {
                registry = new Dictionary<NullObject<Guid?>, Mock>();
                _mockGrainWithGuidKeyRegistry.Add(typeof(TGrain), registry);
            }

            // We've already mocked this grain
            if (registry.ContainsKey(guid)) throw new Exception("GuidGrain already mocked - you must call ClearGuidGrain() first");

            registry.Add(guid, mockGrain);
            mocker?.Invoke(mockGrain);

            Mock
                .Setup(x => x.GetGrain<TGrain>(It.Is<Guid>(g => guid.HasValue ? g == guid : true), It.IsAny<string>()))
                .Callback((Guid g, string c) => callback?.Invoke(g))
                .Returns(() =>
                {
                    return mockGrain.Object;
                });

            return this;
        }

        public GrainFactoryMocker GuidCompoundGrain<TGrain>(Action<Mock<TGrain>> mocker = null, Action<Guid, string> callback = null)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            return GuidCompoundGrain(null, null, mocker, callback);
        }

        public GrainFactoryMocker GuidCompoundGrain<TGrain>(Guid primaryKey, Action<Mock<TGrain>> mocker = null, Action<Guid, string> callback = null)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            return GuidCompoundGrain(primaryKey, null, mocker, callback);
        }

        public GrainFactoryMocker GuidCompoundGrain<TGrain>(string compoundKey, Action<Mock<TGrain>> mocker = null, Action<Guid, string> callback = null)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            return GuidCompoundGrain(null, compoundKey, mocker, callback);
        }

        public GrainFactoryMocker ClearGuidCompoundGrain<TGrain>(Guid? primaryKey = null, string compoundKey = null)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            if (!_mockGrainWithGuidCompoundKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
            {
                return this;
            }

            var key = (primaryKey, compoundKey);
            registry.Remove(key);

            return this;
        }

        public GrainFactoryMocker GuidCompoundGrain<TGrain>(Guid? primaryKey = null, string compoundKey = null, Action<Mock<TGrain>> mocker = null, Action<Guid, string> callback = null)
            where TGrain : class, IGrainWithGuidCompoundKey
        {
            var mockGrain = new Mock<TGrain>
            {
                DefaultValue = DefaultValue.Mock
            };

            if (!_mockGrainWithGuidCompoundKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
            {
                registry = new Dictionary<(Guid?, string), Mock>();
                _mockGrainWithGuidCompoundKeyRegistry.Add(typeof(TGrain), registry);
            }

            var key = (primaryKey, compoundKey);

            // We've already mocked this grain
            if (registry.ContainsKey(key)) throw new Exception("GuidCompoundGrain already mocked - you must call ClearGuidCompoundGrain() first");

            registry.Add(key, mockGrain);
            mocker?.Invoke(mockGrain);

            Mock
                .Setup(x => x.GetGrain<TGrain>(
                    It.Is<Guid>(g => primaryKey.HasValue ? g == primaryKey : true),
                    It.Is<string>(s => !string.IsNullOrEmpty(compoundKey) ? compoundKey.Equals(s, StringComparison.InvariantCulture) : true),
                    It.IsAny<string>()))
                .Callback((Guid g, string k, string c) => callback?.Invoke(g, k))
                .Returns(() =>
                {
                    return mockGrain.Object;
                });

            return this;
        }

        public GrainFactoryMocker LongCompoundGrain<TGrain>(long? primaryKey = null, string compoundKey = null, Action<Mock<TGrain>> mocker = null, Action<long, string> callback = null)
            where TGrain : class, IGrainWithIntegerCompoundKey
        {
            var mockGrain = new Mock<TGrain>();
            mockGrain.DefaultValue = DefaultValue.Mock;

            if (!_mockGrainWithLongCompoundKeyRegistry.TryGetValue(typeof(TGrain), out var registry))
            {
                registry = new Dictionary<(long?, string), Mock>();
                _mockGrainWithLongCompoundKeyRegistry.Add(typeof(TGrain), registry);
            }

            var key = (primaryKey, compoundKey);

            // We've already mocked this grain
            if (registry.ContainsKey(key)) throw new Exception("LongCompoundGrain already mocked - you must call ClearLongCompoundGrain() first");

            registry.Add(key, mockGrain);
            mocker?.Invoke(mockGrain);

            Mock
                .Setup(x => x.GetGrain<TGrain>(
                    It.Is<long>(g => primaryKey.HasValue ? g == primaryKey : true),
                    It.Is<string>(s => !string.IsNullOrEmpty(compoundKey) ? compoundKey.Equals(s, StringComparison.InvariantCulture) : true),
                    It.IsAny<string>()))
                .Callback((long g, string k, string c) => callback?.Invoke(g, k))
                .Returns(() =>
                {
                    return mockGrain.Object;
                });

            return this;
        }
    }
}
