using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

public class GameEndController : MonoBehaviour
{
    [Header("References")]
    public GameObject gameOverPanel;
    public Text scoreText;
    public Button tryAgainButton;
    private string menuSceneName = "Menu";

    private void Start()
    {
        if (gameOverPanel != null)
            gameOverPanel.SetActive(false);

        if (tryAgainButton != null)
            tryAgainButton.onClick.AddListener(OnTryAgain);
    }

    public void TriggerGameOver(int score)
    {
        Debug.Log("Game End Triggered");
        Time.timeScale = 0f;

        if (gameOverPanel != null)
            gameOverPanel.SetActive(true);

        if (scoreText != null)
            scoreText.text = $"Score: {score}";
    }

    private void OnTryAgain()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(menuSceneName);
    }
}