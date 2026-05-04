using UnityEngine;

public class SliceTracker1 : MonoBehaviour
{
    public Transform playerBody;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.position = new Vector3(playerBody.position.x, playerBody.position.y, playerBody.position.z);
    }
}
