using System;
using System.Collections.Generic;
using System.Text;

namespace SharedOrleansUtils
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
