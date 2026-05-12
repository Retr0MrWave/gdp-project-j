using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class MainMenuController : MonoBehaviour
{
    [SerializeField] private string gameSceneName = "SampleScene";
    [SerializeField] private GameObject howToPlayPanel;
    private const string UseLiveDataPrefsKey = "OrderBookUseLiveData";
    [SerializeField] private Toggle liveModeToggle;
    private Toggle cachedLiveModeToggle;

    public Dropdown worldSkin;

    private Toggle GetLiveModeToggle()
    {
        if (liveModeToggle != null)
            return liveModeToggle;

        if (cachedLiveModeToggle == null)
        {
            cachedLiveModeToggle = GetComponentInChildren<Toggle>(true);

            if (cachedLiveModeToggle == null)
            {
                Debug.LogError("MainMenuController requires a reference to the live mode Toggle. Assign 'liveModeToggle' in the inspector or place the Toggle under this controller in the hierarchy.", this);
            }
        }

        return cachedLiveModeToggle;
    }

    public void PlayGame()
    {
        Toggle toggle = GetLiveModeToggle();
        bool useLiveData = toggle != null && toggle.isOn;

        PlayerPrefs.SetInt(UseLiveDataPrefsKey, useLiveData ? 1 : 0);
        PlayerPrefs.Save();
        Debug.Log($"Toggle isOn: {toggle?.isOn}, Saved value: {PlayerPrefs.GetInt(UseLiveDataPrefsKey)}");
        
        PlayerPrefs.SetInt("UseCanyonSkin", worldSkin.value);
        
        StartCoroutine(LoadSceneNextFrame());
    }

    private System.Collections.IEnumerator LoadSceneNextFrame()
    {
        yield return null;
        Time.timeScale = 1f;
        SceneManager.LoadScene(gameSceneName);
    }

    public void ShowHowToPlay()
    {
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(true);
        }
    }

    public void HideHowToPlay()
    {
        if (howToPlayPanel != null)
        {
            howToPlayPanel.SetActive(false);
        }
    }

    public void QuitGame()
    {
        Application.Quit();

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#endif
    }
}