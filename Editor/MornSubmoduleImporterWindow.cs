using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MornSubmoduleImporter
{
    public sealed class MornSubmoduleImporterWindow : EditorWindow
    {
        private class SubmoduleInfo
        {
            public string Url { get; set; }
            public string Name { get; set; }
            public bool IsInstalled { get; set; }
            public bool IsSelected { get; set; }
        }

        private List<SubmoduleInfo> _submodules = new List<SubmoduleInfo>();
        private Vector2 _scrollPosition;
        private bool _isProcessing;
        private string _currentProcessingModule;
        private float _progress;
        private bool _showInstalled = true;
        private bool _showNotInstalled = true;

        [MenuItem("Tools/Morn Submodule Importer")]
        private static void ShowWindow()
        {
            var window = GetWindow<MornSubmoduleImporterWindow>();
            window.titleContent = new GUIContent("Morn Submodule Importer");
            window.minSize = new Vector2(600, 400);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshSubmoduleList();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            // ヘッダー
            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Label("Morn Submodule Manager", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Refresh", GUILayout.Width(80)))
                {
                    RefreshSubmoduleList();
                }
            }

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // プログレスバー表示
            if (_isProcessing)
            {
                EditorGUILayout.Space(5);
                EditorGUI.ProgressBar(
                    EditorGUILayout.GetControlRect(GUILayout.Height(20)),
                    _progress,
                    $"Processing: {_currentProcessingModule}"
                );
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
            }

            // Submodule一覧
            using (var scrollView = new EditorGUILayout.ScrollViewScope(_scrollPosition))
            {
                _scrollPosition = scrollView.scrollPosition;

                if (_submodules.Count == 0)
                {
                    EditorGUILayout.HelpBox("submodule.txt が見つからないか、内容が空です。", MessageType.Warning);
                }
                else
                {
                    // ヘッダー行
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label("Select", GUILayout.Width(50));
                        GUILayout.Label("Repository", GUILayout.Width(200));
                        GUILayout.Label("URL", GUILayout.MinWidth(200));
                        GUILayout.Label("Status", GUILayout.Width(80));
                    }

                    EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

                    // 各Submodule行
                    foreach (var submodule in _submodules)
                    {
                        // フィルタリング
                        if (submodule.IsInstalled && !_showInstalled)
                            continue;
                        if (!submodule.IsInstalled && !_showNotInstalled)
                            continue;

                        using (new EditorGUILayout.HorizontalScope())
                        {
                            // チェックボックス（インストール済みの場合は無効化）
                            using (new EditorGUI.DisabledScope(submodule.IsInstalled || _isProcessing))
                            {
                                submodule.IsSelected = EditorGUILayout.Toggle(
                                    submodule.IsSelected,
                                    GUILayout.Width(50)
                                );
                            }

                            // リポジトリ名
                            GUILayout.Label(submodule.Name, GUILayout.Width(200));

                            // URL
                            GUILayout.Label(submodule.Url, GUILayout.MinWidth(200));

                            // ステータス
                            var statusStyle = new GUIStyle(GUI.skin.label);
                            if (submodule.IsInstalled)
                            {
                                statusStyle.normal.textColor = Color.green;
                                GUILayout.Label("Installed", statusStyle, GUILayout.Width(80));
                            }
                            else
                            {
                                statusStyle.normal.textColor = Color.gray;
                                GUILayout.Label("Not Installed", statusStyle, GUILayout.Width(80));
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);

            // フィルターとアクションボタン
            using (new EditorGUILayout.HorizontalScope())
            {
                // フィルターオプション（左側）
                GUILayout.Label("Filter:", GUILayout.Width(50));
                _showInstalled = EditorGUILayout.ToggleLeft("Show Installed", _showInstalled, GUILayout.Width(110));
                GUILayout.Space(10);
                _showNotInstalled = EditorGUILayout.ToggleLeft("Show Not Installed", _showNotInstalled, GUILayout.Width(130));

                GUILayout.FlexibleSpace();

                // アクションボタン（右側）
                var selectedCount = _submodules.Count(s => s.IsSelected && !s.IsInstalled);
                using (new EditorGUI.DisabledScope(selectedCount == 0 || _isProcessing))
                {
                    if (GUILayout.Button($"Add Selected Submodules ({selectedCount})", GUILayout.Width(200), GUILayout.Height(30)))
                    {
                        AddSelectedSubmodules();
                    }
                }
            }

            EditorGUILayout.Space(5);
        }

        private void RefreshSubmoduleList()
        {
            _submodules.Clear();

            // submodule.txtのパスを取得
            var submoduleTxtPath = Path.Combine(Application.dataPath, "_Morn", "MornSubmoduleImporter", "submodule.txt");

            if (!File.Exists(submoduleTxtPath))
            {
                Debug.LogWarning($"submodule.txt が見つかりません: {submoduleTxtPath}");
                return;
            }

            // ファイルを読み込み
            var lines = File.ReadAllLines(submoduleTxtPath);

            // 既存のsubmoduleを取得
            var installedSubmodules = GetInstalledSubmodules();

            foreach (var line in lines)
            {
                var trimmedLine = line.Trim();
                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#"))
                    continue;

                var repoName = GetRepositoryNameFromUrl(trimmedLine);
                var submodulePath = Path.Combine("Assets", "_Morn", repoName);

                _submodules.Add(new SubmoduleInfo
                {
                    Url = trimmedLine,
                    Name = repoName,
                    IsInstalled = installedSubmodules.Contains(submodulePath),
                    IsSelected = false
                });
            }
        }

        private HashSet<string> GetInstalledSubmodules()
        {
            var result = new HashSet<string>();

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = "submodule status",
                        WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                // git submodule statusの出力をパース
                var lines = output.Split('\n');
                foreach (var line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    // 形式: " 123abc... path/to/submodule (branch)"
                    var parts = line.Trim().Split(' ');
                    if (parts.Length >= 2)
                    {
                        var path = parts[1].Trim();
                        result.Add(path);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"既存のsubmodule取得に失敗しました: {ex.Message}");
            }

            return result;
        }

        private string GetRepositoryNameFromUrl(string url)
        {
            // URLから.gitを除去してリポジトリ名を取得
            var name = url.Split('/').Last().Replace(".git", "");
            return name;
        }

        private async void AddSelectedSubmodules()
        {
            var selectedModules = _submodules.Where(s => s.IsSelected && !s.IsInstalled).ToList();
            if (selectedModules.Count == 0)
                return;

            _isProcessing = true;
            _progress = 0f;

            try
            {
                for (int i = 0; i < selectedModules.Count; i++)
                {
                    var submodule = selectedModules[i];
                    _currentProcessingModule = submodule.Name;
                    _progress = (float)i / selectedModules.Count;

                    // プログレスバーを更新
                    Repaint();

                    var success = await AddSubmodule(submodule);
                    if (!success)
                    {
                        EditorUtility.DisplayDialog(
                            "エラー",
                            $"{submodule.Name} の追加に失敗しました。詳細はConsoleを確認してください。",
                            "OK"
                        );
                        break;
                    }
                }

                _progress = 1f;

                // 完了通知
                EditorUtility.DisplayDialog(
                    "完了",
                    $"{selectedModules.Count} 個のsubmoduleを追加しました。",
                    "OK"
                );

                // リストを更新
                RefreshSubmoduleList();

                // Unityのアセットデータベースを更新
                AssetDatabase.Refresh();
            }
            finally
            {
                _isProcessing = false;
                _currentProcessingModule = "";
            }
        }

        private async System.Threading.Tasks.Task<bool> AddSubmodule(SubmoduleInfo submodule)
        {
            var submodulePath = Path.Combine("Assets", "_Morn", submodule.Name);
            var fullPath = Path.Combine(Application.dataPath, "_Morn", submodule.Name);

            // 既にディレクトリが存在する場合はエラー
            if (Directory.Exists(fullPath))
            {
                EditorUtility.DisplayDialog(
                    "エラー",
                    $"{submodule.Name} のディレクトリが既に存在します。",
                    "OK"
                );
                return false;
            }

            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "git",
                        Arguments = $"submodule add {submodule.Url} {submodulePath}",
                        WorkingDirectory = Path.GetDirectoryName(Application.dataPath),
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                // 非同期で待機
                await System.Threading.Tasks.Task.Run(() => process.WaitForExit());

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                if (process.ExitCode != 0)
                {
                    Debug.LogError($"Submodule追加エラー ({submodule.Name}): {error}");
                    return false;
                }

                Debug.Log($"Submodule追加成功: {submodule.Name}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Submodule追加中に例外が発生しました ({submodule.Name}): {ex.Message}");
                return false;
            }
        }
    }
}