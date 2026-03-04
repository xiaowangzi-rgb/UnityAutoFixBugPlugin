using UnityEngine;

namespace JoyCastle.BugReporter {
    [CreateAssetMenu(
        fileName = "BugReporterConfig",
        menuName = "BugReporter/Config")]
    public class BugReporterConfig : ScriptableObject {
        [Header("服务端")]
        [Tooltip("Bug 上报服务器地址")]
        public string serverUrl = "";

        [Tooltip("Webhook 认证 Token")]
        public string webhookToken = "";

        [Tooltip("项目标识符")]
        public string appId = "";

        [Header("触发方式")]
        public bool enableShake = true;
        public float shakeThreshold = 2.5f;

        [Header("采集器配置")]
        public bool enableLogCollector = true;
        public bool enableRuntimeLog = true;  // 是否采集 Application.logMessageReceived
        public int maxLogLines = 100;
        public bool enableScreenshot = true;
        public bool enableFpsCollector = true;

        [Header("日志文件（可选）")]
        [Tooltip("项目方指定的日志文件路径，留空则只采集运行时日志")]
        public string[] logFilePaths = new string[0];

        [Header("视频采集")]
        public bool enableVideoCollector = true;
        [Tooltip("视频文件大小上限（MB）")]
        public int maxVideoSizeMB = 100;

        [Header("上报配置")]
        [Tooltip("HTTP 请求超时时间(秒)")]
        public int uploadTimeout = 30;
    }
}
