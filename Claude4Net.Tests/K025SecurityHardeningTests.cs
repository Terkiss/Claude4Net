using System;
using System.IO;
using Xunit;
using Claude4Net.Runtime;
using Claude4Net.SDK;

namespace Claude4Net.Tests
{
    [Collection("AppState")]
    public class K025SecurityHardeningTests : IDisposable
    {
        private readonly string _tempRoot;
        private readonly string _workspace;
        private readonly string _outside;
        private readonly string _originalSystemBaseDir;
        private readonly string _originalCwd;

        public K025SecurityHardeningTests()
        {
            _originalSystemBaseDir = AppState.SystemBaseDir;
            _originalCwd = AppState.CurrentCwd;

            _tempRoot = Path.Combine(Path.GetTempPath(), "Claude4Net_K025_" + Guid.NewGuid().ToString("N"));
            _workspace = Path.Combine(_tempRoot, "workspace");
            _outside = Path.Combine(_tempRoot, "outside");

            Directory.CreateDirectory(_workspace);
            Directory.CreateDirectory(_outside);

            AppState.CurrentCwd = _workspace;
            AppState.SystemBaseDir = Path.Combine(_tempRoot, "system");
            Directory.CreateDirectory(AppState.SystemBaseDir);
            AppState.CurrentPermissionMode = PermissionMode.Prompt;
        }

        public void Dispose()
        {
            AppState.SystemBaseDir = _originalSystemBaseDir;
            AppState.CurrentCwd = _originalCwd;
            try { Directory.Delete(_tempRoot, true); } catch { }
        }

        [Fact]
        public void Symlink_DirectEscape_Blocked()
        {
            // Windows?ì„œ ?¬ë³¼ë¦?ë§í¬ ?ì„±?€ ê´€ë¦¬ì ê¶Œí•œ???„ìš”?????ˆìœ¼ë¯€ë¡??¤íŒ¨ ??Skip ì²˜ë¦¬ ? ë„
            // ?˜ì?ë§??¬ê¸°?œëŠ” ?¼ë¦¬??ê²€ì¦ì„ ?„í•´ ResolveFinalPathê°€ ?•ìƒ ?‘ë™?œë‹¤ê³?ê°€?•í•˜ê±°ë‚˜
            // ?¤ì œ ë§í¬ ?ì„±??ê°€?¥í•œ ê²½ìš°?ë§Œ ?ŒìŠ¤???˜í–‰

            string targetFile = Path.Combine(_outside, "secret.txt");
            File.WriteAllText(targetFile, "secret data");

            string linkPath = Path.Combine(_workspace, "leak.txt");

            if (TryCreateSymbolicLink(linkPath, targetFile))
            {
                var evaluator = new PathSafetyEvaluator();
                var result = evaluator.EvaluateSinglePathSafety("leak.txt");
                Assert.Equal(PathSafetyResult.Outside, result);
            }
        }

        [Fact]
        public void Symlink_ChainEscape_Blocked()
        {
            string targetFile = Path.Combine(_outside, "secret.txt");
            File.WriteAllText(targetFile, "secret data");

            string link1 = Path.Combine(_outside, "link1.txt");
            string link2 = Path.Combine(_workspace, "leak_chain.txt");

            if (TryCreateSymbolicLink(link1, targetFile) && TryCreateSymbolicLink(link2, link1))
            {
                var evaluator = new PathSafetyEvaluator();
                var result = evaluator.EvaluateSinglePathSafety("leak_chain.txt");
                Assert.Equal(PathSafetyResult.Outside, result);
            }
        }

        [Fact]
        public void Symlink_Circular_ThrowsOrBlocks()
        {
            string link1 = Path.Combine(_workspace, "circ1.txt");
            string link2 = Path.Combine(_workspace, "circ2.txt");

            // ?œí™˜ ë§í¬ ?ì„± ?œë„ (circ1 -> circ2 -> circ1)
            // OS ?ˆë²¨?ì„œ ?ì„±??ê±°ë??????ˆìœ¼ë¯€ë¡?ì£¼ì˜
            if (TryCreateSymbolicLink(link1, link2) && TryCreateSymbolicLink(link2, link1))
            {
                var evaluator = new PathSafetyEvaluator();
                // ?œí™˜ ë§í¬ ?ì? ??Outsideë¥?ë°˜í™˜?˜ê±°???ˆì™¸ë¥??ˆì „?˜ê²Œ ì²˜ë¦¬?´ì•¼ ??
                var result = evaluator.EvaluateSinglePathSafety("circ1.txt");
                Assert.Equal(PathSafetyResult.Outside, result);
            }
        }

        [Fact]
        public void Symlink_LegitimateInternal_Allowed()
        {
            string internalFile = Path.Combine(_workspace, "data.txt");
            File.WriteAllText(internalFile, "normal data");

            string linkPath = Path.Combine(_workspace, "link_to_data.txt");

            if (TryCreateSymbolicLink(linkPath, internalFile))
            {
                var evaluator = new PathSafetyEvaluator();
                var result = evaluator.EvaluateSinglePathSafety("link_to_data.txt");
                Assert.Equal(PathSafetyResult.Workspace, result);
            }
        }

        [Fact]
        public void SourceGuard_EnvVar_Masking()
        {
            string secret = "sk-ant-1234567890abcdefghij";
            string envLine = $"ANTHROPIC_API_KEY={secret}";

            var result = SourceGuard.Filter(envLine);
            Assert.Contains("ANTHROPIC_API_KEY=****", result.FilteredText);
            Assert.DoesNotContain(secret, result.FilteredText);
        }

        [Fact]
        public void SourceGuard_CommandLine_Masking()
        {
            string secret = "AIzaSyD-1234567890abcdefghij";
            string cmdLine = $"python script.py --key={secret} --other=val";

            var result = SourceGuard.Filter(cmdLine);
            Assert.Contains("--key=****", result.FilteredText);
            Assert.DoesNotContain(secret, result.FilteredText);
        }

        [Fact]
        public void SourceGuard_Json_Masking()
        {
            string json = "{\"api_key\": \"AIzaSyD-1234567890abcdefghij\", \"user\": \"admin\"}";
            var result = SourceGuard.Filter(json);
            Assert.Contains("\"api_key\": \"****\"", result.FilteredText);
            Assert.DoesNotContain("AIzaSyD", result.FilteredText);
        }

        private bool TryCreateSymbolicLink(string path, string target)
        {
            try
            {
                File.CreateSymbolicLink(path, target);
                return true;
            }
            catch
            {
                // ê¶Œí•œ ë¶€ì¡??±ìœ¼ë¡??¤íŒ¨?????ˆìŒ
                return false;
            }
        }
    }
}
