using UnityEngine;
using UnityEngine.UI;

public class HealthNew : MonoBehaviour
{
    public PlayerScoring scorer;
    public Image progressBar;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
    }

    // Update is called once per frame
    void Update()
    {
        progressBar.fillAmount = scorer.Health;
    }
}
