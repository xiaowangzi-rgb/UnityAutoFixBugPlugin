using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace JoyCastle.BugReporter {
    /// <summary>
    /// Bug 反馈面板。
    /// 从 Resources/BugReporter/BugReportPanel prefab 加载 UI。
    /// 打开时立即采集并展示所有信息；点击 GetBtn 截图并刷新预览；点击 CollectBtn 上报。
    ///
    /// Prefab 节点约定：
    ///   - CollectBtn : Button，点击上报
    ///   - CollectInfoPanel/Scroll View/Viewport/Content : 信息列表容器
    ///   - CollectInfoPanel/Scroll View/Viewport/Content/InfoItem : 模板（key + value）
    ///   - ScreenshotPanel/_ScreenShotRawImage : RawImage，展示截图
    ///   - ScreenshotPanel/_ScreenShotRawImage/GetBtn : Button，点击截图
    /// </summary>
    public class BugReportPanel : MonoBehaviour {
        private const string DefaultPrefabPath = "BugReporter/BugReportPanel";

        private GameObject _panelInstance;
        private GameObject _customPrefab;

        // UI 引用
        private Button _collectBtn;
        private Button _getBtn;
        private Transform _contentParent;
        private GameObject _infoItemTemplate;
        private RawImage _screenshotRawImage;

        // 采集缓存（打开时采集一次，上报时直接用）
        private Dictionary<string, string> _cachedFields;
        private Dictionary<string, byte[]> _cachedFiles;

        /// <summary>
        /// 项目方可调用：指定自定义 prefab，不走 Resources 加载。
        /// </summary>
        public void SetPrefab(GameObject prefab) {
            _customPrefab = prefab;
        }

        public void Show() {
            if (_panelInstance != null) return;

            var prefab = _customPrefab != null
                ? _customPrefab
                : Resources.Load<GameObject>(DefaultPrefabPath);

            if (prefab == null) {
                Debug.LogError(
                    $"[BugReporter] Panel prefab not found at Resources/{DefaultPrefabPath}. " +
                    "Please create a prefab or call SetPrefab() to provide one.");
                return;
            }

            _panelInstance = Instantiate(prefab, transform);
            BindUI();
            _panelInstance.SetActive(true);

            // 打开时立即采集并展示信息
            CollectAndDisplay();
        }

        public void Hide() {
            if (_panelInstance != null) {
                Destroy(_panelInstance);
                _panelInstance = null;
                _collectBtn = null;
                _getBtn = null;
                _contentParent = null;
                _infoItemTemplate = null;
                _screenshotRawImage = null;
                _cachedFields = null;
                _cachedFiles = null;
            }
        }

        private void BindUI() {
            var root = _panelInstance.transform;

            // CollectBtn — 上报按钮
            var collectBtnTr = root.Find("CollectBtn");
            if (collectBtnTr != null) {
                _collectBtn = collectBtnTr.GetComponent<Button>();
                _collectBtn?.onClick.AddListener(OnSubmitClicked);
            }

            // InfoItem 模板（在 Content 下）
            var contentTr = root.Find("CollectInfoPanel/Scroll View/Viewport/Content");
            if (contentTr != null) {
                _contentParent = contentTr;
                var itemTr = contentTr.Find("InfoItem");
                if (itemTr != null) {
                    _infoItemTemplate = itemTr.gameObject;
                    _infoItemTemplate.SetActive(false);
                }
            }

            // 截图 RawImage
            var rawImgTr = root.Find("ScreenshotPanel/_ScreenShotRawImage");
            if (rawImgTr != null) {
                _screenshotRawImage = rawImgTr.GetComponent<RawImage>();

                // GetBtn — 截图按钮（在 _ScreenShotRawImage 下）
                var getBtnTr = rawImgTr.Find("GetBtn");
                if (getBtnTr != null) {
                    _getBtn = getBtnTr.GetComponent<Button>();
                    _getBtn?.onClick.AddListener(OnGetScreenshotClicked);
                }
            }
        }

        // ── 打开时立即采集（不含截图） ──

        private void CollectAndDisplay() {
            _cachedFields = new Dictionary<string, string>();
            _cachedFiles = new Dictionary<string, byte[]>();

            var collectors = BugReporterSDK.GetCollectors();
            foreach (var collector in collectors) {
                if (!collector.IsEnabled) continue;
                // 截图采集器跳过（由 GetBtn 手动触发）
                if (collector is ScreenshotCollector) continue;
                try {
                    var result = collector.Collect();
                    if (result.Fields != null) {
                        foreach (var kv in result.Fields)
                            _cachedFields[kv.Key] = kv.Value;
                    }
                    if (result.Files != null) {
                        foreach (var kv in result.Files)
                            _cachedFiles[kv.Key] = kv.Value;
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"[BugReporter] Collector '{collector.Key}' failed: {e.Message}");
                }
            }

            PopulateInfoList(_cachedFields);
        }

        // ── GetBtn: 截图并刷新预览 ──

        private void OnGetScreenshotClicked() {
            _getBtn.interactable = false;

            // 先隐藏面板，截到干净的游戏画面
            _panelInstance.SetActive(false);
            StartCoroutine(DoCaptureScreenshot());
        }

        private IEnumerator DoCaptureScreenshot() {
            var screenshotCollector = BugReporterSDK.GetScreenshotCollector();
            if (screenshotCollector is { IsEnabled: true }) {
                yield return screenshotCollector.CaptureScreenshot();

                var result = screenshotCollector.Collect();
                if (result.Files != null &&
                    result.Files.TryGetValue("screenshot", out var pngBytes) &&
                    pngBytes != null) {
                    // 更新缓存
                    _cachedFiles["screenshot"] = pngBytes;
                }
            }

            // 重新显示面板
            _panelInstance.SetActive(true);

            // 刷新截图预览
            if (_cachedFiles != null &&
                _cachedFiles.TryGetValue("screenshot", out var png) && png != null) {
                ShowScreenshot(png);
            }

            if (_getBtn != null) {
                _getBtn.interactable = true;
            }
        }

        // ── CollectBtn: 上报 ──

        private void OnSubmitClicked() {
            _collectBtn.interactable = false;
            StartCoroutine(DoSubmit());
        }

        private IEnumerator DoSubmit() {
            var report = new BugReport {
                AppId = BugReporterSDK.GetConfig().appId,
                Description = "",
                Fields = _cachedFields ?? new Dictionary<string, string>(),
                Files = _cachedFiles ?? new Dictionary<string, byte[]>(),
            };

            yield return BugReporterSDK.GetUploader().Upload(report, (success, msg) => {
                Debug.Log(success
                    ? "[BugReporter] Report submitted."
                    : $"[BugReporter] Report failed: {msg}");
            });

            if (_collectBtn != null) {
                _collectBtn.interactable = true;
            }
        }

        // ── UI 填充 ──

        private void PopulateInfoList(Dictionary<string, string> fields) {
            if (_contentParent == null || _infoItemTemplate == null) return;

            // 清除之前生成的 item（保留模板）
            for (var i = _contentParent.childCount - 1; i >= 0; i--) {
                var child = _contentParent.GetChild(i).gameObject;
                if (child != _infoItemTemplate) {
                    Destroy(child);
                }
            }

            foreach (var kv in fields) {
                var item = Instantiate(_infoItemTemplate, _contentParent);
                item.SetActive(true);

                var keyText = item.transform.Find("key")?.GetComponent<Text>();
                var valueText = item.transform.Find("value")?.GetComponent<Text>();

                if (keyText != null) keyText.text = kv.Key;
                if (valueText != null) {
                    valueText.text = kv.Value != null && kv.Value.Length > 200
                        ? kv.Value.Substring(0, 200) + "..."
                        : kv.Value ?? "";
                }
            }
        }

        private void ShowScreenshot(byte[] pngBytes) {
            if (_screenshotRawImage == null) return;

            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(pngBytes)) {
                // 释放旧纹理
                if (_screenshotRawImage.texture != null &&
                    _screenshotRawImage.texture is Texture2D oldTex) {
                    Destroy(oldTex);
                }
                _screenshotRawImage.texture = tex;
                _screenshotRawImage.SetNativeSize();
                FitRawImageToParent(_screenshotRawImage);
            }
        }

        private static void FitRawImageToParent(RawImage rawImage) {
            var rt = rawImage.GetComponent<RectTransform>();
            var parentRt = rt.parent as RectTransform;
            if (parentRt == null || rawImage.texture == null) return;

            var parentSize = parentRt.rect.size;
            var texW = (float)rawImage.texture.width;
            var texH = (float)rawImage.texture.height;
            var scale = Mathf.Min(parentSize.x / texW, parentSize.y / texH);

            rt.sizeDelta = new Vector2(texW * scale, texH * scale);
        }
    }
}
