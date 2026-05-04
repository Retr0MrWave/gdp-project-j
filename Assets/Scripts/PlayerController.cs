using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    private Rigidbody _rb;
    public InputActionReference moveActionReference;
    private InputAction _moveAction;
    public Vector2 moveForce = new Vector2(1.0f, 1.0f);
    public Vector2 rotationFactor = new Vector2(45.0f, 45.0f);
    public float rotationSpeed = 10.0f;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _rb = GetComponent<Rigidbody>();

        _moveAction = moveActionReference.action ?? InputSystem.actions.FindAction("Move");
    }

    // Update is called once per frame
    private void Update()
    {
        Vector2 rotationDirection = _moveAction.ReadValue<Vector2>() * rotationFactor;
        Quaternion rotationTo = Quaternion.Euler(-rotationDirection.y, -90, -rotationDirection.x);

        transform.rotation = Quaternion.RotateTowards(transform.rotation, rotationTo, rotationSpeed * Time.deltaTime);
    }

    void FixedUpdate()
    {
        Vector2 moveValue = _moveAction.ReadValue<Vector2>() * moveForce;
        _rb.AddForce(0, moveValue.y, moveValue.x);
    }
}
