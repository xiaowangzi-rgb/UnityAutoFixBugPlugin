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
        private GameObject _screenshotGallery; // 动态创建的截图相册界面
        private Transform _galleryContent;     // 相册内部网格容器
        private GameObject _fullscreenPreview; // 从相册中点开的全屏预览
        private readonly List<Texture2D> _galleryTextures = new(); // 缩略图纹理，便于释放

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
                _screenshotGallery = null;
                _galleryContent = null;
                _fullscreenPreview = null;
                foreach (var tex in _galleryTextures) {
                    if (tex != null) Destroy(tex);
                }
                _galleryTextures.Clear();
                BugReporterSDK.GetScreenshotCollector()?.Clear();
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

            // CloseBtn — 关闭按钮
            var closeBtnTr = root.Find("Panel/CloseBtn");
            if (closeBtnTr != null) {
                var closeBtn = closeBtnTr.GetComponent<Button>();
                closeBtn?.onClick.AddListener(Hide);
            }

            // InfoItem 模板 和 InputItem_IssueTitle（在 Content 下）
            // IssueTitle 输入项（已挪到 CollectInfoPanel 直接子节点）
            var issueTitleTr = root.Find("Panel/CollectInfoPanel/InputItem_IssueTitle");
            if (issueTitleTr != null) {
                _issueTitleItem = issueTitleTr.gameObject;
                _issueTitleInput = issueTitleTr.Find("InputField")?.GetComponent<InputField>();
            }

            var contentTr = root.Find("Panel/CollectInfoPanel/Scroll View/Viewport/Content");
            if (contentTr != null) {
                _contentParent = contentTr;

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
        /// 使用 InputPrefab_Dropdown 模板实例化，每行包含 LeftDropdown / RightDropdown 两个位置。
        /// 每两个字段共用一行，如果总数为奇数则最后一行右侧隐藏。
        /// </summary>
        private void CreateDynamicDropdowns() {
            if (_contentParent == null || _dropdownTemplate == null) return;

            var metadata = BugReporterSDK.GetFieldMetadata();
            var dropdownFields = new List<FieldDefinition>();
            foreach (var field in metadata.Fields.Values) {
                if (field.options == null || field.options.Count == 0) continue;
                // select、work_item_related_multi_select 和 multi_user 都用 Dropdown
                if (field.field_type != "select" && field.field_type != "work_item_related_multi_select" && field.field_type != "multi_user") continue;
                // name 和 description 用 InputField，不用 Dropdown
                if (field.field_key == "name" || field.field_key == "description") continue;
                // 跳过"出自测试用例"字段
                if (field.field_name == "出自测试用例") continue;
                // 跳过经办人和关注人字段
                if (field.field_key == "issue_operator" || field.field_key == "watchers") continue;
                dropdownFields.Add(field);
            }

            // 按指定顺序排列 Dropdown 字段，不在列表中的排到后面
            var fieldOrder = new List<string> {
                "priority",           // 优先级
                "field_805908",       // 严重性
                "issue_stage",        // 发现阶段
                "issue_reporter",     // 报告人
                "discovery_version",  // 发现版本
                "resolve_version",    // 解决版本
            };
            dropdownFields.Sort((a, b) => {
                var idxA = fieldOrder.IndexOf(a.field_key);
                var idxB = fieldOrder.IndexOf(b.field_key);
                if (idxA < 0) idxA = fieldOrder.Count;
                if (idxB < 0) idxB = fieldOrder.Count;
                return idxA.CompareTo(idxB);
            });

            // 对 work_item_related_multi_select（版本类）选项按 value 数字从大到小排序
            foreach (var field in dropdownFields) {
                if (field.field_type == "work_item_related_multi_select") {
                    field.options.Sort((a, b) => {
                        long.TryParse(b.value, out var bv);
                        long.TryParse(a.value, out var av);
                        return bv.CompareTo(av);
                    });
                }
            }

            // 追加本地自定义字段"是否AI修复"（非服务器字段，统一放入列表一起排布）
            // 用 null 占位，后续特殊处理
            dropdownFields.Add(null); // ai_fix 占位

            // 找到 InputVideo_BugVideo 的 sibling index，动态 Dropdown 行插在它前面
            var videoIndex = _videoItem != null ? _videoItem.transform.GetSiblingIndex() : _contentParent.childCount;

            // 每两个字段实例化一行模板
            var rowCount = (dropdownFields.Count + 1) / 2;
            for (var row = 0; row < rowCount; row++) {
                var leftIdx = row * 2;
                var rightIdx = row * 2 + 1;

                var itemGo = Instantiate(_dropdownTemplate, _contentParent);
                itemGo.name = $"DynamicRow_{row}";
                itemGo.SetActive(true);

                // ── 左侧 ──
                FillDropdownSide(itemGo.transform.Find("LeftDropdown"), dropdownFields[leftIdx]);

                // ── 右侧 ──
                var rightSide = itemGo.transform.Find("RightDropdown");
                if (rightIdx < dropdownFields.Count) {
                    FillDropdownSide(rightSide, dropdownFields[rightIdx]);
                } else if (rightSide != null) {
                    // 奇数个字段，最后一行右侧隐藏
                    rightSide.gameObject.SetActive(false);
                }

                // 插到 InputVideo_BugVideo 之前
                itemGo.transform.SetSiblingIndex(videoIndex + row);

                _dynamicDropdownItems.Add(itemGo);
            }

            // 应用 preferences 预填上次选择
            ApplyPreferences(metadata);
        }

        /// <summary>
        /// 根据默认值预选 Dropdown。优先级：项目方设置 > 服务器 preferences > 第一个选项。
        /// 匹配顺序：精确匹配 value/label > 模糊匹配 label 包含传入值。
        /// </summary>
        private void ApplyPreferences(FieldMetadataManager metadata) {
            foreach (var kv in _dynamicDropdowns) {
                var fieldKey = kv.Key;
                var dropdown = kv.Value;
                if (dropdown == null) continue;

                var defaultValue = metadata.GetDefaultValue(fieldKey);
                if (string.IsNullOrEmpty(defaultValue)) continue;

                var fieldDef = metadata.Get(fieldKey);
                if (fieldDef?.options == null) continue;

                // 先精确匹配 value 或 label
                var matched = false;
                for (var i = 0; i < fieldDef.options.Count; i++) {
                    if (fieldDef.options[i].value == defaultValue || fieldDef.options[i].label == defaultValue) {
                        dropdown.value = i;
                        dropdown.RefreshShownValue();
                        matched = true;
                        break;
                    }
                }
                if (matched) continue;

                // 再模糊匹配（label 包含传入值）
                for (var i = 0; i < fieldDef.options.Count; i++) {
                    if (fieldDef.options[i].label.Contains(defaultValue)) {
                        dropdown.value = i;
                        dropdown.RefreshShownValue();
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// 填充一行模板中某一侧（LeftDropdown / RightDropdown）的 key 和 Dropdown。
        /// field 为 null 时表示本地自定义的"是否AI修复"字段。
        /// </summary>
        private void FillDropdownSide(Transform side, FieldDefinition field) {
            if (side == null) return;

            var isAiFix = field == null;
            var fieldKey = isAiFix ? "ai_fix" : field.field_key;
            var fieldName = isAiFix ? "是否AI修复" : field.field_name;

            // 设置 key 标签
            var keyText = side.Find("key")?.GetComponent<Text>();
            if (keyText != null) {
                keyText.text = fieldName + ":";
            }

            // 填充 Dropdown 选项
            var dropdown = side.Find("Dropdown")?.GetComponent<Dropdown>();
            if (dropdown != null) {
                dropdown.ClearOptions();
                if (isAiFix) {
                    dropdown.AddOptions(new List<string> { "False", "True" });
                    dropdown.value = 1;
                } else {
                    var options = new List<string>();
                    foreach (var opt in field.options) {
                        options.Add(opt.label);
                    }
                    dropdown.AddOptions(options);
                    dropdown.value = 0;
                }
                dropdown.RefreshShownValue();
                _dynamicDropdowns[fieldKey] = dropdown;
            }
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

        // ── GetScreenShot_Btn: 多截图 / 相册 ──

        private int GetScreenshotCount() {
            return BugReporterSDK.GetScreenshotCollector()?.Count ?? 0;
        }

        private void OnScreenshotBtnClicked() {
            if (GetScreenshotCount() == 0) {
                CaptureNewScreenshot();
            } else {
                ShowScreenshotGallery();
            }
        }

        private void CaptureNewScreenshot() {
            if (_screenshotBtn != null) _screenshotBtn.interactable = false;
            _panelInstance.SetActive(false);
            // 面板被隐藏后 StartCoroutine 不能在自身执行，借用 SDK 的 MonoBehaviour
            BugReporterSDK.GetInstance().StartCoroutine(DoCaptureScreenshot());
        }

        private IEnumerator DoCaptureScreenshot() {
            var screenshotCollector = BugReporterSDK.GetScreenshotCollector();
            if (screenshotCollector is { IsEnabled: true }) {
                yield return screenshotCollector.CaptureScreenshot();
                RefreshScreenshotCache();
            }

            // 重新显示面板
            _panelInstance.SetActive(true);

            // 若相册已打开，刷新其内容
            if (_screenshotGallery != null) {
                RebuildGallery();
            }

            if (_screenshotBtn != null) {
                _screenshotBtn.interactable = true;
            }
        }

        /// <summary>
        /// 将采集器当前的所有截图同步到 _cachedFiles（清理旧的 screenshot_* / screenshot 键后重建）。
        /// </summary>
        private void RefreshScreenshotCache() {
            _cachedFiles ??= new Dictionary<string, byte[]>();

            var keysToRemove = new List<string>();
            foreach (var key in _cachedFiles.Keys) {
                if (key == "screenshot" || key.StartsWith("screenshot_")) {
                    keysToRemove.Add(key);
                }
            }
            foreach (var key in keysToRemove) _cachedFiles.Remove(key);

            var collector = BugReporterSDK.GetScreenshotCollector();
            if (collector != null) {
                var result = collector.Collect();
                if (result.Files != null) {
                    foreach (var kv in result.Files) {
                        _cachedFiles[kv.Key] = kv.Value;
                    }
                }
            }

            UpdateScreenshotBtnText();
        }

        private void UpdateScreenshotBtnText() {
            if (_screenshotBtnText == null) return;
            var count = GetScreenshotCount();
            _screenshotBtnText.text = count > 0 ? $"截图预览({count})" : "获取截图";
        }

        private void ShowScreenshotGallery() {
            if (_screenshotGallery != null) {
                RebuildGallery();
                return;
            }

            // 全屏遮罩
            var galleryGo = new GameObject("ScreenshotGallery", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            galleryGo.transform.SetParent(_panelInstance.transform, false);
            var galleryRt = galleryGo.GetComponent<RectTransform>();
            galleryRt.anchorMin = Vector2.zero;
            galleryRt.anchorMax = Vector2.one;
            galleryRt.offsetMin = Vector2.zero;
            galleryRt.offsetMax = Vector2.zero;
            galleryGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.92f);

            // 标题
            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            titleGo.transform.SetParent(galleryGo.transform, false);
            var titleRt = titleGo.GetComponent<RectTransform>();
            titleRt.anchorMin = new Vector2(0, 1);
            titleRt.anchorMax = new Vector2(1, 1);
            titleRt.pivot = new Vector2(0.5f, 1);
            titleRt.anchoredPosition = new Vector2(0, -20);
            titleRt.sizeDelta = new Vector2(0, 60);
            var titleText = titleGo.GetComponent<Text>();
            titleText.text = "截图相册";
            titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            titleText.fontSize = 40;
            titleText.color = Color.white;
            titleText.alignment = TextAnchor.MiddleCenter;

            // ScrollRect
            var scrollGo = new GameObject("Scroll", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(ScrollRect));
            scrollGo.transform.SetParent(galleryGo.transform, false);
            var scrollRt = scrollGo.GetComponent<RectTransform>();
            scrollRt.anchorMin = new Vector2(0.05f, 0.18f);
            scrollRt.anchorMax = new Vector2(0.95f, 0.88f);
            scrollRt.offsetMin = Vector2.zero;
            scrollRt.offsetMax = Vector2.zero;
            scrollGo.GetComponent<Image>().color = new Color(0.1f, 0.1f, 0.1f, 0.6f);
            var scroll = scrollGo.GetComponent<ScrollRect>();

            var viewportGo = new GameObject("Viewport", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Mask));
            viewportGo.transform.SetParent(scrollGo.transform, false);
            var viewportRt = viewportGo.GetComponent<RectTransform>();
            viewportRt.anchorMin = Vector2.zero;
            viewportRt.anchorMax = Vector2.one;
            viewportRt.offsetMin = Vector2.zero;
            viewportRt.offsetMax = Vector2.zero;
            viewportGo.GetComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            viewportGo.GetComponent<Mask>().showMaskGraphic = false;

            var contentGo = new GameObject("Content", typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            contentGo.transform.SetParent(viewportGo.transform, false);
            var contentRt = contentGo.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0, 1);
            contentRt.anchorMax = new Vector2(1, 1);
            contentRt.pivot = new Vector2(0.5f, 1);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0, 0);
            var grid = contentGo.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(400, 240); // 横屏游戏：5:3 比例的单元格
            grid.spacing = new Vector2(20, 20);
            grid.padding = new RectOffset(20, 20, 20, 20);
            grid.childAlignment = TextAnchor.UpperLeft;
            var sizeFitter = contentGo.GetComponent<ContentSizeFitter>();
            sizeFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            scroll.content = contentRt;
            scroll.viewport = viewportRt;
            scroll.horizontal = false;
            scroll.vertical = true;

            _galleryContent = contentGo.transform;

            // 关闭按钮
            var closeBtnGo = CreateTextButton(galleryGo.transform, "X", new Color(0.8f, 0.2f, 0.2f, 1f), Color.white, 36);
            var closeBtnRt = closeBtnGo.GetComponent<RectTransform>();
            closeBtnRt.anchorMin = new Vector2(1, 1);
            closeBtnRt.anchorMax = new Vector2(1, 1);
            closeBtnRt.pivot = new Vector2(1, 1);
            closeBtnRt.anchoredPosition = new Vector2(-20, -20);
            closeBtnRt.sizeDelta = new Vector2(80, 80);
            closeBtnGo.GetComponent<Button>().onClick.AddListener(CloseScreenshotGallery);

            // 添加截图按钮
            var addBtnGo = CreateTextButton(galleryGo.transform, "+ 添加截图", new Color(0.2f, 0.6f, 0.2f, 1f), Color.white, 32);
            var addBtnRt = addBtnGo.GetComponent<RectTransform>();
            addBtnRt.anchorMin = new Vector2(0.5f, 0);
            addBtnRt.anchorMax = new Vector2(0.5f, 0);
            addBtnRt.pivot = new Vector2(0.5f, 0);
            addBtnRt.anchoredPosition = new Vector2(0, 30);
            addBtnRt.sizeDelta = new Vector2(320, 90);
            addBtnGo.GetComponent<Button>().onClick.AddListener(CaptureNewScreenshot);

            _screenshotGallery = galleryGo;
            RebuildGallery();
        }

        private void RebuildGallery() {
            if (_galleryContent == null) return;

            // 释放旧缩略图纹理
            foreach (var tex in _galleryTextures) {
                if (tex != null) Destroy(tex);
            }
            _galleryTextures.Clear();

            for (var i = _galleryContent.childCount - 1; i >= 0; i--) {
                Destroy(_galleryContent.GetChild(i).gameObject);
            }

            var collector = BugReporterSDK.GetScreenshotCollector();
            if (collector == null) return;

            for (var i = 0; i < collector.Count; i++) {
                var index = i;
                var pngBytes = collector.Screenshots[i];
                if (pngBytes == null) continue;

                // 单元格
                var cellGo = new GameObject($"Thumb_{i}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                cellGo.transform.SetParent(_galleryContent, false);
                cellGo.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
                cellGo.GetComponent<Button>().onClick.AddListener(() => ShowFullscreenPreview(pngBytes));

                // 缩略图 —— 用 AspectRatioFitter 保持截图真实比例（横屏游戏自动展示为横向）
                var rawImgGo = new GameObject("Image",
                    typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
                rawImgGo.transform.SetParent(cellGo.transform, false);
                var rawImgRt = rawImgGo.GetComponent<RectTransform>();
                rawImgRt.anchorMin = Vector2.zero;
                rawImgRt.anchorMax = Vector2.one;
                rawImgRt.offsetMin = new Vector2(5, 5);
                rawImgRt.offsetMax = new Vector2(-5, -5);
                var tex = new Texture2D(2, 2);
                var fitter = rawImgGo.GetComponent<AspectRatioFitter>();
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                if (tex.LoadImage(pngBytes) && tex.height > 0) {
                    rawImgGo.GetComponent<RawImage>().texture = tex;
                    fitter.aspectRatio = (float)tex.width / tex.height;
                    _galleryTextures.Add(tex);
                }

                // 删除按钮
                var delBtnGo = CreateTextButton(cellGo.transform, "×", new Color(0.85f, 0.2f, 0.2f, 1f), Color.white, 32);
                var delBtnRt = delBtnGo.GetComponent<RectTransform>();
                delBtnRt.anchorMin = new Vector2(1, 1);
                delBtnRt.anchorMax = new Vector2(1, 1);
                delBtnRt.pivot = new Vector2(1, 1);
                delBtnRt.anchoredPosition = new Vector2(-5, -5);
                delBtnRt.sizeDelta = new Vector2(55, 55);
                delBtnGo.GetComponent<Button>().onClick.AddListener(() => DeleteScreenshot(index));

                // 序号
                var numGo = new GameObject("Number", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
                numGo.transform.SetParent(cellGo.transform, false);
                var numRt = numGo.GetComponent<RectTransform>();
                numRt.anchorMin = new Vector2(0, 0);
                numRt.anchorMax = new Vector2(0, 0);
                numRt.pivot = new Vector2(0, 0);
                numRt.anchoredPosition = new Vector2(10, 10);
                numRt.sizeDelta = new Vector2(80, 40);
                var numText = numGo.GetComponent<Text>();
                numText.text = $"#{i + 1}";
                numText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                numText.fontSize = 28;
                numText.color = Color.white;
                numText.alignment = TextAnchor.LowerLeft;
            }
        }

        private void DeleteScreenshot(int index) {
            var collector = BugReporterSDK.GetScreenshotCollector();
            if (collector == null) return;
            collector.RemoveAt(index);
            RefreshScreenshotCache();
            if (collector.Count == 0) {
                CloseScreenshotGallery();
            } else {
                RebuildGallery();
            }
        }

        private void ShowFullscreenPreview(byte[] png) {
            if (_fullscreenPreview != null || png == null) return;

            var root = _screenshotGallery != null ? _screenshotGallery.transform : _panelInstance.transform;
            var previewGo = new GameObject("FullscreenPreview", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            previewGo.transform.SetParent(root, false);
            var previewRt = previewGo.GetComponent<RectTransform>();
            previewRt.anchorMin = Vector2.zero;
            previewRt.anchorMax = Vector2.one;
            previewRt.offsetMin = Vector2.zero;
            previewRt.offsetMax = Vector2.zero;
            previewGo.GetComponent<Image>().color = new Color(0, 0, 0, 0.96f);

            var rawImgGo = new GameObject("Image",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
            rawImgGo.transform.SetParent(previewGo.transform, false);
            var rawImgRt = rawImgGo.GetComponent<RectTransform>();
            rawImgRt.anchorMin = new Vector2(0.05f, 0.1f);
            rawImgRt.anchorMax = new Vector2(0.95f, 0.9f);
            rawImgRt.offsetMin = Vector2.zero;
            rawImgRt.offsetMax = Vector2.zero;
            var tex = new Texture2D(2, 2);
            var previewFitter = rawImgGo.GetComponent<AspectRatioFitter>();
            previewFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            if (tex.LoadImage(png) && tex.height > 0) {
                rawImgGo.GetComponent<RawImage>().texture = tex;
                previewFitter.aspectRatio = (float)tex.width / tex.height;
            }

            var closeBtnGo = CreateTextButton(previewGo.transform, "X", new Color(0.8f, 0.2f, 0.2f, 1f), Color.white, 36);
            var closeBtnRt = closeBtnGo.GetComponent<RectTransform>();
            closeBtnRt.anchorMin = new Vector2(1, 1);
            closeBtnRt.anchorMax = new Vector2(1, 1);
            closeBtnRt.pivot = new Vector2(1, 1);
            closeBtnRt.anchoredPosition = new Vector2(-20, -20);
            closeBtnRt.sizeDelta = new Vector2(80, 80);
            closeBtnGo.GetComponent<Button>().onClick.AddListener(CloseFullscreenPreview);

            _fullscreenPreview = previewGo;
        }

        private void CloseFullscreenPreview() {
            if (_fullscreenPreview == null) return;
            var rawImage = _fullscreenPreview.GetComponentInChildren<RawImage>();
            if (rawImage != null && rawImage.texture is Texture2D tex) {
                Destroy(tex);
            }
            Destroy(_fullscreenPreview);
            _fullscreenPreview = null;
        }

        private void CloseScreenshotGallery() {
            CloseFullscreenPreview();
            foreach (var tex in _galleryTextures) {
                if (tex != null) Destroy(tex);
            }
            _galleryTextures.Clear();
            if (_screenshotGallery != null) {
                Destroy(_screenshotGallery);
                _screenshotGallery = null;
                _galleryContent = null;
            }
            UpdateScreenshotBtnText();
        }

        private static GameObject CreateTextButton(Transform parent, string label, Color bgColor, Color textColor, int fontSize) {
            var btnGo = new GameObject("Btn", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            btnGo.transform.SetParent(parent, false);
            btnGo.GetComponent<Image>().color = bgColor;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGo.transform.SetParent(btnGo.transform, false);
            var textRt = textGo.GetComponent<RectTransform>();
            textRt.anchorMin = Vector2.zero;
            textRt.anchorMax = Vector2.one;
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;
            var text = textGo.GetComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.color = textColor;
            text.alignment = TextAnchor.MiddleCenter;

            return btnGo;
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
            report.Fields["issueDec"] = "操作步骤：\n1.\n实际结果：\n\n期望结果：\n";

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

            Hide();
        }

        // ── UI 填充 ──

        private void PopulateInfoList(Dictionary<string, string> fields) {
            if (_contentParent == null || _infoItemTemplate == null) return;

            // 收集需要保留的 GameObject
            var keepSet = new HashSet<GameObject> {
                _infoItemTemplate, _dropdownTemplate,
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
