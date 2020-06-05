using System;

namespace Orleans.Testing.Utils
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
