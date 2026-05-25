using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace Claude4Net.Tests
{
    public static class TestInitializer
    {
        [ModuleInitializer]
        public static void Initialize()
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            string mockDir = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "mockbin"));
            if (Directory.Exists(mockDir))
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                Environment.SetEnvironmentVariable("PATH", mockDir + Path.PathSeparator + path);
            }
        }
    }
}
