using UnityEngine;
using UnityEngine.UI;

namespace JoyCastle.BugReporter {
    /// <summary>
    /// Bug 反馈面板。
    /// 从 Resources/BugReporter/BugReportPanel prefab 加载 UI，
    /// prefab 中需要包含以下节点名（通过 transform.Find 查找）：
    ///   - "InputField"  : 挂有 InputField 组件
    ///   - "SubmitButton" : 挂有 Button 组件
    ///   - "CancelButton" : 挂有 Button 组件
    /// 也可以通过 SetPrefab() 指定自定义 prefab，跳过 Resources 加载。
    /// </summary>
    public class BugReportPanel : MonoBehaviour {
        private const string DefaultPrefabPath = "BugReporter/BugReportPanel";

        private GameObject _panelInstance;
        private InputField _inputField;
        private GameObject _customPrefab;

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
        }

        public void Hide() {
            if (_panelInstance != null) {
                Destroy(_panelInstance);
                _panelInstance = null;
                _inputField = null;
            }
        }

        private void BindUI() {
            // 查找 InputField
            var inputFieldTr = _panelInstance.transform.Find("InputField");
            if (inputFieldTr != null) {
                _inputField = inputFieldTr.GetComponent<InputField>();
            }
            if (_inputField == null) {
                // 递归查找兜底
                _inputField = _panelInstance.GetComponentInChildren<InputField>();
            }

            // 查找并绑定提交按钮
            BindButton("SubmitButton", OnSubmit);

            // 查找并绑定取消按钮
            BindButton("CancelButton", OnCancel);
        }

        private void BindButton(string name, UnityEngine.Events.UnityAction action) {
            var tr = _panelInstance.transform.Find(name);
            Button btn = null;
            if (tr != null) {
                btn = tr.GetComponent<Button>();
            }
            if (btn == null) {
                Debug.LogWarning($"[BugReporter] Button '{name}' not found in panel prefab.");
                return;
            }
            btn.onClick.AddListener(action);
        }

        private void OnSubmit() {
            var desc = _inputField != null ? _inputField.text : "";
            Hide();
            BugReporterSDK.SubmitSilently(desc);
        }

        private void OnCancel() {
            Hide();
        }
    }
}
