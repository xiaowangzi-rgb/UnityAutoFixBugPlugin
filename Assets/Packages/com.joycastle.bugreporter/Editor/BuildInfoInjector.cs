using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace JoyCastle.BugReporter.Editor {
    public class BuildInfoInjector : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) {
            GenerateBuildInfo();
        }

        public static void GenerateBuildInfo() {
            var info = new BuildInfoData {
                gitBranch = GetGitBranch(),
                gitCommit = GetGitCommit(),
                buildNumber = GetBuildNumber(),
                buildTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss"),
                submoduleBranches = GetSubmoduleBranches(),
            };

            var dir = Path.Combine(Application.dataPath,
                "Packages/com.joycastle.bugreporter/Resources/BugReporter");
            Directory.CreateDirectory(dir);

            var json = JsonUtility.ToJson(info, true);
            File.WriteAllText(Path.Combine(dir, "BuildInfo.json"), json);
            AssetDatabase.Refresh();

            Debug.Log($"[BugReporter] BuildInfo injected: branch={info.gitBranch}, commit={info.gitCommit}, submodules={info.submoduleBranches}");
        }

        private static string GetGitBranch() {
            // 优先读 Jenkins 环境变量
            var env = Environment.GetEnvironmentVariable("GIT_BRANCH");
            if (!string.IsNullOrEmpty(env)) return env;
            // fallback: git 命令
            return RunGit("rev-parse --abbrev-ref HEAD");
        }

        private static string GetGitCommit() {
            var env = Environment.GetEnvironmentVariable("GIT_COMMIT");
            if (!string.IsNullOrEmpty(env)) return env;
            return RunGit("rev-parse --short HEAD");
        }

        private static string GetBuildNumber() {
            return Environment.GetEnvironmentVariable("BUILD_NUMBER") ?? "0";
        }

        /// <summary>
        /// 获取所有 git 子模块的最近一次 commit id。
        /// 格式: "子模块名:commitId" 多个以逗号分隔，如 "configRepo:a1b2c3d,dataRepo:e4f5g6h"
        /// </summary>
        private static string GetSubmoduleBranches() {
            try {
                var psi = new ProcessStartInfo("git",
                    "submodule foreach --quiet \"echo $name:$(git rev-parse --short HEAD)\"") {
                    WorkingDirectory = Application.dataPath,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode != 0 || string.IsNullOrEmpty(output)) return "";
                // 每行一个子模块，合并为逗号分隔
                return output.Replace("\r\n", ",").Replace("\n", ",");
            } catch {
                return "";
            }
        }

        private static string RunGit(string args) {
            try {
                var psi = new ProcessStartInfo("git", args) {
                    WorkingDirectory = Application.dataPath,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };
                using var process = Process.Start(psi);
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 ? output : "unknown";
            } catch {
                return "unknown";
            }
        }

        [Serializable]
        private class BuildInfoData {
            public string gitBranch;
            public string gitCommit;
            public string buildNumber;
            public string buildTime;
            public string submoduleBranches;
        }
    }
}
