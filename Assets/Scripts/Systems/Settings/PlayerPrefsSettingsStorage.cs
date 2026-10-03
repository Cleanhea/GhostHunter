using UnityEngine;

namespace GhostHunter.Systems.Settings
{
    /// <summary>PlayerPrefs 에 설정을 남긴다.</summary>
    public sealed class PlayerPrefsSettingsStorage : IUserSettingsStorage
    {
        public float GetFloat(string key, float defaultValue) => PlayerPrefs.GetFloat(key, defaultValue);
        public void SetFloat(string key, float value) => PlayerPrefs.SetFloat(key, value);
        public int GetInt(string key, int defaultValue) => PlayerPrefs.GetInt(key, defaultValue);
        public void SetInt(string key, int value) => PlayerPrefs.SetInt(key, value);
        public void Save() => PlayerPrefs.Save();
    }
}
