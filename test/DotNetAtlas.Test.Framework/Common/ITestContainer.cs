namespace DotNetAtlas.Test.Framework.Common;

public interface ITestContainer : IAsyncDisposable
{
    /// <summary>
    /// The Docker image name (including tag) used by this test container.
    /// </summary>
    string ImageName { get; }
}
