using UnityEngine;
using UnityEngine.UI;

public class LoadingScreen : MonoBehaviour
{
    public OrderBookWindowController controller;
    public Image progressBar;

    private void Update()
    {
        if (controller == null)
            controller = FindObjectOfType<OrderBookWindowController>();

        if (controller == null)
            return;

        if (controller.IsMeshReady())
        {
            gameObject.SetActive(false);
            return;
        }

        var sampler = LiveOrderBookSampler.instance;
        if (sampler == null)
            return;

        int required = controller.startIndex + controller.windowSize;
        float progress = Mathf.Clamp01((float)sampler.SnapshotCount / required);

        progressBar.fillAmount = progress;
    }
}