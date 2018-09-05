using System;
using System.Collections.Generic;
using System.Text;

namespace SharedOrleansUtils
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
    public class MockStreamStorage : Attribute
    {
        public string StorageName { get; }

        public MockStreamStorage(string storageName)
        {
            StorageName = storageName;
        }
    }
}
