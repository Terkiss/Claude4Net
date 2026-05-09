using System;
using Claude4Net.SDK.Events;

namespace Claude4Net.Runtime
{
    public class SnapshotPolicy
    {
        private readonly int _eventThreshold;

        public SnapshotPolicy(int eventThreshold = 50)
        {
            _eventThreshold = eventThreshold;
        }

        public bool ShouldTakeSnapshot(long currentVersion, long lastSnapshotVersion)
        {
            return (currentVersion - lastSnapshotVersion) >= _eventThreshold;
        }
    }
}
