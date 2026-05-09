using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Claude4Net.SDK.Events;

namespace Claude4Net.SDK
{
    /// <summary>
    /// ?ì´?„íŠ¸ ?´ë²¤?¸ë? ?€?¥í•˜ê³?ë¡œë“œ?˜ê¸° ?„í•œ ?¸í„°?˜ì´?¤ì…?ˆë‹¤.
    /// </summary>
    public interface IAgentEventStore
    {
        /// <summary> ?ˆë¡œ???´ë²¤?¸ë? ?€?¥ì†Œ??ì¶”ê??©ë‹ˆ?? </summary>
        Task AppendEventAsync(string sessionId, IAgentEvent @event);

        /// <summary> ?¹ì • ë²„ì „ ?´í›„???´ë²¤??ëª©ë¡??ê°€?¸ì˜µ?ˆë‹¤. </summary>
        Task<IEnumerable<IAgentEvent>> GetEventsAsync(string sessionId, long afterVersion = 0);

        /// <summary> ?ì´?„íŠ¸ ?íƒœ ?¤ëƒ…?·ì„ ?€?¥í•©?ˆë‹¤. </summary>
        Task SaveSnapshotAsync(string sessionId, AgentStateSnapshot snapshot);

        /// <summary> ê°€??ìµœì‹  ?¤ëƒ…?·ì„ ê°€?¸ì˜µ?ˆë‹¤. </summary>
        Task<AgentStateSnapshot?> GetLatestSnapshotAsync(string sessionId);
    }
}
