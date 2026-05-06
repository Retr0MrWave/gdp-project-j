using UnityEngine;

public class HealthBar : MonoBehaviour
{
    public PlayerScoring scorer;
    private RectTransform myrect;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        myrect = GetComponent<RectTransform>();
    }

    // Update is called once per frame
    void Update()
    {
        myrect.sizeDelta = new Vector2(180 * scorer.Health, 18);
        //myrect.position = new Vector3(scorer.Health * 90 - 90, 0,0);
    }
}
