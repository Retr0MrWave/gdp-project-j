using System;
using UnityEngine;

public class PlayerScoring : MonoBehaviour
{
    private BoxCollider _collider;
    public MeshCollider bidsCollider;
    public MeshCollider asksCollider;
    public OrderBookWindowController controller;


    private float _score;
    public int Score => Mathf.FloorToInt(_score);

    private float _health;
    public float Health => _health;

    public float baseScore = 1.0f; // Score added per second travelled
    public float deviationScore = 1.0f; // Score per unit of deviation from center per second travelled
    public float centerCoordinate = 11.5f; // Y-axis coordinate of the center of the cavern

    public float healthLoss = 0.1f; // Health lost per second of touching the mesh

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _collider = GetComponent<BoxCollider>();

        _score = 0;
        _health = 1.0f;
    }

    // Update is called once per frame
    void Update()
    {
        if (controller.IsMeshReady() == false)
            return;
        _score += Time.deltaTime * (baseScore + deviationScore * Mathf.Abs(transform.position.y - centerCoordinate));
        
        Debug.Log("Score: " + Score + "; Health: " + Health);
    }

    private void OnTriggerStay(Collider other)
    {
        if (controller.IsMeshReady() == false)
            return;
        if (other == bidsCollider || other == asksCollider)
            _health -= Time.deltaTime * healthLoss;
    }
}
