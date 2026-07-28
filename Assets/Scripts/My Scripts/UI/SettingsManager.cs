using UnityEngine;
using UnityEngine.UI;

public class SettingsManager : MonoBehaviour
{
    public Slider musicSlider;

    private const string MUSIC_VOLUME_KEY = "MusicVolume";
    private const string MUSIC_MUTED_KEY = "MusicMuted";
    private const string SFX_MUTED_KEY = "SFXMuted";
    private const string VIBRATION_KEY = "VibrationEnabled";

    public bool IsVibrationEnabled { get; private set; } = true;

    private void Start()
    {
        LoadSettings();

        if (musicSlider != null)
        {
            musicSlider.onValueChanged.AddListener(SetMusicVolume);
        }
    }

    public void MusicOn()
    {
        AudioManager.Instance.musicSource.mute = false;
        PlayerPrefs.SetInt(MUSIC_MUTED_KEY, 0);
        PlayerPrefs.Save();
    }

    public void MusicOff()
    {
        AudioManager.Instance.musicSource.mute = true;
        PlayerPrefs.SetInt(MUSIC_MUTED_KEY, 1);
        PlayerPrefs.Save();
    }

    public void SFXOn()
    {
        AudioManager.Instance.sfxSource.mute = false;
        PlayerPrefs.SetInt(SFX_MUTED_KEY, 0);
        PlayerPrefs.Save();
    }

    public void SFXOff()
    {
        AudioManager.Instance.sfxSource.mute = true;
        PlayerPrefs.SetInt(SFX_MUTED_KEY, 1);
        PlayerPrefs.Save();
    }

    public void VibrationOn()
    {
        IsVibrationEnabled = true;
        PlayerPrefs.SetInt(VIBRATION_KEY, 1);
        PlayerPrefs.Save();
    }

    public void VibrationOff()
    {
        IsVibrationEnabled = false;
        PlayerPrefs.SetInt(VIBRATION_KEY, 0);
        PlayerPrefs.Save();
    }

    public void SetMusicVolume(float volume)
    {
        AudioManager.Instance.musicSource.volume = volume;
        PlayerPrefs.SetFloat(MUSIC_VOLUME_KEY, volume);
        PlayerPrefs.Save();
    }

    private void LoadSettings()
    {
        float savedVolume = PlayerPrefs.GetFloat(MUSIC_VOLUME_KEY, 1f);
        AudioManager.Instance.musicSource.volume = savedVolume;
        if (musicSlider != null)
        {
            musicSlider.value = savedVolume;
        }

        bool musicMuted = PlayerPrefs.GetInt(MUSIC_MUTED_KEY, 0) == 1;
        AudioManager.Instance.musicSource.mute = musicMuted;

        bool sfxMuted = PlayerPrefs.GetInt(SFX_MUTED_KEY, 0) == 1;
        AudioManager.Instance.sfxSource.mute = sfxMuted;

        IsVibrationEnabled = PlayerPrefs.GetInt(VIBRATION_KEY, 1) == 1;
    }
}