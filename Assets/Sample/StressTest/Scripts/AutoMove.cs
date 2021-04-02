using UnityEngine;

public class AutoMove : MonoBehaviour
{
    [SerializeField] private Vector3 direction = Vector3.up;

    [SerializeField] private float length = 0.5f;

    [SerializeField] [Range(0.1f, 5.0f)] private float interval = 2.0f;

    private Vector3 startPosition;
    private float time = 0;


    private void Start()
    {
        startPosition = transform.localPosition;
    }

    private void Update()
    {
        time += Time.deltaTime * 5;
        float ang = (time % interval) / interval * Mathf.PI * 2.0f;
        Vector3 offset = Vector3.Scale(direction, new Vector3(Mathf.Sin(ang), Mathf.Sin(ang), Mathf.Cos(ang))) * length;
        transform.localPosition = startPosition + offset;
    }
}