using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace JoyCastle.BugReporter {
    public class BugReporterSDK : MonoBehaviour {
        private static BugReporterSDK _instance;
        private static BugReporterConfig _config;
        private static readonly List<IInfoCollector> s_collectors = new();
        private static bool s_initialized;
        private static FieldMetadataManager s_fieldMetadata = new();

        private ReportUploader _uploader;
        private FpsCollector _fpsCollector;
        private ScreenshotCollector _screenshotCollector;
        private LogCollector _logCollector;
        private VideoCollector _videoCollector;
        private BugReportPanel _panel;

        // ── 公开 API ──

        public static void Init(BugReporterConfig config) {
            if (s_initialized) {
                Debug.LogWarning("[BugReporter] Already initialized.");
                return;
            }

            _config = config;
            s_collectors.Clear();
            s_fieldMetadata = new FieldMetadataManager();

            // 创建持久化 GameObject
            var go = new GameObject("[BugReporterSDK]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<BugReporterSDK>();
            _instance.Setup();

            // 启动时拉取字段元数据
            _instance.StartCoroutine(_instance.FetchFieldMetadata());

            s_initialized = true;
            Debug.Log("[BugReporter] Initialized.");
        }

        public static void RegisterCollector(IInfoCollector collector) {
            if (collector == null) return;
            s_collectors.Insert(0, collector);
        }

        public static void ShowReportUI() {
            EnsureInitialized();
            if (!s_fieldMetadata.IsReady) {
                Debug.LogWarning("[BugReporter] Field metadata not loaded. Cannot show report UI.");
                return;
            }
            if (_instance._panel == null) {
                _instance._panel = _instance.gameObject.AddComponent<BugReportPanel>();
            }
            _instance._panel.Show();
        }

        public static void SubmitSilently(string description = "") {
            EnsureInitialized();
            if (!s_fieldMetadata.IsReady) {
                Debug.LogWarning("[BugReporter] Field metadata not loaded. Cannot submit report.");
                return;
            }
            _instance.StartCoroutine(_instance.DoSubmit(description));
        }

        public static IReadOnlyList<IInfoCollector> GetCollectors() {
            EnsureInitialized();
            return s_collectors;
        }

        public static ScreenshotCollector GetScreenshotCollector() {
            EnsureInitialized();
            return _instance._screenshotCollector;
        }

        public static VideoCollector GetVideoCollector() {
            EnsureInitialized();
            return _instance._videoCollector;
        }

        public static BugReporterConfig GetConfig() {
            EnsureInitialized();
            return _config;
        }

        public static ReportUploader GetUploader() {
            EnsureInitialized();
            return _instance._uploader;
        }

        /// <summary>
        /// 获取 SDK MonoBehaviour 实例（用于在面板隐藏时执行协程）。
        /// </summary>
        public static BugReporterSDK GetInstance() {
            EnsureInitialized();
            return _instance;
        }

        /// <summary>
        /// 获取字段元数据管理器。
        /// </summary>
        public static FieldMetadataManager GetFieldMetadata() {
            EnsureInitialized();
            return s_fieldMetadata;
        }

        /// <summary>
        /// 字段元数据是否已加载成功。
        /// </summary>
        public static bool IsFieldMetadataReady => s_fieldMetadata.IsReady;

        // ── 内部逻辑 ──

        private void Setup() {
            _uploader = new ReportUploader(_config.serverUrl, _config.webhookToken, _config.uploadTimeout);

            // 注册内置采集器
            s_collectors.Add(new DeviceCollector());
            // 注册BuildInfo采集器
            s_collectors.Add(new BuildInfoCollector());
            // 注册日志采集器
            if (_config.enableLogCollector) {
                _logCollector = new LogCollector(
                    _config.maxLogLines,
                    _config.enableRuntimeLog);
                // 注册项目方配置的日志文件路径
                if (_config.logFilePaths != null) {
                    foreach (var path in _config.logFilePaths) {
                        _logCollector.AddLogFilePath(path);
                    }
                }
                s_collectors.Add(_logCollector);
            }
            // 注册Fps采集器
            if (_config.enableFpsCollector) {
                _fpsCollector = new FpsCollector();
                s_collectors.Add(_fpsCollector);
            }
            // 注册截图采集器
            if (_config.enableScreenshot) {
                _screenshotCollector = new ScreenshotCollector();
                s_collectors.Add(_screenshotCollector);
            }
            // 注册视频采集器
            if (_config.enableVideoCollector) {
                _videoCollector = new VideoCollector(_config.maxVideoSizeMB);
                s_collectors.Add(_videoCollector);
            }
        }

        /// <summary>
        /// 启动时拉取字段元数据。失败则不允许上报缺陷。
        /// </summary>
        private IEnumerator FetchFieldMetadata() {
            if (string.IsNullOrEmpty(_config.serverUrl)) {
                Debug.LogError("[BugReporter] serverUrl is not configured. Bug reporting disabled.");
                yield break;
            }

            var fieldsUrl = _config.serverUrl.TrimEnd('/') + "/api/issue/fields";
            using var request = UnityWebRequest.Get(fieldsUrl);
            request.timeout = 10;

            if (!string.IsNullOrEmpty(_config.webhookToken)) {
                request.SetRequestHeader("X-API-Token", _config.webhookToken);
            }

            Debug.Log($"[BugReporter] Fetching field metadata from: {fieldsUrl}");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success) {
                Debug.LogError($"[BugReporter] Failed to fetch field metadata: {request.error}. Bug reporting disabled.");
                yield break;
            }

            try {
                var json = request.downloadHandler.text;
                var response = JsonUtility.FromJson<FieldsResponse>(json);
                if (response?.fields == null || response.fields.Count == 0) {
                    Debug.LogError("[BugReporter] Field metadata is empty. Bug reporting disabled.");
                    yield break;
                }

                s_fieldMetadata.Load(response.fields);
                Debug.Log($"[BugReporter] Field metadata loaded: {response.fields.Count} fields.");
            } catch (Exception e) {
                Debug.LogError($"[BugReporter] Failed to parse field metadata: {e.Message}. Bug reporting disabled.");
            }
        }

        private void Update() {
            _fpsCollector?.Update();

            // 摇一摇检测
            if (_config.enableShake
                && Input.acceleration.sqrMagnitude > _config.shakeThreshold * _config.shakeThreshold) {
                ShowReportUI();
            }
        }

        private IEnumerator DoSubmit(string description) {
            // 先截图（需要等到帧末）
            if (_screenshotCollector is { IsEnabled: true }) {
                yield return _screenshotCollector.CaptureScreenshot();
            }

            // 汇总所有采集器数据
            var report = new BugReport {
                AppId = _config.appId,
                Description = description,
            };

            foreach (var collector in s_collectors) {
                if (!collector.IsEnabled) continue;
                try {
                    var result = collector.Collect();
                    if (result.Fields != null) {
                        foreach (var kv in result.Fields) {
                            report.Fields[kv.Key] = kv.Value;
                        }
                    }
                    if (result.Files != null) {
                        foreach (var kv in result.Files) {
                            report.Files[kv.Key] = kv.Value;
                        }
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"[BugReporter] Collector '{collector.Key}' failed: {e.Message}");
                }
            }

            // 上报
            yield return _uploader.Upload(report, (success, msg) => {
                Debug.Log(success
                    ? "[BugReporter] Report submitted."
                    : $"[BugReporter] Report failed: {msg}");
            });
        }

        private static void EnsureInitialized() {
            if (!s_initialized) {
                throw new InvalidOperationException(
                    "[BugReporter] SDK not initialized. Call BugReporterSDK.Init() first.");
            }
        }

        private void OnDestroy() {
            _logCollector?.Dispose();
            s_collectors.Clear();
            s_fieldMetadata = new FieldMetadataManager();
            s_initialized = false;
        }
    }
}
