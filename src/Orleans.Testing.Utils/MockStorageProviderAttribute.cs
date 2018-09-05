using System;
using System.Collections.Generic;
using System.Text;

namespace SharedOrleansUtils
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
