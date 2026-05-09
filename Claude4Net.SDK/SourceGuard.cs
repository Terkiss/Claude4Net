using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Linq;

namespace Claude4Net.SDK
{
    /// <summary>
    /// ë¯¼ê° ?•ë³´ ?„í„°ë§?ê²°ê³¼ë¥??´ëŠ” ëª¨ë¸?…ë‹ˆ??
    /// </summary>
    public class RedactionResult
    {
        /// <summary> ?ë³¸ ?ìŠ¤??</summary>
        public string OriginalText { get; set; } = string.Empty;
        /// <summary> ?„í„°ë§ëœ(ë§ˆìŠ¤?¹ëœ) ?ìŠ¤??</summary>
        public string FilteredText { get; set; } = string.Empty;
        /// <summary> ë°œê²¬??ë¯¼ê° ?•ë³´ ? í˜• ëª©ë¡ (?? API Key, Email) </summary>
        public List<string> FoundTypes { get; set; } = new();
        /// <summary> ì´?ë§¤ì¹­ ?Ÿìˆ˜ </summary>
        public int TotalMatches { get; set; }
        /// <summary> ë¯¼ê° ?•ë³´ê°€ ë°œê²¬?˜ì? ?Šì•˜?”ì? ?¬ë? </summary>
        public bool IsClean => TotalMatches == 0;
    }

