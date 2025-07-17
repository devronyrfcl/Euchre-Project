using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Learning : MonoBehaviour
{

    public int MyAge = 21;
    public int OldAge = 10;
    // Start is called before the first frame update
    void Start()
    {
        DebugAge();
    }

    // Update is called once per frame
    void Update()
    {

    }

    void DebugAge()
    {
        Debug.Log(MyAge + OldAge);
    }
}
