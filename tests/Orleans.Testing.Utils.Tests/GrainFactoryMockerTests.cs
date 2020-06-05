using System;
using Moq;
using Xunit;

namespace Orleans.Testing.Utils.Tests
{
    public interface IGuidTestGrain : IGrainWithGuidKey { }

    public class GrainFactoryMockerTests
    {
        [Fact]
        public void ShouldNot_HaveMockedGrain_ByDefault()
        {
            var unit = new GrainFactoryMocker(Mock.Of<IGrainFactory>());
            var reference = unit.Mock.Object.GetGrain<IGuidTestGrain>(Guid.NewGuid());
            Assert.Null(reference);
        }

        [Fact]
        public void Should_HaveMockedGrain_IfSetup()
        {
            var unit = new GrainFactoryMocker(Mock.Of<IGrainFactory>());
            unit.GuidGrain<IGuidTestGrain>();

            var reference = unit.Mock.Object.GetGrain<IGuidTestGrain>(Guid.NewGuid());
            Assert.NotNull(reference);
        }
    }
}
