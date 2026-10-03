namespace GhostHunter.Systems.Settings
{
    /// <summary>설정 값을 남기는 곳. 실제로는 PlayerPrefs, 테스트에서는 메모리.</summary>
    public interface IUserSettingsStorage
    {
        float GetFloat(string key, float defaultValue);
        void SetFloat(string key, float value);
        int GetInt(string key, int defaultValue);
        void SetInt(string key, int value);
        void Save();
    }
}
