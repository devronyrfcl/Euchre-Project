using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.UI;

public class VolumeSettings : MonoBehaviour
{
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;

    private const string MusicVolumeKey = "musicVolume";
    private const string SfxVolumeKey = "sfxVolume";

    private void Start()
    {
        if (PlayerPrefs.HasKey(MusicVolumeKey))
        {
            LoadVolume();
        }
        else
        {
            SetDefaultVolumes();
        }
    }

    public void SetMusicVolume()
    {
        float volume = musicSlider.value;
        audioMixer.SetFloat("music", Mathf.Log10(volume) * 20);
        PlayerPrefs.SetFloat(MusicVolumeKey, volume);
    }

    public void SetsfxVolume()
    {
        float volume = sfxSlider.value;
        audioMixer.SetFloat("sfx", Mathf.Log10(volume) * 20);
        PlayerPrefs.SetFloat(SfxVolumeKey, volume);
    }

    private void LoadVolume()
    {
        musicSlider.value = PlayerPrefs.GetFloat(MusicVolumeKey);
        SetMusicVolume();
        sfxSlider.value = PlayerPrefs.GetFloat(SfxVolumeKey);
        SetsfxVolume();
    }

    private void SetDefaultVolumes()
    {
        // Set default volume values or any other initialization logic here
        SetMusicVolume();
        SetsfxVolume();
    }
}
