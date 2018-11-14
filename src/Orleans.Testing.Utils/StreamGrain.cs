using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Orleans;
using Orleans.Concurrency;
using Orleans.Streams;

namespace SharedOrleansUtils
{
    [Reentrant]
    public class StreamGrain : Grain, IStreamGrain
    {
        public static Dictionary<Guid, Task<List<dynamic>>> Tasks { get; } = new Dictionary<Guid, Task<List<dynamic>>>();

        public async Task Publish<T>(string providerName, Guid streamId, string streamNamespace, T item)
        {
            var provider = GetStreamProvider(providerName);
            var stream = provider.GetStream<T>(streamId, streamNamespace);
            await stream.OnNextAsync(item);
        }

        public async Task Subscribe<T>(string providerName, Guid streamId, string streamNamespace, int threshold)
        {
            var requestId = this.GetPrimaryKey();
            var tcs = new TaskCompletionSource<List<dynamic>>();
            Tasks[requestId] = tcs.Task;

            var eventCount = 0;
            var list = new List<dynamic>();

            var provider = GetStreamProvider(providerName);
            var stream = provider.GetStream<T>(streamId, streamNamespace);
            await stream.SubscribeAsync((item, token) =>
            {
                list.Add(item);
                if (++eventCount >= threshold)
                    tcs.TrySetResult(list);

                return Task.CompletedTask;
            });
        }
    }
}
