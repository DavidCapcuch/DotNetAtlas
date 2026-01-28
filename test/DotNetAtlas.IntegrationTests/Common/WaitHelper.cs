namespace DotNetAtlas.IntegrationTests.Common;

public static class WaitHelper
{
    public static async Task WaitForAsync(Func<Task<bool>> condition, TimeSpan timeout, string? timeoutMessage = null)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < timeout)
        {
            try
            {
                if (await condition())
                {
                    return;
                }
            }
            catch
            {
                // Ignore exceptions during polling
            }

            await Task.Delay(100);
        }

        throw new TimeoutException(timeoutMessage ?? "Condition not met within timeout.");
    }
}
