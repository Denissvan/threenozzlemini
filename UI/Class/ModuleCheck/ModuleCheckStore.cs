using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace UI.ModuleCheck
{
    /// <summary>
    /// 持久化：..\syscfg\ModuleCheck\
    ///   settings.json     设置
    ///   plans.json        已处理计划登记
    ///   active_task.json  未完成任务（每次状态变化、取放前后提交）
    ///   history\任务号.json 已结束任务归档
    /// 写文件先写 .tmp 再替换，断电时不会留下半个文件。
    /// </summary>
    public static class ModuleCheckStore
    {
        private static readonly object ioLock = new object();

        public static string Dir
        {
            get { return Path.Combine(Path.GetFullPath(".."), "syscfg", "ModuleCheck"); }
        }

        private static string SettingsPath { get { return Path.Combine(Dir, "settings.json"); } }
        private static string RegistryPath { get { return Path.Combine(Dir, "plans.json"); } }
        private static string ActiveTaskPath { get { return Path.Combine(Dir, "active_task.json"); } }
        private static string HistoryDir { get { return Path.Combine(Dir, "history"); } }

        private static JsonSerializerSettings JsonSettings()
        {
            var s = new JsonSerializerSettings();
            s.Formatting = Formatting.Indented;
            s.Converters.Add(new StringEnumConverter());
            s.ObjectCreationHandling = ObjectCreationHandling.Replace;
            return s;
        }

        public static ModuleCheckSettings LoadSettings()
        {
            var s = Load<ModuleCheckSettings>(SettingsPath);
            return s ?? new ModuleCheckSettings();
        }

        public static bool SaveSettings(ModuleCheckSettings s, out string err)
        {
            return Save(SettingsPath, s, out err);
        }

        public static ModuleCheckPlanRegistry LoadRegistry()
        {
            return Load<ModuleCheckPlanRegistry>(RegistryPath) ?? new ModuleCheckPlanRegistry();
        }

        public static bool SaveRegistry(ModuleCheckPlanRegistry r, out string err)
        {
            return Save(RegistryPath, r, out err);
        }

        public static ModuleCheckTask LoadActiveTask()
        {
            return Load<ModuleCheckTask>(ActiveTaskPath);
        }

        public static bool SaveActiveTask(ModuleCheckTask t, out string err)
        {
            return Save(ActiveTaskPath, t, out err);
        }

        /// <summary>任务结束：归档到 history 并删除 active_task.json</summary>
        public static bool ArchiveTask(ModuleCheckTask t, out string err)
        {
            err = "";
            try
            {
                lock (ioLock)
                {
                    Directory.CreateDirectory(HistoryDir);
                }
                string name = string.IsNullOrEmpty(t.TaskId) ? DateTime.Now.ToString("yyyyMMddHHmmss") : t.TaskId;
                if (!Save(Path.Combine(HistoryDir, name + ".json"), t, out err)) return false;
                lock (ioLock)
                {
                    if (File.Exists(ActiveTaskPath)) File.Delete(ActiveTaskPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                return false;
            }
        }

        private static T Load<T>(string path) where T : class
        {
            try
            {
                lock (ioLock)
                {
                    if (!File.Exists(path)) return null;
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    return JsonConvert.DeserializeObject<T>(json, JsonSettings());
                }
            }
            catch (Exception ex)
            {
                ModuleCheckLog.Error(ModuleCheckLogCat.Config, null, string.Format("读取 {0} 失败：{1}", path, ex.Message));
                return null;
            }
        }

        private static bool Save(string path, object obj, out string err)
        {
            err = "";
            try
            {
                string json = JsonConvert.SerializeObject(obj, JsonSettings());
                lock (ioLock)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    string tmp = path + ".tmp";
                    File.WriteAllText(tmp, json, new UTF8Encoding(true));
                    if (File.Exists(path)) File.Replace(tmp, path, null);
                    else File.Move(tmp, path);
                }
                return true;
            }
            catch (Exception ex)
            {
                err = ex.Message;
                ModuleCheckLog.Error(ModuleCheckLogCat.Config, null, string.Format("保存 {0} 失败：{1}", path, ex.Message));
                return false;
            }
        }
    }
}
