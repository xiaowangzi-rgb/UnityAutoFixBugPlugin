using UnityEngine;

namespace JoyCastle.BugReporter {
    public class DeviceCollector : IInfoCollector {
        public string Key => "device";
        public bool IsEnabled => true;

        public CollectResult Collect() {
            return new CollectResult {
                Fields = new() {
                    ["deviceModel"] = SystemInfo.deviceModel,
                    ["osVersion"] = SystemInfo.operatingSystem,
                    ["memorySize"] = SystemInfo.systemMemorySize.ToString(),
                    ["processorType"] = SystemInfo.processorType,
                    ["graphicsDeviceName"] = SystemInfo.graphicsDeviceName,
                }
            };
        }
    }
}
