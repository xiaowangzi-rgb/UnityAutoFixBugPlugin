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
    ///   - CollectInfoPanel/Scroll View/Viewport/Content/InputPrefab_Dropdown : Dropdown 模板（key + Dropdown）
    ///   - CollectInfoPanel/Scroll View/Viewport/Content/GetScreenShot_Btn : 截图按钮
    /// </summary>
    public class BugReportPanel : MonoBehaviour {
        private const string DefaultPrefabPath = "BugReporter/BugReportPanel";

        private GameObject _panelInstance;
        private GameObject _customPrefab;

        // UI 引用
        private Button _collectBtn;
        private Button _screenshotBtn;
        private Text _screenshotBtnText;
        private Button _selectVideoBtn;
        private Button _foldBtn;
        private Text _foldBtnText;
        private Text _videoKeyText;
        private Transform _contentParent;
        private GameObject _infoItemTemplate;
        private GameObject _issueTitleItem;
        private InputField _issueTitleInput;
        private GameObject _screenshotBtnItem;
        private GameObject _videoItem;
        private GameObject _foldItem;
        private GameObject _dropdownTemplate; // InputPrefab_Dropdown 模板
        private GameObject _screenshotPreview; // 动态创建的截图预览界面
        private bool _hasScreenshot; // 是否已截图

        // 动态 Dropdown：field_key → (Dropdown, FieldDefinition)
        private readonly Dictionary<string, Dropdown> _dynamicDropdowns = new();
        private readonly List<GameObject> _dynamicDropdownItems = new();

        // 采集信息展开/收起状态
        private bool _infoExpanded;
        private readonly List<GameObject> _infoItems = new();

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

            if (!BugReporterSDK.IsFieldMetadataReady) {
                Debug.LogWarning("[BugReporter] Field metadata not loaded. Cannot show report UI.");
                return;
            }

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
            CreateDynamicDropdowns();
            _panelInstance.SetActive(true);

            // 打开时立即采集并展示信息
            CollectAndDisplay();
        }

        public void Hide() {
            if (_panelInstance != null) {
                Destroy(_panelInstance);
                _panelInstance = null;
                _collectBtn = null;
                _screenshotBtn = null;
                _screenshotBtnText = null;
                _selectVideoBtn = null;
                _foldBtn = null;
                _foldBtnText = null;
                _videoKeyText = null;
                _contentParent = null;
                _infoItemTemplate = null;
                _issueTitleItem = null;
                _issueTitleInput = null;
                _screenshotBtnItem = null;
                _videoItem = null;
                _foldItem = null;
                _dropdownTemplate = null;
                _screenshotPreview = null;
                _hasScreenshot = false;
                _dynamicDropdowns.Clear();
                _dynamicDropdownItems.Clear();
                _infoExpanded = false;
                _infoItems.Clear();
                _cachedFields = null;
                _cachedFiles = null;
            }
        }

        private void BindUI() {
            var root = _panelInstance.transform;

            // CollectBtn — 上报按钮
            var collectBtnTr = root.Find("Panel/CollectBtn");
            if (collectBtnTr != null) {
                _collectBtn = collectBtnTr.GetComponent<Button>();
                _collectBtn?.onClick.AddListener(OnSubmitClicked);
            }

            // InfoItem 模板 和 InputItem_IssueTitle（在 Content 下）
            var contentTr = root.Find("Panel/CollectInfoPanel/Scroll View/Viewport/Content");
            if (contentTr != null) {
                _contentParent = contentTr;

                // IssueTitle 输入项
                var issueTitleTr = contentTr.Find("InputItem_IssueTitle");
                if (issueTitleTr != null) {
                    _issueTitleItem = issueTitleTr.gameObject;
                    _issueTitleInput = issueTitleTr.Find("InputField")?.GetComponent<InputField>();
                }

                // 视频选择项
                var videoTr = contentTr.Find("InputVideo_BugVideo");
                if (videoTr != null) {
                    _videoItem = videoTr.gameObject;
                    _videoKeyText = videoTr.Find("key")?.GetComponent<Text>();
                    var selectBtnTr = videoTr.Find("SelectVideoBtn");
                    if (selectBtnTr != null) {
                        _selectVideoBtn = selectBtnTr.GetComponent<Button>();
                        _selectVideoBtn?.onClick.AddListener(OnSelectVideoClicked);
                    }
                }

                // Fold 展开/收起按钮
                var foldTr = contentTr.Find("Fold");
                if (foldTr != null) {
                    _foldItem = foldTr.gameObject;
                    var foldBtnTr = foldTr.Find("FoldBtn");
                    if (foldBtnTr != null) {
                        _foldBtn = foldBtnTr.GetComponent<Button>();
                        _foldBtnText = foldBtnTr.Find("Text")?.GetComponent<Text>();
                        _foldBtn?.onClick.AddListener(OnFoldClicked);
                    }
                }

                // GetScreenShot_Btn 截图按钮
                var screenshotBtnTr = contentTr.Find("GetScreenShot_Btn");
                if (screenshotBtnTr != null) {
                    _screenshotBtnItem = screenshotBtnTr.gameObject;
                    var getBtnTr = screenshotBtnTr.Find("GetBtn");
                    if (getBtnTr != null) {
                        _screenshotBtn = getBtnTr.GetComponent<Button>();
                        _screenshotBtnText = getBtnTr.Find("Text (Legacy)")?.GetComponent<Text>();
                        _screenshotBtn?.onClick.AddListener(OnScreenshotBtnClicked);
                    }
                }

                // 清理旧的硬编码 Dropdown 节点（如果 prefab 里还有的话）
                DestroyIfExists(contentTr, "InputPriority_Dropdown");
                DestroyIfExists(contentTr, "InputSignificance_Dropdown");
                DestroyIfExists(contentTr, "InputDiscoveryStage_Dropdown");

                // InputPrefab_Dropdown 模板
                var dropdownTemplateTr = contentTr.Find("InputPrefab_Dropdown");
                if (dropdownTemplateTr != null) {
                    _dropdownTemplate = dropdownTemplateTr.gameObject;
                    _dropdownTemplate.SetActive(false);
                }

                // InfoItem 模板
                var itemTr = contentTr.Find("InfoItem");
                if (itemTr != null) {
                    _infoItemTemplate = itemTr.gameObject;
                    _infoItemTemplate.SetActive(false);
                }
            }

        }

        /// <summary>
        /// 根据字段元数据动态创建 Dropdown。
        /// 使用 InputPrefab_Dropdown 模板实例化，放在 Fold 之前。
        /// </summary>
        private void CreateDynamicDropdowns() {
            if (_contentParent == null || _dropdownTemplate == null) return;

            var metadata = BugReporterSDK.GetFieldMetadata();
            var dropdownFields = new List<FieldDefinition>();
            foreach (var field in metadata.Fields.Values) {
                if (field.options == null || field.options.Count == 0) continue;
                // select 和 work_item_related_multi_select 都用 Dropdown
                if (field.field_type != "select" && field.field_type != "work_item_related_multi_select") continue;
                // name 和 description 用 InputField，不用 Dropdown
                if (field.field_key == "name" || field.field_key == "description") continue;
                // 跳过"出自测试用例"字段
                if (field.field_name == "出自测试用例") continue;
                dropdownFields.Add(field);
            }

            // 找到 InputVideo_BugVideo 的 sibling index，动态 Dropdown 插在它前面
            var videoIndex = _videoItem != null ? _videoItem.transform.GetSiblingIndex() : _contentParent.childCount;

            for (var i = 0; i < dropdownFields.Count; i++) {
                var field = dropdownFields[i];

                // 对 work_item_related_multi_select（版本类）选项按 value 数字从大到小排序
                if (field.field_type == "work_item_related_multi_select") {
                    field.options.Sort((a, b) => {
                        long.TryParse(b.value, out var bv);
                        long.TryParse(a.value, out var av);
                        return bv.CompareTo(av);
                    });
                }

                var itemGo = Instantiate(_dropdownTemplate, _contentParent);
                itemGo.name = $"Dynamic_{field.field_key}";
                itemGo.SetActive(true);

                // 设置 key 标签为字段名
                var keyText = itemGo.transform.Find("key")?.GetComponent<Text>();
                if (keyText != null) {
                    keyText.text = field.field_name + ":";
                }

                // 填充 Dropdown 选项
                var dropdown = itemGo.transform.Find("Dropdown")?.GetComponent<Dropdown>();
                if (dropdown != null) {
                    dropdown.ClearOptions();
                    var options = new List<string>();
                    foreach (var opt in field.options) {
                        options.Add(opt.label);
                    }
                    dropdown.AddOptions(options);
                    dropdown.value = 0;
                    dropdown.RefreshShownValue();
                    _dynamicDropdowns[field.field_key] = dropdown;
                }

                // 插到 InputVideo_BugVideo 之前
                itemGo.transform.SetSiblingIndex(videoIndex + i);

                _dynamicDropdownItems.Add(itemGo);
            }

            // 创建"是否AI修复" Dropdown（非服务器字段，本地自定义）
            CreateAiFixDropdown(videoIndex + dropdownFields.Count);
        }

        private void CreateAiFixDropdown(int siblingIndex) {
            if (_dropdownTemplate == null || _contentParent == null) return;

            var itemGo = Instantiate(_dropdownTemplate, _contentParent);
            itemGo.name = "Dynamic_ai_fix";
            itemGo.SetActive(true);

            var keyText = itemGo.transform.Find("key")?.GetComponent<Text>();
            if (keyText != null) {
                keyText.text = "是否AI修复:";
            }

            var dropdown = itemGo.transform.Find("Dropdown")?.GetComponent<Dropdown>();
            if (dropdown != null) {
                dropdown.ClearOptions();
                dropdown.AddOptions(new List<string> { "False", "True" });
                dropdown.value = 0; // 默认 False
                dropdown.RefreshShownValue();
                _dynamicDropdowns["ai_fix"] = dropdown;
            }

            itemGo.transform.SetSiblingIndex(siblingIndex);
            _dynamicDropdownItems.Add(itemGo);
        }

        private static void DestroyIfExists(Transform parent, string childName) {
            var tr = parent.Find(childName);
            if (tr != null) {
                Destroy(tr.gameObject);
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
                // 视频采集器跳过（由 SelectVideoBtn 手动触发）
                if (collector is VideoCollector) continue;
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

        // ── GetScreenShot_Btn: 截图 / 预览 ──

        private void OnScreenshotBtnClicked() {
            if (!_hasScreenshot) {
                // 还没截图 → 执行截图
                _screenshotBtn.interactable = false;
                _panelInstance.SetActive(false);
                // 面板被隐藏后 StartCoroutine 不能在自身执行，借用 SDK 的 MonoBehaviour
                BugReporterSDK.GetInstance().StartCoroutine(DoCaptureScreenshot());
            } else {
                // 已截图 → 弹出预览
                ShowScreenshotPreview();
            }
        }

        private IEnumerator DoCaptureScreenshot() {
            var screenshotCollector = BugReporterSDK.GetScreenshotCollector();
            if (screenshotCollector is { IsEnabled: true }) {
                yield return screenshotCollector.CaptureScreenshot();

                var result = screenshotCollector.Collect();
                if (result.Files != null &&
                    result.Files.TryGetValue("screenshot", out var pngBytes) &&
                    pngBytes != null) {
                    _cachedFiles["screenshot"] = pngBytes;
                    _hasScreenshot = true;
                }
            }

            // 重新显示面板
            _panelInstance.SetActive(true);

            // 更新按钮文字
            if (_hasScreenshot && _screenshotBtnText != null) {
                _screenshotBtnText.text = "预览截图";
            }

            if (_screenshotBtn != null) {
                _screenshotBtn.interactable = true;
            }
        }

        private void ShowScreenshotPreview() {
            if (_screenshotPreview != null) return;
            if (_cachedFiles == null || !_cachedFiles.TryGetValue("screenshot", out var png) || png == null) return;

            // 创建全屏遮罩
            var previewGo = new GameObject("ScreenshotPreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            previewGo.transform.SetParent(_panelInstance.transform, false);
            var previewRt = previewGo.GetComponent<RectTransform>();
            previewRt.anchorMin = Vector2.zero;
            previewRt.anchorMax = Vector2.one;
            previewRt.offsetMin = Vector2.zero;
            previewRt.offsetMax = Vector2.zero;
            var bgImage = previewGo.GetComponent<Image>();
            bgImage.color = new Color(0, 0, 0, 0.9f);

            // 截图 RawImage
            var rawImgGo = new GameObject("ScreenshotImage", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
            rawImgGo.transform.SetParent(previewGo.transform, false);
            var rawImgRt = rawImgGo.GetComponent<RectTransform>();
            rawImgRt.anchorMin = new Vector2(0.05f, 0.1f);
            rawImgRt.anchorMax = new Vector2(0.95f, 0.9f);
            rawImgRt.offsetMin = Vector2.zero;
            rawImgRt.offsetMax = Vector2.zero;
            var rawImage = rawImgGo.GetComponent<RawImage>();

            var tex = new Texture2D(2, 2);
            if (tex.LoadImage(png)) {
                rawImage.texture = tex;
            }

            // 关闭按钮（右上角）
            var closeBtnGo = new GameObject("CloseBtn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            closeBtnGo.transform.SetParent(previewGo.transform, false);
            var closeBtnRt = closeBtnGo.GetComponent<RectTransform>();
            closeBtnRt.anchorMin = new Vector2(1, 1);
            closeBtnRt.anchorMax = new Vector2(1, 1);
            closeBtnRt.pivot = new Vector2(1, 1);
            closeBtnRt.anchoredPosition = new Vector2(-20, -20);
            closeBtnRt.sizeDelta = new Vector2(80, 80);
            var closeBtnImg = closeBtnGo.GetComponent<Image>();
            closeBtnImg.color = new Color(0.8f, 0.2f, 0.2f, 1f);

            // 关闭按钮文字 "X"
            var closeTextGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            closeTextGo.transform.SetParent(closeBtnGo.transform, false);
            var closeTextRt = closeTextGo.GetComponent<RectTransform>();
            closeTextRt.anchorMin = Vector2.zero;
            closeTextRt.anchorMax = Vector2.one;
            closeTextRt.offsetMin = Vector2.zero;
            closeTextRt.offsetMax = Vector2.zero;
            var closeText = closeTextGo.GetComponent<Text>();
            closeText.text = "X";
            closeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            closeText.fontSize = 36;
            closeText.color = Color.white;
            closeText.alignment = TextAnchor.MiddleCenter;

            closeBtnGo.GetComponent<Button>().onClick.AddListener(CloseScreenshotPreview);

            _screenshotPreview = previewGo;
        }

        private void CloseScreenshotPreview() {
            if (_screenshotPreview != null) {
                // 释放预览用的纹理
                var rawImage = _screenshotPreview.GetComponentInChildren<RawImage>();
                if (rawImage != null && rawImage.texture is Texture2D tex) {
                    Destroy(tex);
                }
                Destroy(_screenshotPreview);
                _screenshotPreview = null;
            }
        }

        // ── FoldBtn: 展开/收起采集信息 ──

        private void OnFoldClicked() {
            _infoExpanded = !_infoExpanded;
            foreach (var item in _infoItems) {
                if (item != null) item.SetActive(_infoExpanded);
            }
            if (_foldBtnText != null) {
                _foldBtnText.text = _infoExpanded ? "收起" : "展开";
            }
        }

        // ── SelectVideoBtn: 选择视频 ──

        private void OnSelectVideoClicked() {
            var videoCollector = BugReporterSDK.GetVideoCollector();
            if (videoCollector == null) {
                Debug.LogWarning("[BugReporter] VideoCollector not enabled.");
                return;
            }

            _selectVideoBtn.interactable = false;
            videoCollector.PickVideo((success, msg) => {
                if (_videoKeyText != null) {
                    _videoKeyText.text = success ? msg : "录屏视频（未选择）";
                }
                if (_selectVideoBtn != null) {
                    _selectVideoBtn.interactable = true;
                }
            });
        }

        // ── CollectBtn: 上报 ──

        private void OnSubmitClicked() {
            _collectBtn.interactable = false;
            StartCoroutine(DoSubmit());
        }

        private IEnumerator DoSubmit() {
            // 读取用户输入的标题
            var issueTitle = _issueTitleInput != null ? _issueTitleInput.text : "";

            var report = new BugReport {
                AppId = BugReporterSDK.GetConfig().appId,
                Description = issueTitle,
                Fields = _cachedFields ?? new Dictionary<string, string>(),
                Files = _cachedFiles ?? new Dictionary<string, byte[]>(),
            };

            // 标题也作为字段上报（映射到 name）
            if (!string.IsNullOrEmpty(issueTitle)) {
                report.Fields["issueTitle"] = issueTitle;
            }

            // 描述固定内容上报
            report.Fields["issueDec"] = "操作步骤：\n实际结果：\n期望结果：";

            // 动态 Dropdown 选择项上报：传 option 的 value
            var metadata = BugReporterSDK.GetFieldMetadata();
            foreach (var kv in _dynamicDropdowns) {
                var fieldKey = kv.Key;
                var dropdown = kv.Value;
                if (dropdown == null) continue;

                var fieldDef = metadata.Get(fieldKey);
                if (fieldDef != null) {
                    // 服务器元数据字段：传 option 的 value
                    if (fieldDef.options == null || dropdown.value >= fieldDef.options.Count) continue;
                    report.Fields[fieldKey] = fieldDef.options[dropdown.value].value;
                } else {
                    // 本地自定义字段（如 ai_fix）：直接传 Dropdown 显示文本
                    report.Fields[fieldKey] = dropdown.options[dropdown.value].text;
                }
            }

            // 合并视频采集器的数据
            var videoCollector = BugReporterSDK.GetVideoCollector();
            if (videoCollector is { HasVideo: true }) {
                try {
                    var videoResult = videoCollector.Collect();
                    if (videoResult.Fields != null) {
                        foreach (var kv in videoResult.Fields)
                            report.Fields[kv.Key] = kv.Value;
                    }
                    if (videoResult.Files != null) {
                        foreach (var kv in videoResult.Files)
                            report.Files[kv.Key] = kv.Value;
                    }
                } catch (Exception e) {
                    Debug.LogWarning($"[BugReporter] VideoCollector failed: {e.Message}");
                }
            }

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

            // 收集需要保留的 GameObject
            var keepSet = new HashSet<GameObject> {
                _infoItemTemplate, _dropdownTemplate, _issueTitleItem,
                _screenshotBtnItem, _videoItem, _foldItem
            };
            foreach (var item in _dynamicDropdownItems) {
                keepSet.Add(item);
            }

            // 清除之前生成的 item
            for (var i = _contentParent.childCount - 1; i >= 0; i--) {
                var child = _contentParent.GetChild(i).gameObject;
                if (!keepSet.Contains(child)) {
                    Destroy(child);
                }
            }

            _infoItems.Clear();

            foreach (var kv in fields) {
                var item = Instantiate(_infoItemTemplate, _contentParent);
                // 默认隐藏，等用户点展开才显示
                item.SetActive(_infoExpanded);

                var keyText = item.transform.Find("key")?.GetComponent<Text>();
                var valueText = item.transform.Find("value")?.GetComponent<Text>();

                if (keyText != null) keyText.text = kv.Key + ":";
                if (valueText != null) {
                    valueText.text = kv.Value != null && kv.Value.Length > 200
                        ? kv.Value.Substring(0, 200) + "..."
                        : kv.Value ?? "";
                }

                _infoItems.Add(item);
            }
        }

    }
}
