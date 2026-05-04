using UnityEngine;
using TMPro;

public class ScoreText : MonoBehaviour
{
    public PlayerScoring scorer;
    private TMP_Text myTextComponent;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        myTextComponent = GetComponent<TMP_Text>();
    }

    // Update is called once per frame
    void Update()
    {
        myTextComponent.SetText("Score: " + scorer.Score.ToString());
    }
}
