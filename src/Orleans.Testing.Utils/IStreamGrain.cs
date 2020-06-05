using System;
using System.Threading.Tasks;
using Orleans;

namespace Orleans.Testing.Utils
{
    public interface IStreamGrain : IGrainWithGuidKey
    {
        Task Subscribe<T>(string providerName, Guid streamId, string streamNamespace, int threshold);
        Task Publish<T>(string providerName, Guid streamId, string streamNamespace, T item);
    }
}