    /// <summary>
    /// ë¡œê·¸??ì¶œë ¥ë¬¼ì—??API ?? ë¹„ë?ë²ˆí˜¸ ??ë¯¼ê° ?•ë³´ë¥??ì??˜ê³  ë§ˆìŠ¤?¹í•˜??ë³´ì•ˆ ? í‹¸ë¦¬í‹°?…ë‹ˆ??
    /// </summary>
    public static class SourceGuard
    {
        // ë¯¼ê° ?•ë³´ ?ì?ë¥??„í•œ ?•ê·œ???„í„° ëª©ë¡
        private static readonly List<(string Name, Regex Pattern)> _filters = new()
        {
            ("API Key", new Regex(@"\b(sk-ant-[a-zA-Z0-9_\-]{16,}|sk-[a-zA-Z0-9]{20,}|AIza[0-9A-Za-z_\-]{20,}|gh[pousr]_[A-Za-z0-9_]{20,})\b", RegexOptions.Compiled)),
            ("AWS Access Key", new Regex(@"\b(AKIA[0-9A-Z]{16})\b", RegexOptions.Compiled)),
            ("AWS Secret Key", new Regex(@"\b([a-zA-Z0-9/+=]{40})\b", RegexOptions.Compiled)),
            ("Discord Token", new Regex(@"([a-zA-Z0-9_\-]{24}\.[a-zA-Z0-9_\-]{6}\.[a-zA-Z0-9_\-]{27})", RegexOptions.Compiled)),
            ("Authorization Bearer", new Regex(@"(Bearer\s+[a-zA-Z0-9\-\._~+/]+=*)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("SSH Private Key", new Regex(@"-----BEGIN [A-Z ]+ PRIVATE KEY-----[\s\S]+?-----END [A-Z ]+ PRIVATE KEY-----", RegexOptions.Compiled)),
            ("Connection String Password", new Regex(@"(password|pwd|pwd|secret|key)\s*=\s*([^;]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("JSON Secret", new Regex(@"""([^""]*(?:password|pass|secret|token|key|auth|cred)[^""]*)""\s*:\s*""([^""]+)""", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Generic Secret", new Regex(@"(password|pass|secret|token|key)\s*[:=]\s*([^\s,;\""\'<>]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase)),
            ("Email", new Regex(@"([a-zA-Z0-9_\-\.]+)@([a-zA-Z0-9_\-\.]+)\.([a-zA-Z]{2,5})", RegexOptions.Compiled))
        };

        // ë¯¼ê°??ê²ƒìœ¼ë¡?ê°„ì£¼?˜ëŠ” ???´ë¦„???¤ì›Œ??ëª©ë¡
        private static readonly string[] _sensitiveKeyParts =
        {
            "KEY", "TOKEN", "SECRET", "PASSWORD", "PASS", "PWD", "AUTH",
            "CONNECTION", "CREDENTIAL", "DATABASE", "CERTIFICATE", "PRIVATE",
            "API", "LICENSE", "ACCESS_KEY", "SECRET_KEY", "BEARER"
        };

        /// <summary>
        /// ?…ë ¥ ?ìŠ¤?¸ì—??ë¯¼ê° ?•ë³´ë¥?ì°¾ì•„ ë§ˆìŠ¤??ì²˜ë¦¬?©ë‹ˆ??
        /// </summary>
        /// <param name="input">ê²€?¬í•  ?…ë ¥ ë¬¸ì??/param>
        /// <returns>?„í„°ë§?ê²°ê³¼ ê°ì²´</returns>
        public static RedactionResult Filter(string? input)
        {
            var result = new RedactionResult { OriginalText = input ?? "" };
            if (string.IsNullOrEmpty(input))
            {
                result.FilteredText = "";
                return result;
            }

            string filtered = input;
            // ?±ë¡??ëª¨ë“  ?„í„°ë¥??œíšŒ?˜ë©° ë§¤ì¹­ ?•ì¸
            foreach (var filter in _filters)
            {
                var matches = filter.Pattern.Matches(filtered);
                if (matches.Count > 0)
                {
                    result.TotalMatches += matches.Count;
                    if (!result.FoundTypes.Contains(filter.Name))
                        result.FoundTypes.Add(filter.Name);

                    // ë§¤ì¹­??ë¶€ë¶„ì„ ë§ˆìŠ¤??ë¬¸ìë¡?êµì²´
                    filtered = filter.Pattern.Replace(filtered, m =>
                    {
                        // 'password=value' ?ëŠ” '"password": "value"' ?•ì‹??ê²½ìš° ??ë¶€ë¶„ì? ë³´ì¡´?˜ê³  ê°’ë§Œ ë§ˆìŠ¤??
                        if (m.Groups.Count > 2 && (filter.Name.Contains("JSON") || filter.Name.Contains("Generic") || filter.Name.Contains("Connection")))
                        {
                             string keyPart = m.Value.Substring(0, m.Value.IndexOf(m.Groups[2].Value));
                             return keyPart + "****" + (filter.Name.Contains("JSON") ? "\"" : "");
                        }
                        return "****";
                    });
                }
            }

            result.FilteredText = filtered;
            return result;
        }

        /// <summary>
        /// ?¹ì • ê°’ì´????ê°??ì„ ?ë‹¨?˜ì—¬ ë§ˆìŠ¤?¹ëœ ë¬¸ì?´ì„ ë°˜í™˜?©ë‹ˆ??
        /// </summary>
        /// <param name="value">ê°?/param>
        /// <param name="keyName">ê°’ì´ ?í•œ ???´ë¦„ (? íƒ ?¬í•­)</param>
        /// <returns>ë§ˆìŠ¤?¹ëœ ê²°ê³¼ ë¬¸ì??/returns>
        public static string MaskValue(string? value, string? keyName = null)
        {
            if (string.IsNullOrEmpty(value)) return "(not set)";

            // 1. ?¨í„´ ê¸°ë°˜ ?„í„°ë§??˜í–‰
            var result = Filter(value);
            if (!result.IsClean) return result.FilteredText;

            // 2. ???´ë¦„ ê¸°ë°˜???´ë¦¬?¤í‹± ê²€??(?? ???´ë¦„??'PASS'ê°€ ?¬í•¨??ê²½ìš°)
            if (LooksSensitiveKey(keyName))
                return SecurityUtils.Mask(value);

            return value;
        }

        /// <summary>
        /// ???´ë¦„??ë³´ì•ˆ??ë¯¼ê°???•ë³´ë¥??´ê³  ?ˆì„ ê°€?¥ì„±???ˆëŠ”ì§€ ?•ì¸?©ë‹ˆ??
        /// </summary>
        /// <param name="keyName">ê²€?¬í•  ???´ë¦„</param>
        /// <returns>ë¯¼ê°??ë³´ì´ë©?true</returns>
        public static bool LooksSensitiveKey(string? keyName)
        {
            if (string.IsNullOrWhiteSpace(keyName)) return false;

            // ?¤ì›Œ??ëª©ë¡ ì¤??˜ë‚˜?¼ë„ ?¬í•¨?˜ì–´ ?ˆëŠ”ì§€ ?€?Œë¬¸??êµ¬ë¶„ ?†ì´ ?•ì¸
            string normalized = keyName.ToUpperInvariant();
            return _sensitiveKeyParts.Any(part => normalized.Contains(part));
        }
    }
}
