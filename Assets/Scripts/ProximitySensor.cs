using System;
using UnityEngine;

public class ProximitySensor : MonoBehaviour
{
    private SphereCollider _collider;
    public MeshCollider bidsCollider;
    public MeshCollider asksCollider;

    private bool _closeToMesh;
    public bool CloseToMesh => _closeToMesh;

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _collider = GetComponent<SphereCollider>();
        _closeToMesh = false;
    }

    void Update()
    {
        Debug.Log(CloseToMesh);
    }

    private void OnTriggerEnter(Collider other)
    {
        if (other == bidsCollider || other == asksCollider)
            _closeToMesh = true;
    }

    private void OnTriggerExit(Collider other)
    {
        if (other == bidsCollider || other == asksCollider)
            _closeToMesh = false;
    }
}
