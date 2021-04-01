using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Generator : MonoBehaviour
{
    [SerializeField]
    private GameObject testObject;
    private void Awake()
    {
        for (int i = 0; i < 40; i++)
        {
            for (int j = 0; j < 10; j++)
            {
                GameObject go = Instantiate(testObject);
                go.transform.position = new Vector3(i * 4,0,j * 13);
            }
        }
    }
}
