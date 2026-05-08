using UnityEngine;
using UnityEngine.UI;

public class DamageFlash : MonoBehaviour
{
    public ProximitySensor proxy;
    private double show = 0.0;
    public Image img;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        if (proxy.CloseToMesh)
        {
            if (show < 16) show += 0.5;
        } else
        {
            if (show > 0) show -= 0.5;
        }

        Color color = img.color;
        color.a = (float)show / 256;
        img.color = color;
    }
}
