using UnityEngine;

public class LoadingScreen : MonoBehaviour
{
    public GameObject loadingUI;

    private OrderBookWindowController controller;

    private void Update()
    {
        if (controller == null)
            controller = FindObjectOfType<OrderBookWindowController>();

        if (controller != null && controller.IsMeshReady())
        {
            loadingUI.SetActive(false);
            enabled = false;
        }
    }
}