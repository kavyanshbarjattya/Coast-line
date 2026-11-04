using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Networking;
using TMPro;

[System.Serializable]
public class Track
{
    public string title;
    public string filePath;
    public AudioClip audioClip;
}

public class MusicAppController : MonoBehaviour
{
    [Header("Audio Components")]
    public AudioSource audioSource;

    [Header("UI Buttons & Icons")]
    public Button playButton;
    public Button pauseButton;
    public Button forwardButton;
    public Button backwardButton;
    public Button addMusicButton;

    [Header("UI Visuals")]
    public Sprite playIconSprite;   
    public Sprite pauseIconSprite; 
    private Image playPauseButtonImage;

    [Header("Slider & Playback")]
    public Slider progressSlider;
    private bool isDraggingSlider = false;

    [Header("Scroll View & Playlist")]
    public Transform contentPanel;
    public GameObject songItemPrefab;

    private List<Track> playlist = new List<Track>();
    private int currentTrackIndex = 0;
    private bool isPlaying = false;

    void Start()
    {
        
        playButton.onClick.AddListener(OnPlayPauseClicked);
        pauseButton.onClick.AddListener(OnPlayPauseClicked);
        forwardButton.onClick.AddListener(PlayNextTrack);
        backwardButton.onClick.AddListener(PlayPreviousTrack);
        addMusicButton.onClick.AddListener(OpenFileDialogAndAddMusic);

        playPauseButtonImage = playButton.GetComponent<Image>();

        progressSlider.onValueChanged.AddListener(OnSliderValueChanged);

        UpdatePlayPauseUI();
    }

    void Update()
    {
        if (isPlaying && audioSource.clip != null && !isDraggingSlider)
        {
            if (audioSource.clip.length > 0)
            {
                progressSlider.value = audioSource.time / audioSource.clip.length;
            }
        }
        if (isPlaying && !audioSource.isPlaying && audioSource.time >= audioSource.clip.length)
        {
            PlayNextTrack();
        }
    }

    void OpenFileDialogAndAddMusic()
    {
        string folderPath = System.Environment.GetFolderPath(System.Environment.SpecialFolder.MyMusic);

        #if UNITY_EDITOR
        string path = UnityEditor.EditorUtility.OpenFilePanel("Select Audio File", folderPath, "mp3,wav");
        if (!string.IsNullOrEmpty(path))
        {
            StartCoroutine(LoadTrackIntoPlaylist(path));
        }
        #endif
    }

    IEnumerator LoadTrackIntoPlaylist(string path)
    {
        string url = "file://" + path;
        AudioType audioType = path.EndsWith(".mp3") ? AudioType.MPEG : AudioType.WAV;

        using (UnityWebRequest www = UnityWebRequestMultimedia.GetAudioClip(url, audioType))
        {
            yield return www.SendWebRequest();

            if (www.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError("Failed to load audio: " + www.error);
            }
            else
            {
                AudioClip clip = DownloadHandlerAudioClip.GetContent(www);
                string fileName = Path.GetFileNameWithoutExtension(path);

                Track newTrack = new Track
                {
                    title = fileName,
                    filePath = path,
                    audioClip = clip
                };

                playlist.Add(newTrack);
                AddTrackToScrollView(newTrack, playlist.Count - 1);
                
                if (playlist.Count == 1)
                {
                    currentTrackIndex = 0;
                }
            }
        }
    }
    
    void AddTrackToScrollView(Track track, int index)
    {
        GameObject item = Instantiate(songItemPrefab, contentPanel);
        TextMeshProUGUI titleText = item.GetComponentInChildren<TextMeshProUGUI>();
        if (titleText != null)
        {
            titleText.text = track.title;
        }

        Button itemButton = item.GetComponent<Button>();
        if (itemButton != null)
        {
            int capturedIndex = index;
            itemButton.onClick.AddListener(() => PlayTrack(capturedIndex));
        }
    }
    
    public void OnPlayPauseClicked()
    {
        if (playlist.Count == 0) return;

        if (isPlaying)
        {
            audioSource.Pause();
            isPlaying = false;
        }
        else
        {
            if (audioSource.clip == null)
            {
                PlayTrack(currentTrackIndex);
            }
            else
            {
                audioSource.UnPause();
                isPlaying = true;
            }
        }
        UpdatePlayPauseUI();
    }

    public void PlayTrack(int index)
    {
        if (index < 0 || index >= playlist.Count) return;

        currentTrackIndex = index;
        Track track = playlist[currentTrackIndex];

        if (track.audioClip != null)
        {
            audioSource.clip = track.audioClip;
            audioSource.Play();
            isPlaying = true;
            UpdatePlayPauseUI();
        }
    }

    public void PlayNextTrack()
    {
        if (playlist.Count == 0) return;
        currentTrackIndex = (currentTrackIndex + 1) % playlist.Count;
        PlayTrack(currentTrackIndex);
    }

    public void PlayPreviousTrack()
    {
        if (playlist.Count == 0) return;
        currentTrackIndex = (currentTrackIndex - 1 + playlist.Count) % playlist.Count;
        PlayTrack(currentTrackIndex);
    }

    void UpdatePlayPauseUI()
    {
        if (playPauseButtonImage != null)
        {
            playPauseButtonImage.sprite = isPlaying ? pauseIconSprite : playIconSprite;
        }
    }

    public void OnSliderDragStart()
    {
        isDraggingSlider = true;
    }

    public void OnSliderDragEnd()
    {
        isDraggingSlider = false;
        if (audioSource.clip != null)
        {
            audioSource.time = progressSlider.value * audioSource.clip.length;
        }
    }

    void OnSliderValueChanged(float value)
    {
    }
}