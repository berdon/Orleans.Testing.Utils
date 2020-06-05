
using System;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Orleans.Testing.Utils.Tests
{
    [Collection(nameof(BaseClusterFixtureTests))]
    [Trait("Category", "BVT")]
    [Trait("Target", "Grains")]
    [Trait("Target", "Orleans Test Framework")]
    public class BaseClusterFixtureTests
    {
        private readonly ITestOutputHelper testOutputHelper;

        public BaseClusterFixtureTests(ITestOutputHelper outputHelper)
        {
            testOutputHelper = outputHelper;
        }

        [Fact]
        public async Task VerifyMockedGuidGrainThrowsNotActivatedWhenMethodNotCalled()
        {
            var clusterFixture = new ClusterFixture { OutputHelper = testOutputHelper };
            await clusterFixture.Start();
            using (clusterFixture)

            await clusterFixture.Dispatch(() =>
            {
                var primaryKey = Guid.NewGuid();
                clusterFixture.Mock.GuidGrain<ITestGuidGrain>(primaryKey);
                Assert.ThrowsAny<Exception>(() => clusterFixture.Mock.VerifyGuidGrainActivated<ITestGuidGrain>(primaryKey));
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task VerifyMockedGuidGrainDoesNotThrowOnVerifyActivatedWhenMethodCalled()
        {
            var clusterFixture = new ClusterFixture { OutputHelper = testOutputHelper };
            await clusterFixture.Start();
            using (clusterFixture)

            await clusterFixture.Dispatch(async () =>
            {
                var primaryKey = Guid.NewGuid();
                clusterFixture.Mock.GuidGrain<ITestGuidGrain>(primaryKey);
                var mockGrain = clusterFixture.Mock.Mock.Object.GetGrain<ITestGuidGrain>(primaryKey);
                await mockGrain.Foo();
                clusterFixture.Mock.VerifyGuidGrainActivated<ITestGuidGrain>(primaryKey);
            });
        }

        [Fact]
        public async Task VerifyMockedGuidCompoundGrainThrowsNotActivatedWhenMethodNotCalled()
        {
            var clusterFixture = new ClusterFixture { OutputHelper = testOutputHelper };
            await clusterFixture.Start();
            using (clusterFixture)

            await clusterFixture.Dispatch(() =>
            {
                var primaryKey = Guid.NewGuid();
                var secondaryKey = "asdfadf";
                clusterFixture.Mock.GuidCompoundGrain<ITestGuidCompoundGrain>(primaryKey, secondaryKey);
                Assert.ThrowsAny<Exception>(() => clusterFixture.Mock.VerifyGuidCompoundGrainActivated<ITestGuidCompoundGrain>(primaryKey, secondaryKey));
                return Task.CompletedTask;
            });
        }

        [Fact]
        public async Task VerifyMockedGuidCompoundGrainDoesNotThrowOnVerifyActivatedWhenMethodCalled()
        {
            var clusterFixture = new ClusterFixture { OutputHelper = testOutputHelper };
            await clusterFixture.Start();
            using (clusterFixture)

            await clusterFixture.Dispatch(async () =>
            {
                var primaryKey = Guid.NewGuid();
                var secondaryKey = "asdfadf";
                clusterFixture.Mock.GuidCompoundGrain<ITestGuidCompoundGrain>(primaryKey, secondaryKey);
                var mockGrain = clusterFixture.Mock.Mock.Object.GetGrain<ITestGuidCompoundGrain>(primaryKey, secondaryKey, grainClassNamePrefix: null);
                await mockGrain.Foo();
                clusterFixture.Mock.VerifyGuidCompoundGrainActivated<ITestGuidCompoundGrain>(primaryKey, secondaryKey);
            });
        }

        public class ClusterFixture : Orleans.Testing.Utils.ClusterFixture { }

        public interface ITestGuidGrain : IGrainWithGuidKey
        {
            Task Foo();
        }

        public interface ITestGuidCompoundGrain : IGrainWithGuidCompoundKey
        {
            Task Foo();
        }
    }
}