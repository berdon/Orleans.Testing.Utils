﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Providers;
using Orleans.Runtime;
using Orleans.Serialization;
using Orleans.Storage;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// Ripped from https://github.com/dotnet/orleans/blob/master/test/TestInfrastructure/TestExtensions/MockStorageProvider.cs
/// </summary>
namespace Orleans.Testing.Utils
{
    [DebuggerDisplay("MockStorageProvider:{Name}")]
    public class MockMemoryStorageProvider : IControllable, IGrainStorage
    {
        public enum Operation
        {
            Read,
            Write,
            Clear
        }

        public enum Commands
        {
            InitCount,
            SetValue,
            GetProvideState,
            SetErrorInjection,
            GetLastState,
            ResetHistory
        }
        [Serializable]
        public class StateForTest 
        {
            public int InitCount { get; set; }
            public int CloseCount { get; set; }
            public int ReadCount { get; set; }
            public int WriteCount { get; set; }
            public int DeleteCount { get; set; }
        }

        private static int _instanceNum;
        private readonly int _id;

        private int initCount, closeCount, readCount, writeCount, deleteCount;

        private readonly int numKeys;
        private ILocalDataStore StateStore;
        private SerializationManager serializationManager;
        private const string stateStoreKey = "State";
        private ILogger logger;
        public string LastId { get; private set; }
        public object LastState { get; private set; }
        private readonly IDictionary<Operation, IDictionary<(string GrainType, string GrainKey), AwaitableCallbackState>> _awaitables = new ConcurrentDictionary<Operation, IDictionary<(string GrainType, string GrainKey), AwaitableCallbackState>>();

        public string Name { get; private set; }

        public MockMemoryStorageProvider(ILoggerFactory loggerFactory, SerializationManager serializationManager)
            : this(Guid.NewGuid().ToString(), 2, loggerFactory, serializationManager)
        { }

        public MockMemoryStorageProvider(string name, ILoggerFactory loggerFactory, SerializationManager serializationManager)
            : this(name, 2, loggerFactory, serializationManager)
        { }

        public MockMemoryStorageProvider(string name, int numKeys, ILoggerFactory loggerFactory, SerializationManager serializationManager)
        {
            _id = ++_instanceNum;
            this.numKeys = numKeys;

            this.Name = name;
            this.logger = loggerFactory.CreateLogger(string.Format("Storage.{0}-{1}", this.GetType().Name, this._id));

            logger.Info(0, "Init Name={0}", name);
            this.serializationManager = serializationManager;
            Interlocked.Increment(ref initCount);

            StateStore = new HierarchicalKeyStore(numKeys);

            this._awaitables.Add(Operation.Read, new ConcurrentDictionary<(string GrainType, string GrainKey), AwaitableCallbackState>());
            this._awaitables.Add(Operation.Write, new ConcurrentDictionary<(string GrainType, string GrainKey), AwaitableCallbackState>());
            this._awaitables.Add(Operation.Clear, new ConcurrentDictionary<(string GrainType, string GrainKey), AwaitableCallbackState>());

            logger.Info(0, "Finished Init Name={0}", name);
        }

        public StateForTest GetProviderState()
        {
            var state = new StateForTest();
            state.InitCount = initCount;
            state.CloseCount = closeCount;
            state.DeleteCount = deleteCount;
            state.ReadCount = readCount;
            state.WriteCount = writeCount;
            return state;
        }

        [Serializable]
        public class SetValueArgs
        {
            public Type StateType { get; set; }
            public string GrainType { get; set; }
            public GrainReference GrainReference { get; set; }
            public string Name { get; set; }
            public object Val { get; set; }

        }

        public void SetValue(SetValueArgs args)
        {
            SetValue(args.StateType, args.GrainType, args.GrainReference, args.Name, args.Val);
        }

        private void SetValue(Type stateType, string grainType, GrainReference grainReference, string name, object val)
        {
            lock (StateStore)
            {
                this.logger.Info("Setting stored value {0} for {1} to {2}", name, grainReference, val);
                var keys = MakeGrainStateKeys(grainType, grainReference);
                var storedDict = StateStore.ReadRow(keys);
                if (!storedDict.ContainsKey(stateStoreKey))
                {
                    storedDict[stateStoreKey] = Activator.CreateInstance(stateType);
                } 

                var storedState = storedDict[stateStoreKey];
                var field = storedState.GetType().GetProperty(name).GetSetMethod(true);
                field.Invoke(storedState, new[] { val });
                LastId = GetId(grainReference);
                LastState = storedState;
            }
        }

        public object GetLastState()
        {
            return LastState;
        }

        public T GetLastState<T>()
        {
            return (T) LastState;
        }

        private object GetLastState(string grainType, GrainReference grainReference, IGrainState grainState)
        {
            lock (StateStore)
            {
                var keys = MakeGrainStateKeys(grainType, grainReference);
                var storedStateRow = StateStore.ReadRow(keys);
                if (!storedStateRow.ContainsKey(stateStoreKey))
                {
                    storedStateRow[stateStoreKey] = Activator.CreateInstance(grainState.State.GetType());
                }

                var storedState = storedStateRow[stateStoreKey];
                LastId = GetId(grainReference);
                LastState = storedState;
                return storedState;
            }
        }

