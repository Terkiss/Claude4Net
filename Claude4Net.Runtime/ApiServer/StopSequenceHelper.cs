using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Claude4Net.Runtime.ApiServer
{
    /// <summary>
    /// Applies stop sequences to truncate generated text.
    /// </summary>
    public static class StopSequenceHelper
    {
        public static string Apply(string text, object? stop)
        {
            if (string.IsNullOrEmpty(text) || stop == null) return text;
            var stopList = new List<string>();
            
            if (stop is string s && !string.IsNullOrEmpty(s))
            {
                stopList.Add(s);
            }
            else if (stop is JsonElement elem)
            {
                if (elem.ValueKind == JsonValueKind.String)
                {
                    var str = elem.GetString();
                    if (!string.IsNullOrEmpty(str)) stopList.Add(str);
                }
                else if (elem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in elem.EnumerateArray())
                    {
                        var str = item.GetString();
                        if (!string.IsNullOrEmpty(str)) stopList.Add(str);
                    }
                }
            }
            else if (stop is IEnumerable<string> strSeq)
            {
                stopList.AddRange(strSeq);
            }

            if (stopList.Count == 0) return text;

            int earliestIndex = -1;
            foreach (var stopSeq in stopList)
            {
                int idx = text.IndexOf(stopSeq, StringComparison.Ordinal);
                if (idx >= 0 && (earliestIndex == -1 || idx < earliestIndex))
                {
                    earliestIndex = idx;
                }
            }

            return earliestIndex >= 0 ? text.Substring(0, earliestIndex) : text;
        }
    }
}
