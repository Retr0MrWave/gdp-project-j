using UnityEngine;

public class WallPositioner : MonoBehaviour
{
    public MeshRenderer mesh;
    public enum Wall
    {
        Left,
        Right,
        Floor,
        Ceiling
    }
    public Wall wall;
    private Bounds _bounds;
    
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        _bounds = mesh.bounds;
    }

    // Update is called once per frame
    void Update()
    {
        switch (wall)
        {
            case Wall.Left:
                transform.position = new Vector3(transform.position.x , transform.position.y, _bounds.center.z - _bounds.extents.z);
                break;
            case Wall.Right:
                Debug.DrawLine(_bounds.center, _bounds.center + new Vector3(0,0,_bounds.extents.z));
                Debug.Log(_bounds.center + " " + _bounds.extents);
                transform.position = new Vector3(transform.position.x , transform.position.y, _bounds.center.z + _bounds.extents.z);
                break;
            case Wall.Floor:
                transform.position = new Vector3(transform.position.x , _bounds.center.y - _bounds.extents.y, transform.position.z);
                break;
            case Wall.Ceiling:
                transform.position = new Vector3(transform.position.x , _bounds.center.y + _bounds.extents.y + 20, transform.position.z);
                break;
        }
            
    }
}
