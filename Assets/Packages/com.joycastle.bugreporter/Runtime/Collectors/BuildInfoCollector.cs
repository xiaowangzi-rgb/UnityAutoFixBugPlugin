using UnityEngine;

namespace JoyCastle.BugReporter {
    public class BuildInfoCollector : IInfoCollector {
        public string Key => "buildinfo";
        public bool IsEnabled => true;

        public CollectResult Collect() {
            var fields = new System.Collections.Generic.Dictionary<string, string> {
                ["versionName"] = Application.version,
                ["platform"] = Application.platform.ToString(),
            };

            // 读取打包时注入的 BuildInfo.json
            var buildInfoAsset = Resources.Load<TextAsset>("BugReporter/BuildInfo");
            if (buildInfoAsset != null) {
                var info = JsonUtility.FromJson<BuildInfoData>(buildInfoAsset.text);
                fields["gitBranch"] = info.gitBranch ?? "";
                fields["gitCommit"] = info.gitCommit ?? "";
                fields["buildNumber"] = info.buildNumber ?? "";
                fields["buildTime"] = info.buildTime ?? "";
            }

            return new CollectResult { Fields = fields };
        }

        [System.Serializable]
        private class BuildInfoData {
            public string gitBranch;
            public string gitCommit;
            public string buildNumber;
            public string buildTime;
        }
    }
}