        public virtual Task Close()
        {
            logger.Info(0, "Close");
            Interlocked.Increment(ref closeCount);
            StateStore.Clear();
            return Task.CompletedTask;
        }

        public virtual Task ReadStateAsync(string grainType, GrainReference grainReference, IGrainState grainState)
        {
            logger.Info(0, "ReadStateAsync for {0} {1}", grainType, grainReference);
            Interlocked.Increment(ref readCount);
            lock (StateStore)
            {
                var storedState = GetLastState(grainType, grainReference, grainState);
                grainState.State = this.serializationManager.DeepCopy(storedState); // Read current state data
            }

            if (_awaitables[Operation.Read].TryGetValue((grainType, grainReference.ToKeyString()), out var awaitable))
            {
                if (--awaitable.CallsUntilCallback == 0)
                    awaitable.Callback.Invoke();
            }

            return Task.CompletedTask;
        }

        public virtual Task WriteStateAsync(string grainType, GrainReference grainReference, IGrainState grainState)
        {
            logger.Info(0, "WriteStateAsync for {0} {1}", grainType, grainReference);
            Interlocked.Increment(ref writeCount);
            lock (StateStore)
            {
                var storedState = this.serializationManager.DeepCopy(grainState.State); // Store current state data
                var stateStore = new Dictionary<string, object> {{ stateStoreKey, storedState }};
                StateStore.WriteRow(MakeGrainStateKeys(grainType, grainReference), stateStore, grainState.ETag);

                LastId = GetId(grainReference);
                LastState = storedState;
            }

            if (_awaitables[Operation.Write].TryGetValue((grainType, grainReference.ToKeyString()), out var awaitable))
            {
                if (--awaitable.CallsUntilCallback == 0)
                    awaitable.Callback.Invoke();
            }

            return Task.CompletedTask;
        }

        public virtual Task ClearStateAsync(string grainType, GrainReference grainReference, IGrainState grainState)
        {
            logger.Info(0, "ClearStateAsync for {0} {1}", grainType, grainReference);
            Interlocked.Increment(ref deleteCount);
            var keys = MakeGrainStateKeys(grainType, grainReference);
            lock (StateStore)
            {
                StateStore.DeleteRow(keys, grainState.ETag);
                LastId = GetId(grainReference);
                LastState = null;
            }

            if (_awaitables[Operation.Clear].TryGetValue((grainType, grainReference.ToKeyString()), out var awaitable))
            {
                if (--awaitable.CallsUntilCallback == 0)
                   awaitable.Callback.Invoke();
            }

            return Task.CompletedTask;
        }

        private static string GetId(GrainReference grainReference)
        {
            return grainReference.ToKeyString();
        }
        private static IList<Tuple<string, string>> MakeGrainStateKeys(string grainType, GrainReference grainReference)
        {
            return new[]
            {
                Tuple.Create("GrainType", grainType),
                Tuple.Create("GrainId", GetId(grainReference))
            }.ToList();
        }

        public void ResetHistory()
        {
            // initCount = 0;
            closeCount = readCount = writeCount = deleteCount = 0;
            LastId = null;
            LastState = null;
        }

        /// <summary>
        /// A function to execute a control command.
        /// </summary>
        /// <param name="command">A serial number of the command.</param>
        /// <param name="arg">An opaque command argument</param>
        public virtual Task<object> ExecuteCommand(int command, object arg)
        {
            switch ((Commands)command)
            {
                case Commands.InitCount:
                    return Task.FromResult<object>(initCount);
                case Commands.SetValue:
                    SetValue((SetValueArgs) arg);
                    return Task.FromResult<object>(true); 
                case Commands.GetProvideState:
                    return Task.FromResult<object>(GetProviderState());
                case Commands.GetLastState:
                    return Task.FromResult(GetLastState());
                case Commands.ResetHistory:
                    ResetHistory();
                    return Task.FromResult<object>(true);
                default:
                    return Task.FromResult<object>(true); 
            }
        }

        public Task GetOperationAwaitable(string grainType, GrainReference grain, Operation operation, int calls, object args = null)
        {
            var tcs = new TaskCompletionSource<object>();
            var grainKey = (grainType, grain.ToKeyString());
            _awaitables[operation].Add(grainKey, new AwaitableCallbackState(calls, () => {
                _awaitables[operation].Remove(grainKey);
                tcs.SetResult(args);
            }));
            return tcs.Task;
        }

        public class AwaitableCallbackState
        {
            public int CallsUntilCallback { get; set; }
            public readonly Action Callback;

            public AwaitableCallbackState(int callsUntilCallback, Action callback)
            {
                CallsUntilCallback = callsUntilCallback;
                Callback = callback;
            }
        }
    }
}