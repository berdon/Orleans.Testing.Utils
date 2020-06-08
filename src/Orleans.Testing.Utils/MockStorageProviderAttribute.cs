using System;

namespace Orleans.Testing.Utils
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class MockStorageProvider : Attribute
    {
        public string ProviderName { get; }

        public MockStorageProvider(string providerName)
        {
            ProviderName = providerName;
        }
    }
}
