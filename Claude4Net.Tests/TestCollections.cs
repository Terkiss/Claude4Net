using Xunit;

namespace Claude4Net.Tests
{
    [CollectionDefinition("AppState")]
    public class AppStateCollection : ICollectionFixture<object>
    {
        // This class has no code, and is never created. Its purpose is simply
        // to be the place to apply [CollectionDefinition] and all the
        // ICollectionFixture<> interfaces.
    }
}
