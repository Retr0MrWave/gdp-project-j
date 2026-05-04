using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    public InputActionReference lookActionReference;
    private InputAction _lookAction;

    public float sensitivity = 1.0f;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _lookAction = lookActionReference.action ?? InputSystem.actions.FindAction("Look");
    }

    // Update is called once per frame
    void Update()
    {
        Vector2 rotationDirection = _lookAction.ReadValue<Vector2>() * sensitivity;
        Vector3 newRotation = (transform.rotation * Quaternion.Euler(-rotationDirection.y, rotationDirection.x, 0)).eulerAngles;

        if (newRotation.x < 90) newRotation.x = Mathf.Clamp(newRotation.x, 0, 30);
        if (newRotation.x > 180) newRotation.x = Mathf.Clamp(newRotation.x, 300, 360);

        if (newRotation.y < 90) newRotation.y += 360;
        newRotation.y = Mathf.Clamp(newRotation.y, 180, 360);
        
        newRotation.z = 0;
        Debug.Log(newRotation);

        transform.rotation = Quaternion.Euler(newRotation);
    }
}
