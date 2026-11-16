using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace SaruTask
{
    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public List<string> Roots = new List<string>();
        public static string DataDirectory { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaruTask"); } }
        public static string SettingsPath { get { return Path.Combine(DataDirectory, "settings.json"); } }

        public static AppSettings Defaults()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            return new AppSettings { Roots = new List<string> {
                Path.Combine(desktop, "!!!重要なフォルダ", "!2026業務", "01_active"),
                Path.Combine(desktop, "2026", "task_hub", "2026", "01_active")
            } };
        }

        public static AppSettings Load(out string warning)
        {
            warning = null;
            if (!File.Exists(SettingsPath)) return Defaults();
            try
            {
                using (var stream = File.OpenRead(SettingsPath))
                {
                    var result = (AppSettings)new DataContractJsonSerializer(typeof(AppSettings)).ReadObject(stream);
                    if (result == null || result.Roots == null) throw new InvalidDataException("検索対象の設定がありません。");
                    result.Roots = result.Roots.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                    return result;
                }
            }
            catch (Exception ex) { warning = "設定を読み込めないため初期設定で起動しました: " + ex.Message; return Defaults(); }
        }

        public void Save()
        {
            Directory.CreateDirectory(DataDirectory);
            string temp = SettingsPath + ".tmp";
            using (var stream = File.Create(temp)) new DataContractJsonSerializer(typeof(AppSettings)).WriteObject(stream, this);
            if (File.Exists(SettingsPath)) File.Replace(temp, SettingsPath, SettingsPath + ".bak");
            else File.Move(temp, SettingsPath);
        }
    }
}
