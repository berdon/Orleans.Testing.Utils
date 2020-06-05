using System;

namespace Orleans.Testing.Utils
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class MockStreamProvider : Attribute
    {
        public string ProviderName { get; }

        public MockStreamProvider(string providerName)
        {
            ProviderName = providerName;
        }
    }
}
